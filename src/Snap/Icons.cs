using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Snap;

internal static class Icons
{
    internal static FrameworkElement Make(string name, double size = 24)
    {
        string data = name switch
        {
            "selection" => "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M8,3 H11 M14,3 H16 M21,10 V13 M21,14 V16 M14,21 H11 M10,21 H8 M3,14 V11",
            "screen" => "M3,4 H21 V17 H3 Z M8,21 H16 M12,17 V21",
            "window" => "M3,3 H21 V21 H3 Z M3,8 H21 M6,5.5 H6.2 M9,5.5 H9.2",
            "camera" => "M3,7 H7 L9,4 H15 L17,7 H21 V20 H3 Z M16,13 A4,4 0 1 1 8,13 A4,4 0 1 1 16,13",
            "video" => "M3,6 H16 V19 H3 Z M16,10 L22,6 V19 L16,15",
            "pointer" => "M5,3 L5,20 L10,15 L14,22 L17,20 L13,13 L20,13 Z",
            "close" => "M6,6 L18,18 M18,6 L6,18",
            "folder" => "M3,6 H10 L12,9 H21 V20 H3 Z",
            "check" => "M5,12 L10,17 L20,6",
            _ => "M12,3 V15 M12,20 V21"
        };
        return new Viewbox { Width = size, Height = size, Child = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), Stroke = Brushes.White, StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Width = 24, Height = 24, Stretch = Stretch.None
        }};
    }

    internal static Button Button(object content, string accessibleName, double width, double height)
    {
        var b = new Button { Content = content, Width = width, Height = height,
            Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Arrow,
            ToolTip = accessibleName };
        System.Windows.Automation.AutomationProperties.SetName(b, accessibleName);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        b.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        b.MouseEnter += (_, _) => { if (!Equals(b.Tag, "selected")) b.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)); };
        b.MouseLeave += (_, _) => { if (!Equals(b.Tag, "selected")) b.Background = Brushes.Transparent; };
        b.GotKeyboardFocus += (_, _) => b.Opacity = .75;
        b.LostKeyboardFocus += (_, _) => b.Opacity = 1;
        return b;
    }
}
