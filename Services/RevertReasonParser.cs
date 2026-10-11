using System.Text.RegularExpressions;

namespace DeFi.Services;

public static class RevertReasonParser
{
    private static readonly Regex Pattern =
        new(@"(?:reason string|custom error)\s*'([^']+)'", RegexOptions.IgnoreCase);

    public static string Extract(string message)
    {
        var match = Pattern.Match(message);
        return match.Success ? match.Groups[1].Value.Trim() : message;
    }
}