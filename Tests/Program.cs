using MixService.Mixing;
using MixService.Tests;
using System.Text.Json;

static void Equal(int expected, int actual, string label)
{
    if (expected != actual)
        throw new Exception($"{label}: expected {expected}, got {actual}");
}

Equal(29000, MixFeePolicy.Quote("brick", 20, 20), "20-brick baseline");
Equal(1450, MixFeePolicy.Quote("BRICK", 20, 1), "one brick");
Equal(700, MixFeePolicy.Quote("bag", 10, 1), "other package type");

var job = new MixJob
{
    State = MixJobState.AwaitingDeposit,
    AssignedDrop = "drop-for-this-order",
    PaymentNeedsReview = true,
    QuotedFee = 29000,
    RequiredPackages = 20
};
Equal(29000, job.RemainingBalance, "unpaid balance");
job.AmountPaid = 9000;
Equal(20000, job.RemainingBalance, "partial balance");
var restored = JsonSerializer.Deserialize<MixJob>(JsonSerializer.Serialize(job))!;
Equal((int)MixJobState.AwaitingDeposit, (int)restored.State, "saved deposit state");
if (restored.AssignedDrop != "drop-for-this-order")
    throw new Exception("assigned drop was not persisted");
if (!restored.PaymentNeedsReview)
    throw new Exception("payment review state was not persisted");
Equal(29000, restored.QuotedFee, "saved quote");
Equal(9000, restored.AmountPaid, "saved payment");
Equal(20000, restored.RemainingBalance, "restored balance");
job.AmountPaid = 29000;
Equal(0, job.RemainingBalance, "paid balance");
job.Reset();
Equal(0, job.QuotedFee, "reset quote");
Equal(0, job.AmountPaid, "reset paid amount");
if (job.AssignedDrop != "")
    throw new Exception("reset did not clear assigned drop");
if (job.PaymentNeedsReview)
    throw new Exception("reset did not clear payment review state");
Equal((int)MixJobState.Idle, (int)job.State, "reset state");

var legacy = new MixJob
{
    State = MixJobState.AwaitingDeposit,
    DeadDropGuid = "legacy-drop"
};
legacy.RestoreAssignedDrop();
if (legacy.AssignedDrop != "legacy-drop")
    throw new Exception("legacy drop was not migrated");

DialogueTests.Run();
WeedRecipeResolverTests.Run();

Console.WriteLine("Economy tests passed.");
