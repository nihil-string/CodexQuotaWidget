using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace CodexQuotaWidget.Controls;

public sealed class CircularProgress : FrameworkElement
{
    private static readonly MediaBrush DefaultTrackBrush = CreateBrush(MediaColor.FromArgb(82, 129, 139, 147));
    private static readonly MediaBrush DefaultProgressBrush = CreateBrush(MediaColor.FromRgb(231, 229, 222));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(CircularProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(MediaBrush),
        typeof(CircularProgress),
        new FrameworkPropertyMetadata(DefaultTrackBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush),
        typeof(MediaBrush),
        typeof(CircularProgress),
        new FrameworkPropertyMetadata(DefaultProgressBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(CircularProgress),
        new FrameworkPropertyMetadata(3.2d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public MediaBrush TrackBrush
    {
        get => (MediaBrush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public MediaBrush ProgressBrush
    {
        get => (MediaBrush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var thickness = double.IsFinite(StrokeThickness)
            ? Math.Clamp(StrokeThickness, 0.5, Math.Max(0.5, Math.Min(ActualWidth, ActualHeight)))
            : 3.2;
        var size = Math.Min(ActualWidth, ActualHeight);
        var radius = Math.Max(0, (size - thickness) / 2);
        var center = new WpfPoint(ActualWidth / 2, ActualHeight / 2);
        var trackPen = CreatePen(TrackBrush, thickness);
        var progressPen = CreatePen(ProgressBrush, thickness);

        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        var value = Math.Clamp(Value, 0d, 100d);
        if (value <= 0)
        {
            return;
        }

        if (value >= 99.999)
        {
            drawingContext.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        var start = PointOnCircle(center, radius, -90);
        var end = PointOnCircle(center, radius, -90 + value * 3.6);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(
                end,
                new WpfSize(radius, radius),
                0,
                value > 50,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, progressPen, geometry);
    }

    private static MediaBrush CreateBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static MediaPen CreatePen(MediaBrush brush, double thickness)
    {
        var pen = new MediaPen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
        return pen;
    }

    private static WpfPoint PointOnCircle(WpfPoint center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new WpfPoint(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
