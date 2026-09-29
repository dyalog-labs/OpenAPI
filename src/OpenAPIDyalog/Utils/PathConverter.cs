using System.Text.RegularExpressions;

namespace OpenAPIDyalog.Utils;

/// <summary>
/// Converts OpenAPI URL paths to APL path expressions.
/// </summary>
public static class PathConverter
{
    private static readonly Regex PathParameterRegex =
        new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Converts an OpenAPI path with parameter placeholders to an APL expression.
    /// Each parameter value is percent-encoded, so a value containing "/", "?", "#" or a
    /// space stays within its own path segment.
    /// Example: "/user/{userId}" → "'/user/',(c.∆.HttpCommand.UrlEncode⍕argsNs.userId)"
    /// </summary>
    public static string ToDyalogPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "''";

        var parts = new List<string>();
        var currentIndex = 0;

        foreach (Match match in PathParameterRegex.Matches(path))
        {
            if (match.Index > currentIndex)
            {
                var text = path.Substring(currentIndex, match.Index - currentIndex);
                parts.Add($"'{text}'");
            }

            var paramName = match.Groups[1].Value;
            parts.Add($"(c.∆.HttpCommand.UrlEncode⍕argsNs.{StringHelpers.ToValidAplName(paramName)})");

            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < path.Length)
        {
            parts.Add($"'{path.Substring(currentIndex)}'");
        }

        if (parts.Count == 0) return "''";
        if (parts.Count == 1) return parts[0];

        return string.Join(",", parts);
    }
}
