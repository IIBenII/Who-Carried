# Run Recap Decks — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add each player's deck to Run Recap: real game cards on a new Decks tab, and a compact list in the exported PNG.

**Architecture:**
- A pure `DeckBuilder` groups cards and attaches damage, and it's unit-tested.
- `GameReader` reads decks from the game.
- `CardVisuals` draws cards with the game's pooled `NCard` and returns them to the pool.
- `RecapPanel` and `SummaryCard` gain deck sections.

**Tech Stack:** unchanged (C# / .NET 9, GodotSharp, game DLLs, no NuGet).

**Spec:** `docs/design/specs/2026-09-10-run-recap-decks-design.md`

## Global Constraints

- Everything from the earlier plans still applies: read-only, no NuGet, the x64 `dotnet` by full path, try/catch in every game callback, **no git**.
- **Game is running while Tasks 1–4 are done:** build and test only. Deploying and the preview (Task 5) wait until the game is closed.
- Pooled `NCard`s must always go back through `QueueFreeSafely()` with their original `MouseFilter` restored. Never change their anchors.
- Card scale is 0.44 (slot = `NCard.defaultSize × 0.44`), and the grid has 6 columns.

Code blocks introduced by a backticked path plus a colon are **complete file contents**. The "Insert" and "Replace" steps are exact edits.

---

### Task 1: DeckBuilder (Core, TDD)

**Files:**
- Create: `src/RunRecap/Core/DeckBuilder.cs`, `tests/RunRecap.Tests/DeckBuilderTests.cs`
- Modify: `src/RunRecap/Core/RecapBuilder.cs`

**Interfaces:**
- Produces:
  - `record DeckCard(string Id, string Label, string Type, string Rarity, int UpgradeLevel)`
  - `record DeckEntry(string Id, string Label, string Type, string Rarity, int Count, int UpgradedCount, int Damage)`
  - `record DeckView(ulong PlayerId, string PlayerLabel, string ColorHex, string? IconKey, int CardCount, IReadOnlyList<DeckEntry> Entries)`
  - `DeckBuilder.Build(RunStats, IEnumerable<PlayerInfo>, IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>?) → IReadOnlyList<DeckView>`
  - `DeckBuilder.TypeOrder(string type) → int`
  - `RecapView` gains a last positional member, `IReadOnlyList<DeckView> Decks`
  - `RecapBuilder.Build(..., bool? victory = null, IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks = null)`

- [ ] **Step 1: Failing tests**

`tests/RunRecap.Tests/DeckBuilderTests.cs`:

```csharp
using RunRecap.Core;

namespace RunRecap.Tests;

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
```

- [ ] **Step 2: Run the tests, expecting a compile failure** (`DeckCard` / `DeckBuilder` not found)

Run: `dotnet build tests\RunRecap.Tests --nologo -v q`

- [ ] **Step 3: Implement**

`src/RunRecap/Core/DeckBuilder.cs`:

```csharp
namespace RunRecap.Core;

/// <summary>One card in a player's deck, as plain facts.</summary>
public sealed record DeckCard(string Id, string Label, string Type, string Rarity, int UpgradeLevel);

/// <summary>All copies of one card id in a deck, with the damage that card id dealt this run.</summary>
public sealed record DeckEntry(string Id, string Label, string Type, string Rarity, int Count, int UpgradedCount, int Damage);

public sealed record DeckView(ulong PlayerId, string PlayerLabel, string ColorHex, string? IconKey, int CardCount,
                              IReadOnlyList<DeckEntry> Entries);

public static class DeckBuilder
{
    public static IReadOnlyList<DeckView> Build(RunStats stats, IEnumerable<PlayerInfo> players,
                                                IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks)
    {
        return players.Select(p =>
        {
            IReadOnlyList<DeckCard> cards = decks != null && decks.TryGetValue(p.NetId, out IReadOnlyList<DeckCard>? deck)
                ? deck
                : Array.Empty<DeckCard>();
            PlayerTotals? totals = stats.Get(p.NetId);
            List<DeckEntry> entries = cards
                .GroupBy(c => c.Id)
                .Select(g =>
                {
                    DeckCard first = g.First();
                    string key = new SourceRef(SourceKind.Card, g.Key, first.Label).Key;
                    int damage = totals?.Sources.GetValueOrDefault(key)?.Amount ?? 0;
                    return new DeckEntry(g.Key, first.Label, first.Type, first.Rarity, g.Count(),
                        g.Count(c => c.UpgradeLevel > 0), damage);
                })
                .OrderBy(e => TypeOrder(e.Type))
                .ThenByDescending(e => e.Damage)
                .ThenBy(e => e.Label, StringComparer.Ordinal)
                .ToList();
            return new DeckView(p.NetId, $"{p.Name} · {p.Character}", p.ColorHex,
                string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId, cards.Count, entries);
        }).ToList();
    }

    public static int TypeOrder(string type) => type switch { "Attack" => 0, "Skill" => 1, "Power" => 2, _ => 3 };
}
```

In `src/RunRecap/Core/RecapBuilder.cs`:
- Replace `    IReadOnlyList<Highlight> Highlights,\n    bool? Victory);` with `    IReadOnlyList<Highlight> Highlights,\n    bool? Victory,\n    IReadOnlyList<DeckView> Decks);`
- Replace `        string header,\n        bool? victory = null)` with `        string header,\n        bool? victory = null,\n        IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>>? decks = null)`
- Replace `            Highlights(stats, players, team), victory);` with `            Highlights(stats, players, team), victory, DeckBuilder.Build(stats, byDamage, decks));`

- [ ] **Step 4: Run all tests.** Expected: `32/32 passed`.

---

### Task 2: GameReader deck reading

**Files:** Modify `src/RunRecap/Game/GameReader.cs`. Insert the block directly above `/// <summary>Damage taken and HP healed per player`:

```csharp
    public static IReadOnlyList<DeckCard> DeckFacts(IEnumerable<CardModel> cards) =>
        cards.Select(c => new DeckCard(c.Id.Entry, GameText.Title(c.TitleLocString, c.Id.Entry), c.Type.ToString(),
            c.Rarity.ToString(), c.CurrentUpgradeLevel)).ToList();

    public static IReadOnlyDictionary<ulong, IReadOnlyList<DeckCard>> Decks(IRunState run) =>
        run.Players.ToDictionary(p => p.NetId, p => DeckFacts(p.Deck.Cards));

    /// <summary>The most-upgraded copy of a card in a player's deck; that's the copy we draw.</summary>
    public static CardModel? DeckCardModel(IRunState run, ulong playerId, string cardId) =>
        run.Players.FirstOrDefault(p => p.NetId == playerId)?.Deck.Cards
            .Where(c => c.Id.Entry == cardId)
            .OrderByDescending(c => c.CurrentUpgradeLevel)
            .FirstOrDefault();

```

- [ ] Build. Expected: 0 errors.

---

### Task 3: Real cards + Decks tab

**Files:**
- Create: `src/RunRecap/UI/CardVisuals.cs`
- Modify:
  - `src/RunRecap/UI/RecapTheme.cs`: add two colors and `RarityColor`
  - `src/RunRecap/UI/RecapWidgets.cs`: add `DeckSlot`, `Badge`, `DeckList`, `GroupName` above `private static Control KindTag`
- Replace: `src/RunRecap/UI/RecapPanel.cs`, `src/RunRecap/UI/RecapUi.cs`

`src/RunRecap/UI/CardVisuals.cs`:

```csharp
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>
/// Draws deck cards with the game's own pooled card node. Every card taken from the pool is tracked and handed back,
/// with its mouse filter restored, by <see cref="ReleaseAll"/>, so the game's own screens never see a modified card.
/// </summary>
internal sealed class CardVisuals
{
    public const float CardScale = 0.44f;
    public static readonly Vector2 SlotSize = NCard.defaultSize * CardScale;

    private readonly Func<ulong, string, CardModel?> _cardFor;
    private readonly List<(NCard Card, Control.MouseFilterEnum Filter)> _live = new();

    public CardVisuals(Func<ulong, string, CardModel?> cardFor) => _cardFor = cardFor;

    /// <summary>A fixed-size slot. The real card is attached once the slot enters the scene tree.</summary>
    public Control Slot(ulong playerId, DeckEntry entry)
    {
        (Control slot, Control holder) = EmptySlot();
        CardModel? model = null;
        try { model = _cardFor(playerId, entry.Id); }
        catch (Exception e) { Tracker.LogError("deck card lookup", e); }
        if (model == null) holder.AddChild(TextTile(entry));
        else slot.Ready += () => Attach(holder, model, entry);
        return slot;
    }

    /// <summary>A slot with a plain text tile, for when real cards can't be drawn.</summary>
    public static Control TextSlot(DeckEntry entry)
    {
        (Control slot, Control holder) = EmptySlot();
        holder.AddChild(TextTile(entry));
        return slot;
    }

    /// <summary>Returns every drawn card to the game's pool. Call before discarding the slots.</summary>
    public void ReleaseAll()
    {
        foreach ((NCard card, Control.MouseFilterEnum filter) in _live)
        {
            try
            {
                if (!GodotObject.IsInstanceValid(card)) continue;
                card.MouseFilter = filter;
                card.QueueFreeSafely();
            }
            catch (Exception e)
            {
                Tracker.LogError("deck card release", e);
            }
        }
        _live.Clear();
    }

    private void Attach(Control holder, CardModel model, DeckEntry entry)
    {
        try
        {
            NCard? card = NCard.Create(model, ModelVisibility.Visible);
            if (card == null)
            {
                holder.AddChild(TextTile(entry));
                return;
            }
            _live.Add((card, card.MouseFilter));
            card.MouseFilter = Control.MouseFilterEnum.Ignore; // let the mouse wheel reach the scroll container
            holder.AddChild(card);
            card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            card.Scale = new Vector2(CardScale, CardScale);
            card.Position = -card.PivotOffset * (1 - CardScale); // scaled card's top-left at the slot's top-left
        }
        catch (Exception e)
        {
            Tracker.LogError("deck card render", e);
            holder.AddChild(TextTile(entry));
        }
    }

    private static (Control Slot, Control Holder) EmptySlot()
    {
        var slot = new Control { CustomMinimumSize = SlotSize, Size = SlotSize, MouseFilter = Control.MouseFilterEnum.Pass };
        var holder = new Control { Size = SlotSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddChild(holder);
        return (slot, holder);
    }

    private static Control TextTile(DeckEntry entry)
    {
        Color rarity = RecapTheme.RarityColor(entry.Rarity);
        var tile = new PanelContainer { Size = SlotSize, CustomMinimumSize = SlotSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        tile.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.Inset, 10, rarity, 2, 10, 10));
        VBoxContainer column = RecapWidgets.Column(4);
        Label name = RecapTheme.Text(entry.Label, 16, rarity, bold: true);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(name);
        Label type = RecapTheme.Text(entry.Type.ToUpperInvariant(), 12, RecapTheme.Muted, bold: true);
        type.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(type);
        tile.AddChild(column);
        return tile;
    }
}
```

In `src/RunRecap/UI/RecapTheme.cs`:
- Insert after `public static readonly Color Grey = new("8a8a8a");`:

  ```csharp
      public static readonly Color Purple = new("d9a3ff");
      public static readonly Color Teal = new("5dcaa5");
  ```

- Insert before `public static Label Text(`:

  ```csharp
      /// <summary>Card-name colour by rarity, echoing the game's card banners.</summary>
      public static Color RarityColor(string rarity) => rarity switch
      {
          "Basic" => Muted,
          "Common" => Cream,
          "Uncommon" => Blue,
          "Rare" => Gold,
          "Curse" => Purple,
          "Status" => Faint,
          _ => Teal,
      };

  ```

In `src/RunRecap/UI/RecapWidgets.cs`, insert before `    private static Control KindTag(string kind)`:

```csharp
    /// <summary>A deck card (real or text) with a copies badge and a damage badge laid over it.</summary>
    public static Control DeckSlot(DeckView deck, DeckEntry entry, CardVisuals? cards)
    {
        Control slot = cards?.Slot(deck.PlayerId, entry) ?? CardVisuals.TextSlot(entry);
        var overlay = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Size = CardVisuals.SlotSize };
        overlay.AddThemeConstantOverride("separation", 0);

        var top = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (entry.Count > 1 || entry.UpgradedCount > 0)
        {
            string copies = entry.Count > 1 ? $"×{entry.Count}" : "";
            string upgraded = entry.UpgradedCount == 0 ? "" : entry.UpgradedCount == entry.Count ? "+" : $"{entry.UpgradedCount}+";
            top.AddChild(Badge($"{copies} {upgraded}".Trim(), RecapTheme.Cream));
        }
        overlay.AddChild(top);
        overlay.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (entry.Damage > 0) bottom.AddChild(Badge($"{Num(entry.Damage)} dmg", RecapTheme.Gold));
        overlay.AddChild(bottom);

        slot.AddChild(overlay);
        return slot;
    }

    /// <summary>Compact deck list for the exported image: rarity-coloured names grouped by type, with copies and damage.</summary>
    public static Control DeckList(DeckView deck, Func<string?, Texture2D?> icons, float width)
    {
        Color color = RecapTheme.FromHex(deck.ColorHex);
        VBoxContainer column = Column(3);
        column.CustomMinimumSize = new Vector2(width, 0);

        HBoxContainer who = Row(10);
        who.AddChild(Center(RecapTheme.CharacterIcon(icons(deck.IconKey), deck.PlayerLabel, color, 30)));
        Label name = RecapTheme.Text(deck.PlayerLabel, 17, RecapTheme.Cream, bold: true);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        who.AddChild(Center(name));
        who.AddChild(Center(RecapTheme.Text($"{deck.CardCount} cards", 14, RecapTheme.Muted)));
        column.AddChild(who);

        int group = -1;
        foreach (DeckEntry entry in deck.Entries)
        {
            int order = DeckBuilder.TypeOrder(entry.Type);
            if (order != group)
            {
                group = order;
                Label heading = RecapTheme.Text(GroupName(order), 12, RecapTheme.Faint, bold: true);
                heading.CustomMinimumSize = new Vector2(0, 24);
                heading.VerticalAlignment = VerticalAlignment.Bottom;
                column.AddChild(heading);
            }
            HBoxContainer line = Row(6);
            Label label = RecapTheme.Text(entry.Label, 15, RecapTheme.RarityColor(entry.Rarity));
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            if (entry.Count > 1) line.AddChild(RecapTheme.Text($"×{entry.Count}", 14, RecapTheme.Muted));
            if (entry.UpgradedCount > 0)
                line.AddChild(RecapTheme.Text(entry.UpgradedCount == entry.Count ? "+" : $"{entry.UpgradedCount}+",
                    14, RecapTheme.Green, bold: true));
            Label damage = RecapTheme.Text(entry.Damage > 0 ? Num(entry.Damage) : "—", 15,
                entry.Damage > 0 ? RecapTheme.Cream : RecapTheme.Faint, bold: entry.Damage > 0);
            damage.CustomMinimumSize = new Vector2(48, 0);
            damage.HorizontalAlignment = HorizontalAlignment.Right;
            line.AddChild(damage);
            column.AddChild(line);
        }
        if (deck.Entries.Count == 0) column.AddChild(RecapTheme.Text("No deck data.", 15, RecapTheme.Muted));
        return column;
    }

    private static Control Badge(string text, Color color)
    {
        var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel",
            RecapTheme.Box(new Color(RecapTheme.Outline, 0.88f), 8, RecapTheme.PanelBorder, 1, 7, 2));
        badge.AddChild(RecapTheme.Text(text, 14, color, bold: true));
        return badge;
    }

    private static string GroupName(int order) => order switch { 0 => "ATTACKS", 1 => "SKILLS", 2 => "POWERS", _ => "OTHER" };

```

`src/RunRecap/UI/RecapPanel.cs`:

```csharp
using Godot;
using RunRecap.Core;

namespace RunRecap.UI;

/// <summary>What the caller needs to drive an open panel (switch tabs, show a status message).</summary>
internal sealed record PanelHandle(Control Root, TabContainer Tabs, Label Status);

/// <summary>The interactive recap: dimmed backdrop, framed panel, header, and five tabs.</summary>
internal static class RecapPanel
{
    public const string IdleHint = "F8 toggles";
    private const float PanelWidth = 1000f;
    private const float BarWidth = 470f;

    public static PanelHandle Create(RecapView view, Func<string?, Texture2D?> icons, CardVisuals? cards,
                                     Action onClose, Action<PanelHandle> onSave)
    {
        var root = new Control { Name = "RunRecap", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) onClose();
        };
        root.AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        StyleBoxFlat frame = RecapTheme.Box(RecapTheme.PanelBg, 14, RecapTheme.PanelBorder, 2, 28, 22);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 24;
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);

        VBoxContainer column = RecapWidgets.Column(14);
        panel.AddChild(column);

        Label status = RecapTheme.Text(IdleHint, 14, RecapTheme.Faint);
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 440) };
        RecapTheme.StyleTabs(tabs);
        var handle = new PanelHandle(root, tabs, status);

        column.AddChild(Header(view, status, onClose, () => onSave(handle)));
        tabs.AddChild(OverviewTab(view, icons));
        tabs.AddChild(SourcesTab(view, icons));
        tabs.AddChild(TimelineTab(view, icons));
        tabs.AddChild(DefenseTab(view, icons));
        tabs.AddChild(DecksTab(view, icons, cards));
        column.AddChild(tabs);
        return handle;
    }

    private static Control Header(RecapView view, Label status, Action onClose, Action onSave)
    {
        HBoxContainer row = RecapWidgets.Row(16);
        row.AddChild(RecapWidgets.Center(RecapTheme.Text("Run recap", 36, RecapTheme.Gold, bold: true, outline: 8)));
        Color tone = view.Victory switch { true => RecapTheme.Gold, false => RecapTheme.Red, null => RecapTheme.Muted };
        row.AddChild(RecapWidgets.Center(RecapTheme.Text(view.Header, 18, tone)));
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(RecapWidgets.Center(status));
        Button save = RecapTheme.ActionButton("Save image");
        save.Pressed += onSave;
        row.AddChild(RecapWidgets.Center(save));
        Button close = RecapTheme.ActionButton("Close");
        close.Pressed += onClose;
        row.AddChild(RecapWidgets.Center(close));
        return row;
    }

    private static Control OverviewTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(12, "Overview");
        foreach (BarRow row in view.Overview)
            box.AddChild(RecapWidgets.PlayerRow(row, icons(row.IconKey), BarWidth, showShare: true));
        if (view.Overview.All(r => r.Value == 0))
            box.AddChild(RecapTheme.Text("No damage recorded yet.", 17, RecapTheme.Muted));
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        box.AddChild(RecapWidgets.Highlights(view.Highlights));
        return box;
    }

    private static Control SourcesTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(16, "Sources");
        HFlowContainer chips = Chips();
        VBoxContainer rows = RecapWidgets.Column(10);
        var group = new ButtonGroup();

        void ShowPlayer(int index)
        {
            foreach (Node child in rows.GetChildren()) child.QueueFree();
            IReadOnlyList<BarRow> sourceRows = view.Sources[index].Rows;
            foreach (BarRow row in sourceRows) rows.AddChild(RecapWidgets.SourceRow(row, BarWidth, 260));
            if (sourceRows.Count == 0) rows.AddChild(RecapTheme.Text("No damage recorded yet.", 17, RecapTheme.Muted));
        }

        for (int i = 0; i < view.Sources.Count; i++)
        {
            int index = i;
            SourcesView source = view.Sources[i];
            Button chip = RecapTheme.PlayerChip(source.PlayerLabel, icons(source.IconKey),
                RecapTheme.FromHex(source.ColorHex), group);
            chip.ButtonPressed = i == 0;
            chip.Pressed += () => ShowPlayer(index);
            chips.AddChild(chip);
        }

        box.AddChild(chips);
        box.AddChild(rows);
        if (view.Sources.Count > 0) ShowPlayer(0);
        else rows.AddChild(RecapTheme.Text("No damage recorded yet.", 17, RecapTheme.Muted));
        return box;
    }

    private static Control TimelineTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(0, "Timeline");
        box.AddChild(RecapWidgets.Chart(view, icons, 910, 300));
        return box;
    }

    private static Control DefenseTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(0, "Defense");
        box.AddChild(RecapWidgets.DefenseGrid(view.Defense, icons));
        return box;
    }

    private static Control DecksTab(RecapView view, Func<string?, Texture2D?> icons, CardVisuals? cards)
    {
        VBoxContainer box = RecapWidgets.Column(12, "Decks");
        HFlowContainer chips = Chips();
        Label summary = RecapTheme.Text("", 15, RecapTheme.Muted);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 330),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(grid);
        var group = new ButtonGroup();

        void ShowDeck(int index)
        {
            cards?.ReleaseAll();
            foreach (Node child in grid.GetChildren()) child.QueueFree();
            DeckView deck = view.Decks[index];
            summary.Text = $"{deck.CardCount} cards · {deck.Entries.Count} different";
            foreach (DeckEntry entry in deck.Entries) grid.AddChild(RecapWidgets.DeckSlot(deck, entry, cards));
            if (deck.Entries.Count == 0) grid.AddChild(RecapTheme.Text("No deck data.", 17, RecapTheme.Muted));
        }

        for (int i = 0; i < view.Decks.Count; i++)
        {
            int index = i;
            DeckView deck = view.Decks[i];
            Button chip = RecapTheme.PlayerChip(deck.PlayerLabel, icons(deck.IconKey), RecapTheme.FromHex(deck.ColorHex), group);
            chip.ButtonPressed = i == 0;
            chip.Pressed += () => ShowDeck(index);
            chips.AddChild(chip);
        }

        box.AddChild(chips);
        box.AddChild(summary);
        box.AddChild(scroll);
        if (view.Decks.Count > 0) ShowDeck(0);
        else grid.AddChild(RecapTheme.Text("No deck data.", 17, RecapTheme.Muted));
        return box;
    }

    private static HFlowContainer Chips()
    {
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 10);
        chips.AddThemeConstantOverride("v_separation", 8);
        return chips;
    }
}
```

`src/RunRecap/UI/RecapUi.cs`:

```csharp
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>Owns the overlay: F8 polling, the canvas layer, opening on the end-of-run screen, and image export.</summary>
internal static class RecapUi
{
    private static CanvasLayer? _layer;
    private static Control? _panel;
    private static CardVisuals? _cards;
    private static bool _f8WasDown;

    public static void Install()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += OnFrame;
            DevPreview.StartIfFlagged(Tracker.DataDir);
        }
        else
        {
            Log.Warn("[RunRecap] no SceneTree at init; F8 toggle unavailable");
        }
    }

    public static void Toggle()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) Hide();
        else Show();
    }

    /// <summary>Opens the recap for the current (or just-finished) run.</summary>
    public static void Show()
    {
        IRunState? run = Tracker.CurrentRun;
        if (run == null) return;
        RunStats stats = Tracker.Stats;
        bool? victory = stats.Finished ? stats.Victory : null;
        RecapView view = RecapBuilder.Build(stats, GameReader.Players(run), GameReader.Defense(run),
            GameReader.Header(run, victory), victory, GameReader.Decks(run));
        ShowView(view, id => GameReader.CharacterIcon(run, id),
            new CardVisuals((playerId, cardId) => GameReader.DeckCardModel(run, playerId, cardId)));
    }

    public static PanelHandle ShowView(RecapView view, Func<string?, Texture2D?> icons, CardVisuals? cards)
    {
        Hide();
        _cards = cards;
        PanelHandle handle = RecapPanel.Create(view, icons, cards, Hide, h => Export(view, icons, h));
        _panel = handle.Root;
        EnsureLayer().AddChild(handle.Root);
        return handle;
    }

    /// <summary>Closes the panel. Cards go back to the game's pool first, before their slots are freed.</summary>
    public static void Hide()
    {
        _cards?.ReleaseAll();
        _cards = null;
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) _panel.QueueFree();
        _panel = null;
    }

    /// <summary>Called after the victory/defeat screen is ready: open the recap once its banner has animated in.</summary>
    public static void OnGameOverScreen(NGameOverScreen screen)
    {
        screen.TreeExiting += Hide;
        Later.Run(1.5, () =>
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsInsideTree()) Show();
        });
    }

    private static void Export(RecapView view, Func<string?, Texture2D?> icons, PanelHandle handle)
    {
        string result = view.Victory switch { true => "victory", false => "defeat", null => "in-progress" };
        string folder = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures), "Run Recap");
        string path = Path.Combine(folder, $"run-{DateTime.Now:yyyy-MM-dd_HHmm}-{result}.png");
        handle.Status.Text = "Saving...";
        PngExporter.Save(SummaryCard.Create(view, icons), SummaryCard.Width, path, error =>
        {
            Tracker.Note(error == null ? $"exported {path}" : $"export failed: {error}");
            if (GodotObject.IsInstanceValid(handle.Status))
                handle.Status.Text = error == null ? "Saved to Pictures\\Run Recap" : "Couldn't save the image";
        });
    }

    private static void OnFrame()
    {
        try
        {
            bool down = Input.IsKeyPressed(Key.F8);
            if (down && !_f8WasDown) Toggle();
            _f8WasDown = down;
        }
        catch (Exception e)
        {
            Tracker.LogError("F8", e);
        }
    }

    private static CanvasLayer EnsureLayer()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return _layer;
        _layer = new CanvasLayer { Layer = 100, Name = "RunRecapLayer" };
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _layer);
        return _layer;
    }
}
```

- [ ] Build. Expected: errors only in `DevPreview.cs`, which still uses the old `ShowView` signature. Task 4 fixes them.

---

### Task 4: PNG deck lists + dev preview decks

**Files:** Replace `src/RunRecap/UI/SummaryCard.cs` and `src/RunRecap/UI/DevPreview.cs`.

`src/RunRecap/UI/SummaryCard.cs`:

```csharp
using System.Globalization;
using Godot;
using RunRecap.Core;

namespace RunRecap.UI;

/// <summary>A single shareable image of the whole recap: overview, highlights, top sources, timeline, defense, decks.</summary>
internal static class SummaryCard
{
    public const int Width = 1200;
    private const int Pad = 40;
    private const float Inner = Width - 2 * Pad;

    public static Control Create(RecapView view, Func<string?, Texture2D?> icons)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0) };
        card.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.PanelBg, 0, RecapTheme.PanelBorder, 3, Pad, 34));
        VBoxContainer column = RecapWidgets.Column(22);
        card.AddChild(column);

        HBoxContainer header = RecapWidgets.Row(18);
        header.AddChild(RecapWidgets.Center(RecapTheme.Text("Run recap", 46, RecapTheme.Gold, bold: true, outline: 10)));
        Color tone = view.Victory switch { true => RecapTheme.Gold, false => RecapTheme.Red, null => RecapTheme.Muted };
        header.AddChild(RecapWidgets.Center(RecapTheme.Text(view.Header, 21, tone)));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        header.AddChild(RecapWidgets.Center(RecapTheme.Text(
            DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture), 17, RecapTheme.Muted)));
        column.AddChild(header);
        column.AddChild(new ColorRect { Color = RecapTheme.Divider, CustomMinimumSize = new Vector2(0, 1) });

        foreach (BarRow row in view.Overview)
            column.AddChild(RecapWidgets.PlayerRow(row, icons(row.IconKey), 600, showShare: true));
        column.AddChild(RecapWidgets.Highlights(view.Highlights));

        column.AddChild(Section("Top sources"));
        column.AddChild(TopSources(view, icons));

        column.AddChild(Section("Damage per fight"));
        column.AddChild(RecapWidgets.Chart(view, icons, Inner - 26, 260));

        column.AddChild(Section("Defense"));
        column.AddChild(RecapWidgets.DefenseGrid(view.Defense, icons));

        if (view.Decks.Any(d => d.Entries.Count > 0))
        {
            column.AddChild(Section("Decks"));
            column.AddChild(Decks(view, icons));
        }

        Label footer = RecapTheme.Text("Run Recap · Slay the Spire 2", 14, RecapTheme.Faint);
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        column.AddChild(footer);
        return card;
    }

    private static Control TopSources(RecapView view, Func<string?, Texture2D?> icons)
    {
        int columns = Math.Clamp(view.Sources.Count, 1, 4);
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 24);
        float colWidth = (Inner - 28 * (columns - 1)) / columns;
        foreach (SourcesView s in view.Sources)
        {
            Color color = RecapTheme.FromHex(s.ColorHex);
            VBoxContainer col = RecapWidgets.Column(8);
            col.CustomMinimumSize = new Vector2(colWidth, 0);

            HBoxContainer who = RecapWidgets.Row(10);
            who.AddChild(RecapWidgets.Center(RecapTheme.CharacterIcon(icons(s.IconKey), s.PlayerLabel, color, 30)));
            Label name = RecapTheme.Text(s.PlayerLabel, 17, RecapTheme.Cream, bold: true);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            who.AddChild(RecapWidgets.Center(name));
            col.AddChild(who);

            foreach (BarRow row in s.Rows.Where(r => !r.Label.StartsWith(RecapBuilder.OtherPrefix + " (", StringComparison.Ordinal)).Take(3))
            {
                HBoxContainer line = RecapWidgets.Row(8);
                Label label = RecapTheme.Text(row.Label, 16, RecapTheme.Cream);
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                line.AddChild(label);
                line.AddChild(RecapTheme.Text(RecapWidgets.Num(row.Value), 16, RecapTheme.Cream, bold: true));
                col.AddChild(line);
                col.AddChild(RecapWidgets.Bar(row.Fraction, color, colWidth, 6));
            }
            grid.AddChild(col);
        }
        return grid;
    }

    private static Control Decks(RecapView view, Func<string?, Texture2D?> icons)
    {
        int columns = Math.Clamp(view.Decks.Count, 1, 4);
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 24);
        float colWidth = (Inner - 28 * (columns - 1)) / columns;
        foreach (DeckView deck in view.Decks) grid.AddChild(RecapWidgets.DeckList(deck, icons, colWidth));
        return grid;
    }

    private static Control Section(string title) => RecapTheme.Text(title, 24, RecapTheme.Gold, bold: true, outline: 6);
}
```

`src/RunRecap/UI/DevPreview.cs`:

```csharp
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>
/// Developer-only visual check. If data/preview.flag exists, opens the panel with sample data shortly after start-up,
/// saves a screenshot of each tab plus the exported card into data/, then closes. Inert otherwise.
/// </summary>
internal static class DevPreview
{
    private static readonly string[] TabNames = { "overview", "sources", "timeline", "defense", "decks" };

    public static void StartIfFlagged(string dataDir)
    {
        if (!File.Exists(Path.Combine(dataDir, "preview.flag"))) return;
        Later.Run(10, () => Run(dataDir));
    }

    private sealed record Sample(RecapView View, Func<string?, Texture2D?> Icons, Func<ulong, string, CardModel?> CardFor);

    private static void Run(string dataDir)
    {
        Sample sample = BuildSample();
        PanelHandle handle = RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
        CaptureTab(0);

        void CaptureTab(int index)
        {
            if (index >= TabNames.Length)
            {
                PngExporter.Save(SummaryCard.Create(sample.View, sample.Icons), SummaryCard.Width,
                    Path.Combine(dataDir, $"preview-{TabNames.Length + 1}-export.png"), error =>
                    {
                        Tracker.Note(error == null ? "preview done" : $"preview export failed: {error}");
                        RecapUi.Hide();
                    });
                return;
            }
            handle.Tabs.CurrentTab = index;
            Later.Run(0.9, () =>
            {
                Image screen = ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage();
                screen.SavePng(Path.Combine(dataDir, $"preview-{index + 1}-{TabNames[index]}.png"));
                CaptureTab(index + 1);
            });
        }
    }

    private static Sample BuildSample()
    {
        CharacterModel[] characters = ModelDb.AllCharacters.Take(3).ToArray();
        string[] names = { "Ash", "Mika", "Sam" };
        var players = new List<PlayerInfo>();
        var deckModels = new Dictionary<ulong, List<CardModel>>();
        for (int i = 0; i < characters.Length; i++)
        {
            CharacterModel character = characters[i];
            ulong id = (ulong)(i + 1);
            players.Add(new PlayerInfo(id, names[i], GameReader.CharacterName(character),
                GameReader.CharacterHex(character, i), character.Id.Entry));
            deckModels[id] = character.StartingDeck
                .Concat(character.CardPool.AllCards.Where(card => card.Rarity != CardRarity.Basic).Take(10))
                .ToList();
        }

        var stats = new RunStats();
        var rng = new Random(7);
        int floor = 1;
        for (int act = 1; act <= 3; act++)
        {
            for (int fight = 0; fight < 5; fight++, floor += 3)
            {
                stats.BeginFight(act, floor, fight == 4 ? "Boss" : "Hallway fight");
                foreach (PlayerInfo p in players)
                {
                    List<CardModel> attacks = deckModels[p.NetId].Where(card => card.Type == CardType.Attack).ToList();
                    for (int hit = 0; hit < 4 && attacks.Count > 0; hit++)
                    {
                        CardModel card = attacks[rng.Next(attacks.Count)];
                        int amount = rng.Next(8, 30) * act * (fight == 4 ? 2 : 1);
                        stats.RecordDamage(p.NetId, new SourceRef(SourceKind.Card, card.Id.Entry,
                            GameText.Title(card.TitleLocString, card.Id.Entry)), amount);
                    }
                    stats.RecordBlocked(p.NetId, rng.Next(10, 40) * act);
                }
                stats.RecordDamage(players[1].NetId, new SourceRef(SourceKind.Power, "POISON_POWER", "Poison"),
                    rng.Next(10, 40) * act);
                stats.EndFight();
            }
        }

        Dictionary<ulong, DefenseTotals> defense = players.ToDictionary(
            p => p.NetId, _ => new DefenseTotals(rng.Next(150, 400), rng.Next(60, 200)));
        Dictionary<ulong, IReadOnlyList<DeckCard>> decks = deckModels.ToDictionary(
            kv => kv.Key, kv => GameReader.DeckFacts(kv.Value));
        RecapView view = RecapBuilder.Build(stats, players, defense, "Victory · Act 3 · Floor 43", victory: true, decks: decks);

        Func<string?, Texture2D?> icons = id =>
            characters.FirstOrDefault(c => c.Id.Entry == id) is CharacterModel c ? GameReader.CharacterIcon(c) : null;
        Func<ulong, string, CardModel?> cardFor = (playerId, cardId) =>
            deckModels.TryGetValue(playerId, out List<CardModel>? inDeck) ? inDeck.FirstOrDefault(card => card.Id.Entry == cardId) : null;
        return new Sample(view, icons, cardFor);
    }
}
```

- [ ] Build and test. Expected: 0 errors, `32/32 passed`.

---

### Task 5: Install + visual check (only after the game is closed)

- [ ] Deploy (`tools\deploy.ps1`), create `data\preview.flag`, launch the game, and wait for a fresh `data\preview-6-export.png`.
- [ ] Review `preview-5-decks.png` and check: real cards are drawn and not blank, they're scaled into their slots with no overlap or offset, and the ×N / N+ / damage badges are readable. Review `preview-6-export.png` and check the Decks columns.
- [ ] Fix the placement or sizing if needed, then redeploy and re-preview (at most 3 rounds).
- [ ] Move the flag to the Recycle Bin, close the game, and hand over to the owner: F8 → Decks, switch players, scroll, close/reopen the panel, open the game's own deck view (checks pool hygiene), and Save image.
