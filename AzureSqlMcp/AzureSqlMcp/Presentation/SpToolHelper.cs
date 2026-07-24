using System.Text.RegularExpressions;

namespace AzureSqlMcp.Presentation;

internal static class SpToolHelper
{
    internal static bool IsValidSpName(string name) =>
        Regex.IsMatch(name, @"^[\w\.\[\]]+$");

    internal static async Task<string> SafeExecute(string spName, string errorLabel, Func<Task<string>> action)
    {
        if (!IsValidSpName(spName)) return "Invalid stored procedure name.";
        try { return await action(); }
        catch (ArgumentException ex) { return $"Invalid parameters: {ex.Message}"; }
        catch (Exception ex)         { return $"{errorLabel}: {ex.Message}"; }
    }
}
