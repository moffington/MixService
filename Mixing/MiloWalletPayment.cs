using System;
using S1API.Money;

namespace MixService.Mixing;

/// <summary>
/// The payment boundary for an order. A Milo cash drop can replace this
/// implementation without changing order or conversation state.
/// </summary>
public static class MiloWalletPayment
{
    public static bool TryCollect(int amount, out string problem)
    {
        problem = string.Empty;
        if (amount < 0)
        {
            problem = "invalid payment amount";
            return false;
        }

        try
        {
            if (Money.GetCashBalance() < amount)
            {
                problem = "not enough cash on hand";
                return false;
            }

            Money.ChangeCashBalance(-amount, true, true);
            return true;
        }
        catch (Exception ex)
        {
            problem = $"cash payment failed: {ex.Message}";
            return false;
        }
    }

    public static bool TryRefund(int amount, out string problem)
    {
        problem = string.Empty;
        try
        {
            if (amount > 0)
                Money.ChangeCashBalance(amount, true, true);
            return true;
        }
        catch (Exception ex)
        {
            problem = $"cash refund failed: {ex.Message}";
            return false;
        }
    }
}
