using System;
using System.Collections.Generic;
using System.Linq;
using MixService.Dialogue;
using MixService.Mixing;

namespace MixService.Tests;

/// <summary>
/// Game-runtime-free checks on the Milo dialogue layer. These cover the things a
/// bank can silently get wrong: a line that renders a gap because a fact was
/// never set, a line that repeats a drop the player was already given, a line
/// that grew into a speech, and a composed message that outgrew the sum of its
/// parts.
/// </summary>
internal static class DialogueTests
{
    private const string DropName = "the gazebo (blue bin)";
    private const string DropMarker = "gazebo";

    /// <summary>
    /// Milo texts. One line is not a paragraph. A handful of hand-written
    /// exceptions may be listed here as (intent, exact line); the list should
    /// stay empty, because an exception is a decision to be quiet about.
    /// </summary>
    private const int MaxWordsPerLine = 20;

    private static readonly HashSet<string> LongLineExceptions =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Budgets for the two messages Milo builds out of more than one fragment.
    /// These are what actually reaches the player's phone, so they matter more
    /// than any single line's length.
    /// </summary>
    private const int MaxWordsInBriefing = 25;
    private const int MaxWordsInDepositStatus = 20;

    public static void Run()
    {
        EveryIntentHasLines();
        NoBankIsMissingOrExtra();
        EveryLineRendersCleanly();
        DepositTextNamesInput();
        UnpaidQuotesStateTheTiming();
        PackagingAgreesWithCount();
        EveryLineIsTerse();
        NoLineClaimsAFalseShortfall();
        FirstTimeMessagesNameTheDrop();
        RepeatMessagesNeverNameTheDrop();
        SameSpotOnlyWhenTrue();
        PaidNoteNeverQuotesMoney();
        ButtonLabelsAreCanonicalAndStable();
        ComposedMessagesStayShort();
        LargeOrderThreshold();
        DescribeDurationCases();
        JoinSkipsEmptyFragments();
        RepeatGuardSemantics();
        ButtonsDoNotDisturbTheLatch();
        StatusStillNeverRepeatsAcrossButtons();
        ConsecutivePicksDiffer();
        UnknownIntentThrows();

        Console.WriteLine("Dialogue tests passed.");
    }

    // ---------------------------------------------------------------
    // Fact sets
    // ---------------------------------------------------------------

    private static MiloLineFacts FullFacts()
    {
        return new MiloLineFacts
        {
            ProductName = "Banana Butter",
            InputProductName = "Cocaine",
            DropLocation = DropName,
            LocationKnown = true,
            SameSpotAsDeposit = true,
            ProductCollected = true,
            BrickCount = 20,
            Fee = 29000,
            Due = 20000,
            Payment = MiloPaymentState.Partial,
            Duration = "6h",
            TimeLeft = "4h 20m",
            PackageCount = 20,
            Quality = "Regular",
            Packaging = "Brick",
            Failure = MiloPaymentFailure.InsufficientCash
        };
    }

    private static MiloLineFacts FactsFor(bool locationKnown)
    {
        MiloLineFacts facts = FullFacts();
        facts.LocationKnown = locationKnown;
        return facts;
    }

    private static MiloLineFacts PickupFacts(bool sameSpot)
    {
        MiloLineFacts facts = FullFacts();
        facts.LocationKnown = true;
        facts.SameSpotAsDeposit = sameSpot;
        return facts;
    }

    /// <summary>
    /// The states a line can legitimately be written for. Every bank is swept
    /// under all of them, so a line that only renders under one state is still
    /// checked under the states where it is reachable.
    /// </summary>
    private static List<MiloLineFacts> FactVariants()
    {
        var variants = new List<MiloLineFacts>();

        MiloLineFacts one = FullFacts();
        one.BrickCount = 1;
        one.PackageCount = 1;
        one.Fee = 1450;
        one.Due = 1450;
        one.Payment = MiloPaymentState.Unpaid;
        variants.Add(one);

        MiloLineFacts small = FullFacts();
        small.BrickCount = 5;
        small.Fee = 7250;
        small.Due = 7250;
        small.Payment = MiloPaymentState.Unpaid;
        small.PackageCount = 5;
        variants.Add(small);

        MiloLineFacts partial = FullFacts();
        partial.BrickCount = 5;
        partial.Payment = MiloPaymentState.Partial;
        variants.Add(partial);

        MiloLineFacts paid = FullFacts();
        paid.BrickCount = 5;
        paid.Due = 0;
        paid.Payment = MiloPaymentState.Paid;

        // Nothing is outstanding on a settled order, so a collection failure
        // cannot also be true. Milo only ever reports one with cash due.
        paid.Failure = MiloPaymentFailure.None;
        variants.Add(paid);

        variants.Add(FullFacts());
        variants.Add(FactsFor(false));
        variants.Add(PickupFacts(true));
        variants.Add(PickupFacts(false));

        MiloLineFacts collected = FullFacts();
        collected.ProductCollected = false;
        variants.Add(collected);

        MiloLineFacts unknown = FullFacts();
        unknown.ProductCollected = false;
        unknown.LocationKnown = false;
        variants.Add(unknown);

        return variants;
    }

    private static Dictionary<MiloIntent, MiloBank> Banks() =>
        MiloDialogueBank.Build();

    private static void DepositTextNamesInput()
    {
        foreach (string input in new[] { "Cocaine", "Meth", "Shrooms" })
        {
            MiloLineFacts facts = FullFacts();
            facts.InputProductName = input;

            foreach (MiloIntent intent in new[]
                     {
                         MiloIntent.DepositInstructions,
                         MiloIntent.AwaitingDeposit
                     })
            {
                foreach (MiloBankTier tier in Banks()[intent].Tiers)
                {
                    foreach (string line in tier.Lines)
                    {
                        string rendered = MiloLineFormatter.Format(line, facts);
                        if (line.Contains("{input}", StringComparison.Ordinal) &&
                            !rendered.Contains(input, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new Exception($"{intent} lost {input}: {rendered}");
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<MiloIntent> ButtonIntents() =>
        Enum.GetValues(typeof(MiloIntent))
            .Cast<MiloIntent>()
            .Where(intent => intent.ToString().StartsWith("Button"));

    private static int Words(string value) =>
        value.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries)
            .Length;

    // ---------------------------------------------------------------

    private static void EveryIntentHasLines()
    {
        Dictionary<MiloIntent, MiloBank> banks = Banks();

        foreach (MiloIntent intent in Enum.GetValues(typeof(MiloIntent)))
        {
            if (!banks.TryGetValue(intent, out MiloBank? bank))
                throw new Exception($"{intent}: no dialogue bank");

            if (bank.Tiers.Length == 0)
                throw new Exception($"{intent}: bank has no tiers");

            foreach (MiloBankTier tier in bank.Tiers)
            {
                if (tier.Lines.Length == 0)
                    throw new Exception($"{intent}: a tier has no lines");
            }

            if (bank.Resolve(FullFacts()).Length == 0)
                throw new Exception($"{intent}: resolves to nothing");
        }
    }

    private static void NoBankIsMissingOrExtra()
    {
        Dictionary<MiloIntent, MiloBank> banks = Banks();
        int expected = Enum.GetValues(typeof(MiloIntent)).Length;

        if (banks.Count != expected)
        {
            throw new Exception(
                $"expected {expected} banks, found {banks.Count}");
        }
    }

    private static void EveryLineRendersCleanly()
    {
        foreach (MiloLineFacts facts in FactVariants())
        {
            foreach (KeyValuePair<MiloIntent, MiloBank> entry in Banks())
            {
                foreach (string line in entry.Value.Resolve(facts))
                {
                    string rendered =
                        MiloLineFormatter.Format(line, facts);

                    if (rendered.Length == 0)
                    {
                        throw new Exception(
                            $"{entry.Key}: line rendered empty: {line}");
                    }

                    if (rendered.Contains("{") ||
                        rendered.Contains("}"))
                    {
                        throw new Exception(
                            $"{entry.Key}: unfilled fact in: {line} " +
                            $"-> {rendered}");
                    }

                    if (rendered.Contains("  "))
                    {
                        throw new Exception(
                            $"{entry.Key}: double space in: {line} " +
                            $"-> {rendered}");
                    }

                    // A paid order must never show a balance, and an unpaid one
                    // must never show a zero.
                    if (rendered.Contains("$0"))
                    {
                        throw new Exception(
                            $"{entry.Key}: shows a zero balance: {line} " +
                            $"-> {rendered}");
                    }

                    if (facts.Due == 0 && line.Contains("{due}"))
                    {
                        throw new Exception(
                            $"{entry.Key}: quotes a due amount on a paid " +
                            $"order: {line}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The quote is the first place the player learns when the money moves, so
    /// every line that is reachable with cash outstanding has to say it. A bare
    /// "cost is $1,450" reads like they pay now.
    /// </summary>
    private static void UnpaidQuotesStateTheTiming()
    {
        var states = new[]
        {
            MiloPaymentState.Unpaid,
            MiloPaymentState.Partial
        };

        foreach (MiloPaymentState state in states)
        {
            foreach (int bricks in new[] { 1, 5, 20 })
            {
                MiloLineFacts facts = FullFacts();
                facts.Payment = state;
                facts.BrickCount = bricks;
                facts.Due = 7250;

                foreach (string line in Banks()[MiloIntent.OrderQuote]
                             .Resolve(facts))
                {
                    string rendered =
                        MiloLineFormatter.Format(line, facts);

                    bool statesTiming =
                        rendered.Contains("collect", StringComparison.Ordinal) ||
                        rendered.Contains("handoff", StringComparison.Ordinal) ||
                        rendered.Contains("goods", StringComparison.Ordinal);

                    if (!statesTiming)
                    {
                        throw new Exception(
                            $"quote never says when the money moves " +
                            $"({bricks} bricks, {state}): {rendered}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// "20 regular brick" is worse than no line at all. The packaging word has
    /// to agree with the count it sits next to.
    /// </summary>
    private static void PackagingAgreesWithCount()
    {
        foreach (int count in new[] { 1, 2, 20 })
        {
            MiloLineFacts facts = FullFacts();
            facts.BrickCount = count;
            facts.PackageCount = count;
            facts.Packaging = "Brick";

            foreach (string line in Banks()[MiloIntent.PickupReady]
                         .Resolve(facts))
            {
                string rendered =
                    MiloLineFormatter.Format(line, facts);

                if (count == 1 &&
                    rendered.IndexOf(
                        "1 regular brick,",
                        StringComparison.Ordinal) < 0 &&
                    rendered.IndexOf(
                        "1 regular brick at",
                        StringComparison.Ordinal) < 0)
                {
                    throw new Exception(
                        "one package did not read as singular: " + rendered);
                }

                if (count > 1 &&
                    rendered.IndexOf(
                        $"{count} regular bricks",
                        StringComparison.Ordinal) < 0)
                {
                    throw new Exception(
                        $"{count} packages did not read as plural: " +
                        rendered);
                }
            }
        }
    }

    private static void EveryLineIsTerse()
    {
        foreach (KeyValuePair<MiloIntent, MiloBank> entry in Banks())
        {
            foreach (MiloBankTier tier in entry.Value.Tiers)
            {
                foreach (string line in tier.Lines)
                {
                    if (LongLineExceptions.Contains(line))
                        continue;

                    int words = Words(
                        MiloLineFormatter.Format(line, FullFacts()));

                    if (words > MaxWordsPerLine)
                    {
                        throw new Exception(
                            $"{entry.Key}: {words} words is a speech: " +
                            $"{line}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// {due} is the whole amount still owed, not the gap between the player's
    /// cash and that amount. Wording it as a shortfall states something false.
    /// </summary>
    private static void NoLineClaimsAFalseShortfall()
    {
        Dictionary<string, string> banned = new Dictionary<string, string>
        {
            { "light", "reads as a cash shortfall, which {due} is not" },
            { "no money on me", "sounds like Milo is the one broke" }
        };

        foreach (KeyValuePair<MiloIntent, MiloBank> entry in Banks())
        {
            foreach (MiloBankTier tier in entry.Value.Tiers)
            {
                foreach (string line in tier.Lines)
                {
                    foreach (KeyValuePair<string, string> rule in banned)
                    {
                        if (line.IndexOf(
                                rule.Key,
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            throw new Exception(
                                $"{entry.Key}: {rule.Value}: {line}");
                        }
                    }
                }
            }
        }
    }

    private static void FirstTimeMessagesNameTheDrop()
    {
        Dictionary<MiloIntent, MiloBank> banks = Banks();
        bool sawGate = false;

        foreach (MiloIntent intent in FirstMentionGated(banks))
        {
            sawGate = true;

            foreach (MiloBankTier tier in banks[intent].Tiers)
            {
                if (tier.When == null ||
                    !tier.When(FactsFor(false)) ||
                    tier.When(FactsFor(true)))
                    continue;

                foreach (string line in tier.Lines)
                {
                    if (!line.Contains("{drop}"))
                    {
                        throw new Exception(
                            $"{intent}: first-time line hides the drop: " +
                            line);
                    }
                }
            }
        }

        if (!sawGate)
            throw new Exception("no first-mention gates found at all");
    }

    private static void RepeatMessagesNeverNameTheDrop()
    {
        Dictionary<MiloIntent, MiloBank> banks = Banks();

        foreach (MiloIntent intent in FirstMentionGated(banks))
        {
            // SameSpot only changes wording, never whether the location leaks,
            // so both answers have to hold.
            foreach (bool sameSpot in new[] { false, true })
            {
                MiloLineFacts facts = PickupFacts(sameSpot);

                foreach (string line in banks[intent].Resolve(facts))
                {
                    string rendered =
                        MiloLineFormatter.Format(line, facts);

                    if (rendered.Contains("{drop}"))
                    {
                        throw new Exception(
                            $"{intent}: repeat line still interpolates the " +
                            $"drop: {line}");
                    }

                    if (rendered.IndexOf(
                            DropMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        throw new Exception(
                            $"{intent}: repeat line repeats the drop name: " +
                            rendered);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Tiers that open only while the drop location is still unannounced.
    /// Found by probing rather than by a hardcoded list, so a new bank that
    /// forgets the rule is caught here.
    /// </summary>
    private static IEnumerable<MiloIntent> FirstMentionGated(
        Dictionary<MiloIntent, MiloBank> banks)
    {
        foreach (KeyValuePair<MiloIntent, MiloBank> entry in banks)
        {
            bool gated = entry.Value.Tiers.Any(tier =>
                tier.When != null &&
                tier.Lines.Length > 0 &&
                tier.When(FactsFor(false)) &&
                !tier.When(FactsFor(true)));

            if (gated)
                yield return entry.Key;
        }
    }

    private static void SameSpotOnlyWhenTrue()
    {
        MiloLineFacts moved = PickupFacts(sameSpot: false);

        foreach (string line in Banks()[MiloIntent.PickupReady]
                     .Resolve(moved))
        {
            if (line.Contains("same spot", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("where i told you",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "PickupReady claims a familiar spot without one: " + line);
            }
        }

        if (Banks()[MiloIntent.PickupReady]
                .Resolve(PickupFacts(sameSpot: true)).Length == 0)
        {
            throw new Exception(
                "PickupReady resolves nothing when same spot");
        }
    }

    private static void PaidNoteNeverQuotesMoney()
    {
        MiloLineFacts facts = FullFacts();
        facts.Payment = MiloPaymentState.Paid;
        facts.Due = 0;

        foreach (string line in Banks()[MiloIntent.SettlementNote]
                     .Resolve(facts))
        {
            if (line.Contains("{due}"))
            {
                throw new Exception(
                    "SettlementNote quotes money on a paid order: " + line);
            }
        }
    }

    private static void ButtonLabelsAreCanonicalAndStable()
    {
        var dialogue = new MiloDialogue(seed: 7);

        foreach (MiloIntent intent in ButtonIntents())
        {
            foreach (MiloLineFacts facts in FactVariants())
            {
                foreach (MiloBankTier tier in Banks()[intent].Tiers)
                {
                    if (tier.When != null && !tier.When(facts))
                        continue;

                    if (tier.Lines.Length != 1)
                    {
                        throw new Exception(
                            $"{intent}: a button tier has " +
                            $"{tier.Lines.Length} lines, so a label would " +
                            $"be dead weight: " +
                            string.Join("|", tier.Lines));
                    }
                }
            }

            // Same facts, same label, every time, seed or no seed.
            string first = dialogue.Canonical(intent, FullFacts());
            string second = dialogue.Canonical(intent, FullFacts());

            if (first != second)
                throw new Exception($"{intent}: label is not stable");

            string viaPick = dialogue.Pick(intent, FullFacts());
            if (viaPick != first)
            {
                throw new Exception(
                    $"{intent}: label differs from the only line: " +
                    $"{first} vs {viaPick}");
            }
        }
    }

    private static void ComposedMessagesStayShort()
    {
        var dialogue = new MiloDialogue(seed: 99);

        foreach (MiloLineFacts facts in FactVariants())
        {
            foreach (int brickCount in new[] { 1, 5, 10, 20 })
            {
                MiloLineFacts sized = FullFacts();
                sized.BrickCount = brickCount;
                sized.PackageCount = brickCount;
                sized.LocationKnown = facts.LocationKnown;
                sized.SameSpotAsDeposit = facts.SameSpotAsDeposit;
                sized.ProductCollected = facts.ProductCollected;
                sized.Payment = facts.Payment;
                sized.Due = facts.Due;

                CheckBriefing(dialogue, sized);
                CheckDepositStatus(dialogue, sized);
            }
        }
    }

    private static void CheckBriefing(
        MiloDialogue dialogue,
        MiloLineFacts facts)
    {
        string[] quotes =
            Banks()[MiloIntent.OrderQuote].Resolve(facts);
        string[] instructions =
            Banks()[MiloIntent.DepositInstructions].Resolve(facts);

        // Every reachable pairing, not just the one the picker happened to take.
        foreach (string quote in quotes)
        {
            foreach (string instruction in instructions)
            {
                string composed = MiloLineFormatter.Join(
                    MiloLineFormatter.Format(quote, facts),
                    MiloLineFormatter.Format(instruction, facts));

                int words = Words(composed);

                if (words > MaxWordsInBriefing)
                {
                    throw new Exception(
                        $"deposit briefing is {words} words: {composed}");
                }
            }
        }

        // And the real path must actually fit too.
        string live = MiloDialogue.DepositBriefing(facts);
        if (Words(live) > MaxWordsInBriefing)
        {
            throw new Exception(
                $"deposit briefing is {Words(live)} words: {live}");
        }
    }

    private static void CheckDepositStatus(
        MiloDialogue dialogue,
        MiloLineFacts facts)
    {
        string[] waiting =
            Banks()[MiloIntent.AwaitingDeposit].Resolve(facts);
        string[] notes =
            Banks()[MiloIntent.SettlementNote].Resolve(facts);

        foreach (string head in waiting)
        {
            foreach (string tail in notes)
            {
                string composed = MiloLineFormatter.Join(
                    MiloLineFormatter.Format(head, facts),
                    MiloLineFormatter.Format(tail, facts));

                int words = Words(composed);

                if (words > MaxWordsInDepositStatus)
                {
                    throw new Exception(
                        $"deposit status is {words} words: {composed}");
                }
            }
        }

        string live = MiloDialogue.DepositStatus(facts);
        if (Words(live) > MaxWordsInDepositStatus)
        {
            throw new Exception(
                $"deposit status is {Words(live)} words: {live}");
        }
    }

    private static void LargeOrderThreshold()
    {
        if (new MiloLineFacts { BrickCount = 20 }.IsLargeOrder != true)
            throw new Exception("20 bricks is not a large order");
        if (new MiloLineFacts { BrickCount = 10 }.IsLargeOrder != false)
            throw new Exception("10 bricks is a large order");
        if (new MiloLineFacts { BrickCount = 0 }.IsLargeOrder != false)
            throw new Exception("an empty order is a large order");
    }

    private static void DescribeDurationCases()
    {
        Equal("0m", MiloDialogue.DescribeDuration(0), "zero minutes");
        Equal("40m", MiloDialogue.DescribeDuration(40), "minutes only");
        Equal("6h", MiloDialogue.DescribeDuration(360), "whole hours");
        Equal("4h 20m", MiloDialogue.DescribeDuration(260), "hours and minutes");
        Equal("0m", MiloDialogue.DescribeDuration(-30), "negative clamp");
    }

    private static void JoinSkipsEmptyFragments()
    {
        Equal(
            "one two",
            MiloDialogue.Join("one", "", "  ", "two"),
            "join drops empty fragments");

        Equal(
            "one two",
            MiloDialogue.Join("one", "  two  "),
            "join trims fragments");

        Equal(string.Empty, MiloDialogue.Join(), "join of nothing");
    }

    private static void RepeatGuardSemantics()
    {
        var guard = new MiloRepeatGuard();

        if (!guard.ShouldEmit("a"))
            throw new Exception("guard swallowed a fresh value");
        if (guard.ShouldEmit("a"))
            throw new Exception("guard allowed an immediate repeat");
        if (!guard.ShouldEmit("b"))
            throw new Exception("guard swallowed a different value");
        if (!guard.ShouldEmit("a"))
            throw new Exception("guard blocked a value after a change");
        if (guard.ShouldEmit(null))
            throw new Exception("guard accepted nothing");
        if (guard.ShouldEmit(string.Empty))
            throw new Exception("guard accepted an empty value");

        guard.Clear();
        if (guard.Last != null)
            throw new Exception("clear left a value behind");
        if (!guard.ShouldEmit("a"))
            throw new Exception("clear did not release the latch");
    }

    /// <summary>
    /// Regression: a status message is followed by the creation of a button.
    /// If the button touched the latch, the status message would be eligible to
    /// repeat itself on the next status check.
    /// </summary>
    private static void ButtonsDoNotDisturbTheLatch()
    {
        var dialogue = new MiloDialogue(seed: 4242);

        dialogue.Pick(MiloIntent.ProcessingStatus, FullFacts());
        string? held = dialogue.LastEmitted;

        if (held == null)
            throw new Exception("picking a line did not latch it");

        foreach (MiloIntent intent in ButtonIntents())
        {
            dialogue.Canonical(intent, FullFacts());

            if (dialogue.LastEmitted != held)
            {
                throw new Exception(
                    $"button {intent} evicted Milo's last message from " +
                    $"the latch");
            }
        }
    }

    private static void StatusStillNeverRepeatsAcrossButtons()
    {
        var dialogue = new MiloDialogue(seed: 31337);
        MiloLineFacts facts = FullFacts();

        string? previous = null;

        for (int i = 0; i < 300; i++)
        {
            string status =
                dialogue.Pick(MiloIntent.ProcessingStatus, facts);

            // Rebuild the response buttons the way the NPC does, every time.
            dialogue.Canonical(MiloIntent.ButtonCheckStatus, facts);
            dialogue.Canonical(MiloIntent.ButtonCancelOrder, facts);

            if (string.Equals(status, previous, StringComparison.Ordinal))
            {
                throw new Exception(
                    "status repeated across a button rebuild: " + status);
            }

            previous = status;
        }
    }

    private static void ConsecutivePicksDiffer()
    {
        var dialogue = new MiloDialogue(seed: 20240917);

        PickManyAndDiffer(dialogue, MiloIntent.Greeting);
        PickManyAndDiffer(dialogue, MiloIntent.AwaitingDeposit);
        PickManyAndDiffer(dialogue, MiloIntent.PickupReady);
        PickManyAndDiffer(dialogue, MiloIntent.ProcessingStatus);

        // Variants must actually vary.
        MiloLineFacts repeatFacts = FullFacts();
        repeatFacts.LocationKnown = true;
        repeatFacts.BrickCount = 5;

        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++)
            seen.Add(dialogue.Pick(
                MiloIntent.AwaitingDeposit,
                repeatFacts));

        if (seen.Count < 3)
        {
            throw new Exception(
                $"awaiting-deposit bank only produced " +
                $"{seen.Count} distinct lines");
        }
    }

    private static void PickManyAndDiffer(
        MiloDialogue dialogue,
        MiloIntent intent)
    {
        string? previous = null;

        for (int i = 0; i < 200; i++)
        {
            string line = dialogue.Pick(intent, FullFacts());

            if (string.Equals(line, previous, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"{intent}: said the same thing twice in a row: {line}");
            }

            previous = line;
        }
    }

    private static void UnknownIntentThrows()
    {
        var dialogue = new MiloDialogue(seed: 1);

        MiloIntent missing = (MiloIntent)9999;

        try
        {
            dialogue.Pick(missing, FullFacts());
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new Exception("a bank-less intent did not throw");
    }

    private static void Equal(
        string expected,
        string actual,
        string label)
    {
        if (expected != actual)
        {
            throw new Exception(
                $"{label}: expected '{expected}', got '{actual}'");
        }
    }
}
