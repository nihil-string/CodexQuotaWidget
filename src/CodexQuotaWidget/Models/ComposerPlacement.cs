namespace CodexQuotaWidget.Models;

internal readonly record struct ScreenRectangle(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double CenterX => Left + Width / 2;
    public double CenterY => Top + Height / 2;

    public bool IsFinitePositive =>
        double.IsFinite(Left) &&
        double.IsFinite(Top) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height) &&
        Width > 0 &&
        Height > 0;
}

internal static class ComposerPlacement
{
    private const double HorizontalInset = 8;
    private const double MinimumWidth = 64;
    private const double MaximumAnchorCenterDifference = 12;
    private const double VerticalOffset = 2;

    public static bool TryCreate(
        ScreenRectangle permissions,
        ScreenRectangle model,
        double desiredWidth,
        double desiredHeight,
        out ScreenRectangle placement)
    {
        placement = default;
        if (!permissions.IsFinitePositive ||
            !model.IsFinitePositive ||
            !double.IsFinite(desiredWidth) || desiredWidth <= 0 ||
            !double.IsFinite(desiredHeight) || desiredHeight <= 0 ||
            model.Left <= permissions.Right ||
            Math.Abs(model.CenterY - permissions.CenterY) > MaximumAnchorCenterDifference)
        {
            return false;
        }

        var availableLeft = permissions.Right + HorizontalInset;
        var availableRight = model.Left - HorizontalInset;
        var availableWidth = availableRight - availableLeft;
        if (availableWidth < MinimumWidth)
        {
            return false;
        }

        var width = Math.Min(desiredWidth, availableWidth);
        var centerY = (permissions.CenterY + model.CenterY) / 2;
        placement = new ScreenRectangle(
            availableLeft + (availableWidth - width) / 2,
            centerY - desiredHeight / 2 + VerticalOffset,
            width,
            desiredHeight);
        return true;
    }
}
