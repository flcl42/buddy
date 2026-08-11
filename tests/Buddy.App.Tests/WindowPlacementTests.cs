namespace Buddy.App.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void CentersDefaultWindowInsideTheVmWorkArea()
    {
        (int x, int y) = WindowPlacement.CenterWithin(
            areaX: 0,
            areaY: 0,
            areaWidth: 1_600,
            areaHeight: 952,
            windowWidth: 1_260,
            windowHeight: 830);

        Assert.Equal(170, x);
        Assert.Equal(61, y);
    }

    [Fact]
    public void KeepsAnOversizedWindowAnchoredToTheWorkAreaOrigin()
    {
        (int x, int y) = WindowPlacement.CenterWithin(
            areaX: 1_920,
            areaY: 40,
            areaWidth: 1_024,
            areaHeight: 700,
            windowWidth: 1_260,
            windowHeight: 830);

        Assert.Equal(1_920, x);
        Assert.Equal(40, y);
    }
}
