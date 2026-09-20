using System.Text;
using System.Threading.Channels;

namespace TestPty.Tests;

/// <summary>
/// Replays scripted output and records what was written, so the session, the screen and the waits are
/// testable with no real process.
/// </summary>
internal sealed class FakePtyBackend : IPtyBackend
{
    private readonly Channel<byte[]> outgoing = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte> written = [];
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();

    private byte[] pending = [];
    private int pendingOffset;

    public bool IsRunning { get; private set; }

    public int? ExitCode { get; private set; }

    public PtyOptions? StartedWith { get; private set; }

    public (int Columns, int Rows)? LastResize { get; private set; }

    public bool WasKilled { get; private set; }

    public int DisposeCount { get; private set; }

    /// <summary>Everything the session wrote to the terminal, decoded as UTF-8.</summary>
    public string WrittenText
    {
        get
        {
            lock (gate)
            {
                return Encoding.UTF8.GetString([.. written]);
            }
        }
    }

    public void Start(PtyOptions options)
    {
        StartedWith = options;
        IsRunning = true;
    }

    /// <summary>Queues output the child is to have produced, delivered as one read.</summary>
    public void Emit(string text) => Emit(Encoding.UTF8.GetBytes(text));

    public void Emit(byte[] bytes) => outgoing.Writer.TryWrite(bytes);

    /// <summary>Ends the output stream and reports the given status.</summary>
    public void Exit(int exitCode)
    {
        IsRunning = false;
        ExitCode = exitCode;
        outgoing.Writer.TryComplete();
        exited.TrySetResult();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            written.AddRange(data);
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (pendingOffset == pending.Length)
        {
            if (!await outgoing.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            if (!outgoing.Reader.TryRead(out byte[]? next))
            {
                return 0;
            }

            pending = next;
            pendingOffset = 0;
        }

        int count = Math.Min(buffer.Length, pending.Length - pendingOffset);
        pending.AsSpan(pendingOffset, count).CopyTo(buffer.Span);
        pendingOffset += count;
        return count;
    }

    public void Resize(int columns, int rows) => LastResize = (columns, rows);

    public bool WaitForExit(TimeSpan timeout) => exited.Task.Wait(timeout);

    public void Kill()
    {
        WasKilled = true;

        if (ExitCode is null)
        {
            Exit(-1);
        }
    }

    public void Dispose()
    {
        DisposeCount++;
        outgoing.Writer.TryComplete();
        exited.TrySetResult();
        IsRunning = false;
    }
}
