namespace TestPty.Tests;

/// <summary>
/// Keeps a <see cref="FakePtyBackend"/> talking on a timer, so a test can show that a wait for
/// quiet does not return while output is still arriving.
/// </summary>
internal sealed class OutputNoise : IDisposable
{
    private readonly Timer timer;

    public OutputNoise(FakePtyBackend backend, TimeSpan interval)
    {
        timer = new Timer(_ => backend.Emit("."), null, TimeSpan.Zero, interval);
    }

    public void Dispose() => timer.Dispose();
}
