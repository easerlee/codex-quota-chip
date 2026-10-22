namespace CodexQuotaChip;

public readonly record struct WindowPlacement(int X, int Y, int Width, int Height);

// Where the overlay sits on a horizontal taskbar. Pure arithmetic, so the two display sides can
// be tested without touching the real taskbar.
public static class Placement
{
    public const uint EdgeLeft = 0;
    public const uint EdgeTop = 1;
    public const uint EdgeRight = 2;
    public const uint EdgeBottom = 3;

    // Gap between the overlay and whatever it is parked next to.
    public const int Gap = 8;

    // How far in front of the tray a "pill" can sit and still belong to it. Anything further away
    // is the other end of the bar - the Start button, say - not something in front of the tray.
    public const int PillReach = 400;

    // Windows 11 draws its widget (weather) pill in front of the tray on the taskbar's own XAML
    // layer, so it has no window to measure. UIAutomation still reports it as a button, and it is
    // the non-task-list button nearest the tray. Zero means none found.
    public static int PillLeftEdge(int trayLeft, IReadOnlyList<(int Left, int Right, bool TaskList)> buttons)
    {
        var picked = 0;
        var nearest = int.MinValue;
        foreach (var button in buttons)
        {
            if (button.TaskList || button.Left <= 0 || button.Right > trayLeft || trayLeft - button.Right > PillReach)
            {
                continue;
            }

            if (button.Right > nearest)
            {
                nearest = button.Right;
                picked = button.Left;
            }
        }

        return picked;
    }

    public static WindowPlacement Compute(
        uint edge,
        Win32.Rect taskbar,
        int preferredWidth,
        int fallbackHeight,
        int screenWidth,
        int screenHeight,
        int trayLeft,
        bool pinLeft = false)
    {
        if (edge is EdgeTop or EdgeBottom && !taskbar.IsEmpty)
        {
            var width = Math.Min(Math.Max(1, preferredWidth), taskbar.Width);

            // Right (the default) sits immediately left of the notification area, so a
            // left-aligned taskbar keeps its Start button clear. Left is the taskbar's own far
            // end, which is the empty one on a Windows 11 centre-aligned taskbar. If the tray is
            // unknown, or there is no room beside it, the taskbar's left edge is the fallback.
            var x = pinLeft
                ? taskbar.Left + Gap
                : trayLeft > taskbar.Left + width + Gap
                    ? trayLeft - width - Gap
                    : taskbar.Left;

            return Clamp(new WindowPlacement(x, taskbar.Top, width, taskbar.Height), screenWidth, screenHeight);
        }

        // A vertical taskbar leaves no sensible strip: fall back to the bottom-left corner.
        return Fallback(preferredWidth, fallbackHeight, screenWidth, screenHeight);
    }

    public static WindowPlacement Fallback(int preferredWidth, int height, int screenWidth, int screenHeight) =>
        Clamp(new WindowPlacement(0, Math.Max(0, screenHeight - height), preferredWidth, height), screenWidth, screenHeight);

    public static uint InferEdge(Win32.Rect taskbar, int screenWidth, int screenHeight)
    {
        if (taskbar.Width >= taskbar.Height)
        {
            return taskbar.Top + taskbar.Height / 2 < screenHeight / 2 ? EdgeTop : EdgeBottom;
        }

        return taskbar.Left + taskbar.Width / 2 < screenWidth / 2 ? EdgeLeft : EdgeRight;
    }

    private static WindowPlacement Clamp(WindowPlacement placement, int screenWidth, int screenHeight)
    {
        var width = Math.Clamp(placement.Width, 1, Math.Max(1, screenWidth));
        var height = Math.Clamp(placement.Height, 1, Math.Max(1, screenHeight));
        return new WindowPlacement(
            Math.Clamp(placement.X, 0, Math.Max(0, screenWidth - width)),
            Math.Clamp(placement.Y, 0, Math.Max(0, screenHeight - height)),
            width,
            height);
    }
}
