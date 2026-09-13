using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>Exact ties taking turns when the list changes from split to split (Strength-down on one enemy).</summary>
public static class TieTurnsTests
{
    private static string Show(IReadOnlyList<(string Key, decimal Weight)> parts, int[] shares) =>
        string.Join(",", parts.Select((p, i) => $"{p.Key}:{shares[i]}"));

    [Test]
    public static void EqualPartsTakeTurns()
    {
        var ties = new TieTurnsByKey<string>();
        var parts = new[] { ("A", 3m), ("B", 3m) };
        Check.Equal("A:3,B:2", Show(parts, ties.Split(5, parts)), "the first tie goes to the first seen");
        Check.Equal("A:2,B:3", Show(parts, ties.Split(5, parts)), "then the other");
        Check.Equal("A:3,B:2", Show(parts, ties.Split(5, parts)), "then back");
    }

    [Test]
    public static void SharesLineUpWithTheCallersListInAnyOrder()
    {
        var ties = new TieTurnsByKey<string>();
        Check.Equal("A:1,B:0", Show(new[] { ("A", 1m), ("B", 1m) }, ties.Split(1, new[] { ("A", 1m), ("B", 1m) })), "A first");
        var flipped = new[] { ("B", 1m), ("A", 1m) };
        Check.Equal("B:1,A:0", Show(flipped, ties.Split(1, flipped)), "B's turn, whatever order it's asked in");
    }

    [Test]
    public static void PartsThatComeAndGoKeepTheirPlace()
    {
        var ties = new TieTurnsByKey<string>();
        var ab = new[] { ("A", 1m), ("B", 1m) };
        Check.Equal("A:1,B:0", Show(ab, ties.Split(1, ab)), "A wins; the turn passes to B");
        var cb = new[] { ("C", 1m), ("B", 1m) };
        Check.Equal("C:0,B:1", Show(cb, ties.Split(1, cb)), "B's turn though C is new; the turn passes to C");
        var abc = new[] { ("A", 1m), ("B", 1m), ("C", 1m) };
        Check.Equal("A:0,B:0,C:1", Show(abc, ties.Split(1, abc)), "C's turn");
        Check.Equal("A:1,B:0,C:0", Show(abc, ties.Split(1, abc)), "round to A");
    }

    [Test]
    public static void WithoutTiesItMatchesThePlainSplit()
    {
        var ties = new TieTurnsByKey<string>();
        var parts = new[] { ("A", 2m), ("B", 1.5m), ("C", 0.5m) };
        int[] plain = DebuffBonus.SplitIndexed(7, parts.Select(p => p.Item2).ToList());
        Check.Equal(string.Join(",", plain), string.Join(",", ties.Split(7, parts)), "3.5, 2.625, 0.875");
    }

    [Test]
    public static void ARepeatedKeyIsCountedOnce()
    {
        // Callers shouldn't pass the same key twice; if one does, its weights add up and the first entry gets the points.
        var ties = new TieTurnsByKey<string>();
        var parts = new[] { ("A", 1m), ("B", 2m), ("A", 1m) };
        int[] shares = ties.Split(8, parts);
        Check.Equal("A:4,B:4,A:0", Show(parts, shares), "A's two entries count as one of 2");
        Check.Equal(8, shares.Sum(), "adds up");
    }

    [Test]
    public static void NothingToShare()
    {
        var ties = new TieTurnsByKey<string>();
        Check.Equal(0, ties.Split(5, Array.Empty<(string, decimal)>()).Length, "no parts");
        Check.Equal("A:0", Show(new[] { ("A", 1m) }, ties.Split(0, new[] { ("A", 1m) })), "no points");
    }
}
