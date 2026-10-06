using System.Text;

namespace OpenAPIDyalog.Utils;

/// <summary>
/// Helper methods for string manipulation in templates.
/// </summary>
public static class StringHelpers
{
    private static readonly HashSet<char> ValidAplChars = new()
    {
        // ASCII letters
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',

        // Underscore
        '_',

        // Accented characters
        'À', 'Á', 'Â', 'Ã', 'Ä', 'Å', 'Æ', 'Ç', 'È', 'É', 'Ê', 'Ë',
        'Ì', 'Í', 'Î', 'Ï', 'Ð', 'Ñ', 'Ò', 'Ó', 'Ô', 'Õ', 'Ö', 'Ø',
        'Ù', 'Ú', 'Û', 'Ü', 'Ý', 'ß',
        'à', 'á', 'â', 'ã', 'ä', 'å', 'æ', 'ç', 'è', 'é', 'ê', 'ë',
        'ì', 'í', 'î', 'ï', 'ð', 'ñ', 'ò', 'ó', 'ô', 'õ', 'ö', 'ø',
        'ù', 'ú', 'û', 'ü', 'þ',

        // Digits
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',

        // Delta characters
        '∆', '⍙',

        // Underscored/Circled (depends on font) letters
        'Ⓐ', 'Ⓑ', 'Ⓒ', 'Ⓓ', 'Ⓔ', 'Ⓕ', 'Ⓖ', 'Ⓗ', 'Ⓘ', 'Ⓙ', 'Ⓚ', 'Ⓛ', 'Ⓜ',
        'Ⓝ', 'Ⓞ', 'Ⓟ', 'Ⓠ', 'Ⓡ', 'Ⓢ', 'Ⓣ', 'Ⓤ', 'Ⓥ', 'Ⓦ', 'Ⓧ', 'Ⓨ', 'Ⓩ'
    };

    private const char   DeltaUnderbar = '⍙';
    private const string FallbackName  = "empty";

    /// <summary>
    /// Converts a string into a valid APL identifier.
    /// If the name is not a valid APL identifier or begins with delta-underbar,
    /// constructs a replacement beginning with delta-underbar, with each delta-underbar
    /// and non-valid character replaced by (delta-underbar, decimal UCS code, delta-underbar),
    /// and all other characters left unchanged.
    /// </summary>
    /// <param name="name">The string to convert.</param>
    /// <returns>A valid APL identifier.</returns>
    public static string ToValidAplName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return $"{DeltaUnderbar}{FallbackName}";

        bool needsTransform = false;

        // Check if name starts with delta-underbar
        if (name[0] == DeltaUnderbar)
        {
            needsTransform = true;
        }
        // Check if name starts with a digit (invalid as first character)
        else if (char.IsDigit(name[0]))
        {
            needsTransform = true;
        }
        // Check if all characters are valid
        else
        {
            foreach (char c in name)
            {
                if (!ValidAplChars.Contains(c))
                {
                    needsTransform = true;
                    break;
                }
            }
        }

        if (!needsTransform)
            return name;

        // Transform the name
        var result = new StringBuilder();
        result.Append(DeltaUnderbar);

        foreach (char c in name)
        {
            if (c == DeltaUnderbar || !ValidAplChars.Contains(c))
            {
                // Replace with ⍙<UCS code>⍙
                result.Append(DeltaUnderbar);
                result.Append((int)c);
                result.Append(DeltaUnderbar);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Quotes a string as an APL character vector literal, doubling any embedded quotes.
    /// </summary>
    public static string ToAplString(string text) => "'" + text.Replace("'", "''") + "'";

    /// <summary>
    /// Collapses a possibly multi-line string onto a single line, for use in a one-line
    /// comment or a Markdown table cell.
    /// </summary>
    public static string ToSingleLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Makes a string safe for a Markdown table cell: one line, with pipes escaped.
    /// </summary>
    public static string ToTableCell(string? text) => ToSingleLine(text).Replace("|", "\\|");

    /// <summary>
    /// Prefixes each line of a multiline string with the APL lamp symbol (⍝).
    /// </summary>
    /// <param name="text">The text to comment.</param>
    /// <returns>The text with each line commented.</returns>
    public static string CommentLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        return string.Join("\n", lines.Select(line => "⍝ " + line));
    }


}
