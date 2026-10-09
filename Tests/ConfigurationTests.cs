using System.Text.Json;
using MixService.Mixing;
using MixService.Utils;

namespace MixService.Tests;

internal static class ConfigurationTests
{
    public static void Run()
    {
        Equal(20000, MixFeePolicy.Quote("brick", 20, 20, 1000),
            "configured price applies to the full order");
        Equal(2000000, MixFeePolicy.Quote("brick", 20, 20,
            Constants.Constraints.MaxPricePerBrick), "maximum configured price");
        Equal(700, MixFeePolicy.Quote("bag", 10, 1, 1000),
            "brick setting does not change other package pricing");
        RejectPrice(0);
        RejectPrice(-1);

        var booked = new MixJob
        {
            State = MixJobState.AwaitingDeposit,
            QuotedFee = MixFeePolicy.Quote("brick", 20, 5, 1000),
            ProcessingDurationMinutes = 90
        };
        // Simulate changing the price used by later orders.
        Equal(10000, MixFeePolicy.Quote("brick", 20, 5, 2000), "new quote");
        var restored = RoundTrip(booked);
        restored.RestoreProcessingDuration();
        Equal(5000, restored.QuotedFee, "existing quote survives settings change and reload");
        Equal(90, restored.ProcessingDurationMinutes, "booked duration survives reload");

        restored.State = MixJobState.Processing;
        restored.StartedAtGameMinutes = 1400;
        restored.CompletesAtGameMinutes = 1490;
        restored = RoundTrip(restored);
        restored.RestoreProcessingDuration();
        Equal(90, restored.ProcessingDurationMinutes, "active duration survives reload");
        Equal(1490, restored.CompletesAtGameMinutes, "active deadline is preserved");

        var legacyPending = JsonSerializer.Deserialize<MixJob>(
            """{"State":1,"QuotedFee":1450}""")!;
        legacyPending.RestoreProcessingDuration();
        Equal(360, legacyPending.ProcessingDurationMinutes,
            "older pending order retains the original six hours");

        var legacyProcessing = JsonSerializer.Deserialize<MixJob>(
            """{"State":2,"StartedAtGameMinutes":1400,"CompletesAtGameMinutes":1640}""")!;
        legacyProcessing.RestoreProcessingDuration();
        Equal(240, legacyProcessing.ProcessingDurationMinutes,
            "older active order derives duration from its saved deadline");
        Equal(1640, legacyProcessing.CompletesAtGameMinutes,
            "migration leaves the deadline untouched");
        legacyProcessing.RestoreProcessingDuration();
        Equal(240, legacyProcessing.ProcessingDurationMinutes, "migration is repeatable");

        restored.Reset();
        Equal(0, restored.ProcessingDurationMinutes, "reset clears the booked duration");
        restored.RestoreProcessingDuration();
        Equal(0, restored.ProcessingDurationMinutes, "idle job stays empty");

        foreach (int minutes in new[] { 1, Constants.Constraints.MaxProcessingMinutes })
        {
            var boundary = RoundTrip(new MixJob
            {
                State = MixJobState.AwaitingDeposit,
                ProcessingDurationMinutes = minutes
            });
            boundary.RestoreProcessingDuration();
            Equal(minutes, boundary.ProcessingDurationMinutes, "duration boundary survives reload");
        }
    }

    private static MixJob RoundTrip(MixJob job) =>
        JsonSerializer.Deserialize<MixJob>(JsonSerializer.Serialize(job))!;

    private static void RejectPrice(int price)
    {
        try
        {
            MixFeePolicy.Quote("brick", 20, 1, price);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new Exception($"invalid price {price} was accepted");
    }

    private static void Equal(int expected, int actual, string label)
    {
        if (expected != actual)
            throw new Exception($"{label}: expected {expected}, got {actual}");
    }
}
