namespace MixService.Mixing;

/// <summary>
/// Why a wallet call failed, separated from the diagnostic message so the
/// dialogue layer can pick a fitting bank without parsing English.
/// </summary>
public enum MiloPaymentFailure
{
    None = 0,

    // The caller asked for a nonsensical amount.
    InvalidAmount = 1,

    // Cash could not be read at all, so nothing can be proven either way.
    BalanceUnavailable = 2,

    // The player simply does not carry enough cash.
    InsufficientCash = 3,

    // Cash may have moved but the new balance could not be read.
    ChangeUnverifiable = 4,

    // Cash moved by an amount we did not ask for.
    ChangeMismatch = 5
}
