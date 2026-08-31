using System.Text;

namespace AspireManager.Core;

/// <summary>
/// Strips terminal escape sequences from log content. Services log in colour (Serilog themes, Rust's
/// <c>tracing</c>, dapr), and a ListView paints the escapes literally as <c>[2m</c> rather than obeying them.
/// </summary>
public static class AnsiText
{
    /// <summary>
    /// Removes CSI and OSC sequences and any remaining control characters, leaving the text. Tabs become
    /// spaces because a ListView row has no tab stops.
    /// </summary>
    public static string Strip(string text)
    {
        if (text.IndexOf('') < 0 && !text.Any(char.IsControl))
        {
            return text;
        }

        StringBuilder result = new(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c != '')
            {
                result.Append(c switch
                {
                    '\t' => ' ',
                    _ when char.IsControl(c) => '\0',
                    _ => c,
                });

                i++;
                continue;
            }

            i++;
            if (i >= text.Length)
            {
                break;
            }

            // CSI: ESC [ params final-byte. OSC: ESC ] ... terminated by BEL or ESC \.
            if (text[i] == '[')
            {
                i++;
                while (i < text.Length && !char.IsBetween(text[i], '@', '~'))
                {
                    i++;
                }

                i++;
            }
            else if (text[i] == ']')
            {
                i++;
                while (i < text.Length && text[i] != '\a' && text[i] != '')
                {
                    i++;
                }

                // ESC \ terminators consume both characters.
                i += i < text.Length && text[i] == '' ? 2 : 1;
            }
            else
            {
                i++;
            }
        }

        return result.Replace("\0", string.Empty).ToString();
    }
}
