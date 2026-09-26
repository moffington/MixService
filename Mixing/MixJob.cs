namespace MixService.Mixing;

public enum MixJobState
{
    Idle = 0,
    AwaitingDeposit = 1,
    Processing = 2,
    ReadyForPickup = 3
}

public sealed class MixJob
{
    public MixJobState State { get; set; } = MixJobState.Idle;

    public string InputProductId { get; set; } = "";
    public string InputProductName { get; set; } = "";

    public string TargetProductId { get; set; } = "";
    public string TargetProductName { get; set; } = "";

    public string PackagingId { get; set; } = "";
    public string PackagingName { get; set; } = "";

    public int PackageCapacity { get; set; }
    public int RequiredPackages { get; set; }

    public string InputQuality { get; set; } = "";

    public string DeadDropGuid { get; set; } = "";

    public int StartedAtGameMinutes { get; set; }
    public int CompletesAtGameMinutes { get; set; }

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

        InputQuality = "";

        DeadDropGuid = "";

        StartedAtGameMinutes = 0;
        CompletesAtGameMinutes = 0;
    }
}