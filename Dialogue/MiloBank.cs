using System;

namespace MixService.Dialogue;

public delegate bool MiloTierPredicate(MiloLineFacts facts);

/// <summary>
/// One hand-authored group of lines. A null <see cref="When"/> is the fallback
/// tier and must always be the last one.
/// </summary>
public sealed class MiloBankTier
{
    public MiloBankTier(
        MiloTierPredicate? when,
        params string[] lines)
    {
        When = when;
        Lines = lines ?? Array.Empty<string>();
    }

    public MiloTierPredicate? When { get; }

    public string[] Lines { get; }
}

/// <summary>
/// A bank is an ordered list of tiers. The first tier whose predicate accepts
/// the facts wins; a tier with no lines is skipped so a half-filled variant
/// cannot silence an intent.
/// </summary>
public sealed class MiloBank
{
    public MiloBank(params MiloBankTier[] tiers)
    {
        Tiers = tiers ?? Array.Empty<MiloBankTier>();
    }

    public MiloBankTier[] Tiers { get; }

    public string[] Resolve(MiloLineFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));

        foreach (MiloBankTier tier in Tiers)
        {
            if (tier.Lines.Length == 0)
                continue;

            if (tier.When == null || tier.When(facts))
                return tier.Lines;
        }

        return Array.Empty<string>();
    }
}
