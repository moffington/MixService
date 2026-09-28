using System;
using S1API.DeadDrops;

namespace MixService.Mixing;

/// <summary>
/// Resolves the drop stored on an order and chooses a clean drop for a new one.
/// No particular world drop is part of Milo's transaction contract.
/// </summary>
public static class OrderDropAssignment
{
    private static readonly Random Random = new();

    public static DeadDropInstance? ChooseEmpty()
    {
        DeadDropInstance[] available = DeadDropManager.Empty;
        return available.Length == 0
            ? null
            : available[Random.Next(available.Length)];
    }

    public static DeadDropInstance? Resolve(string guid) =>
        string.IsNullOrWhiteSpace(guid)
            ? null
            : DeadDropManager.GetByGUID(guid);
}
