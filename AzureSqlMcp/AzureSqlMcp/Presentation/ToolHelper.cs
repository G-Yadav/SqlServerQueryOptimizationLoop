namespace AzureSqlMcp.Presentation;

internal static class ToolHelper
{
    internal static async Task<string> SafeExecute(string name, string errorLabel, Func<Task<string>> action)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Object name must not be empty.";
        try { return await action(); }
        catch (ArgumentException ex) { return $"Invalid parameters: {ex.Message}"; }
        catch (Exception ex)         { return $"{errorLabel}: {ex.Message}"; }
    }
}
