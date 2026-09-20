using System.Text;

namespace TestPty;

/// <summary>
/// A wait gave up. Carries what was waited for, how long, and the screen as it stood — a timeout
/// that does not show the screen wastes the failure.
/// </summary>
public sealed class PtyTimeoutException : Exception
{
    public PtyTimeoutException(
        string waitedFor,
        TimeSpan timeout,
        string screenText,
        string captureTail,
        string? capturePath = null)
        : base(BuildMessage(waitedFor, timeout, screenText, captureTail, capturePath))
    {
        WaitedFor = waitedFor;
        Timeout = timeout;
        ScreenText = screenText;
        CaptureTail = captureTail;
        CapturePath = capturePath;
    }

    /// <summary>A description of the condition that never became true.</summary>
    public string WaitedFor { get; }

    /// <summary>How long the wait was given.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>The whole virtual screen when the wait gave up.</summary>
    public string ScreenText { get; }

    /// <summary>The tail of the decoded output, escape sequences included.</summary>
    public string CaptureTail { get; }

    /// <summary>Where the full capture was written, if the session was told to write one.</summary>
    public string? CapturePath { get; }

    private static string BuildMessage(
        string waitedFor,
        TimeSpan timeout,
        string screenText,
        string captureTail,
        string? capturePath)
    {
        var builder = new StringBuilder();
        builder.Append("Timed out after ").Append(timeout.TotalSeconds).Append("s waiting for ").Append(waitedFor).Append('.');
        builder.Append("\n\nScreen:\n").Append(screenText);
        builder.Append("\n\nOutput tail:\n").Append(captureTail);

        if (capturePath is not null)
        {
            builder.Append("\n\nFull capture: ").Append(capturePath);
        }

        return builder.ToString();
    }
}
