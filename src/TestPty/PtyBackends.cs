namespace TestPty;

/// <summary>Picks the backend this platform has.</summary>
public static class PtyBackends
{
    public static IPtyBackend Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPtyBackend();
        }

        return new UnixPtyBackend();
    }
}
