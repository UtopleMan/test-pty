using System.Globalization;
using System.Text;

namespace TestPty;

/// <summary>Renders captured output so a failure message can be read by a person.</summary>
internal static class Readable
{
    public static string Quote(string text) => $"\"{Render(text)}\"";

    public static string Render(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (char character in text)
        {
            builder.Append(RenderCharacter(character));
        }

        return builder.ToString();
    }

    private static string RenderCharacter(char character) => character switch
    {
        Ansi.Escape => "\\e",
        '\r' => "\\r",
        '\n' => "\\n",
        '\t' => "\\t",
        '\\' => "\\\\",
        _ when char.IsControl(character) => "\\x" + ((int)character).ToString("x2", CultureInfo.InvariantCulture),
        _ => character.ToString(),
    };
}
