using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class DeckBuilderTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30", "IRONCLAD");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");

    private static DeckCard C(string id, string type = "Attack", int upgrade = 0, string rarity = "Common") =>
        new(id, id.ToLowerInvariant(), type, rarity, upgrade);

    private static Dictionary<ulong, IReadOnlyList<DeckCard>> AliceDeck(params DeckCard[] cards) => new() { [1] = cards };

    [Test]
    public static void DuplicatesAreGroupedWithUpgradeCounts()
    {
        DeckView v = DeckBuilder.Build(new RunStats(), new[] { Alice },
            AliceDeck(C("STRIKE"), C("STRIKE"), C("STRIKE", upgrade: 1), C("BASH")))[0];
        Check.Equal(4, v.CardCount, "card count");
        DeckEntry strike = v.Entries.Single(e => e.Id == "STRIKE");
        Check.Equal(3, strike.Count, "strike copies");
        Check.Equal(1, strike.UpgradedCount, "upgraded strikes");
        Check.Equal(1, v.Entries.Single(e => e.Id == "BASH").Count, "bash copies");
    }

    [Test]
    public static void DamageComesFromTheCardsSourceTotal()
    {
        var s = new RunStats();
        s.RecordDamage(1, new SourceRef(SourceKind.Card, "STRIKE", "Strike"), 30);
        s.RecordDamage(1, new SourceRef(SourceKind.Power, "STRIKE", "not a card"), 99);
        DeckView v = DeckBuilder.Build(s, new[] { Alice }, AliceDeck(C("STRIKE"), C("BASH")))[0];
        Check.Equal(30, v.Entries.Single(e => e.Id == "STRIKE").Damage, "strike damage");
        Check.Equal(0, v.Entries.Single(e => e.Id == "BASH").Damage, "bash damage");
    }

    [Test]
    public static void SortedByTypeThenDamageThenName()
    {
        var s = new RunStats();
        s.RecordDamage(1, new SourceRef(SourceKind.Card, "HEAVY", "heavy"), 50);
        s.RecordDamage(1, new SourceRef(SourceKind.Card, "BASH", "bash"), 10);
        DeckView v = DeckBuilder.Build(s, new[] { Alice }, AliceDeck(
            C("CURSED", "Curse"), C("DEFEND", "Skill"), C("BASH"), C("INFLAME", "Power"), C("ANGER"), C("HEAVY")))[0];
        Check.Equal("HEAVY,BASH,ANGER,DEFEND,INFLAME,CURSED", string.Join(",", v.Entries.Select(e => e.Id)), "order");
    }

    [Test]
    public static void PlayersWithoutDeckDataGetAnEmptyView()
    {
        IReadOnlyList<DeckView> views = DeckBuilder.Build(new RunStats(), new[] { Alice, Bob }, AliceDeck(C("STRIKE")));
        Check.Equal(2, views.Count, "a view per player");
        Check.Equal(0, views[1].Entries.Count, "bob has no entries");
        Check.Equal(0, views[1].CardCount, "bob has no cards");
        Check.True(views[1].IconKey == null, "bob has no icon key");
        Check.Equal("IRONCLAD", views[0].IconKey, "alice icon key");
        Check.Equal("Alice · Ironclad", views[0].PlayerLabel, "label");
        Check.Equal(1UL, views[0].PlayerId, "player id");
    }

    [Test]
    public static void RecapViewCarriesDecksInDamageOrder()
    {
        var s = new RunStats();
        s.RecordDamage(2, new SourceRef(SourceKind.Card, "X", "x"), 10);
        var decks = new Dictionary<ulong, IReadOnlyList<DeckCard>> { [1] = new[] { C("A") }, [2] = new[] { C("X") } };
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, new Dictionary<ulong, DefenseTotals>(), "h", decks: decks);
        Check.Equal(2UL, v.Decks[0].PlayerId, "bob (more damage) first, like Sources");
        Check.Equal(1UL, v.Decks[1].PlayerId, "alice second");
        Check.Equal(10, v.Decks[0].Entries[0].Damage, "damage attached");
    }
}
