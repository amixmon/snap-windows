using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snap.Interop;

namespace Snap.Capture;

/// <summary>A rectangle in physical desktop pixels. The right and bottom edges are exclusive.</summary>
public readonly record struct CaptureRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

public sealed record DisplayInfo(string DeviceName, CaptureRect Bounds, CaptureRect WorkArea, bool IsPrimary);

public sealed record WindowInfo(nint Handle, string Title, CaptureRect Bounds);

/// <summary>
/// An owned copy of a cursor's image, hotspot, and physical desktop position at one instant.
/// Dispose after the capture session. Windows cursor changes do not affect this object.
/// </summary>
public sealed class CapturedPointer : IDisposable
{
    private readonly CursorIconHandle icon;
    private int disposed;

    internal CapturedPointer(nint icon, int x, int y, int hotspotX, int hotspotY)
    {
        this.icon = new CursorIconHandle(icon);
        X = x;
        Y = y;
        HotspotX = hotspotX;
        HotspotY = hotspotY;
        IsVisible = icon != 0;
    }

    public bool IsVisible { get; }
    public int X { get; }
    public int Y { get; }
    public int HotspotX { get; }
    public int HotspotY { get; }

    internal nint AcquireIcon(ref bool referenceAdded)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        icon.DangerousAddRef(ref referenceAdded);
        return icon.DangerousGetHandle();
    }

    internal void ReleaseIcon() => icon.DangerousRelease();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            icon.Dispose();
    }

    private sealed class CursorIconHandle : SafeHandle
    {
        internal CursorIconHandle(nint handle) : base(nint.Zero, true) => SetHandle(handle);
        public override bool IsInvalid => handle == nint.Zero;
        protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);
    }
}

/// <summary>
/// Captures the composed Windows desktop. The application must declare PerMonitorV2 DPI awareness
/// before calling these methods so every coordinate remains a physical desktop pixel.
/// </summary>
public static class DesktopCapture
{
    public static CaptureRect GetVirtualBounds()
    {
        var displays = GetDisplays();
        if (displays.Count == 0)
            throw new InvalidOperationException("Windows did not report an active display.");

        var left = displays.Min(display => display.Bounds.X);
        var top = displays.Min(display => display.Bounds.Y);
        var right = displays.Max(display => display.Bounds.Right);
        var bottom = displays.Max(display => display.Bounds.Bottom);
        return new CaptureRect(left, top, checked(right - left), checked(bottom - top));
    }

    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        Exception? callbackError = null;
        var success = NativeMethods.EnumDisplayMonitors(0, 0, EnumerateMonitor, 0);
        if (callbackError is not null)
            throw callbackError;
        if (!success)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate the displays.");

        return displays.OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.Bounds.X).ThenBy(display => display.Bounds.Y).ToArray();

        bool EnumerateMonitor(nint monitor, nint deviceContext, ref NativeMethods.Rect bounds, nint data)
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                DeviceName = string.Empty
            };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                // Keep managed exceptions on the managed side of the native callback boundary.
                callbackError = new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read a display's bounds.");
                return false;
            }

            var monitorBounds = FromNative(info.Monitor);
            if (monitorBounds.Width > 0 && monitorBounds.Height > 0)
                displays.Add(new DisplayInfo(info.DeviceName, monitorBounds, FromNative(info.WorkArea),
                    (info.Flags & NativeMethods.MonitorInfoPrimary) != 0));
            return true;
        }
    }

    /// <summary>Returns selectable app windows in desktop stacking order, with the frontmost first.</summary>
    public static IReadOnlyList<WindowInfo> GetWindows()
    {
        var windows = new List<WindowInfo>();
        var shellWindow = NativeMethods.GetShellWindow();
        var processId = (uint)Environment.ProcessId;
        var displays = GetDisplays();
        if (!NativeMethods.EnumWindows(EnumerateWindow, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate desktop windows.");
        return windows.AsReadOnly();

        bool EnumerateWindow(nint window, nint data)
        {
            // A window may disappear between any two calls. Such windows are simply skipped.
            if (window == shellWindow || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
                return true;
            if (NativeMethods.GetWindowThreadProcessId(window, out var ownerProcessId) == 0 || ownerProcessId == processId)
                return true;
            if (NativeMethods.DwmGetWindowInt(window, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) >= 0 && cloaked != 0)
                return true;

            var extendedStyle = nint.Size == 8
                ? NativeMethods.GetWindowLongPtr64(window, NativeMethods.GwlExStyle).ToInt64()
                : NativeMethods.GetWindowLong32(window, NativeMethods.GwlExStyle);
            if ((extendedStyle & NativeMethods.WsExNoActivate) != 0 ||
                ((extendedStyle & NativeMethods.WsExToolWindow) != 0 && (extendedStyle & NativeMethods.WsExAppWindow) == 0))
                return true;

            var className = new StringBuilder(256);
            NativeMethods.GetClassName(window, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            var titleLength = NativeMethods.GetWindowTextLength(window);
            if (titleLength <= 0)
                return true;
            var titleBuffer = new StringBuilder((int)Math.Min(titleLength + 1L, 32768L));
            NativeMethods.GetWindowText(window, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.ToString().Trim();
            if (title.Length == 0)
                return true;

            // DWM bounds omit invisible resizing borders and are already in physical pixels.
            var result = NativeMethods.DwmGetWindowRect(window, NativeMethods.DwmwaExtendedFrameBounds,
                out var nativeBounds, Marshal.SizeOf<NativeMethods.Rect>());
            if (result < 0 && !NativeMethods.GetWindowRect(window, out nativeBounds))
                return true;
            var windowBounds = FromNative(nativeBounds);
            if (windowBounds.Width <= 1 || windowBounds.Height <= 1 ||
                !displays.Any(display => Intersects(windowBounds, display.Bounds)))
                return true;

            windows.Add(new WindowInfo(window, title, windowBounds));
            return true;
        }
    }

    public static nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>Captures a duplicate cursor image and position without capturing desktop pixels.</summary>
    public static CapturedPointer CapturePointer()
    {
        var cursor = new NativeMethods.CursorInfo { Size = Marshal.SizeOf<NativeMethods.CursorInfo>() };
        if (!NativeMethods.GetCursorInfo(ref cursor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read the pointer.");
        if ((cursor.Flags & NativeMethods.CursorShowing) == 0 || cursor.Cursor == 0)
            return new CapturedPointer(0, cursor.ScreenPosition.X, cursor.ScreenPosition.Y, 0, 0);

        var icon = NativeMethods.CopyIcon(cursor.Cursor);
        if (icon == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to copy the pointer.");
        try
        {
            if (!NativeMethods.GetIconInfo(icon, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read the pointer hotspot.");
            try
            {
                return new CapturedPointer(icon, cursor.ScreenPosition.X, cursor.ScreenPosition.Y,
                    checked((int)info.HotspotX), checked((int)info.HotspotY));
            }
            finally
            {
                if (info.MaskBitmap != 0)
                    NativeMethods.DeleteObject(info.MaskBitmap);
                if (info.ColorBitmap != 0)
                    NativeMethods.DeleteObject(info.ColorBitmap);
            }
        }
        catch
        {
            NativeMethods.DestroyIcon(icon);
            throw;
        }
    }

    /// <summary>
    /// Composites only the saved pointer onto a copy of the supplied pixels. Bounds must describe
    /// the source image in physical desktop pixels; no desktop or current cursor is read.
    /// </summary>
    public static BitmapSource ApplyPointer(BitmapSource source, CaptureRect bounds, CapturedPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pointer);
        ValidateBounds(bounds);
        if (source.PixelWidth != bounds.Width || source.PixelHeight != bounds.Height)
            throw new ArgumentException("The image dimensions must exactly match the physical capture bounds.", nameof(source));

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bounds.Width, bounds.Height), ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var pixels = source.Format == PixelFormats.Bgr32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            pixels.CopyPixels(new System.Windows.Int32Rect(0, 0, bounds.Width, bounds.Height), data.Scan0,
                checked(data.Stride * bounds.Height), data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        using (var graphics = Graphics.FromImage(bitmap))
            DrawPointer(graphics, bounds, pointer);
        return ToFrozenSnapshot(bitmap);
    }

    /// <summary>Returns an immutable snapshot which can be used by any WPF dispatcher.</summary>
    public static BitmapSource Capture(CaptureRect bounds, bool includePointer = false)
    {
        using var bitmap = CaptureBitmap(bounds, includePointer);
        return ToFrozenSnapshot(bitmap);
    }

    /// <summary>
    /// Requests an unoccluded window image, falling back to its visible desktop rectangle if the
    /// application cannot render it. Call on a worker thread: PrintWindow is a synchronous API.
    /// Protected content and some GPU-rendered applications may be black or incomplete.
    /// </summary>
    public static BitmapSource CaptureWindow(WindowInfo window, bool includePointer = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        return TryCaptureWindow(window, out var snapshot, includePointer)
            ? snapshot
            : Capture(window.Bounds, includePointer);
    }

    /// <summary>
    /// Returns false when a window disappeared, is unresponsive, cannot print, or produced a uniform
    /// placeholder. A successful result does not guarantee that a third-party app rendered every
    /// GPU surface. PrintWindow can still block if its owner hangs during this request; use a worker.
    /// </summary>
    public static bool TryCaptureWindow(WindowInfo window, [NotNullWhen(true)] out BitmapSource? snapshot,
        bool includePointer = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        ValidateBounds(window.Bounds);
        snapshot = null;
        if (NativeMethods.IsIconic(window.Handle) || NativeMethods.IsHungAppWindow(window.Handle) ||
            !NativeMethods.GetWindowRect(window.Handle, out var nativeBounds))
            return false;

        var fullBounds = FromNative(nativeBounds);
        if (fullBounds.Width <= 0 || fullBounds.Height <= 0)
            return false;
        ValidateBounds(fullBounds);

        // PrintWindow draws relative to GetWindowRect, which includes invisible resize borders.
        // Crop those away to preserve the exact DWM frame used in the selection overlay.
        var crop = new Rectangle(window.Bounds.X - fullBounds.X, window.Bounds.Y - fullBounds.Y,
            window.Bounds.Width, window.Bounds.Height);
        if (crop.X < 0 || crop.Y < 0 || crop.Right > fullBounds.Width || crop.Bottom > fullBounds.Height)
            return false;

        using var fullBitmap = new Bitmap(fullBounds.Width, fullBounds.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(fullBitmap))
        {
            // A success return can mean that an app ignored WM_PRINT. A solid sentinel lets
            // the pixel check reject that result instead of showing an empty overview tile.
            graphics.Clear(System.Drawing.Color.Magenta);
            var deviceContext = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(window.Handle, deviceContext, NativeMethods.PwRenderFullContent))
                    return false;
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }

        using var cropped = fullBitmap.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        if (!HasRenderablePixels(cropped))
            return false;
        if (includePointer)
        {
            using var graphics = Graphics.FromImage(cropped);
            DrawPointer(graphics, window.Bounds);
        }

        snapshot = ToFrozenSnapshot(cropped);
        return true;
    }

    private static BitmapSource ToFrozenSnapshot(Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            // Bgr32 deliberately ignores GDI's unused alpha byte. Treating it as Bgra32 can
            // turn an otherwise valid desktop capture transparent when saving it as PNG.
            var snapshot = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32,
                null, bitmapData.Scan0, checked(bitmapData.Stride * bitmap.Height), bitmapData.Stride);
            snapshot.Freeze();
            return snapshot;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static bool HasRenderablePixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var firstPixel = Marshal.ReadInt32(data.Scan0) & 0x00ffffff;
            var row = new int[bitmap.Width];
            var hasVariation = false;
            long untouchedPixels = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(nint.Add(data.Scan0, checked(y * data.Stride)), row, 0, row.Length);
                foreach (var pixel in row)
                {
                    var color = pixel & 0x00ffffff;
                    hasVariation |= color != firstPixel;
                    if (color == 0x00ff00ff)
                        untouchedPixels++;
                }
            }
            // Some apps print their title bar while leaving the client area untouched. Reject
            // substantial sentinel remains too. A genuinely magenta app uses the desktop fallback.
            return hasVariation && untouchedPixels <= ((long)bitmap.Width * bitmap.Height) / 1000;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Returns an owned GDI bitmap. The caller must dispose it after use.</summary>
    public static Bitmap CaptureBitmap(CaptureRect bounds, bool includePointer = false)
    {
        ValidateBounds(bounds);
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            CopyDesktopPixels(graphics, bounds);
            if (includePointer)
                DrawPointer(graphics, bounds);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void CopyDesktopPixels(Graphics graphics, CaptureRect bounds)
    {
        // Equivalent to Graphics.CopyFromScreen, with CAPTUREBLT to include layered windows.
        // .NET 8 rejects combined CopyPixelOperation flags, so use its underlying GDI API.
        var screenContext = NativeMethods.GetDC(0);
        if (screenContext == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to access the desktop.");
        try
        {
            var destinationContext = graphics.GetHdc();
            try
            {
                if (!NativeMethods.BitBlt(destinationContext, 0, 0, bounds.Width, bounds.Height,
                    screenContext, bounds.X, bounds.Y, NativeMethods.SourceCopyCaptureBlt))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to capture desktop pixels.");
            }
            finally
            {
                graphics.ReleaseHdc(destinationContext);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(0, screenContext);
        }
    }

    public static void SavePng(BitmapSource snapshot, string path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(snapshot));
        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void DrawPointer(Graphics graphics, CaptureRect bounds)
    {
        using var pointer = CapturePointer();
        DrawPointer(graphics, bounds, pointer);
    }

    private static void DrawPointer(Graphics graphics, CaptureRect bounds, CapturedPointer pointer)
    {
        var referenceAdded = false;
        try
        {
            var icon = pointer.AcquireIcon(ref referenceAdded);
            if (icon == 0)
                return;
            var deviceContext = graphics.GetHdc();
            try
            {
                var x = checked(pointer.X - bounds.X - pointer.HotspotX);
                var y = checked(pointer.Y - bounds.Y - pointer.HotspotY);
                if (!NativeMethods.DrawIconEx(deviceContext, x, y, icon, 0, 0, 0, 0, NativeMethods.DiNormal))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to draw the pointer.");
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }
        finally
        {
            if (referenceAdded)
                pointer.ReleaseIcon();
        }
    }

    private static CaptureRect FromNative(NativeMethods.Rect bounds) =>
        new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);

    private static bool Intersects(CaptureRect first, CaptureRect second) =>
        first.X < second.Right && first.Right > second.X && first.Y < second.Bottom && first.Bottom > second.Y;

    private static void ValidateBounds(CaptureRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture width and height must be greater than zero.");
        _ = bounds.Right;
        _ = bounds.Bottom;
        _ = checked(bounds.Width * 4 * bounds.Height);
    }
}
