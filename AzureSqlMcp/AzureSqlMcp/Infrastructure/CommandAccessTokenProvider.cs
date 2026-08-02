using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AzureSqlMcp.Application;
using Microsoft.Extensions.Options;

namespace AzureSqlMcp.Infrastructure;

/// <summary>
/// Obtains an Entra ID access token by running an operator-supplied shell command
/// (<see cref="SqlConnectionOptions.TokenCommand"/>) and caches it until shortly before it expires,
/// re-running the command on demand. MFA is satisfied out-of-band (e.g. a prior <c>az login</c>);
/// the command only mints/refreshes tokens. Returns <c>null</c> when no command is configured, so
/// the factory falls back to the plain connection string.
/// </summary>
public sealed class CommandAccessTokenProvider : IAccessTokenProvider, IDisposable
{
    // Refresh a little before actual expiry so an in-flight connection never uses a stale token.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    // Lifetime assumed when the command emits a bare token with no expiry (conservative).
    private static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromMinutes(55);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private readonly string? _command;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc;

    public CommandAccessTokenProvider(IOptions<SqlConnectionOptions> options)
        => _command = string.IsNullOrWhiteSpace(options.Value.TokenCommand) ? null : options.Value.TokenCommand;

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_command is null) return null; // not in token mode — factory uses the connection string as-is

        if (IsCachedTokenFresh()) return _cachedToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsCachedTokenFresh()) return _cachedToken; // another caller refreshed while we waited

            var (token, expiresAtUtc) = await AcquireAsync(_command, ct);
            _cachedToken = token;
            _expiresAtUtc = expiresAtUtc;
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsCachedTokenFresh()
        => _cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc - RefreshSkew;

    private static async Task<(string Token, DateTimeOffset ExpiresAtUtc)> AcquireAsync(string command, CancellationToken ct)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(isWindows ? "/c" : "-c");
        psi.ArgumentList.Add(command);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start the token command process.");

        // Drain both pipes concurrently with the wait to avoid a full-buffer deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"Token command did not complete within {CommandTimeout.TotalSeconds:N0}s.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Token command exited with code {process.ExitCode}: {stderr.Trim()}");

        return Parse(stdout);
    }

    private static (string Token, DateTimeOffset ExpiresAtUtc) Parse(string stdout)
    {
        var text = stdout.Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("Token command produced no output.");

        // Preferred: JSON from `az account get-access-token` — { accessToken, expires_on, ... }
        if (text.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("accessToken", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String)
                return (tokenEl.GetString()!, ReadExpiry(root));
        }

        // Fallback: a bare token string on stdout — assume a conservative default lifetime.
        return (text, DateTimeOffset.UtcNow + DefaultTokenTtl);
    }

    private static DateTimeOffset ReadExpiry(JsonElement root)
    {
        // `expires_on` is epoch seconds (UTC) — the most reliable field. The tz-less `expiresOn`
        // string is ambiguous, so we prefer the default TTL over trusting it.
        if (root.TryGetProperty("expires_on", out var epochEl) &&
            epochEl.ValueKind == JsonValueKind.Number && epochEl.TryGetInt64(out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch);

        return DateTimeOffset.UtcNow + DefaultTokenTtl;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort — the process is already gone or unkillable */ }
    }

    public void Dispose() => _gate.Dispose();
}
