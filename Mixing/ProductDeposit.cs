using System;

namespace MixService.Mixing;

public sealed class ProductDeposit
{
    public ProductDeposit(
        int slotIndex,
        string productId,
        string productName,
        bool isPackaged,
        string packagingId,
        string packagingName,
        int packageCapacity,
        int stackQuantity,
        int instanceQuantity,
        string quality)
    {
        SlotIndex = slotIndex;
        ProductId = productId;
        ProductName = productName;

        IsPackaged = isPackaged;
        PackagingId = packagingId;
        PackagingName = packagingName;
        PackageCapacity = packageCapacity;

        StackQuantity = stackQuantity;
        InstanceQuantity = instanceQuantity;
        Quality = quality;
    }

    /// <summary>
    /// Source slot index. Aggregated batches use -1.
    /// </summary>
    public int SlotIndex { get; }

    public string ProductId { get; }
    public string ProductName { get; }

    public bool IsPackaged { get; }
    public string PackagingId { get; }
    public string PackagingName { get; }
    public int PackageCapacity { get; }

    /// <summary>
    /// Number of product instances/packages represented by the slot.
    /// </summary>
    public int StackQuantity { get; }

    /// <summary>
    /// Quantity reported by the wrapped ProductInstance.
    /// Retained separately while we learn the game's inventory semantics.
    /// </summary>
    public int InstanceQuantity { get; }

    /// <summary>
    /// Runtime-independent quality name, e.g. Standard, Premium, Heavenly.
    /// </summary>
    public string Quality { get; }

    public int TotalProductUnits =>
        StackQuantity * PackageCapacity;

    public bool IsSameBatchKind(ProductDeposit other)
    {
        return string.Equals(
                ProductId,
                other.ProductId,
                StringComparison.OrdinalIgnoreCase)
            && IsPackaged == other.IsPackaged
            && string.Equals(
                PackagingId,
                other.PackagingId,
                StringComparison.OrdinalIgnoreCase)
            && PackageCapacity == other.PackageCapacity
            && string.Equals(
                Quality,
                other.Quality,
                StringComparison.OrdinalIgnoreCase);
    }
}