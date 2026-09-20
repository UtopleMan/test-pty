using System.Runtime.Versioning;

namespace TestPty;

/// <summary>
/// Finds the image a program name refers to, in the parent, before any fork. A name that cannot be
/// resolved becomes an exception at the call site rather than a child that exits 127.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static class ProgramResolver
{
    public static string Resolve(string program)
    {
        if (program.Contains('/', StringComparison.Ordinal))
        {
            string full = Path.GetFullPath(program);

            return IsExecutable(full)
                ? full
                : throw new PtyStartException($"'{program}' is not an executable file.");
        }

        foreach (string directory in SearchPath())
        {
            string candidate = Path.Combine(directory, program);

            if (IsExecutable(candidate))
            {
                return candidate;
            }
        }

        throw new PtyStartException($"'{program}' was not found on PATH.");
    }

    private static IEnumerable<string> SearchPath()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        return path.Split(':', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        const UnixFileMode anyExecuteBit =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        return (File.GetUnixFileMode(path) & anyExecuteBit) != 0;
    }
}
