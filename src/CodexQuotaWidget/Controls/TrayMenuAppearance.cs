using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace CodexQuotaWidget.Controls;

internal static class TrayMenuAppearance
{
    public static Forms.ToolStripRenderer CreateRenderer() => new Renderer();

    public static void ApplyRoundedRegion(Forms.ToolStrip menu, int radius)
    {
        var bounds = new Rectangle(System.Drawing.Point.Empty, menu.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        menu.Region?.Dispose();
        menu.Region = new Region(path);
    }

    private sealed class Renderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly Color MenuBackground = Color.FromArgb(240, 26, 32, 38);
        private static readonly Color HighlightBackground = Color.FromArgb(49, 66, 75, 83);
        private static readonly Color Border = Color.FromArgb(82, 97, 106, 115);
        private static readonly Color Separator = Color.FromArgb(54, 67, 75, 82);
        private static readonly Color Accent = Color.FromArgb(118, 217, 192);

        public Renderer()
            : base(new Forms.ProfessionalColorTable { UseSystemColors = false })
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(MenuBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(MenuBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(HighlightBackground);
            var bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var path = CreateRoundedRectangle(bounds, 6);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Separator);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 9, y, e.Item.Width - 9, y);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            using var font = new Font("Segoe UI Symbol", 8F, FontStyle.Bold);
            Forms.TextRenderer.DrawText(
                e.Graphics,
                "✓",
                font,
                e.ImageRectangle,
                Accent,
                Forms.TextFormatFlags.HorizontalCenter |
                Forms.TextFormatFlags.VerticalCenter |
                Forms.TextFormatFlags.NoPadding);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled is not false
                ? Color.FromArgb(242, 241, 236)
                : Color.FromArgb(110, 115, 120);
            base.OnRenderArrow(e);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
