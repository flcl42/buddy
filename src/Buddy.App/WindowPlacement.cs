namespace Buddy.App;

public static class WindowPlacement
{
    public static (int X, int Y) CenterWithin(
        int areaX,
        int areaY,
        int areaWidth,
        int areaHeight,
        int windowWidth,
        int windowHeight) =>
        (
            areaX + Math.Max(0, (areaWidth - windowWidth) / 2),
            areaY + Math.Max(0, (areaHeight - windowHeight) / 2)
        );
}
