using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Snap.Interop;

namespace Snap.Capture;

/// <summary>
/// Records a physical screen rectangle to a silent H.264 MP4 using Windows Media
/// Foundation. Await StopAsync before using the file: MP4 needs finalization.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private const int FramesPerSecond = 30;
    private readonly object _gate = new();
    private readonly Func<CaptureRect, bool, Bitmap> _frameProvider;
    private Session? _session;
    private bool _disposed;

    public ScreenRecorder() : this(DesktopCapture.CaptureBitmap) { }

    internal ScreenRecorder(Func<CaptureRect, bool, Bitmap> frameProvider) =>
        _frameProvider = frameProvider ?? throw new ArgumentNullException(nameof(frameProvider));

    /// <summary>Raised on the recording worker after cleanup. UI subscribers must dispatch to their UI thread.</summary>
    public event EventHandler<Exception>? Failed;

    public bool IsRecording
    {
        get { lock (_gate) return _session is { } session && Volatile.Read(ref session.Recording) != 0; }
    }

    public TimeSpan Elapsed
    {
        get { lock (_gate) return _session?.Elapsed ?? TimeSpan.Zero; }
    }

    /// <summary>
    /// Completes after the first frame is accepted by the encoder. Odd dimensions
    /// are cropped by one pixel at the right or bottom to satisfy H.264.
    /// </summary>
    public Task StartAsync(CaptureRect bounds, bool includePointer, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (bounds.Width < 2 || bounds.Height < 2)
            throw new ArgumentOutOfRangeException(nameof(bounds), "A recording area must be at least 2 × 2 pixels.");
        // Check the buffer allocation without silently overflowing on an invalid rectangle.
        _ = checked(bounds.Width * bounds.Height * 4);
        var evenBounds = new CaptureRect(bounds.X, bounds.Y, bounds.Width & ~1, bounds.Height & ~1);
        var path = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The recording filename must end in .mp4.", nameof(outputPath));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is { } previous && !previous.Completion.Task.IsCompleted)
                throw new InvalidOperationException("A recording is already starting, recording, or saving.");
            if (File.Exists(path))
                throw new IOException("A file already exists at the recording destination.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var session = new Session();
            session.Worker = new Thread(() => Record(session, evenBounds, includePointer, path))
            {
                IsBackground = true,
                Name = "Snap screen recorder"
            };
            session.Worker.SetApartmentState(ApartmentState.MTA);
            _session = session;
            try
            {
                session.Worker.Start();
            }
            catch (Exception error)
            {
                session.Started.TrySetException(error);
                session.Completion.TrySetException(error);
            }
            return session.Started.Task;
        }
    }

    /// <summary>Stops capture, flushes the encoder, and finalizes the MP4. Encoding errors are rethrown.</summary>
    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_session is not { } session)
                return Task.CompletedTask;
            session.RequestStop();
            return session.Completion.Task;
        }
    }

    private void Record(Session session, CaptureRect bounds, bool includePointer, string path)
    {
        bool comInitialized = false;
        bool mediaInitialized = false;
        MediaFoundationWriter? writer = null;
        Exception? failure = null;
        try
        {
            MediaFoundation.Check(MediaFoundation.CoInitializeEx(IntPtr.Zero, 0), "Initialize recording COM thread");
            comInitialized = true;
            MediaFoundation.Check(MediaFoundation.MFStartup(MediaFoundation.Version, 0), "Initialize Windows Media Foundation");
            mediaInitialized = true;
            writer = new MediaFoundationWriter(path, bounds.Width, bounds.Height, FramesPerSecond);
            session.BeginTiming();
            var clock = Stopwatch.StartNew();
            long nextFrame = 0;
            do
            {
                // Timestamps stay on a 30 fps grid. When capture/encoding runs
                // slowly, skip missed slots instead of speeding up playback.
                var dueTicks = nextFrame * TimeSpan.TicksPerSecond / FramesPerSecond;
                var waitTicks = dueTicks - clock.Elapsed.Ticks;
                if (nextFrame > 0 && waitTicks > 0 && session.WaitForStop(TimeSpan.FromTicks(waitTicks)))
                    break;
                if (nextFrame > 0 && session.IsStopRequested)
                    break;
                var frame = nextFrame == 0 ? 0 : Math.Max(nextFrame, clock.Elapsed.Ticks * FramesPerSecond / TimeSpan.TicksPerSecond);
                var timestamp = frame * TimeSpan.TicksPerSecond / FramesPerSecond;
                var duration = (frame + 1) * TimeSpan.TicksPerSecond / FramesPerSecond - timestamp;
                using var bitmap = _frameProvider(bounds, includePointer);
                writer.WriteFrame(bitmap, timestamp, duration);
                Volatile.Write(ref session.Recording, 1);
                session.Started.TrySetResult();
                nextFrame = frame + 1;
            }
            while (!session.IsStopRequested);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            session.EndTiming();
            Volatile.Write(ref session.Recording, 0);
            try
            {
                // Even on capture failure, try to preserve all frames already written.
                writer?.Complete();
            }
            catch (Exception error)
            {
                failure = Combine(failure, error);
            }
            writer?.Dispose();
            if (mediaInitialized)
            {
                try { MediaFoundation.Check(MediaFoundation.MFShutdown(), "Shut down Windows Media Foundation"); }
                catch (Exception error) { failure = Combine(failure, error); }
            }
            if (comInitialized)
                MediaFoundation.CoUninitialize();

            if (failure is null)
            {
                session.Started.TrySetResult();
                session.Completion.TrySetResult();
            }
            else
            {
                session.Started.TrySetException(failure);
                session.Completion.TrySetException(failure);
                NotifyFailure(failure);
            }
        }
    }

    private static Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException("Recording failed and could not be fully finalized.", first, second);

    private void NotifyFailure(Exception failure)
    {
        if (Failed is not { } handlers)
            return;
        foreach (EventHandler<Exception> handler in handlers.GetInvocationList())
        {
            // A UI subscriber cannot be allowed to terminate the native worker.
            try { handler(this, failure); }
            catch { }
        }
    }

    /// <summary>Stops and finalizes any current recording. Prefer StopAsync for reporting failures.</summary>
    public void Dispose()
    {
        Session? session;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            session = _session;
            session?.RequestStop();
        }
        if (session is not null && Thread.CurrentThread != session.Worker)
        {
            // Failures are available from StopAsync and Failed; disposal still
            // guarantees native resources have been released before returning.
            try { session.Completion.Task.GetAwaiter().GetResult(); }
            catch { }
        }
    }

    private sealed class Session
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Thread? Worker;
        internal int Recording;
        private long _start;
        private long _end;
        private readonly object _signal = new();
        private int _stopRequested;

        internal bool IsStopRequested => Volatile.Read(ref _stopRequested) != 0;

        internal void RequestStop()
        {
            lock (_signal)
            {
                Volatile.Write(ref _stopRequested, 1);
                Monitor.PulseAll(_signal);
            }
        }

        internal bool WaitForStop(TimeSpan timeout)
        {
            lock (_signal)
            {
                if (!IsStopRequested)
                    Monitor.Wait(_signal, timeout);
                return IsStopRequested;
            }
        }

        internal void BeginTiming() => Interlocked.Exchange(ref _start, Stopwatch.GetTimestamp());
        internal void EndTiming() => Interlocked.Exchange(ref _end, Stopwatch.GetTimestamp());

        internal TimeSpan Elapsed
        {
            get
            {
                var start = Interlocked.Read(ref _start);
                if (start == 0)
                    return TimeSpan.Zero;
                var end = Interlocked.Read(ref _end);
                return Stopwatch.GetElapsedTime(start, end == 0 ? Stopwatch.GetTimestamp() : end);
            }
        }
    }
}
