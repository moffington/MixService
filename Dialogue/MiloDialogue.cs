using System;
using System.Collections.Generic;

namespace MixService.Dialogue;

/// <summary>
/// Picks one of Milo's lines for a given intent. This is the only place dialogue
/// selection happens; order logic just says what is true and asks for a line.
///
/// Anti-repetition uses <see cref="MiloRepeatGuard"/>, the same single-slot
/// latch order handling already used to avoid logging a rejected deposit twice,
/// so the two never fight over what counts as a repeat.
///
/// Two access paths, and the difference matters:
///   Pick()   tracked. A random line, recorded in the latch so the next Pick
///            of the same intent cannot say the same thing twice.
///   Label()  untracked. The one canonical line for a bank, no randomness and
///            no latch access at all.
/// Use Label for response button text. Buttons are rebuilt on nearly every
/// message, so letting them touch the latch would evict Milo's previous
/// message from it and let that message repeat on the next status check.
/// </summary>
public sealed class MiloDialogue
{
    /// <summary>
    /// The instance the game talks to. Tests build their own with a fixed seed.
    /// </summary>
    public static MiloDialogue Shared { get; } = new MiloDialogue();

    private readonly MiloRepeatGuard _guard = new MiloRepeatGuard();
    private readonly Random _random;
    private readonly Dictionary<MiloIntent, MiloBank> _banks;

    public MiloDialogue(int? seed = null)
    {
        _random = seed.HasValue
            ? new Random(seed.Value)
            : new Random();

        _banks = MiloDialogueBank.Build();
    }

    /// <summary>
    /// Response button text: deterministic, and invisible to the repeat latch.
    /// </summary>
    public static string Label(
        MiloIntent intent,
        MiloLineFacts facts) =>
        Shared.Canonical(intent, facts);

    public static string Line(
        MiloIntent intent,
        MiloLineFacts facts) =>
        Shared.Pick(intent, facts);

    /// <summary>
    /// The first message Milo sends for a new order: the quote, then where to
    /// put the goods. Composed here rather than in the NPC so the two halves
    /// can be kept short together.
    /// </summary>
    public static string DepositBriefing(MiloLineFacts facts) =>
        MiloLineFormatter.Join(
            Line(MiloIntent.OrderQuote, facts),
            Line(MiloIntent.DepositInstructions, facts));

    /// <summary>
    /// The "still waiting on you" status message.
    /// </summary>
    public static string DepositStatus(MiloLineFacts facts) =>
        MiloLineFormatter.Join(
            Line(MiloIntent.AwaitingDeposit, facts),
            Line(MiloIntent.SettlementNote, facts));

    public static string DescribeDuration(int totalMinutes) =>
        MiloLineFormatter.DescribeDuration(totalMinutes);

    /// <summary>
    /// Joins several selected lines into one message.
    /// </summary>
    public static string Join(params string?[] parts) =>
        MiloLineFormatter.Join(parts);

    public string Pick(
        MiloIntent intent,
        MiloLineFacts facts) =>
        MiloLineFormatter.Format(
            Choose(Resolve(intent, facts)),
            facts);

    /// <summary>
    /// The canonical line for a bank. Deliberately not random and deliberately
    /// not recorded in the latch.
    /// </summary>
    public string Canonical(
        MiloIntent intent,
        MiloLineFacts facts)
    {
        string[] lines = Resolve(intent, facts);

        if (lines.Length == 0)
        {
            throw new InvalidOperationException(
                $"Milo dialogue bank for intent '{intent}' " +
                $"matched no tier for these facts.");
        }

        return MiloLineFormatter.Format(lines[0], facts);
    }

    /// <summary>
    /// What the latch currently holds. Exposed so the tests can prove that
    /// untracked paths leave it alone.
    /// </summary>
    public string? LastEmitted => _guard.Last;

    /// <summary>
    /// Exposed for the test sweep so every line in every bank can be rendered.
    /// </summary>
    public IReadOnlyDictionary<MiloIntent, MiloBank> Banks => _banks;

    private string[] Resolve(
        MiloIntent intent,
        MiloLineFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));

        if (!_banks.TryGetValue(intent, out MiloBank? bank))
        {
            throw new InvalidOperationException(
                $"No Milo dialogue bank for intent '{intent}'.");
        }

        string[] lines = bank.Resolve(facts);

        if (lines.Length == 0)
        {
            throw new InvalidOperationException(
                $"Milo dialogue bank for intent '{intent}' " +
                $"matched no tier for these facts.");
        }

        return lines;
    }

    private string Choose(string[] lines)
    {
        // A single-option tier has nothing to choose between, so it is emitted
        // as-is rather than fighting the latch.
        if (lines.Length == 1)
            return lines[0];

        int start = _random.Next(lines.Length);

        for (int offset = 0; offset < lines.Length; offset++)
        {
            string candidate =
                lines[(start + offset) % lines.Length];

            if (_guard.ShouldEmit(candidate))
                return candidate;
        }

        // Every line in this bank is the one thing already said.
        return lines[start];
    }
}
