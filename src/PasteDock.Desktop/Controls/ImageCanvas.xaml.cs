using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PasteDock.Desktop.Controls;

public partial class ImageCanvas : UserControl
{
    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(nameof(Image), typeof(BitmapSource), typeof(ImageCanvas),
        new PropertyMetadata(null, (instance, _) => ((ImageCanvas)instance).UpdateImage()));
    public BitmapSource? Image { get => (BitmapSource?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    private bool _fit = true;
    private Point? _panStart;
    private double _panX, _panY;
    public ImageCanvas() => InitializeComponent();

    private void UpdateImage()
    {
        if (Picture is null) return;
        Picture.Source = Image;
        if (Image is not null) { Picture.Width = Image.PixelWidth; Picture.Height = Image.PixelHeight; }
        _fit = true; Fit();
    }
    private void Fit()
    {
        if (Image is null || Scroller.ActualWidth < 1 || Scroller.ActualHeight < 1) return;
        var scale = Math.Min((Scroller.ActualWidth - 48) / Image.PixelWidth, (Scroller.ActualHeight - 70) / Image.PixelHeight);
        SetScale(Math.Min(1, Math.Max(0.02, scale)));
    }
    private void SetScale(double value)
    {
        Scale.ScaleX = Scale.ScaleY = Math.Clamp(value, 0.02, 10);
        ZoomLabel.Content = _fit ? "Fit" : $"{Scale.ScaleX * 100:0}%";
    }
    private void CanvasSizeChanged(object sender, SizeChangedEventArgs e) { if (_fit) Fit(); }
    private void CanvasLoaded(object sender, RoutedEventArgs e) => Fit();
    private void ZoomOut(object sender, RoutedEventArgs e) { _fit = false; SetScale(Scale.ScaleX / 1.2); }
    private void ZoomIn(object sender, RoutedEventArgs e) { _fit = false; SetScale(Scale.ScaleX * 1.2); }
    private void ToggleFit(object sender, RoutedEventArgs e) { _fit = !_fit; if (_fit) Fit(); else SetScale(1); }
    private void CanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var pointer = e.GetPosition(Scroller);
        var x = (Scroller.HorizontalOffset + pointer.X) / Scale.ScaleX;
        var y = (Scroller.VerticalOffset + pointer.Y) / Scale.ScaleY;
        _fit = false; SetScale(Scale.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(x * Scale.ScaleX - pointer.X);
        Scroller.ScrollToVerticalOffset(y * Scale.ScaleY - pointer.Y);
        e.Handled = true;
    }
    private void CanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Scrollbars retain their normal drag/click behavior instead of starting image panning.
        for (var origin = e.OriginalSource as DependencyObject; origin is not null;)
        {
            if (origin is ScrollBar) return;
            if (ReferenceEquals(origin, Scroller)) break;
            origin = origin is Visual ? VisualTreeHelper.GetParent(origin) : LogicalTreeHelper.GetParent(origin);
        }
        if (Image is null) return;
        if (e.ClickCount == 2) { ToggleFit(sender, e); e.Handled = true; return; }
        _panStart = e.GetPosition(Scroller); _panX = Scroller.HorizontalOffset; _panY = Scroller.VerticalOffset;
        Scroller.CaptureMouse(); Scroller.Cursor = Cursors.Hand; e.Handled = true;
    }
    private void CanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is not Point origin) return;
        var current = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_panX + origin.X - current.X);
        Scroller.ScrollToVerticalOffset(_panY + origin.Y - current.Y);
    }
    private void CanvasMouseUp(object sender, MouseButtonEventArgs e) { _panStart = null; Scroller.ReleaseMouseCapture(); Scroller.Cursor = null; }
    private void CanvasLostCapture(object sender, MouseEventArgs e) { _panStart = null; Scroller.Cursor = null; }
}
