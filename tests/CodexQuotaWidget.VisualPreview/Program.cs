using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaWidget;
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
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: CodexQuotaWidget.VisualPreview <output.png>");
            return 2;
        }

        var outputPath = System.IO.Path.GetFullPath(args[0]);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath)!);

        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var backdrop = CreateBackdropWindow();
        backdrop.Show();

        var window = new MainWindow
        {
            Left = 160,
            Top = 120,
            Topmost = true
        };
        window.Show();
        window.Activate();

        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);

        window.UpdateLayout();
        var topLeft = window.PointToScreen(new System.Windows.Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));

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

    private static Window CreateBackdropWindow()
    {
        var canvas = new Canvas { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 39, 48)) };
        canvas.Children.Add(new WpfRectangle
        {
            Width = 360,
            Height = 420,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 60, 72))
        });
        var block = new WpfRectangle
        {
            Width = 260,
            Height = 210,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 58, 63))
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
