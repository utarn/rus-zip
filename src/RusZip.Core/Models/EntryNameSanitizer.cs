using System.Text;

namespace RusZip.Core.Models;

/// <summary>
/// Sanitizes untrusted text (archive entry names, exception messages, file paths) before it
/// crosses a render boundary: terminal output, JSON payloads, or GUI text.
///
/// Strips C0/C1 control characters (U+0000–U+001F, U+007F, U+0080–U+009F) — including tab,
/// ESC (0x1B), NUL (0x00), CR and LF — that could inject terminal escape sequences, raw NUL
/// bytes, or line noise into output. Non-control content is preserved verbatim.
/// </summary>
public static class EntryNameSanitizer
{
    /// <summary>
    /// Removes C0/C1 control characters from a string while keeping all other content intact.
    /// Suitable for terminal table cells, JSON <c>path</c> fields, tree nodes and breadcrumbs.
    /// Returns the original string when nothing needs to be stripped (no allocation).
    /// </summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (!ContainsControlChar(value)) return value;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!IsControlChar(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips C0/C1 control characters and collapses CR/LF into a single trimmed line.
    /// Designed for one-line status bars and console error lines, where multi-line or
    /// control-byte content from attacker-controlled sources must not break layout.
    /// </summary>
    public static string SingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (!ContainsControlChar(value)) return value;

        var sb = new StringBuilder(value.Length);
        bool pendingSpace = false;

        foreach (var ch in value)
        {
            if (ch is '\r' or '\n')
            {
                pendingSpace = true; // collapse newlines into a single separator space
            }
            else if (IsControlChar(ch))
            {
                // strip all other control bytes (tab, ESC, NUL, C1, ...)
            }
            else
            {
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// True for Unicode C0/C1 control characters: U+0000–U+001F, U+007F (DEL), U+0080–U+009F.
    /// </summary>
    public static bool IsControlChar(char ch) =>
        ch <= '\u001f' || ch == '\u007f' || (ch >= '\u0080' && ch <= '\u009f');

    private static bool ContainsControlChar(string value)
    {
        foreach (var ch in value)
        {
            if (IsControlChar(ch)) return true;
        }

        return false;
    }
}
