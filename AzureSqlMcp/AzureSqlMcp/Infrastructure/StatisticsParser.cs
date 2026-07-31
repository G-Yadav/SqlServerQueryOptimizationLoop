using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzureSqlMcp.Application;
using Jint;

namespace AzureSqlMcp.Infrastructure;

/// <summary>
/// Runs the vendored open-source STATISTICS parser (<c>parser.js</c>) in-process via Jint
/// and extracts the grand-total logical reads plus execution CPU/elapsed time. The JavaScript
/// source and its English <c>lang</c> config are embedded resources — no Node runtime, no
/// subprocess, no external files.
/// </summary>
public sealed class StatisticsParser : IStatisticsParser
{
    // parser.js ships as an ES module; strip its `export { ... }` so Jint can run it as a script.
    private static readonly Regex ExportBlock = new(@"export\s*\{[^}]*\}\s*;?", RegexOptions.Compiled);

    private readonly string _parserScript = ExportBlock.Replace(ReadResource("parser.js"), string.Empty);
    private readonly string _langJson = LangEn.Json;

    public BenchmarkStats Parse(string statisticsText)
    {
        if (string.IsNullOrWhiteSpace(statisticsText)) return default;

        // Jint's Engine is not thread-safe, so build a fresh one per call.
        var engine = new Engine();
        engine.Execute("var console = { log: function(){}, error: function(){}, warn: function(){} };");
        engine.Execute(_parserScript);
        engine.SetValue("__input", statisticsText);
        engine.SetValue("__langJson", _langJson);

        var totalJson = engine
            .Evaluate("JSON.stringify(parseData(__input, JSON.parse(__langJson)).total)")
            .AsString();

        using var doc = JsonDocument.Parse(totalJson);
        var total = doc.RootElement;
        var logical = total.GetProperty("iototal").GetProperty("total").GetProperty("logical").GetInt64();
        var execution = total.GetProperty("executiontotal");
        var cpu = execution.GetProperty("cpu").GetInt64();
        var elapsed = execution.GetProperty("elapsed").GetInt64();
        return new BenchmarkStats(logical, cpu, elapsed);
    }

    private static string ReadResource(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("StatsParser." + fileName, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
