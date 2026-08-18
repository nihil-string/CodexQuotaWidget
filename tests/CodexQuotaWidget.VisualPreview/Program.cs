using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaWidget;
using CodexQuotaWidget.Models;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using WpfRectangle = System.Windows.Shapes.Rectangle;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 3)
        {
            Console.Error.WriteLine(
                "Usage: CodexQuotaWidget.VisualPreview <output.png> [--weekly-only|--five-hour-only] [--menu]");
            return 2;
        }

        var outputPath = System.IO.Path.GetFullPath(args[0]);
        var options = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (options.Any(option => option is not "--menu" and not "--weekly-only" and not "--five-hour-only") ||
            options.Contains("--weekly-only") && options.Contains("--five-hour-only"))
        {
            Console.Error.WriteLine("Invalid preview options.");
            return 2;
        }

        var showMenu = options.Contains("--menu");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath)!);

        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var backdrop = CreateBackdropWindow();
        backdrop.Show();

        var window = new MainWindow(CreatePreviewSnapshot(options))
        {
            Left = 160,
            Top = 120,
            Topmost = true
        };
        window.Show();
        window.Activate();

        if (showMenu && window.Content is FrameworkElement root && root.ContextMenu is { } contextMenu)
        {
            contextMenu.PlacementTarget = root;
            contextMenu.Placement = PlacementMode.RelativePoint;
            contextMenu.HorizontalOffset = 125;
            contextMenu.VerticalOffset = 20;
            contextMenu.IsOpen = true;
        }

        window.UpdateLayout();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);

        var topLeft = window.PointToScreen(new System.Windows.Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = showMenu ? 310 : Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = showMenu ? 320 : Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));

        using var bitmap = new DrawingBitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = DrawingGraphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                new DrawingPoint((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y)),
                DrawingPoint.Empty,
                new DrawingSize(width, height));
        }

        bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine(outputPath);
        Environment.Exit(0);
        return 0;
    }

    private static RateLimitSnapshot CreatePreviewSnapshot(ISet<string> options)
    {
        var fiveHour = options.Contains("--weekly-only")
            ? null
            : new RateLimitWindow(20, 300, new DateTimeOffset(2030, 1, 1, 3, 34, 0, TimeSpan.Zero));
        var weekly = options.Contains("--five-hour-only")
            ? null
            : new RateLimitWindow(55, 10_080, new DateTimeOffset(2030, 1, 5, 16, 45, 0, TimeSpan.Zero));
        return new RateLimitSnapshot(
            fiveHour,
            weekly,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "visual-preview-fixture");
    }

    private static Window CreateBackdropWindow()
    {
        var canvas = new Canvas { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)) };
        canvas.Children.Add(new WpfRectangle
        {
            Width = 360,
            Height = 420,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 247, 247))
        });
        var block = new WpfRectangle
        {
            Width = 260,
            Height = 210,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 244, 244))
        };
        Canvas.SetLeft(block, 270);
        Canvas.SetTop(block, 95);
        canvas.Children.Add(block);

        return new Window
        {
            Width = 700,
            Height = 430,
            Left = 0,
            Top = 0,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.Transparent,
            Content = canvas
        };
    }
}
