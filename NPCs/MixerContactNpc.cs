using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using MixService.Mixing;
using S1API.DeadDrops;
using S1API.Entities;
using S1API.GameTime;
using S1API.Messaging;
using S1API.Products;
using S1API.Saveables;
using S1API.Items;

namespace MixService.NPCs;

public sealed class MixerContactNpc : NPC
{
    private const int TargetPageSize = 3;

    private const string CocaineProductId = "cocaine";
    private const string BrickPackagingId = "brick";
    private const int BrickCapacity = 20;

    private const int ProcessingDurationMinutes = 6 * 60;

    public static MixerContactNpc? Instance { get; private set; }

    [SaveableField("mixservice_job")]
    private MixJob _job = new();

    private DeadDropInstance? _reservedDrop;
    private bool _depositAcknowledged;
    private string? _lastRejectedDeposit;

    private DeadDropInstance? _pickupDrop;
    private bool _pickupListenerAttached;

    private bool _processingClockAttached;

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
        _job.RestoreAssignedDrop();
    }

    public void InitializeAfterWorldLoad()
    {
        _job ??= new MixJob();
        _job.RestoreAssignedDrop();

        ReleaseReservedDrop();
        ReleasePickupDrop();
        DetachProcessingClock();

        MelonLogger.Msg(
            $"Initializing Milo after world load: {_job.State}");

        switch (_job.State)
        {
            case MixJobState.AwaitingPayment:
                RestoreLegacyAwaitingPayment();
                break;

            case MixJobState.AwaitingDeposit:
                RestoreAwaitingDeposit();
                break;

            case MixJobState.Processing:
                RestoreProcessing();
                break;

            case MixJobState.ReadyForPickup:
                RestoreReadyForPickup();
                break;

            default:
                _job.Reset();
                PresentIdleMenu();
                break;
        }
    }

    private void PresentIdleMenu()
    {
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

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
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        ProductDefinition[] targets =
            GetDiscoveredCocaineTargets();

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
            (targets.Length + TargetPageSize - 1)
            / TargetPageSize;

        if (page < 0)
            page = 0;

        if (page >= pageCount)
            page = 0;

        int start =
            page * TargetPageSize;

        int end =
            Math.Min(
                start + TargetPageSize,
                targets.Length);

        var responses = new List<Response>();

        for (int i = start; i < end; i++)
        {
            ProductDefinition target =
                targets[i];

            var response = new Response
            {
                Label =
                    $"mixservice_target_{page}_{i}",

                Text =
                    target.Name
            };

            string targetId =
                target.ID;

            string targetName =
                target.Name;

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

                Text =
                    "more..."
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

            Text =
                "never mind"
        };

        cancel.OnTriggered =
            PresentIdleMenu;

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
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        var responses =
            new List<Response>();

        int[] quantities =
        {
            1,
            5,
            10,
            20
        };

        foreach (int quantity in quantities)
        {
            int capturedQuantity =
                quantity;

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
        // Old response buttons can remain visible in the phone conversation.
        // Never let one replace an unpaid or in-progress job.
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        DeadDropInstance? assignedDrop =
            OrderDropAssignment.ChooseEmpty();

        if (assignedDrop == null)
        {
            Messaging.SendTextMessage(
                "can't find a clean drop right now. try me later.",
                new[] { CreateStatusResponse() },
                responseDelay: 0f);
            return;
        }

        _job.Reset();

        _job.State = MixJobState.AwaitingDeposit;
        _job.AssignedDrop = assignedDrop.GUID;
        _reservedDrop = assignedDrop;

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

        _job.QuotedFee = MixFeePolicy.Quote(
            _job.PackagingId,
            _job.PackageCapacity,
            _job.RequiredPackages);

        AttachReservedDropListener();

        MelonLogger.Msg(
            $"Quoted order: {requiredBricks} cocaine bricks -> " +
            $"'{targetProductName}' for ${_job.QuotedFee:N0} " +
            $"at '{assignedDrop.Name}' GUID={assignedDrop.GUID}.");

        SendDepositInstructions();
    }

    private void SendDepositInstructions()
    {
        if (_job.State != MixJobState.AwaitingDeposit ||
            _reservedDrop == null)
            return;

        string brickWord =
            _job.RequiredPackages == 1 ? "brick" : "bricks";

        string payment = _job.RemainingBalance > 0
            ? _job.AmountPaid > 0
                ? $"my cut's ${_job.QuotedFee:N0} total; " +
                  $"${_job.RemainingBalance:N0} remains due " +
                  "when i collect it. "
                : $"my cut's ${_job.QuotedFee:N0}, " +
                  "due when i collect it. "
            : $"my cut's ${_job.QuotedFee:N0}, " +
              "already paid. ";

        MelonLogger.Msg(
            $"Assigned order: ${_job.QuotedFee:N0} quoted; " +
            $"{_job.RequiredPackages} cocaine {brickWord} -> " +
            $"'{_job.TargetProductName}' at '{_reservedDrop.Name}' " +
            $"GUID={_reservedDrop.GUID}");

        Messaging.SendTextMessage(
            $"{payment}drop " +
            $"{_job.RequiredPackages} cocaine {brickWord} " +
            $"at {FormatDropLocation(_reservedDrop)}. " +
            $"keep the whole batch the same quality.",
            new[] { CreateCancelOrderResponse() },
            responseDelay: 0f);
    }

    private void RestoreLegacyAwaitingPayment()
    {
        DeadDropInstance? assignedDrop =
            OrderDropAssignment.ChooseEmpty();

        if (assignedDrop == null)
        {
            Messaging.SendTextMessage(
                "need a clean drop for your order. check back soon.",
                new[] { CreateStatusResponse(), CreateCancelOrderResponse() },
                responseDelay: 0f);
            return;
        }

        _job.AssignedDrop = assignedDrop.GUID;
        _job.State = MixJobState.AwaitingDeposit;
        _reservedDrop = assignedDrop;
        AttachReservedDropListener();
        SendDepositInstructions();
    }

    private void RestoreAwaitingDeposit()
    {
        if (string.IsNullOrWhiteSpace(
                _job.AssignedDrop))
        {
            MelonLogger.Warning(
                "Saved order had no assigned-drop GUID. " +
                "Cancelling it.");

            if (!RefundUncollectedOrder())
                return;
            _job.Reset();
            PresentIdleMenu();
            return;
        }

        _reservedDrop =
            OrderDropAssignment.Resolve(
                _job.AssignedDrop);

        if (_reservedDrop == null)
        {
            MelonLogger.Warning(
                $"Could not restore dead drop " +
                $"'{_job.AssignedDrop}'. " +
                $"Cancelling saved order.");

            if (!RefundUncollectedOrder())
                return;
            _job.Reset();

            Messaging.SendTextMessage(
                "lost the drop. we'll have to start that one over.",
                responses: null,
                responseDelay: 0f);

            PresentIdleMenu();
            return;
        }

        AttachReservedDropListener();

        if (_job.PaymentNeedsReview)
        {
            Messaging.SendTextMessage(
                $"the cash change for this order needs review. " +
                $"the assigned drop is {_reservedDrop.Name}.",
                new[] { CreateStatusResponse() },
                responseDelay: 0f);
            return;
        }

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
            $"at {_reservedDrop.Name}. " +
            (_job.RemainingBalance > 0
                ? $"${_job.RemainingBalance:N0} due when i collect it."
                : "your cut's paid."),
            new[] { CreateStatusResponse(), CreateCancelOrderResponse() });

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

        if (_job.PaymentNeedsReview)
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

        StartProcessing(batch);
    }

    private void StartProcessing(
        ProductDeposit batch)
    {
        if (_reservedDrop == null)
            return;

        DeadDropInstance drop =
            _reservedDrop;

        if (_job.PaymentNeedsReview)
        {
            Messaging.SendTextMessage(
                "the cash change for this order needs review. " +
                "i've left your product in the drop.",
                new[] { CreateStatusResponse() },
                responseDelay: 0f);
            return;
        }

        if (_job.QuotedFee <= 0 ||
            _job.AmountPaid < 0 ||
            _job.AmountPaid > _job.QuotedFee)
        {
            MelonLogger.Error(
                "Order has an invalid fee or paid amount; " +
                "the deposited product was left in place.");
            return;
        }

        int chargedNow = _job.RemainingBalance;
        if (chargedNow > 0)
        {
            if (!MiloWalletPayment.TryCharge(
                    chargedNow,
                    out string paymentProblem,
                    out bool paymentNeedsReview))
            {
                _job.PaymentNeedsReview = paymentNeedsReview;
                Messaging.SendTextMessage(
                    $"can't collect the batch yet. {paymentProblem}. " +
                    $"your product's still at {drop.Name}.",
                    paymentNeedsReview
                        ? new[] { CreateStatusResponse() }
                        : new[] { CreateStatusResponse(), CreateCancelOrderResponse() },
                    responseDelay: 0f);
                return;
            }

            // Keep credit on the order until the batch is collected or a
            // compensating refund has been verified.
            _job.AmountPaid += chargedNow;
        }

        /*
         * Detach BEFORE mutating storage so our own removal
         * doesn't recursively fire the deposit handler.
         */
        drop.Storage.OnContentsChanged -=
            OnDropContentsChanged;

        _depositAcknowledged = true;
        _lastRejectedDeposit = null;

        if (!DepositInspector.TryRemoveExactBatch(
                drop.Storage,
                batch,
                out int removedPackages,
                out string problem))
        {
            MelonLogger.Error(
                $"Failed to collect validated batch: {problem}");

            /*
             * With no product removed, undo only the charge made during
             * this attempt. If that refund cannot be verified, retain the
             * paid credit so a retry never charges the same fee twice.
             */
            if (removedPackages == 0)
            {
                if (chargedNow > 0)
                {
                    if (MiloWalletPayment.TryRefund(
                            chargedNow,
                            out string refundProblem,
                            out bool refundNeedsReview))
                    {
                        _job.AmountPaid -= chargedNow;
                    }
                    else
                    {
                        _job.PaymentNeedsReview = refundNeedsReview;
                        MelonLogger.Error(
                            $"Could not reverse collection charge: " +
                            refundProblem);
                    }
                }

                _depositAcknowledged = false;

                drop.Storage.OnContentsChanged +=
                    OnDropContentsChanged;

                RejectDeposit(
                    $"couldn't collect the batch: {problem}");

                return;
            }

            /*
             * Partial removal should never occur after our
             * validation pass. Don't silently continue.
             */
            MelonLogger.Error(
                $"PARTIAL BATCH REMOVAL: removed " +
                $"{removedPackages} of " +
                $"{batch.StackQuantity} packages.");

            return;
        }

        _reservedDrop = null;

        _job.InputQuality =
            batch.Quality;

        _job.StartedAtGameMinutes =
            GameClock.CurrentAbsoluteMinutes;

        _job.CompletesAtGameMinutes =
            _job.StartedAtGameMinutes
            + ProcessingDurationMinutes;

        _job.State =
            MixJobState.Processing;

        AttachProcessingClock();

        MelonLogger.Msg(
            $"Processing started: " +
            $"{batch.StackQuantity}x " +
            $"{batch.Quality} " +
            $"{batch.ProductName} " +
            $"{batch.PackagingName} -> " +
            $"'{_job.TargetProductName}'. " +
            $"Start={_job.StartedAtGameMinutes}, " +
            $"Complete={_job.CompletesAtGameMinutes}");

        Messaging.SendTextMessage(
            $"got it. i'll have " +
            $"{_job.TargetProductName} " +
            $"ready in about six hours.",
            new[] { CreateStatusResponse() },
            responseDelay: 0f);
    }

    private void RestoreProcessing()
    {
        AttachProcessingClock();

        /*
         * Handles loading a save after the completion
         * deadline already passed.
         */
        if (CheckProcessingCompletion())
            return;

        int remaining =
            Math.Max(
                0,
                _job.CompletesAtGameMinutes
                - GameClock.CurrentAbsoluteMinutes);

        Messaging.SendTextMessage(
            $"still working on " +
            $"{_job.TargetProductName}. " +
            $"about {FormatDuration(remaining)} left.",
            new[] { CreateStatusResponse() },
            responseDelay: 0f);

        MelonLogger.Msg(
            $"Restored processing order. " +
            $"Now={GameClock.CurrentAbsoluteMinutes}, " +
            $"Complete={_job.CompletesAtGameMinutes}, " +
            $"Remaining={remaining}m");
    }

    private Response CreateStatusResponse()
    {
        var status = new Response
        {
            Label = "mixservice_check_status",
            Text = "check my batch"
        };

        status.OnTriggered =
            SendCurrentStatus;

        return status;
    }

    private void SendCurrentStatus()
    {
        switch (_job.State)
        {
            case MixJobState.AwaitingPayment:
                RestoreLegacyAwaitingPayment();
                break;

            case MixJobState.Processing:
            {
                int remaining =
                    Math.Max(
                        0,
                        _job.CompletesAtGameMinutes
                        - GameClock.CurrentAbsoluteMinutes);

                Messaging.SendTextMessage(
                    $"still working on " +
                    $"{_job.TargetProductName}. " +
                    $"about {FormatDuration(remaining)} left.",
                    new[] { CreateStatusResponse() },
                    responseDelay: 0f);

                break;
            }

            case MixJobState.ReadyForPickup:
                EnsurePickupReady();
                break;

            case MixJobState.AwaitingDeposit:
                if (_job.PaymentNeedsReview)
                {
                    Messaging.SendTextMessage(
                        "the cash change for this order needs review. " +
                        "i've left your product in the assigned drop.",
                        new[] { CreateStatusResponse() },
                        responseDelay: 0f);
                    break;
                }

                if (_reservedDrop != null && !_reservedDrop.IsEmpty)
                {
                    OnDropContentsChanged();
                    if (_job.State != MixJobState.AwaitingDeposit ||
                        _job.PaymentNeedsReview)
                        break;
                }

                Messaging.SendTextMessage(
                    $"still waiting on " +
                    $"{_job.RequiredPackages} cocaine " +
                    $"{(_job.RequiredPackages == 1 ? "brick" : "bricks")} " +
                    $"at {_reservedDrop?.Name ?? "your assigned drop"}. " +
                    (_job.RemainingBalance > 0
                        ? $"${_job.RemainingBalance:N0} due on collection."
                        : "your cut's paid."),
                    new[] { CreateStatusResponse(), CreateCancelOrderResponse() },
                    responseDelay: 0f);

                break;

            default:
                PresentIdleMenu();
                break;
        }
    }

    private void EnsurePickupReady()
    {
        if (_job.State !=
            MixJobState.ReadyForPickup)
        {
            return;
        }

        /*
        * Output already exists from a previous session/save.
        * Reconnect to the persisted pickup drop instead of
        * manufacturing another batch.
        */
        if (_job.OutputSpawned)
        {
            if (string.IsNullOrWhiteSpace(
                    _job.PickupDropGuid))
            {
                MelonLogger.Error(
                    "Ready job says output was spawned " +
                    "but has no pickup-drop GUID.");

                return;
            }

            DeadDropInstance? existingDrop =
                DeadDropManager.GetByGUID(
                    _job.PickupDropGuid);

            if (existingDrop == null)
            {
                MelonLogger.Error(
                    $"Could not restore pickup drop " +
                    $"'{_job.PickupDropGuid}'.");

                Messaging.SendTextMessage(
                    "something's wrong with the pickup. " +
                    "don't place another order yet.",
                    new[] { CreateStatusResponse() },
                    responseDelay: 0f);

                return;
            }

            AttachPickupDrop(existingDrop);

            int remainingPackages =
                CountMatchingPickupPackages(
                    existingDrop);

            /*
            * If the save says output existed but it is no
            * longer in the drop, the player already collected it.
            */
            if (remainingPackages <= 0)
            {
                CompletePickup();
                return;
            }

            SendPickupReadyMessage(
                existingDrop,
                remainingPackages);

            return;
        }

        DeadDropInstance? pickupDrop =
            ResolvePickupDropForNewOutput();

        if (pickupDrop == null)
        {
            MelonLogger.Warning(
                "No empty dead drop available for completed order.");

            Messaging.SendTextMessage(
                $"{_job.TargetProductName} is done, " +
                $"but i need a clean drop. check back.",
                new[] { CreateStatusResponse() },
                responseDelay: 0f);

            return;
        }

        if (ItemManager.GetDefinition(
                _job.TargetProductId)
            is not ProductDefinition target)
        {
            MelonLogger.Error(
                $"Could not resolve target product " +
                $"'{_job.TargetProductId}'.");

            return;
        }

        if (ItemManager.GetDefinition(
                _job.PackagingId)
            is not PackagingDefinition packaging)
        {
            MelonLogger.Error(
                $"Could not resolve packaging " +
                $"'{_job.PackagingId}'.");

            return;
        }

        if (!PackagedProductFactory.TryCreate(
                target,
                packaging,
                _job.RequiredPackages,
                _job.InputQuality,
                out ProductInstance? output,
                out string problem))
        {
            MelonLogger.Error(
                $"Could not create completed batch: {problem}");

            Messaging.SendTextMessage(
                "hit a problem packaging your batch. " +
                "don't place another order yet.",
                new[] { CreateStatusResponse() },
                responseDelay: 0f);

            return;
        }

        if (output == null)
            return;

        _job.PickupDropGuid =
            pickupDrop.GUID;

        /*
        * Insert the one packaged product stack. Its quantity
        * is RequiredPackages, so 1 means one brick and 10 means
        * ten bricks in the stack.
        */
        pickupDrop.Storage.AddItem(output);

        int insertedPackages =
            CountMatchingPickupPackages(
                pickupDrop);

        if (insertedPackages !=
            _job.RequiredPackages)
        {
            MelonLogger.Error(
                $"Pickup verification failed at " +
                $"'{pickupDrop.Name}': expected " +
                $"{_job.RequiredPackages} package(s), found " +
                $"{insertedPackages}.");

            /*
            * If anything was inserted, mark it spawned so
            * a retry cannot duplicate product.
            */
            if (insertedPackages > 0)
            {
                _job.OutputSpawned = true;
                AttachPickupDrop(pickupDrop);
            }

            return;
        }

        _job.OutputSpawned = true;

        AttachPickupDrop(pickupDrop);

        MelonLogger.Msg(
            $"Spawned completed order: " +
            $"{_job.RequiredPackages}x " +
            $"{_job.InputQuality} " +
            $"{_job.TargetProductName} " +
            $"{_job.PackagingName} " +
            $"at '{pickupDrop.Name}' " +
            $"GUID={pickupDrop.GUID}");

        SendPickupReadyMessage(
            pickupDrop,
            insertedPackages);
    }

    private DeadDropInstance?
        ResolvePickupDropForNewOutput()
    {
        /*
        * Prefer the original handoff drop. It gives the whole
        * exchange a nice "same spot" feel and avoids unnecessary
        * map hopping.
        */
        if (!string.IsNullOrWhiteSpace(
                _job.AssignedDrop))
        {
            DeadDropInstance? original =
                OrderDropAssignment.Resolve(
                    _job.AssignedDrop);

            if (original != null &&
                original.IsEmpty)
            {
                return original;
            }
        }

        /*
        * Someone/native gameplay occupied it while Milo was
        * working, so choose another clean drop instead.
        */
        return OrderDropAssignment.ChooseEmpty();
    }

    private int CountMatchingPickupPackages(
        DeadDropInstance drop)
    {
        int total = 0;

        foreach (ProductDeposit deposit
                in DepositInspector.Inspect(
                    drop.Storage))
        {
            if (!string.Equals(
                    deposit.ProductId,
                    _job.TargetProductId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(
                    deposit.PackagingId,
                    _job.PackagingId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(
                    deposit.Quality,
                    _job.InputQuality,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (deposit.PackageCapacity !=
                _job.PackageCapacity)
            {
                continue;
            }

            total +=
                deposit.StackQuantity;
        }

        return total;
    }

    private void SendPickupReadyMessage(
        DeadDropInstance drop,
        int packageCount)
    {
        string packageWord =
            packageCount == 1
                ? _job.PackagingName.ToLowerInvariant()
                : _job.PackagingName.ToLowerInvariant() + "s";

        Messaging.SendTextMessage(
            $"{_job.TargetProductName} is done. " +
            $"left {packageCount} " +
            $"{_job.InputQuality.ToLowerInvariant()} " +
            $"{packageWord} at {FormatDropLocation(drop)}.",
            new[] { CreateStatusResponse() },
            responseDelay: 0f);
    }

    private static string FormatDropLocation(
        DeadDropInstance drop)
    {
        string name = drop.Name?.Trim() ?? "";
        string description =
            (drop.Description?.Trim() ?? "")
            .TrimEnd('.', '!', '?');

        if (string.IsNullOrWhiteSpace(name))
            return description;

        if (string.IsNullOrWhiteSpace(description) ||
            DropLocationsOverlap(name, description))
            return name;

        return $"{name} ({description})";
    }

    private static bool DropLocationsOverlap(
        string name,
        string description)
    {
        string normalizedName = NormalizeDropLocation(name);
        string normalizedDescription =
            NormalizeDropLocation(description);

        if (normalizedName.Length == 0 ||
            normalizedDescription.Length == 0)
            return false;

        string paddedName = $" {normalizedName} ";
        string paddedDescription =
            $" {normalizedDescription} ";

        return paddedName.Contains(
                paddedDescription.Trim(),
                StringComparison.Ordinal) ||
            paddedDescription.Contains(
                paddedName.Trim(),
                StringComparison.Ordinal);
    }

    private static string NormalizeDropLocation(string value)
    {
        IEnumerable<string> words = value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(
                    word.Where(char.IsLetterOrDigit).ToArray())
                .ToLowerInvariant())
            .Where(word => word.Length > 0 &&
                word != "a" && word != "an" && word != "the");

        return string.Join(" ", words);
    }

    private void AttachPickupDrop(
        DeadDropInstance drop)
    {
        ReleasePickupDrop();

        _pickupDrop = drop;

        _pickupDrop.Storage.OnContentsChanged +=
            OnPickupContentsChanged;

        _pickupListenerAttached = true;
    }

    private void ReleasePickupDrop()
    {
        if (_pickupDrop != null &&
            _pickupListenerAttached)
        {
            _pickupDrop.Storage.OnContentsChanged -=
                OnPickupContentsChanged;
        }

        _pickupDrop = null;
        _pickupListenerAttached = false;
    }

    private void OnPickupContentsChanged()
    {
        if (_pickupDrop == null)
            return;

        if (_job.State !=
            MixJobState.ReadyForPickup)
        {
            return;
        }

        if (!_job.OutputSpawned)
            return;

        int remaining =
            CountMatchingPickupPackages(
                _pickupDrop);

        if (remaining > 0)
        {
            MelonLogger.Msg(
                $"Pickup partially collected; " +
                $"{remaining} package(s) remain.");

            return;
        }

        CompletePickup();
    }

    private void CompletePickup()
    {
        string target =
            _job.TargetProductName;

        int packageCount =
            _job.RequiredPackages;

        ReleasePickupDrop();
        ReleaseReservedDrop();
        DetachProcessingClock();

        _job.Reset();

        MelonLogger.Msg(
            $"Pickup completed: " +
            $"{packageCount}x '{target}' collected.");

        var another = new Response
        {
            Label =
                "mixservice_after_pickup_new_order",

            Text =
                "got another batch"
        };

        another.OnTriggered = () =>
        {
            PresentTargetMenu(0);
        };

        Messaging.SendTextMessage(
            "all yours. hit me up when you need another one.",
            new[] { another },
            responseDelay: 0f);
    }

    private void RestoreReadyForPickup()
    {
        EnsurePickupReady();
    }

    private void AttachProcessingClock()
    {
        if (_processingClockAttached)
            return;

        TimeManager.OnTick +=
            OnGameTimeTick;

        TimeManager.OnSleepEnd +=
            OnSleepEnd;

        _processingClockAttached = true;
    }

    private void DetachProcessingClock()
    {
        if (!_processingClockAttached)
            return;

        TimeManager.OnTick =
            (TimeManager.OnTick - OnGameTimeTick)!;

        TimeManager.OnSleepEnd =
            (TimeManager.OnSleepEnd - OnSleepEnd)!;

        _processingClockAttached = false;
    }

    private void OnGameTimeTick()
    {
        CheckProcessingCompletion();
    }

    private void OnSleepEnd(int minutesSkipped)
    {
        MelonLogger.Msg(
            $"Sleep ended; skipped " +
            $"{minutesSkipped} game minutes.");

        CheckProcessingCompletion();
    }

    private bool CheckProcessingCompletion()
    {
        if (_job.State !=
            MixJobState.Processing)
        {
            return false;
        }

        int now =
            GameClock.CurrentAbsoluteMinutes;

        if (now <
            _job.CompletesAtGameMinutes)
        {
            return false;
        }

        _job.State =
            MixJobState.ReadyForPickup;

        DetachProcessingClock();

        MelonLogger.Msg(
            $"Processing completed: " +
            $"'{_job.TargetProductName}'. " +
            $"Now={now}, " +
            $"Deadline={_job.CompletesAtGameMinutes}");

        EnsurePickupReady();

        return true;
    }

    private static string FormatDuration(
        int totalMinutes)
    {
        totalMinutes =
            Math.Max(0, totalMinutes);

        int hours =
            totalMinutes / 60;

        int minutes =
            totalMinutes % 60;

        if (hours <= 0)
            return $"{minutes}m";

        if (minutes == 0)
            return $"{hours}h";

        return $"{hours}h {minutes}m";
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

        cancel.OnTriggered =
            CancelOrder;

        return cancel;
    }

    private void CancelOrder()
    {
        if (_job.State !=
            MixJobState.AwaitingDeposit &&
            _job.State != MixJobState.AwaitingPayment)
        {
            SendCurrentStatus();
            return;
        }

        string target =
            _job.TargetProductName;

        if (!RefundUncollectedOrder())
            return;

        ReleaseReservedDrop();
        DetachProcessingClock();

        _job.Reset();

        MelonLogger.Msg(
            $"Cancelled order for '{target}'.");

        var startAgain = new Response
        {
            Label =
                "mixservice_after_cancel_new_order",

            Text =
                "set up another"
        };

        startAgain.OnTriggered = () =>
        {
            PresentTargetMenu(0);
        };

        Messaging.SendTextMessage(
            "cancelled.",
            new[] { startAgain });
    }

    private bool RefundUncollectedOrder()
    {
        if (_job.PaymentNeedsReview)
        {
            Messaging.SendTextMessage(
                "the cash change on this order needs review. " +
                "i can't cancel it automatically yet.",
                new[] { CreateStatusResponse() },
                responseDelay: 0f);
            return false;
        }

        if (MiloWalletPayment.TryRefund(
                _job.AmountPaid,
                out string problem,
                out bool paymentNeedsReview))
            return true;

        _job.PaymentNeedsReview = paymentNeedsReview;
        MelonLogger.Error(problem);
        Messaging.SendTextMessage(
            "can't settle your refund right now. " +
            "the order's still on my books.",
            new[] { CreateStatusResponse() },
            responseDelay: 0f);
        return false;
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
        ReleasePickupDrop();
        DetachProcessingClock();

        if (ReferenceEquals(Instance, this))
            Instance = null;

        base.OnDestroyed();
    }
}
