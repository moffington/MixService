using MixService.Mixing;
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
    State = MixJobState.AwaitingPayment,
    QuotedFee = 29000,
    RequiredPackages = 20
};
Equal(29000, job.RemainingBalance, "unpaid balance");
job.AmountPaid = 9000;
Equal(20000, job.RemainingBalance, "partial balance");
var restored = JsonSerializer.Deserialize<MixJob>(JsonSerializer.Serialize(job))!;
Equal((int)MixJobState.AwaitingPayment, (int)restored.State, "saved payment state");
Equal(29000, restored.QuotedFee, "saved quote");
Equal(9000, restored.AmountPaid, "saved payment");
Equal(20000, restored.RemainingBalance, "restored balance");
job.AmountPaid = 29000;
Equal(0, job.RemainingBalance, "paid balance");
job.Reset();
Equal(0, job.QuotedFee, "reset quote");
Equal(0, job.AmountPaid, "reset paid amount");
Equal((int)MixJobState.Idle, (int)job.State, "reset state");

Console.WriteLine("Economy tests passed.");
