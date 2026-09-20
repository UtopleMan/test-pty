namespace TestPty.Interop;

/// <summary>The errno values this code reacts to, rather than merely reports.</summary>
internal static class Errno
{
    /// <summary><c>EINTR</c>. A blocking call that a signal cut short and that must be retried.</summary>
    public const int Interrupted = 4;
}
