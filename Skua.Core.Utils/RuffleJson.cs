using System.Text;

namespace Skua.Core.Utils;

/// <summary>
/// Ruffle's JSON.stringify writes whole Numbers as "897326396.0" where Flash Player writes "897326396".
/// The bot reads IDs as integers, so this drops a trailing ".0" outside of JSON strings.
/// </summary>
public static class RuffleJson
{
    public static string? NormalizeNumbers(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(".0", StringComparison.Ordinal) < 0)
            return text;
        char first = text[0];
        if (first != '{' && first != '[' && first != '-' && !char.IsDigit(first))
            return text;

        StringBuilder? sb = null;
        bool inString = false, escaped = false;
        int copied = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"')
            {
                inString = true;
                continue;
            }
            if (c != '.' || i == 0 || !char.IsDigit(text[i - 1]) || i + 1 >= text.Length || text[i + 1] != '0')
                continue;

            int end = i + 1;
            while (end < text.Length && text[end] == '0')
                end++;
            if (end < text.Length && (char.IsDigit(text[end]) || text[end] is 'e' or 'E'))
                continue;

            int start = i - 1;
            while (start > 0 && char.IsDigit(text[start - 1]))
                start--;
            if (start > 0 && text[start - 1] == '-')
                start--;
            if (start > 0 && text[start - 1] is not (':' or ',' or '[' or ' '))
                continue;

            sb ??= new StringBuilder(text.Length);
            sb.Append(text, copied, i - copied);
            copied = end;
            i = end - 1;
        }

        if (sb is null)
            return text;
        sb.Append(text, copied, text.Length - copied);
        return sb.ToString();
    }
}
