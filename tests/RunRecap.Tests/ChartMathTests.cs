using RunRecap.Core;

namespace RunRecap.Tests;

public static class ChartMathTests
{
    [Test]
    public static void NiceCeilingRoundsUpToFriendlyAxisValues()
    {
        Check.Equal(1, ChartMath.NiceCeiling(0), "0");
        Check.Equal(1, ChartMath.NiceCeiling(1), "1");
        Check.Equal(5, ChartMath.NiceCeiling(3), "3");
        Check.Equal(10, ChartMath.NiceCeiling(7), "7");
        Check.Equal(100, ChartMath.NiceCeiling(95), "95");
        Check.Equal(200, ChartMath.NiceCeiling(120), "120");
        Check.Equal(2500, ChartMath.NiceCeiling(2400), "2400");
        Check.Equal(5000, ChartMath.NiceCeiling(2600), "2600");
    }

    [Test]
    public static void GridCeilingFitsTightlyAndSplitsIntoFourRoundSteps()
    {
        Check.Equal(4, ChartMath.GridCeiling(0), "0");
        Check.Equal(8, ChartMath.GridCeiling(7), "7");
        Check.Equal(16, ChartMath.GridCeiling(13), "13");
        Check.Equal(100, ChartMath.GridCeiling(95), "95");
        Check.Equal(120, ChartMath.GridCeiling(120), "120");
        Check.Equal(160, ChartMath.GridCeiling(130), "130");
        Check.Equal(1600, ChartMath.GridCeiling(1309), "1309");
        Check.Equal(2400, ChartMath.GridCeiling(2400), "2400");
        Check.Equal(3200, ChartMath.GridCeiling(2600), "2600");
        foreach (int value in new[] { 21, 55, 333, 777, 4321, 98765 })
        {
            int top = ChartMath.GridCeiling(value);
            Check.True(top >= value && top % 4 == 0 && top <= value * 1.34 + 4, $"{value} → {top}");
        }
    }
}
