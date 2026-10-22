using System;
using System.Windows;
using Snap.Capture;

namespace Snap;

[Flags]
internal enum DragEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16, New = 32 }

internal static class SelectionGeometry
{
    internal static Rect Initial(CaptureRect monitor) => new(monitor.X + monitor.Width * 3.0 / 8,
        monitor.Y + monitor.Height * 3.0 / 8, monitor.Width / 4.0, monitor.Height / 4.0);

    internal static Rect Clamp(Rect value, Rect bounds)
    {
        double w = Math.Min(Math.Max(1, value.Width), bounds.Width);
        double h = Math.Min(Math.Max(1, value.Height), bounds.Height);
        return new Rect(Math.Clamp(value.X, bounds.Left, bounds.Right - w),
            Math.Clamp(value.Y, bounds.Top, bounds.Bottom - h), w, h);
    }

    internal static DragEdge HitTest(Rect rect, Point p, double tolerance)
    {
        var expanded = rect; expanded.Inflate(tolerance, tolerance);
        if (!expanded.Contains(p)) return DragEdge.New;
        DragEdge edge = DragEdge.None;
        if (Math.Abs(p.X - rect.Left) <= tolerance) edge |= DragEdge.Left;
        else if (Math.Abs(p.X - rect.Right) <= tolerance) edge |= DragEdge.Right;
        if (Math.Abs(p.Y - rect.Top) <= tolerance) edge |= DragEdge.Top;
        else if (Math.Abs(p.Y - rect.Bottom) <= tolerance) edge |= DragEdge.Bottom;
        return edge == DragEdge.None ? DragEdge.Move : edge;
    }

    internal static Rect Drag(Rect original, Point anchor, Point current, DragEdge edge, Rect bounds)
    {
        current = new Point(Math.Clamp(current.X, bounds.Left, bounds.Right), Math.Clamp(current.Y, bounds.Top, bounds.Bottom));
        if (edge == DragEdge.Move)
            return Clamp(new Rect(original.X + current.X - anchor.X, original.Y + current.Y - anchor.Y, original.Width, original.Height), bounds);
        double left = edge == DragEdge.New ? anchor.X : original.Left;
        double top = edge == DragEdge.New ? anchor.Y : original.Top;
        double right = edge == DragEdge.New ? current.X : original.Right;
        double bottom = edge == DragEdge.New ? current.Y : original.Bottom;
        if ((edge & DragEdge.Left) != 0) left = current.X;
        if ((edge & DragEdge.Right) != 0) right = current.X;
        if ((edge & DragEdge.Top) != 0) top = current.Y;
        if ((edge & DragEdge.Bottom) != 0) bottom = current.Y;
        return Clamp(new Rect(new Point(Math.Min(left, right), Math.Min(top, bottom)),
            new Point(Math.Max(left, right), Math.Max(top, bottom))), bounds);
    }

    internal static CaptureRect Pixels(Rect rect, CaptureRect desktop)
    {
        int x = Math.Clamp((int)Math.Round(rect.Left), desktop.X, desktop.Right - 1);
        int y = Math.Clamp((int)Math.Round(rect.Top), desktop.Y, desktop.Bottom - 1);
        int right = Math.Clamp((int)Math.Round(rect.Right), x + 1, desktop.Right);
        int bottom = Math.Clamp((int)Math.Round(rect.Bottom), y + 1, desktop.Bottom);
        return new CaptureRect(x, y, right - x, bottom - y);
    }
}
