using MixService.Mixing;

namespace MixService.Dialogue;

/// <summary>
/// How much of Milo's fee is still outstanding on the order he is describing.
/// </summary>
public enum MiloPaymentState
{
    Unpaid = 0,
    Partial = 1,
    Paid = 2
}

/// <summary>
/// The facts a line may need. Populate only what the bank for the chosen intent
/// actually interpolates, so Milo never volunteers a value the player did not ask
/// for. Everything is optional: an unset fact renders as an empty string and a
/// bank that needs it will fail the test sweep rather than ship broken prose.
/// </summary>
public sealed class MiloLineFacts
{
    /// <summary>
    /// A brick count at or above this is worth its own voice.
    /// </summary>
    public const int LargeOrderBricks = 20;

    public string ProductName { get; set; } = "";
    public string InputProductName { get; set; } = "";

    /// <summary>
    /// Formatted "name (description)" for the drop in play. Left empty when the
    /// NPC has no drop object, in which case {drop} renders as "the drop".
    /// </summary>
    public string DropLocation { get; set; } = "";

    /// <summary>
    /// True once the current drop location has actually been put in front of the
    /// player for this order. False means Milo still has to name it.
    /// </summary>
    public bool LocationKnown { get; set; }

    /// <summary>
    /// True when the finished batch is waiting in the same drop the player
    /// already knows. Lets Milo say "same spot" without ever lying about it.
    /// </summary>
    public bool SameSpotAsDeposit { get; set; }

    /// <summary>
    /// True once Milo has taken the deposited product off the player's hands.
    /// </summary>
    public bool ProductCollected { get; set; }

    public int BrickCount { get; set; }

    public int? Fee { get; set; }

    /// <summary>
    /// Still owed on this order, collected at handoff.
    /// </summary>
    public int? Due { get; set; }

    public MiloPaymentState Payment { get; set; } =
        MiloPaymentState.Unpaid;

    /// <summary>
    /// Full processing time, already worded for the player ("6h").
    /// </summary>
    public string Duration { get; set; } = "";

    /// <summary>
    /// Time left before the batch is ready, already worded for the player ("4h 20m").
    /// </summary>
    public string TimeLeft { get; set; } = "";

    public int? PackageCount { get; set; }

    public string Quality { get; set; } = "";

    public string Packaging { get; set; } = "";

    public MiloPaymentFailure Failure { get; set; } =
        MiloPaymentFailure.None;

    public bool IsLargeOrder =>
        BrickCount >= LargeOrderBricks;
}
