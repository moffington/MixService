using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using MixService.Mixing;
using S1API.DeadDrops;
using S1API.Entities;
using S1API.Messaging;
using S1API.Products;
using S1API.Saveables;

namespace MixService.NPCs;

public sealed class MixerContactNpc : NPC
{
    private const int TargetPageSize = 3;

    private const string CocaineProductId = "cocaine";
    private const string BrickPackagingId = "brick";
    private const int BrickCapacity = 20;

    public static MixerContactNpc? Instance { get; private set; }

    private static readonly System.Random Random = new();

    [SaveableField("mixservice_job")]
    private MixJob _job = new();

    private DeadDropInstance? _reservedDrop;
    private bool _depositAcknowledged;
    private string? _lastRejectedDeposit;

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
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        _job ??= new MixJob();

        ReleaseReservedDrop();

        switch (_job.State)
        {
            case MixJobState.AwaitingDeposit:
                RestoreAwaitingDeposit();
                break;

            case MixJobState.Processing:
                Messaging.SendTextMessage(
                    "still working on your batch.",
                    responses: null,
                    responseDelay: 0f);
                break;

            case MixJobState.ReadyForPickup:
                Messaging.SendTextMessage(
                    "your batch is ready.",
                    responses: null,
                    responseDelay: 0f);
                break;

            default:
                _job.Reset();
                PresentIdleMenu();
                break;
        }
    }

    private void PresentIdleMenu()
    {
        var order = new Response
        {
            Label = "mixservice_new_order",
            Text = "need a batch mixed"
        };

        order.OnTriggered = () =>
        {
            PresentTargetMenu(0);
        };

        Messaging.SendTextMessage(
            "yo. need something?",
            new[] { order });
    }

    private void PresentTargetMenu(int page)
    {
        ProductDefinition[] targets = GetDiscoveredCocaineTargets();

        if (targets.Length == 0)
        {
            var retry = new Response
            {
                Label = "mixservice_no_targets_retry",
                Text = "never mind"
            };

            retry.OnTriggered = PresentIdleMenu;

            Messaging.SendTextMessage(
                "you don't have any cocaine mixes figured out yet.",
                new[] { retry });

            return;
        }

        int pageCount =
            (targets.Length + TargetPageSize - 1) /
            TargetPageSize;

        if (page < 0)
            page = 0;

        if (page >= pageCount)
            page = 0;

        int start = page * TargetPageSize;
        int end = Math.Min(
            start + TargetPageSize,
            targets.Length);

        var responses = new List<Response>();

        for (int i = start; i < end; i++)
        {
            ProductDefinition target = targets[i];

            var response = new Response
            {
                Label =
                    $"mixservice_target_{page}_{i}",
                Text = target.Name
            };

            string targetId = target.ID;
            string targetName = target.Name;

            response.OnTriggered = () =>
            {
                PresentQuantityMenu(
                    targetId,
                    targetName);
            };

            responses.Add(response);
        }

        if (pageCount > 1)
        {
            int nextPage =
                (page + 1) % pageCount;

            var more = new Response
            {
                Label =
                    $"mixservice_target_more_{page}",
                Text = "more..."
            };

            more.OnTriggered = () =>
            {
                PresentTargetMenu(nextPage);
            };

            responses.Add(more);
        }

        var cancel = new Response
        {
            Label =
                $"mixservice_target_cancel_{page}",
            Text = "never mind"
        };

        cancel.OnTriggered = PresentIdleMenu;

        responses.Add(cancel);

        Messaging.SendTextMessage(
            "what're we making?",
            responses.ToArray());
    }

    private static ProductDefinition[]
        GetDiscoveredCocaineTargets()
    {
        return ProductManager.DiscoveredProducts
            .Where(product =>
                product.PrimaryDrugType ==
                    DrugType.Cocaine
                && !string.Equals(
                    product.ID,
                    CocaineProductId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(product => product.Name)
            .ToArray();
    }

    private void PresentQuantityMenu(
        string targetProductId,
        string targetProductName)
    {
        var responses = new List<Response>();

        int[] quantities =
        {
            1,
            5,
            10,
            20
        };

        foreach (int quantity in quantities)
        {
            int capturedQuantity = quantity;

            var response = new Response
            {
                Label =
                    $"mixservice_qty_{capturedQuantity}",
                Text =
                    capturedQuantity == 1
                        ? "1 brick"
                        : $"{capturedQuantity} bricks"
            };

            response.OnTriggered = () =>
            {
                CreateOrder(
                    targetProductId,
                    targetProductName,
                    capturedQuantity);
            };

            responses.Add(response);
        }

        var back = new Response
        {
            Label = "mixservice_qty_back",
            Text = "different mix"
        };

        back.OnTriggered = () =>
        {
            PresentTargetMenu(0);
        };

        responses.Add(back);

        Messaging.SendTextMessage(
            $"how much {targetProductName}?",
            responses.ToArray());
    }

    private void CreateOrder(
        string targetProductId,
        string targetProductName,
        int requiredBricks)
    {
        ReleaseReservedDrop();

        DeadDropInstance[] emptyDrops =
            DeadDropManager.Empty;

        if (emptyDrops.Length == 0)
        {
            Messaging.SendTextMessage(
                "can't find a clean drop right now.",
                responses: null,
                responseDelay: 0f);

            MelonLogger.Warning(
                "No empty dead drops were available.");

            PresentIdleMenu();
            return;
        }

        _reservedDrop =
            emptyDrops[Random.Next(emptyDrops.Length)];

        _job.Reset();

        _job.State =
            MixJobState.AwaitingDeposit;

        _job.InputProductId =
            CocaineProductId;

        _job.InputProductName =
            "Cocaine";

        _job.TargetProductId =
            targetProductId;

        _job.TargetProductName =
            targetProductName;

        _job.PackagingId =
            BrickPackagingId;

        _job.PackagingName =
            "Brick";

        _job.PackageCapacity =
            BrickCapacity;

        _job.RequiredPackages =
            requiredBricks;

        _job.DeadDropGuid =
            _reservedDrop.GUID;

        AttachReservedDropListener();

        string brickWord =
            requiredBricks == 1
                ? "brick"
                : "bricks";

        MelonLogger.Msg(
            $"Created order: " +
            $"{requiredBricks} cocaine {brickWord} -> " +
            $"'{targetProductName}' " +
            $"at '{_reservedDrop.Name}' " +
            $"GUID={_reservedDrop.GUID}");

        Messaging.SendTextMessage(
            $"drop {requiredBricks} cocaine {brickWord} " +
            $"at {_reservedDrop.Name}. " +
            $"{_reservedDrop.Description}. " +
            $"keep the whole batch the same quality.",
            new[] { CreateCancelOrderResponse() });
    }

    private void RestoreAwaitingDeposit()
    {
        if (string.IsNullOrWhiteSpace(
                _job.DeadDropGuid))
        {
            MelonLogger.Warning(
                "Saved order had no dead-drop GUID. " +
                "Cancelling it.");

            _job.Reset();
            PresentIdleMenu();
            return;
        }

        _reservedDrop =
            DeadDropManager.GetByGUID(
                _job.DeadDropGuid);

        if (_reservedDrop == null)
        {
            MelonLogger.Warning(
                $"Could not restore dead drop " +
                $"'{_job.DeadDropGuid}'. " +
                $"Cancelling saved order.");

            _job.Reset();

            Messaging.SendTextMessage(
                "lost the drop. we'll have to start that one over.",
                responses: null,
                responseDelay: 0f);

            PresentIdleMenu();
            return;
        }

        AttachReservedDropListener();

        string brickWord =
            _job.RequiredPackages == 1
                ? "brick"
                : "bricks";

        MelonLogger.Msg(
            $"Restored awaiting order: " +
            $"{_job.RequiredPackages} cocaine {brickWord} -> " +
            $"'{_job.TargetProductName}' " +
            $"at '{_reservedDrop.Name}'");

        Messaging.SendTextMessage(
            $"still waiting on " +
            $"{_job.RequiredPackages} cocaine {brickWord} " +
            $"at {_reservedDrop.Name}.",
            new[] { CreateCancelOrderResponse() });

        /*
         * The player may have saved after placing items
         * in the dead drop but before this mod observed
         * the storage event. Inspect immediately.
         */
        if (!_reservedDrop.IsEmpty)
            OnDropContentsChanged();
    }

    private void AttachReservedDropListener()
    {
        if (_reservedDrop == null)
            return;

        _depositAcknowledged = false;
        _lastRejectedDeposit = null;

        _reservedDrop.Storage.OnContentsChanged +=
            OnDropContentsChanged;
    }

    private void OnDropContentsChanged()
    {
        if (_reservedDrop == null)
            return;

        if (_job.State !=
            MixJobState.AwaitingDeposit)
            return;

        if (_depositAcknowledged)
            return;

        if (_reservedDrop.IsEmpty)
        {
            _lastRejectedDeposit = null;
            return;
        }

        if (!DepositInspector.TryGetUniformBatch(
                _reservedDrop.Storage,
                out ProductDeposit? batch,
                out string problem))
        {
            RejectDeposit(problem);
            return;
        }

        if (batch == null)
            return;

        if (!string.Equals(
                batch.ProductId,
                _job.InputProductId,
                StringComparison.OrdinalIgnoreCase))
        {
            RejectDeposit(
                $"expected {_job.InputProductName}, " +
                $"found {batch.ProductName}");

            return;
        }

        if (!batch.IsPackaged)
        {
            RejectDeposit(
                "cocaine is unpackaged; bricks required");

            return;
        }

        if (!string.Equals(
                batch.PackagingId,
                _job.PackagingId,
                StringComparison.OrdinalIgnoreCase))
        {
            RejectDeposit(
                $"expected {_job.PackagingName}, " +
                $"found {batch.PackagingName}");

            return;
        }

        if (batch.PackageCapacity !=
            _job.PackageCapacity)
        {
            RejectDeposit(
                $"unexpected brick capacity " +
                $"{batch.PackageCapacity}; " +
                $"expected {_job.PackageCapacity}");

            return;
        }

        if (batch.StackQuantity !=
            _job.RequiredPackages)
        {
            RejectDeposit(
                $"need exactly " +
                $"{_job.RequiredPackages} bricks; " +
                $"drop contains " +
                $"{batch.StackQuantity}");

            return;
        }

        _lastRejectedDeposit = null;
        _depositAcknowledged = true;

        string description =
            DepositInspector.Describe(batch);

        MelonLogger.Msg(
            $"Validated order deposit: " +
            $"{description} -> " +
            $"'{_job.TargetProductName}'");

        /*
         * INTENTIONALLY NON-DESTRUCTIVE FOR THIS PASS.
         *
         * The order remains AwaitingDeposit.
         * Next pass will atomically consume the exact
         * product slots and transition to Processing.
         */

        Messaging.SendTextMessage(
            $"that's it. got {description}.",
            responses: null,
            responseDelay: 0f);
    }

    private void RejectDeposit(string problem)
    {
        if (_reservedDrop == null)
            return;

        string rejection =
            $"Deposit at '{_reservedDrop.Name}' " +
            $"not accepted: {problem}";

        if (string.Equals(
                rejection,
                _lastRejectedDeposit,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastRejectedDeposit =
            rejection;

        MelonLogger.Warning(rejection);
    }

    private Response CreateCancelOrderResponse()
    {
        var cancel = new Response
        {
            Label = "mixservice_cancel_order",
            Text = "cancel it"
        };

        cancel.OnTriggered = CancelOrder;

        return cancel;
    }

    private void CancelOrder()
    {
        string target =
            _job.TargetProductName;

        ReleaseReservedDrop();
        _job.Reset();

        MelonLogger.Msg(
            $"Cancelled order for '{target}'.");

        var startAgain = new Response
        {
            Label =
                "mixservice_after_cancel_new_order",
            Text = "set up another"
        };

        startAgain.OnTriggered = () =>
        {
            PresentTargetMenu(0);
        };

        Messaging.SendTextMessage(
            "cancelled.",
            new[] { startAgain });
    }

    private void ReleaseReservedDrop()
    {
        if (_reservedDrop != null)
        {
            _reservedDrop.Storage.OnContentsChanged -=
                OnDropContentsChanged;

            _reservedDrop = null;
        }

        _depositAcknowledged = false;
        _lastRejectedDeposit = null;
    }

    protected override void OnDestroyed()
    {
        ReleaseReservedDrop();

        if (ReferenceEquals(Instance, this))
            Instance = null;

        base.OnDestroyed();
    }
}