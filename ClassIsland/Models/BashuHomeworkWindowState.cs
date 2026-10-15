using System;

namespace ClassIsland.Models;

public sealed class BashuHomeworkWindowState
{
    public double WindowScale { get; set; } = 1;
    public double CompactWidth { get; set; } = 520;
    public string? ScreenName { get; set; }
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public bool HasPosition { get; set; }
    public double RightInset { get; set; } = 16;
    public double TopInset { get; set; } = 16;

    public void Normalize()
    {
        WindowScale = double.IsFinite(WindowScale) ? Math.Clamp(WindowScale, 0.75, 1.5) : 1;
        CompactWidth = double.IsFinite(CompactWidth) ? Math.Clamp(CompactWidth, 320, 960) : 520;
        RightInset = double.IsFinite(RightInset) ? Math.Max(0, RightInset) : 16;
        TopInset = double.IsFinite(TopInset) ? Math.Max(0, TopInset) : 16;
    }

    public (double Width, double Height) GetSize(bool expanded, double availableWidth, double availableHeight)
    {
        Normalize();
        // Keep the compact viewport independent of text scaling. Width has its own control.
        var width = expanded ? 440 * WindowScale : CompactWidth;
        var height = (expanded ? 640 : 62) * WindowScale;
        return (Math.Max(1, Math.Min(width, availableWidth - 24)),
            Math.Max(1, Math.Min(height, availableHeight - 24)));
    }

    public (double Left, double Top) GetPosition(double width, double height, double availableWidth, double availableHeight)
    {
        Normalize();
        return (Math.Clamp(availableWidth - width - RightInset, 0, Math.Max(0, availableWidth - width)),
            Math.Clamp(TopInset, 0, Math.Max(0, availableHeight - height)));
    }
}
