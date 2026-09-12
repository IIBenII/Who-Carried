using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Draws deck cards with the game's own pooled card node. Every card taken from the pool is tracked and handed back,
/// with its mouse filter restored, by <see cref="ReleaseAll"/>, so the game's own screens never see a modified card.
/// </summary>
internal sealed class CardVisuals
{
    private readonly Func<ulong, string, CardModel?> _cardFor;
    private readonly List<(NCard Card, Control.MouseFilterEnum Filter)> _live = new();

    public CardVisuals(Func<ulong, string, CardModel?> cardFor) => _cardFor = cardFor;

    /// <summary>The size of the game's card node at scale 1, in screen pixels.</summary>
    public static Vector2 CardSize => NCard.defaultSize;

    /// <summary>A fixed-size slot of <paramref name="scale"/> × the game's card. The real card is attached once the slot enters the scene tree.</summary>
    public Control Slot(ulong playerId, DeckEntry entry, float scale)
    {
        (Control slot, Control holder) = EmptySlot(scale);
        CardModel? model = null;
        try { model = _cardFor(playerId, entry.Id); }
        catch (Exception e) { Tracker.LogError("deck card lookup", e); }
        if (model == null) holder.AddChild(TextTile(entry, scale));
        else slot.Ready += () => Attach(holder, model, entry, scale);
        return slot;
    }

    /// <summary>A slot with a plain text tile, for when real cards can't be drawn.</summary>
    public static Control TextSlot(DeckEntry entry, float scale)
    {
        (Control slot, Control holder) = EmptySlot(scale);
        holder.AddChild(TextTile(entry, scale));
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

    private void Attach(Control holder, CardModel model, DeckEntry entry, float scale)
    {
        try
        {
            NCard? card = NCard.Create(model, ModelVisibility.Visible);
            if (card == null)
            {
                holder.AddChild(TextTile(entry, scale));
                return;
            }
            _live.Add((card, card.MouseFilter));
            card.MouseFilter = Control.MouseFilterEnum.Ignore; // let the mouse wheel reach the scroll container
            holder.AddChild(card);
            card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            card.Scale = new Vector2(scale, scale);
            // The game's card node draws centred on its own origin (its grids place cards at cell centres), so
            // put the origin at the slot's centre.
            card.Position = CardSize * scale / 2;
        }
        catch (Exception e)
        {
            Tracker.LogError("deck card render", e);
            holder.AddChild(TextTile(entry, scale));
        }
    }

    private static (Control Slot, Control Holder) EmptySlot(float scale)
    {
        Vector2 size = CardSize * scale;
        var slot = new Control { CustomMinimumSize = size, Size = size, MouseFilter = Control.MouseFilterEnum.Pass };
        var holder = new Control { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddChild(holder);
        return (slot, holder);
    }

    /// <summary>A card the game can't draw: its name in its rarity's colour and its type, on a card-shaped tile.</summary>
    private static Control TextTile(DeckEntry entry, float scale)
    {
        Vector2 size = CardSize * scale;
        float u = scale * 2; // one design pixel on a half-size card
        Color rarity = RecapTheme.RarityColor(entry.Rarity);
        var tile = new PanelContainer { Size = size, CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        tile.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.Inset, 10 * u, RecapTheme.TypeColor(entry.Type), 3 * u, 10 * u, 10 * u));
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", (int)(4 * u));
        var name = new Label { Text = entry.Label, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        if (RecapTheme.Bold is Font bold) name.AddThemeFontOverride("font", bold);
        name.AddThemeFontSizeOverride("font_size", Math.Max(8, (int)(17 * u)));
        name.AddThemeColorOverride("font_color", rarity);
        column.AddChild(name);
        var type = new Label { Text = entry.Type.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center };
        if (RecapTheme.Regular is Font regular) type.AddThemeFontOverride("font", regular);
        type.AddThemeFontSizeOverride("font_size", Math.Max(7, (int)(12 * u)));
        type.AddThemeColorOverride("font_color", RecapTheme.Muted);
        column.AddChild(type);
        tile.AddChild(column);
        return tile;
    }
}
