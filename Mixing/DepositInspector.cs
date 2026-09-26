using System;
using System.Collections.Generic;
using S1API.Products;
using S1API.Storages;

namespace MixService.Mixing;

public static class DepositInspector
{
    public static IReadOnlyList<ProductDeposit> Inspect(
        StorageInstance storage)
    {
        var deposits = new List<ProductDeposit>();
        var slots = storage.Slots;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            var item = slot.ItemInstance;

            if (item is not ProductInstance product)
                continue;

            var definition = product.Definition;

            bool isPackaged = product.IsPackaged;

            string packagingId = string.Empty;
            string packagingName = string.Empty;
            int packageCapacity = 1;

            if (isPackaged)
            {
                var packaging = product.AppliedPackaging;

                packagingId = packaging.ID;
                packagingName = packaging.Name;
                packageCapacity = Math.Max(1, packaging.Quantity);
            }

            deposits.Add(
                new ProductDeposit(
                    slotIndex: i,
                    productId: definition.ID,
                    productName: definition.Name,
                    isPackaged: isPackaged,
                    packagingId: packagingId,
                    packagingName: packagingName,
                    packageCapacity: packageCapacity,
                    stackQuantity: slot.Quantity,
                    instanceQuantity: product.Quantity,
                    quality: product.Quality.ToString()));
        }

        return deposits;
    }

    /// <summary>
    /// Validates that every occupied slot contains compatible product:
    /// same product, same packaging, same quality.
    /// Compatible slots are combined into one logical batch.
    /// </summary>
    public static bool TryGetUniformBatch(
        StorageInstance storage,
        out ProductDeposit? batch,
        out string problem)
    {
        batch = null;
        problem = string.Empty;

        var slots = storage.Slots;
        var deposits = new List<ProductDeposit>();

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            var item = slot.ItemInstance;

            if (item == null)
                continue;

            if (item is not ProductInstance)
            {
                problem =
                    $"slot {i} contains non-product item '{item.Definition.Name}'";
                return false;
            }
        }

        deposits.AddRange(Inspect(storage));

        if (deposits.Count == 0)
        {
            problem = "no product found";
            return false;
        }

        ProductDeposit first = deposits[0];

        int totalStackQuantity = 0;
        int totalInstanceQuantity = 0;

        foreach (ProductDeposit deposit in deposits)
        {
            if (!first.IsSameBatchKind(deposit))
            {
                problem =
                    "drop contains mixed product, packaging, or quality";
                return false;
            }

            totalStackQuantity += deposit.StackQuantity;
            totalInstanceQuantity += deposit.InstanceQuantity;
        }

        batch = new ProductDeposit(
            slotIndex: -1,
            productId: first.ProductId,
            productName: first.ProductName,
            isPackaged: first.IsPackaged,
            packagingId: first.PackagingId,
            packagingName: first.PackagingName,
            packageCapacity: first.PackageCapacity,
            stackQuantity: totalStackQuantity,
            instanceQuantity: totalInstanceQuantity,
            quality: first.Quality);

        return true;
    }

    public static bool TryRemoveExactBatch(
        StorageInstance storage,
        ProductDeposit expected,
        out int removedPackages,
        out string problem)
    {
        removedPackages = 0;
        problem = string.Empty;

        /*
        * Revalidate immediately before mutation.
        * At this point the storage must contain nothing except
        * the exact uniform batch we previously accepted.
        */
        if (!TryGetUniformBatch(
                storage,
                out ProductDeposit? actual,
                out problem))
        {
            return false;
        }

        if (actual == null)
        {
            problem = "no product batch found";
            return false;
        }

        if (!actual.IsSameBatchKind(expected))
        {
            problem =
                "batch changed before it could be collected";

            return false;
        }

        if (actual.StackQuantity != expected.StackQuantity)
        {
            problem =
                $"batch quantity changed; expected " +
                $"{expected.StackQuantity}, found " +
                $"{actual.StackQuantity}";

            return false;
        }

        /*
        * TryRemoveQuantity normally isn't specific enough for us
        * because it only matches the product definition ID.
        *
        * Here it IS safe because we just proved that every occupied
        * slot contains the same product, packaging and quality,
        * and that its total quantity exactly matches the order.
        *
        * This also avoids Storage.RemoveItem's native-object identity
        * comparison, which does not survive our IL2CPP wrapper path.
        */
        removedPackages =
            storage.TryRemoveQuantity(
                expected.ProductId,
                expected.StackQuantity);

        if (removedPackages != expected.StackQuantity)
        {
            problem =
                $"storage removed {removedPackages} packages; " +
                $"expected {expected.StackQuantity}";

            return false;
        }

        return true;
    }

    public static string Describe(ProductDeposit batch)
    {
        string product =
            batch.ProductName.ToLowerInvariant();

        if (!batch.IsPackaged)
        {
            string unitWord =
                batch.StackQuantity == 1 ? "unit" : "units";

            return
                $"{batch.StackQuantity} {batch.Quality.ToLowerInvariant()} " +
                $"{product} {unitWord} ({batch.TotalProductUnits} total units)";
        }

        string packaging =
            batch.PackagingName.ToLowerInvariant();

        if (batch.StackQuantity != 1)
            packaging += "s";

        return
            $"{batch.StackQuantity} {batch.Quality.ToLowerInvariant()} " +
            $"{product} {packaging} ({batch.TotalProductUnits} total units)";
    }
}