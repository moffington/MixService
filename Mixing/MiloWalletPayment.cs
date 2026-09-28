using System;
using S1API.Money;

namespace MixService.Mixing;

/// <summary>
/// Verifies each wallet change against the balance reported by S1API.
/// S1API's change methods return void, so an unevidenced change is not a payment.
/// </summary>
public static class MiloWalletPayment
{
    private const float BalanceTolerance = 0.01f;

    public static bool TryCharge(
        int amount,
        out string problem,
        out bool paymentNeedsReview,
        out MiloPaymentFailure failure)
    {
        problem = string.Empty;
        paymentNeedsReview = false;
        failure = MiloPaymentFailure.None;
        if (amount <= 0)
        {
            problem = "invalid payment amount";
            failure = MiloPaymentFailure.InvalidAmount;
            return false;
        }

        float before;
        try
        {
            before = Money.GetCashBalance();
        }
        catch (Exception ex)
        {
            problem = $"could not read cash balance: {ex.Message}";
            failure = MiloPaymentFailure.BalanceUnavailable;
            return false;
        }

        if (!IsValidBalance(before))
        {
            problem = "cash balance is unavailable";
            failure = MiloPaymentFailure.BalanceUnavailable;
            return false;
        }

        if (before < amount)
        {
            problem = "not enough cash on hand";
            failure = MiloPaymentFailure.InsufficientCash;
            return false;
        }

        return TryApply(
            before, -amount, amount,
            out problem, out paymentNeedsReview, out failure);
    }

    public static bool TryRefund(
        int amount,
        out string problem,
        out bool paymentNeedsReview,
        out MiloPaymentFailure failure)
    {
        problem = string.Empty;
        paymentNeedsReview = false;
        failure = MiloPaymentFailure.None;
        if (amount < 0)
        {
            problem = "invalid refund amount";
            failure = MiloPaymentFailure.InvalidAmount;
            return false;
        }

        if (amount == 0)
            return true;

        float before;
        try
        {
            before = Money.GetCashBalance();
        }
        catch (Exception ex)
        {
            problem = $"could not read cash balance: {ex.Message}";
            failure = MiloPaymentFailure.BalanceUnavailable;
            return false;
        }

        if (!IsValidBalance(before))
        {
            problem = "cash balance is unavailable";
            failure = MiloPaymentFailure.BalanceUnavailable;
            return false;
        }

        return TryApply(
            before, amount, amount,
            out problem, out paymentNeedsReview, out failure);
    }

    private static bool TryApply(
        float before,
        float change,
        int expectedAmount,
        out string problem,
        out bool paymentNeedsReview,
        out MiloPaymentFailure failure)
    {
        problem = string.Empty;
        paymentNeedsReview = false;
        failure = MiloPaymentFailure.None;
        Exception? changeError = null;

        try
        {
            Money.ChangeCashBalance(change, true, true);
        }
        catch (Exception ex)
        {
            // The game or a balance-change listener may throw after the cash
            // moved. Read the balance before deciding whether to retry.
            changeError = ex;
        }

        float after;
        try
        {
            after = Money.GetCashBalance();
        }
        catch (Exception ex)
        {
            paymentNeedsReview = true;
            failure = MiloPaymentFailure.ChangeUnverifiable;
            problem = $"cash changed but could not be checked: {ex.Message}";
            return false;
        }

        if (!IsValidBalance(after))
        {
            paymentNeedsReview = true;
            failure = MiloPaymentFailure.ChangeUnverifiable;
            problem = "cash changed but the resulting balance is unavailable";
            return false;
        }

        float observed = change < 0f
            ? before - after
            : after - before;

        if (MatchesChange(observed, expectedAmount))
            return true;

        paymentNeedsReview = Math.Abs(observed) > BalanceTolerance;
        failure = MiloPaymentFailure.ChangeMismatch;
        problem = paymentNeedsReview
            ? $"cash changed by an unexpected amount ({observed:N2}); " +
              "the order needs review"
            : changeError == null
                ? "cash balance did not change"
                : $"cash change failed: {changeError.Message}";
        return false;
    }

    private static bool IsValidBalance(float balance) =>
        !float.IsNaN(balance) &&
        !float.IsInfinity(balance) &&
        balance >= 0f;

    private static bool MatchesChange(float observed, int requested) =>
        Math.Abs(observed - requested) <= BalanceTolerance;
}
