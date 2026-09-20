namespace TestPty;

/// <summary>Picks the backend this platform has.</summary>
public static class PtyBackends
{
    public static IPtyBackend Create()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "This harness is Unix-only. Windows support was dropped rather than shipped unverified; " +
                "see plans/pty-test-harness.md.");
        }

        return new UnixPtyBackend();
    }
}
