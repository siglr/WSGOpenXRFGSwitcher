using System.Drawing;

namespace Wsg.OpenXR;

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    public Rectangle Fit(Rectangle workingArea, Size minimum) {
        var width = Math.Clamp(Width, Math.Min(minimum.Width, workingArea.Width), workingArea.Width);
        var height = Math.Clamp(Height, Math.Min(minimum.Height, workingArea.Height), workingArea.Height);
        return new Rectangle(Math.Clamp(X, workingArea.Left, workingArea.Right - width),
            Math.Clamp(Y, workingArea.Top, workingArea.Bottom - height), width, height);
    }
}
