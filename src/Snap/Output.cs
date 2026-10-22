using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snap.Capture;

namespace Snap;

internal static class Output
{
    internal static string NewPath(bool video)
    {
        string root = Environment.GetFolderPath(video ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), video ? "Videos" : "Pictures");
        string directory = Path.Combine(root, video ? "Screencasts" : "Screenshots");
        Directory.CreateDirectory(directory);
        string stem = $"{(video ? "Screencast" : "Screenshot")} from {DateTime.Now:yyyy-MM-dd HH-mm-ss-fff}";
        string path = Path.Combine(directory, stem + (video ? ".mp4" : ".png"));
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{stem} ({i}){(video ? ".mp4" : ".png")}");
        return path;
    }

    internal static async Task<bool> CopyAsync(BitmapSource image, string path)
    {
        using var png = new MemoryStream();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(png); png.Position = 0;
        var data = new DataObject();
        data.SetImage(image); data.SetData("PNG", png);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return true; }
            catch (ExternalException) { await Task.Delay(80); }
        }
        return false;
    }

    internal static void Reveal(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
}

internal sealed class CaptureToast : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    internal CaptureToast(string path, BitmapSource? image, bool copied)
    {
        Title = "Snap — Saved"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent; AllowsTransparency = true;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (image != null) content.Children.Add(new Image { Source = image, Width = 72, Height = 54, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 14, 0) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = image == null ? "Recording saved" : "Screenshot captured", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = image == null ? "Saved in Videos / Screencasts" : copied ? "Copied to clipboard · Saved to Pictures" : "Saved to Pictures · Clipboard busy", Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)), FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
        content.Children.Add(text);
        var open = Icons.Button(Icons.Make("folder", 20), "Show in folder", 38, 38); open.Margin = new Thickness(16, 0, 0, 0);
        open.Click += (_, _) => { Output.Reveal(path); Close(); }; content.Children.Add(open);
        Content = new Border { Child = content, Padding = new Thickness(18), Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)), CornerRadius = new CornerRadius(14) };
        Loaded += (_, _) => { var work = SystemParameters.WorkArea; Left = work.Right - ActualWidth - 24; Top = work.Top + 24; timer.Start(); };
        timer.Tick += (_, _) => Close(); Closed += (_, _) => timer.Stop();
        MouseEnter += (_, _) => timer.Stop(); MouseLeave += (_, _) => timer.Start();
    }
}

internal sealed class RecordingIndicator : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch elapsed = new();
    internal event EventHandler? StopRequested;
    internal RecordingIndicator()
    {
        Title = "Snap — Recording"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true; Background = Brushes.Transparent;
        var text = new TextBlock { Text = "00:00   ■", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold };
        var stop = Icons.Button(text, "Stop recording", 120, 40);
        stop.Background = new SolidColorBrush(Color.FromRgb(195, 32, 42));
        stop.Click += (_, _) => { stop.IsEnabled = false; text.Text = "Saving…"; timer.Stop(); StopRequested?.Invoke(this, EventArgs.Empty); };
        Content = stop;
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        Loaded += (_, _) => { var work = SystemParameters.WorkArea; Left = work.Right - ActualWidth - 20; Top = work.Top + 16; elapsed.Start(); timer.Start(); };
        timer.Tick += (_, _) => text.Text = $"●  {elapsed.Elapsed:mm\\:ss}   ■";
        Closed += (_, _) => timer.Stop();
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
