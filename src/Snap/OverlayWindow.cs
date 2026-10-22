using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Snap.Capture;

namespace Snap;

internal enum CaptureMode { Selection, Screen, Window }
internal sealed record CaptureRequest(CaptureRect Bounds, BitmapSource Image, bool Recording, bool IncludePointer);

internal sealed class OverlayWindow : Window
{
    private readonly CaptureRect desktop;
    private readonly IReadOnlyList<DisplayInfo> displays;
    private readonly IReadOnlyList<WindowInfo> windows;
    private readonly BitmapSource snapshot;
    private readonly CapturedPointer capturedPointer;
    private readonly nint foreground;
    private readonly Grid root = new();
    private readonly Canvas layer = new();
    private readonly Border panel;
    private readonly Button close, shutter, pointer, photo, video;
    private readonly Dictionary<CaptureMode, Button> modeButtons = new();
    private readonly CaptureSurface surface;
    private ScrollViewer? overview;
    private DisplayInfo selectedDisplay;
    private WindowInfo? selectedWindow;
    private BitmapSource? selectedWindowImage;
    private Rect selection;
    private CaptureMode mode;
    private bool recording, includePointer, accepted;
    private Point anchor;
    private Rect original;
    private DragEdge drag;
    private int overviewGeneration;
    private static Rect? previousSelection;

    internal event EventHandler<CaptureRequest>? Captured;

    internal OverlayWindow(bool startRecording = false)
    {
        foreground = DesktopCapture.GetForegroundWindow();
        desktop = DesktopCapture.GetVirtualBounds();
        displays = DesktopCapture.GetDisplays();
        windows = DesktopCapture.GetWindows();
        selectedDisplay = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        snapshot = DesktopCapture.Capture(desktop);
        capturedPointer = DesktopCapture.CapturePointer();
        selection = previousSelection.HasValue ? SelectionGeometry.Clamp(previousSelection.Value, DesktopRect) : SelectionGeometry.Initial(selectedDisplay.Bounds);
        recording = startRecording;
        Title = "Snap — Capture";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true; Topmost = true; Background = Brushes.Black;
        Left = desktop.X; Top = desktop.Y; Width = desktop.Width; Height = desktop.Height;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        surface = new CaptureSurface(this);
        root.Children.Add(surface); root.Children.Add(layer); Content = root;
        var panelContent = new StackPanel();
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in new[] { (CaptureMode.Selection, "selection", "Selection", "S"), (CaptureMode.Screen, "screen", "Screen", "C"), (CaptureMode.Window, "window", "Window", "W") })
        {
            var content = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            var icon = Icons.Make(item.Item2, 28); icon.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = item.Item3, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 9, 0, 0) });
            var button = Icons.Button(content, $"{item.Item3} ({item.Item4})", 100, 76);
            button.Margin = new Thickness(3, 0, 3, 0);
            var captureMode = item.Item1; button.Click += (_, _) => SetMode(captureMode);
            modeButtons[item.Item1] = button; choices.Children.Add(button);
        }
        panelContent.Children.Add(choices);
        var bottom = new Grid { Margin = new Thickness(3, 17, 3, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition());
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        photo = Icons.Button(Icons.Make("camera", 19), "Screenshot", 38, 38);
        video = Icons.Button(Icons.Make("video", 19), "Record screen (V)", 38, 38);
        photo.Click += (_, _) => SetRecording(false); video.Click += (_, _) => SetRecording(true);
        toggle.Children.Add(photo); toggle.Children.Add(video); bottom.Children.Add(toggle);
        var rings = new Grid { Width = 52, Height = 52 };
        rings.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(1) });
        rings.Children.Add(new Ellipse { Fill = Brushes.White, Margin = new Thickness(7), Name = "ShutterFill" });
        shutter = Icons.Button(rings, "Capture (Enter / Space / Ctrl+C)", 58, 58);
        shutter.Click += (_, _) => Accept(); Grid.SetColumn(shutter, 1); bottom.Children.Add(shutter);
        pointer = Icons.Button(Icons.Make("pointer", 20), "Include pointer (P)", 38, 38);
        pointer.HorizontalAlignment = HorizontalAlignment.Right; pointer.VerticalAlignment = VerticalAlignment.Center;
        pointer.Click += (_, _) => { includePointer = !includePointer; UpdateButtons(); };
        Grid.SetColumn(pointer, 2); bottom.Children.Add(pointer); panelContent.Children.Add(bottom);
        panel = new Border { Child = panelContent, Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(15),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = .45 } };
        layer.Children.Add(panel);
        close = Icons.Button(Icons.Make("close", 16), "Cancel (Esc)", 30, 30);
        close.Background = new SolidColorBrush(Color.FromRgb(65, 65, 65)); close.Click += (_, _) => Close(); layer.Children.Add(close);
        SourceInitialized += (_, _) => PositionNative();
        Loaded += (_, _) => { PositionNative(); LayoutControls(); Activate(); Focus(); };
        SizeChanged += (_, _) => { LayoutControls(); surface.InvalidateVisual(); };
        PreviewKeyDown += OnKey;
        surface.MouseDown += BeginDrag; surface.MouseMove += ContinueDrag; surface.MouseUp += EndDrag;
        surface.LostMouseCapture += (_, _) => { drag = DragEdge.None; panel.Opacity = 1; close.Opacity = 1; };
        Closed += (_, _) => { overviewGeneration++; capturedPointer.Dispose(); if (!accepted) previousSelection = selection; };
        UpdateButtons();
    }

    private Rect DesktopRect => new(desktop.X, desktop.Y, desktop.Width, desktop.Height);
    private double Scale => ActualWidth > 0 ? desktop.Width / ActualWidth : 1;
    private Point ScreenPoint(Point local) => new(desktop.X + local.X * Scale, desktop.Y + local.Y * Scale);
    private Rect LocalRect(Rect physical) => new((physical.X - desktop.X) / Scale, (physical.Y - desktop.Y) / Scale, physical.Width / Scale, physical.Height / Scale);
    private static Rect AsRect(CaptureRect r) => new(r.X, r.Y, r.Width, r.Height);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private void PositionNative() => SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1), desktop.X, desktop.Y, desktop.Width, desktop.Height, 0x0040);

    private void LayoutControls()
    {
        if (ActualWidth <= 0 || panel == null) return;
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        var work = LocalRect(AsRect(primary.WorkArea));
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double x = work.X + (work.Width - panel.DesiredSize.Width) / 2;
        double y = Math.Max(work.Top + 30, work.Bottom - panel.DesiredSize.Height - 48);
        Canvas.SetLeft(panel, x); Canvas.SetTop(panel, y);
        Canvas.SetLeft(close, x + panel.DesiredSize.Width - 10); Canvas.SetTop(close, y - 14);
        if (overview != null)
        {
            Canvas.SetLeft(overview, work.Left + 48); Canvas.SetTop(overview, work.Top + 48);
            overview.Width = Math.Max(1, work.Width - 96); overview.Height = Math.Max(1, y - work.Top - 78);
        }
    }

    private void SetMode(CaptureMode value)
    {
        if (recording && value == CaptureMode.Window) return;
        overviewGeneration++;
        mode = value;
        if (overview != null) { layer.Children.Remove(overview); overview = null; }
        if (mode == CaptureMode.Window) BuildOverview();
        UpdateButtons(); LayoutControls(); surface.InvalidateVisual();
    }

    private void SetRecording(bool value)
    {
        recording = value;
        if (recording && mode == CaptureMode.Window) SetMode(CaptureMode.Selection);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        foreach (var pair in modeButtons)
        {
            pair.Value.Tag = pair.Key == mode ? "selected" : null;
            pair.Value.Background = pair.Key == mode ? new SolidColorBrush(Color.FromRgb(76, 76, 76)) : Brushes.Transparent;
            pair.Value.IsEnabled = !(recording && pair.Key == CaptureMode.Window);
            pair.Value.Opacity = pair.Value.IsEnabled ? 1 : .28;
        }
        foreach (var b in new[] { photo, video, pointer }) { b.Tag = null; b.Background = Brushes.Transparent; }
        var active = recording ? video : photo; active.Tag = "selected"; active.Background = new SolidColorBrush(Color.FromRgb(86, 86, 86));
        if (includePointer) { pointer.Tag = "selected"; pointer.Background = new SolidColorBrush(Color.FromRgb(86, 86, 86)); }
        ((Ellipse)((Grid)shutter.Content).Children[1]).Fill = recording ? new SolidColorBrush(Color.FromRgb(237, 51, 59)) : Brushes.White;
        shutter.IsEnabled = mode != CaptureMode.Window || selectedWindow != null;
        surface.InvalidateVisual();
    }

    private async void BuildOverview()
    {
        int generation = overviewGeneration;
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        selectedWindow = windows.FirstOrDefault(w => w.Handle == foreground) ?? windows.FirstOrDefault();
        var pending = new List<(WindowInfo Window, Border Border, Image Preview)>();
        foreach (var window in windows)
        {
            BitmapSource image = Crop(snapshot, window.Bounds);
            if (window == selectedWindow) selectedWindowImage = image;
            var stack = new StackPanel { Margin = new Thickness(10) };
            var preview = new Image { Source = image, Width = 250, Height = 145, Stretch = Stretch.Uniform };
            stack.Children.Add(preview);
            stack.Children.Add(new TextBlock { Text = window.Title, Foreground = Brushes.White, MaxWidth = 240,
                TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), FontSize = 12 });
            var border = new Border { Child = stack, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12),
                BorderBrush = window == selectedWindow ? Brushes.White : Brushes.Transparent, Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)) };
            var button = Icons.Button(border, window.Title, 282, 200); button.Margin = new Thickness(12);
            button.Click += (_, _) => {
                selectedWindow = window; selectedWindowImage = (BitmapSource)preview.Source;
                foreach (Button sibling in wrap.Children) ((Border)sibling.Content).BorderBrush = Brushes.Transparent;
                border.BorderBrush = Brushes.White; UpdateButtons();
            };
            wrap.Children.Add(button);
            pending.Add((window, border, preview));
        }
        if (windows.Count == 0) wrap.Children.Add(new TextBlock { Text = "No windows available. Choose Selection or Screen.", Foreground = Brushes.White, FontSize = 16 });
        overview = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layer.Children.Insert(0, overview);
        // Render one window at a time, with a deadline. PrintWindow cannot be cancelled;
        // stop requesting previews if an app is hung, keeping the overlay responsive.
        foreach (var item in pending)
        {
            if (mode != CaptureMode.Window || !IsVisible || generation != overviewGeneration) break;
            var task = Task.Run(() => DesktopCapture.TryCaptureWindow(item.Window, out var result) ? result : null);
            if (await Task.WhenAny(task, Task.Delay(500)) != task)
            {
                _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                break;
            }
            try
            {
                var rendered = await task;
                if (generation != overviewGeneration || !IsVisible) break;
                if (rendered == null) continue;
                item.Preview.Source = rendered;
                if (selectedWindow == item.Window) selectedWindowImage = rendered;
            }
            catch { /* Frozen desktop preview stays available when an app cannot render. */ }
        }
    }

    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        var p = ScreenPoint(e.GetPosition(surface));
        if (mode == CaptureMode.Screen)
        {
            selectedDisplay = displays.FirstOrDefault(d => AsRect(d.Bounds).Contains(p)) ?? selectedDisplay;
            surface.InvalidateVisual(); return;
        }
        if (mode != CaptureMode.Selection) return;
        anchor = p; original = selection;
        drag = e.ChangedButton == MouseButton.Right || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? DragEdge.New : SelectionGeometry.HitTest(selection, p, 9 * Scale);
        if (drag == DragEdge.New) selection = new Rect(p, new Size(1, 1));
        surface.CaptureMouse(); panel.Opacity = .1; close.Opacity = .1;
        surface.InvalidateVisual(); e.Handled = true;
    }

    private void ContinueDrag(object sender, MouseEventArgs e)
    {
        if (mode != CaptureMode.Selection) { surface.Cursor = Cursors.Arrow; return; }
        var p = ScreenPoint(e.GetPosition(surface));
        if (drag != DragEdge.None)
        {
            selection = SelectionGeometry.Drag(original, anchor, p, drag, DesktopRect);
            surface.InvalidateVisual(); return;
        }
        var edge = SelectionGeometry.HitTest(selection, p, 9 * Scale);
        surface.Cursor = edge switch {
            DragEdge.Move => Cursors.SizeAll,
            DragEdge.Left or DragEdge.Right => Cursors.SizeWE,
            DragEdge.Top or DragEdge.Bottom => Cursors.SizeNS,
            DragEdge.Left | DragEdge.Top or DragEdge.Right | DragEdge.Bottom => Cursors.SizeNWSE,
            DragEdge.Right | DragEdge.Top or DragEdge.Left | DragEdge.Bottom => Cursors.SizeNESW,
            _ => Cursors.Cross
        };
    }

    private void EndDrag(object sender, MouseButtonEventArgs e)
    {
        if (drag == DragEdge.None) return;
        drag = DragEdge.None; surface.ReleaseMouseCapture(); panel.Opacity = 1; close.Opacity = 1;
        previousSelection = selection; surface.InvalidateVisual(); e.Handled = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (drag != DragEdge.None && e.Key != Key.Escape) return;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.S: SetMode(CaptureMode.Selection); break;
            case Key.C when Keyboard.Modifiers.HasFlag(ModifierKeys.Control): Accept(); break;
            case Key.C: SetMode(CaptureMode.Screen); break;
            case Key.W: SetMode(CaptureMode.Window); break;
            case Key.P: includePointer = !includePointer; UpdateButtons(); break;
            case Key.V: SetRecording(!recording); break;
            case Key.Enter: case Key.Space: Accept(); break;
            case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                if (mode != CaptureMode.Selection) return;
                int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                selection.Offset(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                selection = SelectionGeometry.Clamp(selection, DesktopRect); surface.InvalidateVisual(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Accept()
    {
        if (accepted || (mode == CaptureMode.Window && selectedWindow == null)) return;
        var bounds = mode switch { CaptureMode.Screen => selectedDisplay.Bounds, CaptureMode.Window => selectedWindow!.Bounds, _ => SelectionGeometry.Pixels(selection, desktop) };
        if (recording && (bounds.Width < 2 || bounds.Height < 2)) return;
        var image = mode == CaptureMode.Window ? selectedWindowImage! : Crop(snapshot, bounds);
        if (includePointer && !recording) image = DesktopCapture.ApplyPointer(image, bounds, capturedPointer);
        accepted = true; previousSelection = selection;
        Hide(); Captured?.Invoke(this, new CaptureRequest(bounds, image, recording, includePointer)); Close();
    }

    private BitmapSource Crop(BitmapSource source, CaptureRect bounds)
    {
        var clipped = Rect.Intersect(AsRect(bounds), AsRect(desktop));
        // Preserve the requested window dimensions when its frame extends off-screen.
        // Unsupported window rendering gets black padding outside the visible desktop.
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, bounds.Width, bounds.Height));
            if (!clipped.IsEmpty && clipped.Width > 0 && clipped.Height > 0)
            {
                var cropped = new CroppedBitmap(source, new Int32Rect((int)clipped.X - desktop.X, (int)clipped.Y - desktop.Y, (int)clipped.Width, (int)clipped.Height));
                dc.DrawImage(cropped, new Rect(clipped.X - bounds.X, clipped.Y - bounds.Y, clipped.Width, clipped.Height));
            }
        }
        var result = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
        result.Render(drawing); result.Freeze(); return result;
    }

    private sealed class CaptureSurface : FrameworkElement
    {
        private readonly OverlayWindow owner;
        internal CaptureSurface(OverlayWindow owner) { this.owner = owner; Focusable = true; }
        protected override void OnRender(DrawingContext dc)
        {
            var o = owner;
            dc.DrawImage(o.snapshot, new Rect(0, 0, ActualWidth, ActualHeight));
            if (o.mode == CaptureMode.Window) { dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(165, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight)); return; }
            Rect target = o.LocalRect(o.mode == CaptureMode.Screen ? AsRect(o.selectedDisplay.Bounds) : o.selection);
            var shade = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(target));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), null, shade);
            dc.DrawRectangle(null, new Pen(Brushes.White, 2), target);
            if (o.mode == CaptureMode.Selection)
            {
                foreach (var p in new[] { target.TopLeft, target.TopRight, target.BottomLeft, target.BottomRight })
                    dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 1), p, 5, 5);
                var label = new FormattedText($"{Math.Round(o.selection.Width)} × {Math.Round(o.selection.Height)}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double y = target.Top > 36 ? target.Top - 32 : target.Bottom + 12;
                var box = new Rect(target.Left + (target.Width - label.Width - 20) / 2, y, label.Width + 20, 25);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(36, 36, 36)), null, box, 6, 6);
                dc.DrawText(label, new Point(box.Left + 10, box.Top + 4));
            }
        }
    }
}
