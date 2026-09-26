using MelonLoader;
using S1API.DeadDrops;
using S1API.Entities;
using S1API.Messaging;

namespace MixService.NPCs;

public sealed class MixerContactNpc : NPC
{
    public static MixerContactNpc? Instance { get; private set; }

    private static readonly System.Random Random = new();

    private DeadDropInstance? _reservedDrop;
    private bool _depositAcknowledged;

    public override bool IsPhysical => false;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        builder.WithIdentity(
            id: "mixservice:milo",
            firstName: "Milo",
            lastName: "");
    }

    protected override void OnCreated()
    {
        base.OnCreated();

        Instance = this;

        var orderResponse = new Response
        {
            Label = "mixservice_test_order",
            Text = "got some work for you"
        };

        orderResponse.OnTriggered = ReserveDeadDrop;

        Messaging.SendTextMessage(
            "yo. need something?",
            new[] { orderResponse });
    }

    private void ReserveDeadDrop()
    {
        ReleaseReservedDrop();

        DeadDropInstance[] emptyDrops = DeadDropManager.Empty;

        if (emptyDrops.Length == 0)
        {
            Messaging.SendTextMessage(
                "can't find a clean drop right now.",
                responses: null,
                responseDelay: 0f);

            MelonLogger.Warning("[MixService] No empty dead drops were available.");
            return;
        }

        _reservedDrop =
            emptyDrops[Random.Next(emptyDrops.Length)];

        _depositAcknowledged = false;
        _reservedDrop.Storage.OnContentsChanged += OnDropContentsChanged;

        MelonLogger.Msg(
            $"[MixService] Reserved dead drop '{_reservedDrop.Name}' " +
            $"GUID={_reservedDrop.GUID} " +
            $"Description='{_reservedDrop.Description}'");

        Messaging.SendTextMessage(
            $"yeah. leave it at {_reservedDrop.Name}. {_reservedDrop.Description}",
            responses: null,
            responseDelay: 0f);
    }

    private void OnDropContentsChanged()
    {
        if (_reservedDrop == null)
            return;

        if (_depositAcknowledged)
            return;

        if (_reservedDrop.IsEmpty)
            return;

        MelonLogger.Msg(
            $"[MixService] Deposit detected at '{_reservedDrop.Name}'. " +
            $"ItemCount={_reservedDrop.ItemCount}");

        foreach (var slot in _reservedDrop.Storage.Slots)
        {
            var item = slot.ItemInstance;

            if (item == null)
                continue;

            var definition = item.Definition;

            MelonLogger.Msg(
                $"[MixService]   Slot: " +
                $"ID='{definition.ID}', " +
                $"Name='{definition.Name}', " +
                $"SlotQuantity={slot.Quantity}, " +
                $"InstanceQuantity={item.Quantity}, " +
                $"Type={item.GetType().FullName}");

            if (item is S1API.Products.ProductInstance product)
            {
                MelonLogger.Msg(
                    $"[MixService]     Product: " +
                    $"Packaged={product.IsPackaged}, " +
                    $"Quality={product.Quality}");

                if (product.IsPackaged)
                {
                    var packaging = product.AppliedPackaging;

                    MelonLogger.Msg(
                        $"[MixService]     Packaging: " +
                        $"ID='{packaging.ID}', " +
                        $"Name='{packaging.Name}', " +
                        $"Capacity={packaging.Quantity}");
                }
            }
        }

        _depositAcknowledged = true;

        Messaging.SendTextMessage(
            "got it. i'll take it from here.",
            responses: null,
            responseDelay: 0f);

        _reservedDrop.Storage.OnContentsChanged -= OnDropContentsChanged;
    }

    private void ReleaseReservedDrop()
    {
        if (_reservedDrop != null)
        {
            _reservedDrop.Storage.OnContentsChanged -= OnDropContentsChanged;
            _reservedDrop = null;
        }

        _depositAcknowledged = false;
    }

    protected override void OnDestroyed()
    {
        ReleaseReservedDrop();

        if (ReferenceEquals(Instance, this))
            Instance = null;

        base.OnDestroyed();
    }
}