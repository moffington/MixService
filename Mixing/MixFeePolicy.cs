using System;
using MixService.Utils;

namespace MixService.Mixing;

/// <summary>
/// Sets Milo's service price per package. Order handling stores the quote,
/// leaving this policy free to consider relationship or market state later.
/// </summary>
public static class MixFeePolicy
{
    public static int Quote(
        string packagingId,
        int packageCapacity,
        int packageCount,
        int pricePerBrick = Constants.Defaults.PricePerBrick)
    {
        if (string.IsNullOrWhiteSpace(packagingId))
            throw new ArgumentException("Packaging is required.", nameof(packagingId));
        if (packageCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(packageCapacity));
        if (packageCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(packageCount));
        if (pricePerBrick <= 0)
            throw new ArgumentOutOfRangeException(nameof(pricePerBrick));

        // The current order menu offers bricks only. Other packaging can be
        // priced by capacity when future order types are introduced.
        int perPackage = string.Equals(
            packagingId, "brick", StringComparison.OrdinalIgnoreCase)
            ? pricePerBrick
            : Math.Max(100, checked(packageCapacity * 70));

        return checked(perPackage * packageCount);
    }
}
