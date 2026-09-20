using System.Text;

namespace TestPty;

/// <summary>
/// Builds the single command-line string <c>CreateProcessW</c> takes, using the quoting rules the C
/// runtime uses to take it apart again.
/// </summary>
internal static class WindowsCommandLine
{
    public static string Build(string program, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendQuoted(builder, program);

        foreach (string argument in arguments)
        {
            builder.Append(' ');
            AppendQuoted(builder, argument);
        }

        return builder.ToString();
    }

    private static void AppendQuoted(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && !argument.AsSpan().ContainsAny(' ', '\t', '"'))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');

        for (int index = 0; index < argument.Length; index++)
        {
            int backslashes = 0;

            while (index < argument.Length && argument[index] == '\\')
            {
                backslashes++;
                index++;
            }

            if (index == argument.Length)
            {
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[index] == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            builder.Append(argument[index]);
        }

        builder.Append('"');
    }
}
