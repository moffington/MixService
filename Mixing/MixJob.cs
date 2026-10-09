using MixService.Utils;

namespace MixService.Mixing;

public enum MixJobState
{
    Idle = 0,
    AwaitingDeposit = 1,
    Processing = 2,
    ReadyForPickup = 3,
    AwaitingPayment = 4 // Legacy saves only; new orders go to AwaitingDeposit.
}

public sealed class MixJob
{
    public MixJobState State { get; set; } =
        MixJobState.Idle;

    public string InputProductId { get; set; } = "";
    public string InputProductName { get; set; } = "";

    public string TargetProductId { get; set; } = "";
    public string TargetProductName { get; set; } = "";

    public string PackagingId { get; set; } = "";
    public string PackagingName { get; set; } = "";

    public int PackageCapacity { get; set; }
    public int RequiredPackages { get; set; }

    // Fixed at order creation; future price changes do not alter an existing quote.
    public int QuotedFee { get; set; }
    // Fixed alongside the fee at order creation, before any deposit is made.
    // Zero identifies older saves that did not store a duration.
    public int ProcessingDurationMinutes { get; set; }
    public int AmountPaid { get; set; }
    // Blocks automatic retries if a wallet call may have moved an unknown amount.
    public bool PaymentNeedsReview { get; set; }
    public int RemainingBalance =>
        System.Math.Max(0, QuotedFee - AmountPaid);

    public string InputQuality { get; set; } = "";

    // The input handoff assigned to this order. Persist its GUID so a reload
    // never chooses a different drop while product is waiting there.
    public string AssignedDrop { get; set; } = "";

    // Kept only to migrate orders saved before AssignedDrop existed.
    public string DeadDropGuid { get; set; } = "";

    /*
     * PickupDropGuid may be the assigned drop, but is separate
     * because that drop could become occupied while Milo
     * is processing the order.
     */
    public string PickupDropGuid { get; set; } = "";

    public int StartedAtGameMinutes { get; set; }
    public int CompletesAtGameMinutes { get; set; }

    /*
     * Idempotence guard. Once true we never manufacture
     * this order again just because the save was reloaded.
     */
    public bool OutputSpawned { get; set; }

    public void RestoreAssignedDrop()
    {
        if (string.IsNullOrWhiteSpace(AssignedDrop))
            AssignedDrop = DeadDropGuid;
    }

    public void RestoreProcessingDuration()
    {
        if (State == MixJobState.Idle || ProcessingDurationMinutes > 0)
            return;

        long recordedDuration = (long)CompletesAtGameMinutes - StartedAtGameMinutes;
        ProcessingDurationMinutes =
            (State == MixJobState.Processing || State == MixJobState.ReadyForPickup)
            && recordedDuration > 0 && recordedDuration <= int.MaxValue
                ? (int)recordedDuration
                : Constants.Defaults.ProcessingMinutes;
    }

    public void Reset()
    {
        State = MixJobState.Idle;

        InputProductId = "";
        InputProductName = "";

        TargetProductId = "";
        TargetProductName = "";

        PackagingId = "";
        PackagingName = "";

        PackageCapacity = 0;
        RequiredPackages = 0;
        QuotedFee = 0;
        ProcessingDurationMinutes = 0;
        AmountPaid = 0;
        PaymentNeedsReview = false;

        InputQuality = "";

        AssignedDrop = "";
        DeadDropGuid = "";
        PickupDropGuid = "";

        StartedAtGameMinutes = 0;
        CompletesAtGameMinutes = 0;

        OutputSpawned = false;
    }
}
