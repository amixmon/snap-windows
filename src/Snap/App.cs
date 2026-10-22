using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Snap.Capture;
using Forms = System.Windows.Forms;

namespace Snap;

internal sealed class App : Application
{
    private const string Identity = "Snap.Native.Windows.v1";
    private Mutex? mutex;
    private CancellationTokenSource lifetime = new();
    private OverlayWindow? overlay;
    private ScreenRecorder? recorder;
    private RecordingIndicator? indicator;
    private Forms.NotifyIcon? tray;
    private HwndSource? messages;
    private bool resident, busy, stopping;
    private string? recordingPath;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--quit")) return RequestShutdown();
        try
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) => { e.Handled = true; app.Report(e.Exception); };
            return app.Run();
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            MessageBox.Show(ex.Message, "Snap could not start");
            return 1;
        }
    }

    // Setup and uninstall use this quiet command. Wait until the running instance
    // has finished saving a recording and released its mutex before returning.
    private static int RequestShutdown()
    {
        try
        {
            if (!Mutex.TryOpenExisting("Local\\" + Identity, out var running)) return 0;
            using (running)
            {
                using (var pipe = new NamedPipeClientStream(".", Identity, PipeDirection.Out))
                {
                    pipe.Connect(1500);
                    using var writer = new StreamWriter(pipe);
                    writer.WriteLine("quit");
                }
                bool released;
                try { released = running.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { released = true; }
                if (!released) return 2;
                running.ReleaseMutex();
                return 0;
            }
        }
        catch (Exception ex) { Log(ex.ToString()); return 1; }
    }

    private static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "Snap.log"), $"{DateTime.UtcNow:O} {text}\n"); }
        catch { /* Error reporting must never prevent application shutdown. */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, "Local\\" + Identity, out bool first);
        string command = e.Args.Contains("--quit") ? "quit" : e.Args.Contains("--background") ? "background" : "capture";
        if (!first)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", Identity, PipeDirection.Out);
                pipe.Connect(1500); using var writer = new StreamWriter(pipe); writer.WriteLine(command);
            }
            catch (Exception ex) { MessageBox.Show("Snap is already running but could not be reached.\n" + ex.Message, "Snap"); }
            Shutdown(); return;
        }
        if (command == "quit") { Shutdown(); return; }
        _ = ListenAsync();
        if (command == "background") EnableBackground(); else OpenCapture();
    }

    private async Task ListenAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Identity, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var reader = new StreamReader(pipe);
                string? command = await reader.ReadLineAsync(lifetime.Token);
                if (command == "quit") { await FinishRecordingAsync(); Shutdown(); return; }
                if (command == "background") EnableBackground();
                else OpenCapture();
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { if (!lifetime.IsCancellationRequested) await Task.Delay(100); }
        }
    }

    private void EnableBackground()
    {
        if (resident) return;
        resident = true;
        messages = new HwndSource(new HwndSourceParameters("Snap hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        messages.AddHook(MessageHook);
        bool print = RegisterHotKey(messages.Handle, 1, 0x4000, 0x2C);
        RegisterHotKey(messages.Handle, 2, 0x4001, 0x2C);
        RegisterHotKey(messages.Handle, 3, 0x4004, 0x2C);
        RegisterHotKey(messages.Handle, 4, 0x4007, 0x52);
        bool fallback = RegisterHotKey(messages.Handle, 5, 0x4003, 0x53);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Take screenshot", null, (_, _) => Dispatcher.Invoke(OpenCaptureDefault));
        menu.Items.Add("Record screen", null, (_, _) => Dispatcher.Invoke(() => OpenCapture(true)));
        menu.Items.Add("Stop recording", null, async (_, _) => await Dispatcher.InvokeAsync(() => FinishRecordingAsync()).Task.Unwrap());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open screenshots folder", null, (_, _) => {
            string dir = Path.GetDirectoryName(Output.NewPath(false))!;
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        });
        menu.Items.Add("Quit Snap", null, async (_, _) => { await FinishRecordingAsync(); Shutdown(); });
        tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "Snap — Take a screenshot", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenCapture();
        tray.ShowBalloonTip(4000, "Snap is ready", print ? "Press Print Screen to capture." : fallback ? "Print Screen is in use. Press Ctrl+Alt+S to capture." : "Open Snap from the tray to capture; keyboard shortcuts are in use.", Forms.ToolTipIcon.Info);
    }

    private void OpenCaptureDefault() => OpenCapture();
    private async void OpenCapture(bool video = false)
    {
        if (recorder != null || busy) return;
        if (overlay != null) { overlay.Activate(); return; }
        busy = true;
        try
        {
            await DismissToastsAsync();
            overlay = new OverlayWindow(video);
            overlay.Captured += async (_, request) => await HandleCaptureAsync(request);
            overlay.Closed += (_, _) => { overlay = null; ExitIfIdle(); };
            overlay.Show();
        }
        catch (Exception ex) { overlay = null; Report(ex); }
        finally { busy = false; ExitIfIdle(); }
    }

    private async Task HandleCaptureAsync(CaptureRequest request)
    {
        busy = true;
        try
        {
            if (request.Recording)
            {
                // Let DWM remove the capture overlay before recording live frames.
                await Task.Delay(180);
                recordingPath = Output.NewPath(true);
                recorder = new ScreenRecorder();
                var activeRecorder = recorder;
                recorder.Failed += (_, _) => Dispatcher.BeginInvoke(async () => {
                    // StartAsync and StopAsync already report their own failures.
                    // Handle only errors that occur during an active recording.
                    if (!busy && ReferenceEquals(recorder, activeRecorder)) await FinishRecordingAsync(false);
                });
                await recorder.StartAsync(request.Bounds, request.IncludePointer, recordingPath);
                indicator = new RecordingIndicator(); indicator.StopRequested += async (_, _) => await FinishRecordingAsync(); indicator.Show();
            }
            else
            {
                string path = Output.NewPath(false); DesktopCapture.SavePng(request.Image, path);
                bool copied = await Output.CopyAsync(request.Image, path);
                ShowToast(path, request.Image, copied);
            }
        }
        catch (Exception ex)
        {
            if (recorder != null) { recorder.Dispose(); recorder = null; }
            Report(ex);
        }
        finally { busy = false; ExitIfIdle(); }
    }

    private async Task FinishRecordingAsync(bool showToast = true)
    {
        if (recorder == null || stopping) return;
        stopping = true; busy = true;
        var current = recorder;
        try
        {
            await current.StopAsync();
            if (showToast && recordingPath != null) ShowToast(recordingPath, null, false);
        }
        catch (Exception ex) { Report(ex); }
        finally
        {
            current.Dispose(); recorder = null; indicator?.Close(); indicator = null;
            stopping = false; busy = false; ExitIfIdle();
        }
    }

    private void ShowToast(string path, BitmapSource? image, bool copied)
    {
        var toast = new CaptureToast(path, image, copied);
        toast.Closed += (_, _) => ExitIfIdle(); toast.Show();
    }

    private async void DirectCapture(bool window)
    {
        if (busy || overlay != null || recorder != null) return;
        busy = true;
        try
        {
            nint active = DesktopCapture.GetForegroundWindow();
            await DismissToastsAsync();
            var target = window ? DesktopCapture.GetWindows().FirstOrDefault(w => w.Handle == active) : null;
            if (window && target == null) { busy = false; OpenCapture(); return; }
            BitmapSource image;
            if (target != null)
            {
                var task = Task.Run(() => DesktopCapture.CaptureWindow(target));
                image = await Task.WhenAny(task, Task.Delay(600)) == task ? await task : DesktopCapture.Capture(target.Bounds);
                _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
            else image = DesktopCapture.Capture(DesktopCapture.GetVirtualBounds());
            string path = Output.NewPath(false); DesktopCapture.SavePng(image, path);
            ShowToast(path, image, await Output.CopyAsync(image, path));
        }
        catch (Exception ex) { Report(ex); }
        finally { busy = false; ExitIfIdle(); }
    }

    private async Task DismissToastsAsync()
    {
        var toasts = Windows.OfType<CaptureToast>().ToArray();
        foreach (var toast in toasts) toast.Close();
        if (toasts.Length > 0) await Task.Delay(100);
    }

    private nint MessageHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x0312) return 0;
        handled = true;
        switch ((int)wParam)
        {
            case 1: case 5: OpenCapture(); break;
            case 2: DirectCapture(true); break;
            case 3: DirectCapture(false); break;
            case 4: if (recorder != null) _ = FinishRecordingAsync(); else OpenCapture(true); break;
        }
        return 0;
    }

    private void ExitIfIdle()
    {
        if (!resident && !busy && recorder == null && overlay == null && !Windows.OfType<CaptureToast>().Any()) Shutdown();
    }

    private void Report(Exception ex)
    {
        Log(ex.ToString());
        MessageBox.Show(ex.Message, "Snap — Could not complete capture", MessageBoxButton.OK, MessageBoxImage.Warning);
        ExitIfIdle();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifetime.Cancel(); lifetime.Dispose();
        if (messages != null) { for (int i = 1; i <= 5; i++) UnregisterHotKey(messages.Handle, i); messages.Dispose(); }
        tray?.Dispose(); recorder?.Dispose(); mutex?.Dispose(); base.OnExit(e);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
