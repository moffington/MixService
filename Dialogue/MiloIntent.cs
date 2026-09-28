namespace MixService.Dialogue;

/// <summary>
/// One entry per thing Milo is trying to say. Add a value here, then add a bank
/// for it in <see cref="MiloDialogueBank"/>; anything without a bank throws so
/// a missing line is caught by the tests instead of in front of the player.
/// </summary>
public enum MiloIntent
{
    // ---- Conversation openers and menus ----
    Greeting,
    NoTargetsDiscovered,
    TargetPrompt,
    QuantityPrompt,

    // ---- Taking the order ----
    OrderQuote,
    DepositInstructions,

    // ---- Waiting on the player ----
    AwaitingDeposit,
    SettlementNote,

    // ---- Working on the order ----
    ProcessingStarted,
    ProcessingStatus,
    CollectionFailed,
    PaymentNeedsReview,

    // ---- Handing the order over ----
    PickupReady,
    PickupDropUnavailable,
    PickupBroken,
    PackagingFailed,
    PickupComplete,

    // ---- Walking away from the order ----
    OrderCancelled,
    RefundNeedsReview,
    RefundFailed,
    LostDrop,
    NoDropForNewOrder,
    NoDropForRestoredOrder,

    // ---- Response button labels ----
    ButtonNewOrder,
    ButtonNeverMind,
    ButtonMore,
    ButtonQuantity,
    ButtonDifferentMix,
    ButtonCheckStatus,
    ButtonAfterPickup,
    ButtonCancelOrder,
    ButtonAfterCancel
}
