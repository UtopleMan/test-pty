using System.Text;
using System.Text.RegularExpressions;

namespace TestPty;

/// <summary>
/// The façade a test drives. Owns a backend, the pump that reads from it, the raw capture and the
/// virtual screen. Every wait is for output to arrive or for the screen to settle, always with a
/// timeout, and never for a fixed period.
/// </summary>
public sealed class PtySession : IDisposable
{
    private const int ReadBufferBytes = 4096;
    private const int CaptureTailCharacters = 2000;

    private static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(150);

    private readonly IPtyBackend backend;
    private readonly PtyOptions options;
    private readonly Screen screen;
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly List<byte> capture = [];
    private readonly StringBuilder decoded = new();
    private readonly List<StreamWaiter> streamWaiters = [];
    private readonly List<ScreenWaiter> screenWaiters = [];
    private readonly List<IdleWaiter> idleWaiters = [];
    private readonly CancellationTokenSource pumpCancellation = new();
    private readonly Lock gate = new();

    private string decodedSnapshot = string.Empty;
    private int consumedOffset;
    private Task pump = Task.CompletedTask;
    private bool isDisposed;

    private PtySession(IPtyBackend backend, PtyOptions options)
    {
        this.backend = backend;
        this.options = options;
        screen = new Screen(options.Size.Columns, options.Size.Rows);
    }

    /// <summary>Starts a program on the backend this platform has, and begins reading from it.</summary>
    public static PtySession Start(PtyOptions options) => Start(PtyBackends.Create(), options);

    /// <summary>Starts a program on the given backend and begins reading from it.</summary>
    public static PtySession Start(IPtyBackend backend, PtyOptions options)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(options);

        var session = new PtySession(backend, options);
        session.StartBackendAndPump();
        return session;
    }

    /// <summary>The virtual screen as the program has drawn it.</summary>
    public Screen Screen => screen;

    /// <summary>Every byte the program has written, in order.</summary>
    public byte[] RawCapture
    {
        get
        {
            lock (gate)
            {
                return [.. capture];
            }
        }
    }

    /// <summary>
    /// The capture decoded as UTF-8, escape sequences included. This is what the text and pattern
    /// waits search; use <see cref="Screen"/> when the assertion is about what the program drew.
    /// </summary>
    public string PlainText
    {
        get
        {
            lock (gate)
            {
                return decodedSnapshot;
            }
        }
    }

    /// <summary>The program's exit status, or <see langword="null"/> while it is still running.</summary>
    public int? ExitCode => backend.ExitCode;

    /// <summary>Writes text to the terminal as a keyboard would.</summary>
    public void Send(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        backend.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Writes bytes to the terminal as a keyboard would.</summary>
    public void SendBytes(ReadOnlySpan<byte> data) => backend.Write(data);

    /// <summary>
    /// Tells the program the window changed size. The virtual screen keeps the size it was created
    /// with: reflowing what is already drawn is not something this harness guesses at.
    /// </summary>
    public void Resize(int columns, int rows) => backend.Resize(columns, rows);

    /// <summary>
    /// Completes as soon as <paramref name="text"/> appears in the output that has not already been
    /// matched by an earlier wait, so a second wait for the same prompt waits for the next one.
    /// </summary>
    public Task WaitFor(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        return WaitForMatch(
            $"text {Readable.Quote(text)}",
            (haystack, from) =>
            {
                int index = haystack.IndexOf(text, from, StringComparison.Ordinal);
                return index < 0 ? -1 : index + text.Length;
            },
            timeout,
            cancellationToken);
    }

    /// <summary>Completes as soon as <paramref name="pattern"/> matches the output not already matched.</summary>
    public Task WaitFor(Regex pattern, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return WaitForMatch(
            $"pattern /{pattern}/",
            (haystack, from) =>
            {
                Match match = pattern.Match(haystack, from);
                return match.Success ? match.Index + Math.Max(match.Length, 1) : -1;
            },
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// Completes once <paramref name="condition"/> holds, re-evaluated on every update. For the
    /// assertions the stream cannot express, such as where the cursor is.
    /// </summary>
    public async Task WaitForScreen(
        Func<Screen, bool> condition,
        string? description = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        TimeSpan limit = timeout ?? options.DefaultTimeout;
        string what = description ?? "the screen to satisfy a condition";
        ScreenWaiter? waiter = null;

        lock (gate)
        {
            if (!condition(screen))
            {
                waiter = new ScreenWaiter(condition);
                screenWaiters.Add(waiter);
            }
        }

        if (waiter is null)
        {
            return;
        }

        try
        {
            await AwaitWithTimeout(waiter.Completion.Task, what, limit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                screenWaiters.Remove(waiter);
            }
        }
    }

    /// <summary>
    /// Completes once no output has arrived for <paramref name="quietPeriod"/>. The honest way to say
    /// "it has finished drawing" without knowing what it drew.
    /// </summary>
    public async Task WaitForIdle(
        TimeSpan? quietPeriod = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan quiet = quietPeriod ?? DefaultQuietPeriod;
        TimeSpan limit = timeout ?? options.DefaultTimeout;
        var waiter = new IdleWaiter(quiet);

        lock (gate)
        {
            idleWaiters.Add(waiter);
        }

        waiter.Restart();

        try
        {
            await AwaitWithTimeout(waiter.Completion.Task, $"idle for {quiet.TotalMilliseconds}ms", limit, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                idleWaiters.Remove(waiter);
            }

            waiter.Dispose();
        }
    }

    /// <summary>Waits for the program to exit, as distinct from merely going quiet.</summary>
    public async Task<int> WaitForExit(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        TimeSpan limit = timeout ?? options.DefaultTimeout;

        if (!backend.WaitForExit(limit))
        {
            throw TimedOut("the program to exit", limit);
        }

        await AwaitWithTimeout(pump, "the output to end after the program exited", limit, cancellationToken)
            .ConfigureAwait(false);

        return backend.ExitCode
            ?? throw new InvalidOperationException("The program exited but no status was reported.");
    }

    /// <summary>
    /// Writes <c>capture.raw</c> and <c>capture.txt</c> to <paramref name="directory"/>, so a failure
    /// on a machine nobody is watching can still be read afterwards.
    /// </summary>
    public string SaveCapture(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "capture.raw"), RawCapture);

        var readable = new StringBuilder();
        readable.Append("--- screen ---\n").Append(screen.ToText()).Append('\n');
        readable.Append("\n--- stream ---\n").Append(Readable.Render(PlainText)).Append('\n');
        File.WriteAllText(Path.Combine(directory, "capture.txt"), readable.ToString());

        return directory;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        pumpCancellation.Cancel();

        if (backend.IsRunning)
        {
            backend.Kill();
        }

        backend.Dispose();
        pumpCancellation.Dispose();

        lock (gate)
        {
            foreach (IdleWaiter waiter in idleWaiters)
            {
                waiter.Dispose();
            }

            idleWaiters.Clear();
        }
    }

    private void StartBackendAndPump()
    {
        backend.Start(options);
        pump = Task.Run(() => PumpAsync(pumpCancellation.Token));
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReadBufferBytes];

        while (!cancellationToken.IsCancellationRequested)
        {
            int count;

            try
            {
                count = await backend.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (count == 0)
            {
                return;
            }

            Append(buffer.AsSpan(0, count));
        }
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        List<TaskCompletionSource> satisfied = [];

        lock (gate)
        {
            capture.AddRange(bytes);
            Decode(bytes);
            CollectSatisfiedStreamWaiters(satisfied);
            CollectSatisfiedScreenWaiters(satisfied);

            foreach (IdleWaiter waiter in idleWaiters)
            {
                waiter.Restart();
            }
        }

        foreach (TaskCompletionSource completion in satisfied)
        {
            completion.TrySetResult();
        }
    }

    private void Decode(ReadOnlySpan<byte> bytes)
    {
        int charCount = decoder.GetCharCount(bytes, flush: false);

        if (charCount == 0)
        {
            return;
        }

        Span<char> characters = charCount <= 1024 ? stackalloc char[charCount] : new char[charCount];
        int written = decoder.GetChars(bytes, characters, flush: false);
        ReadOnlySpan<char> text = characters[..written];

        screen.Feed(text);
        decoded.Append(text);
        decodedSnapshot = decoded.ToString();
    }

    private void CollectSatisfiedStreamWaiters(List<TaskCompletionSource> satisfied)
    {
        for (int index = streamWaiters.Count - 1; index >= 0; index--)
        {
            StreamWaiter waiter = streamWaiters[index];
            int matchEnd = waiter.TryMatch(decodedSnapshot, waiter.SearchFrom);

            if (matchEnd < 0)
            {
                continue;
            }

            consumedOffset = matchEnd;
            satisfied.Add(waiter.Completion);
            streamWaiters.RemoveAt(index);
        }
    }

    private void CollectSatisfiedScreenWaiters(List<TaskCompletionSource> satisfied)
    {
        for (int index = screenWaiters.Count - 1; index >= 0; index--)
        {
            ScreenWaiter waiter = screenWaiters[index];

            if (!waiter.IsSatisfiedBy(screen))
            {
                continue;
            }

            satisfied.Add(waiter.Completion);
            screenWaiters.RemoveAt(index);
        }
    }

    private async Task WaitForMatch(
        string description,
        Func<string, int, int> tryMatch,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        TimeSpan limit = timeout ?? options.DefaultTimeout;
        StreamWaiter? waiter = null;

        lock (gate)
        {
            int matchEnd = tryMatch(decodedSnapshot, consumedOffset);

            if (matchEnd >= 0)
            {
                consumedOffset = matchEnd;
            }
            else
            {
                waiter = new StreamWaiter(tryMatch, consumedOffset);
                streamWaiters.Add(waiter);
            }
        }

        if (waiter is null)
        {
            return;
        }

        try
        {
            await AwaitWithTimeout(waiter.Completion.Task, description, limit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                streamWaiters.Remove(waiter);
            }
        }
    }

    private async Task AwaitWithTimeout(
        Task task,
        string description,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(description, timeout);
        }
    }

    private PtyTimeoutException TimedOut(string description, TimeSpan timeout)
    {
        string? capturePath = options.CaptureDirectory is null ? null : SaveCapture(options.CaptureDirectory);

        return new PtyTimeoutException(description, timeout, screen.ToText(), CaptureTail(), capturePath);
    }

    private string CaptureTail()
    {
        string text = PlainText;
        string tail = text.Length <= CaptureTailCharacters ? text : text[^CaptureTailCharacters..];
        return Readable.Render(tail);
    }

    private sealed class StreamWaiter(Func<string, int, int> tryMatch, int searchFrom)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SearchFrom => searchFrom;

        public int TryMatch(string haystack, int from) => tryMatch(haystack, from);
    }

    private sealed class ScreenWaiter(Func<Screen, bool> condition)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSatisfiedBy(Screen screen) => condition(screen);
    }

    private sealed class IdleWaiter : IDisposable
    {
        private readonly TimeSpan quietPeriod;
        private readonly Timer timer;
        private readonly Lock timerGate = new();

        private bool isDisposed;

        public IdleWaiter(TimeSpan quietPeriod)
        {
            this.quietPeriod = quietPeriod;
            Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            timer = new Timer(_ => Completion.TrySetResult(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public TaskCompletionSource Completion { get; }

        /// <summary>Pushes the deadline out; called whenever output arrives.</summary>
        public void Restart()
        {
            lock (timerGate)
            {
                if (isDisposed)
                {
                    return;
                }

                timer.Change(quietPeriod, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            lock (timerGate)
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
                timer.Dispose();
            }
        }
    }
}
