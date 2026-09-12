namespace WhoCarried.Core;

public readonly record struct AttributionResult(ulong? PlayerId, SourceRef Source);

/// <summary>
/// Decides who gets credit for a hit on an enemy, and under which source.
/// Source precedence: pet, then card, then the model on top of the choice-context stack, then the fallback (poison).
/// Player precedence: the dealer's player (pets resolve to their owner), then the chosen source's owner.
/// </summary>
public static class Attribution
{
    public static AttributionResult Resolve(DamageFacts facts)
    {
        if (facts.Pet != null)
        {
            // A pet's hit is listed under the pet, split by what made it attack: "Osty via Unleash".
            SourceCandidate? trigger = facts.Card ?? facts.StackTop;
            SourceRef pet = facts.Pet.Source;
            SourceRef source = trigger == null || trigger.Source.Kind is SourceKind.Pet or SourceKind.Monster || trigger.Source.Id == pet.Id
                ? pet
                : PetVia(pet, trigger.Source);
            return new AttributionResult(facts.DealerPlayerId ?? facts.Pet.OwnerId, source);
        }
        SourceCandidate? chosen = facts.Card ?? facts.StackTop ?? facts.Fallback;
        return new AttributionResult(facts.DealerPlayerId ?? chosen?.OwnerId, chosen?.Source ?? SourceRef.Unknown);
    }

    /// <summary>Separator in a pet-via-trigger source id: "OSTY&gt;UNLEASH".</summary>
    public const char ViaSeparator = '>';

    public static SourceRef PetVia(SourceRef pet, SourceRef trigger) =>
        new(SourceKind.Pet, $"{pet.Id}{ViaSeparator}{trigger.Id}", $"{pet.Label} via {trigger.Label}");

    /// <summary>
    /// Who put a debuff on an enemy. A player applier wins (pets already resolve to their owner). An applier that
    /// isn't a player (an enemy) means no player gets it. With no applier at all, the card's owner, then the owner
    /// of whatever started the action.
    /// </summary>
    public static ulong? ResolveApplier(ulong? applierPlayerId, bool applierIsNonPlayer, SourceCandidate? card,
                                        SourceCandidate? stackTop)
    {
        if (applierPlayerId.HasValue) return applierPlayerId;
        if (applierIsNonPlayer) return null;
        return card?.OwnerId ?? stackTop?.OwnerId;
    }
}
