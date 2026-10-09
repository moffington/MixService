using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using MixService.Dialogue;
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

    private const string BrickPackagingId = "brick";
    private const int BrickCapacity = 20;

    private sealed class MixFamily
    {
        public MixFamily(DrugType drugType, string inputProductId, string inputProductName)
        {
            DrugType = drugType;
            InputProductId = inputProductId;
            InputProductName = inputProductName;
        }

        public DrugType DrugType { get; }
        public string InputProductId { get; }
        public string InputProductName { get; }
    }

    private static readonly MixFamily[] MixFamilies =
    {
        new(DrugType.Cocaine, "cocaine", "Cocaine"),
        new(DrugType.Methamphetamine, "meth", "Meth"),
        new(DrugType.Shrooms, "shrooms", "Shrooms"),
        new(DrugType.Marijuana, "weed", "Weed")
    };

    private const int ProcessingDurationMinutes = 6 * 60;

    public static MixerContactNpc? Instance { get; private set; }

    [SaveableField("mixservice_job")]
    private MixJob _job = new();

    private DeadDropInstance? _reservedDrop;
    private bool _depositAcknowledged;
    private MiloRepeatGuard _rejectionGuard = new MiloRepeatGuard();

    /*
     * Whether Milo has already put the current drop in front of the player
     * for this order. Once true, follow-up messages talk about "the drop" or
     * "same spot" instead of repeating a location the player already has.
     */
    private bool _depositLocationKnown;
    private bool _pickupLocationKnown;

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

        /*
         * A fresh session has told the player nothing about either drop, so
         * the first message after loading names its location again.
         */
        _depositLocationKnown = false;
        _pickupLocationKnown = false;

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

    // ---------------------------------------------------------------
    // Dialogue
    //
    // Order logic above decides what is true; everything below just hands
    // those facts to the dialogue layer and sends whatever comes back.
    // ---------------------------------------------------------------

    /// <summary>
    /// Facts every order line may need, read straight off the current job.
    /// </summary>
    private MiloLineFacts JobFacts()
    {
        return new MiloLineFacts
        {
            ProductName = _job.TargetProductName,
            InputProductName = _job.InputProductName,
            BrickCount = _job.RequiredPackages,
            Fee = _job.QuotedFee,
            Due = _job.RemainingBalance,
            Quality = _job.InputQuality,
            Packaging = _job.PackagingName,
            PackageCount = _job.RequiredPackages,
            Payment = _job.RemainingBalance <= 0
                ? MiloPaymentState.Paid
                : _job.AmountPaid > 0
                    ? MiloPaymentState.Partial
                    : MiloPaymentState.Unpaid,
            ProductCollected =
                _job.State == MixJobState.Processing ||
                _job.State == MixJobState.ReadyForPickup
        };
    }

    private MiloLineFacts DepositFacts(
        DeadDropInstance? drop)
    {
        MiloLineFacts facts = JobFacts();

        /*
         * Whether the line may name the drop is decided by LocationKnown, so
         * the location is simply available and the bank picks.
         */
        facts.LocationKnown = _depositLocationKnown;
        facts.DropLocation = drop != null
            ? FormatDropLocation(drop)
            : string.Empty;

        return facts;
    }

    private MiloLineFacts PickupFacts(DeadDropInstance drop)
    {
        MiloLineFacts facts = JobFacts();

        facts.LocationKnown = _pickupLocationKnown;
        facts.SameSpotAsDeposit = !string.IsNullOrWhiteSpace(
                _job.AssignedDrop) &&
            string.Equals(
                _job.AssignedDrop,
                drop.GUID,
                StringComparison.OrdinalIgnoreCase);
        facts.DropLocation = FormatDropLocation(drop);

        return facts;
    }

    private static Response Button(
        MiloIntent intent,
        Action onTriggered,
        string label)
    {
        var response = new Response
        {
            Label = label,

            // Label, not Line: button text is deterministic and must not
            // touch the latch that keeps Milo's messages from repeating.
            Text = MiloDialogue.Label(
                intent,
                new MiloLineFacts())
        };

        response.OnTriggered = onTriggered;

        return response;
    }

    private static Response QuantityButton(
        int quantity,
        Action<int> onTriggered)
    {
        var response = new Response
        {
            Label = $"mixservice_qty_{quantity}",
            Text = MiloDialogue.Label(
                MiloIntent.ButtonQuantity,
                new MiloLineFacts
                {
                    BrickCount = quantity
                })
        };

        response.OnTriggered = () => onTriggered(quantity);

        return response;
    }

    private void PresentIdleMenu()
    {
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        var order = Button(
            MiloIntent.ButtonNewOrder,
            PresentDrugMenu,
            "mixservice_new_order");

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.Greeting,
                new MiloLineFacts()),
            new[] { order });
    }

    private void PresentDrugMenu()
    {
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        var responses = new List<Response>();
        foreach (MixFamily family in MixFamilies)
        {
            MixFamily selected = family;
            var response = new Response
            {
                Label = $"mixservice_drug_{family.InputProductId}",
                Text = family.InputProductName
            };
            response.OnTriggered = () => PresentTargetMenu(selected, 0);
            responses.Add(response);
        }

        responses.Add(Button(
            MiloIntent.ButtonNeverMind,
            PresentIdleMenu,
            "mixservice_drug_cancel"));

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.DrugPrompt,
                new MiloLineFacts()),
            responses.ToArray());
    }

    private void PresentTargetMenu(MixFamily family, int page)
    {
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        ProductDefinition[] targets =
            GetDiscoveredTargets(family);
        bool hadDiscoveredTargets = targets.Length > 0;

        if (family.DrugType == DrugType.Marijuana)
        {
            if (!GameMixRecipeReader.TryReadWeedRecipes(
                    out IReadOnlyList<MixRecipeLink> recipes,
                    out string problem))
            {
                MelonLogger.Warning($"Weed recipes unavailable: {problem}");
                SendWeedRecipesUnavailable();
                return;
            }

            targets = targets
                .Where(product => WeedRecipeResolver.TryResolveBase(
                    product.ID, recipes, out _, out _))
                .ToArray();

            if (hadDiscoveredTargets && targets.Length == 0)
            {
                SendWeedRecipesUnavailable();
                return;
            }
        }

        if (targets.Length == 0)
        {
            var retry = Button(
                MiloIntent.ButtonDifferentDrug,
                PresentDrugMenu,
                "mixservice_no_targets_retry");

            Messaging.SendTextMessage(
                MiloDialogue.Line(
                    MiloIntent.NoTargetsDiscovered,
                    new MiloLineFacts
                    {
                        InputProductName = family.InputProductName
                    }),
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
                if (family.DrugType == DrugType.Marijuana)
                {
                    if (!TryGetWeedBase(
                            targetId,
                            out string baseId,
                            out string baseName,
                            out string problem))
                    {
                        MelonLogger.Warning(
                            $"Cannot order weed mix '{targetId}': {problem}");
                        SendWeedRecipesUnavailable();
                        return;
                    }

                    PresentQuantityMenu(
                        family, targetId, targetName, baseId, baseName);
                }
                else
                {
                    PresentQuantityMenu(
                        family,
                        targetId,
                        targetName,
                        family.InputProductId,
                        family.InputProductName);
                }
            };

            responses.Add(response);
        }

        if (pageCount > 1)
        {
            int nextPage =
                (page + 1) % pageCount;

            responses.Add(
                Button(
                    MiloIntent.ButtonMore,
                    () => PresentTargetMenu(family, nextPage),
                    $"mixservice_target_more_{page}"));
        }

        responses.Add(
            Button(
                MiloIntent.ButtonDifferentDrug,
                PresentDrugMenu,
                $"mixservice_target_cancel_{page}"));

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.TargetPrompt,
                new MiloLineFacts()),
            responses.ToArray());
    }

    private static ProductDefinition[]
        GetDiscoveredTargets(MixFamily family)
    {
        return ProductManager.DiscoveredProducts
            .Where(product =>
                product.PrimaryDrugType ==
                    family.DrugType
                && !IsBaseProduct(family, product.ID))
            .OrderBy(product => product.Name)
            .ToArray();
    }

    private static bool IsBaseProduct(MixFamily family, string productId)
    {
        if (family.DrugType == DrugType.Marijuana)
            return WeedRecipeResolver.IsBaseProduct(productId);

        return string.Equals(
            family.InputProductId,
            productId,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetWeedBase(
        string targetProductId,
        out string baseProductId,
        out string baseProductName,
        out string problem)
    {
        baseProductId = string.Empty;
        baseProductName = string.Empty;
        if (!GameMixRecipeReader.TryReadWeedRecipes(
                out IReadOnlyList<MixRecipeLink> recipes,
                out problem))
        {
            return false;
        }

        if (!WeedRecipeResolver.TryResolveBase(
                targetProductId,
                recipes,
                out baseProductId,
                out problem))
        {
            return false;
        }

        if (ItemManager.GetDefinition(baseProductId)
            is not ProductDefinition baseProduct ||
            baseProduct.PrimaryDrugType != DrugType.Marijuana)
        {
            problem = $"weed base '{baseProductId}' is unavailable";
            return false;
        }

        baseProductName = baseProduct.Name;
        return true;
    }

    private bool ValidateAwaitingWeedOrder(bool repeatMessage = false)
    {
        if (_job.State != MixJobState.AwaitingDeposit)
            return true;

        bool isWeedOrder = WeedRecipeResolver.IsBaseProduct(
                _job.InputProductId) ||
            ItemManager.GetDefinition(_job.TargetProductId)
                is ProductDefinition target &&
            target.PrimaryDrugType == DrugType.Marijuana;

        if (!isWeedOrder)
            return true;

        if (!TryGetWeedBase(
                _job.TargetProductId,
                out string expectedBaseId,
                out string expectedBaseName,
                out string problem))
        {
            SendBlockedWeedOrder(
                MiloIntent.WeedOrderRecipeUnavailable,
                problem,
                repeatMessage);
            return false;
        }

        if (!string.Equals(
                _job.InputProductId,
                expectedBaseId,
                StringComparison.OrdinalIgnoreCase))
        {
            SendBlockedWeedOrder(
                MiloIntent.WeedOrderNeedsRebooking,
                $"saved input '{_job.InputProductId}' differs from " +
                    $"recipe base '{expectedBaseId}'",
                repeatMessage);
            return false;
        }

        _job.InputProductName = expectedBaseName;
        return true;
    }

    private void SendBlockedWeedOrder(
        MiloIntent intent,
        string problem,
        bool repeatMessage)
    {
        string warning = $"Weed order '{_job.TargetProductId}' blocked: {problem}";
        if (!repeatMessage && !_rejectionGuard.ShouldEmit(warning))
            return;

        MelonLogger.Warning(warning);
        Messaging.SendTextMessage(
            MiloDialogue.Line(intent, DepositFacts(_reservedDrop)),
            intent == MiloIntent.WeedOrderNeedsRebooking
                ? new[] { CreateCancelOrderResponse() }
                : new[] { CreateStatusResponse(), CreateCancelOrderResponse() },
            responseDelay: 0f);
    }

    private void SendWeedRecipesUnavailable()
    {
        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.WeedRecipesUnavailable,
                new MiloLineFacts()),
            new[]
            {
                Button(
                    MiloIntent.ButtonDifferentDrug,
                    PresentDrugMenu,
                    "mixservice_weed_recipe_retry")
            });
    }

    private void PresentQuantityMenu(
        MixFamily family,
        string targetProductId,
        string targetProductName,
        string inputProductId,
        string inputProductName)
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
            responses.Add(
                QuantityButton(
                    quantity,
                    selected => CreateOrder(
                        family,
                        targetProductId,
                        targetProductName,
                        inputProductId,
                        inputProductName,
                        selected)));
        }

        responses.Add(
            Button(
                MiloIntent.ButtonDifferentMix,
                () => PresentTargetMenu(family, 0),
                "mixservice_qty_back"));

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.QuantityPrompt,
                new MiloLineFacts
                {
                    ProductName = targetProductName
                }),
            responses.ToArray());
    }

    private void CreateOrder(
        MixFamily family,
        string targetProductId,
        string targetProductName,
        string inputProductId,
        string inputProductName,
        int requiredBricks)
    {
        // Old response buttons can remain visible in the phone conversation.
        // Never let one replace an unpaid or in-progress job.
        if (_job.State != MixJobState.Idle)
        {
            SendCurrentStatus();
            return;
        }

        // Old phone responses remain clickable. Recheck discovery and family
        // before assigning a drop or saving a new job.
        ProductDefinition? target = GetDiscoveredTargets(family)
            .FirstOrDefault(product => string.Equals(
                product.ID,
                targetProductId,
                StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            PresentTargetMenu(family, 0);
            return;
        }

        targetProductName = target.Name;

        if (family.DrugType == DrugType.Marijuana)
        {
            if (!TryGetWeedBase(
                    targetProductId,
                    out string currentBaseId,
                    out string currentBaseName,
                    out string problem))
            {
                MelonLogger.Warning(
                    $"Cannot create weed order '{targetProductId}': {problem}");
                SendWeedRecipesUnavailable();
                return;
            }

            if (!string.Equals(
                    inputProductId,
                    currentBaseId,
                    StringComparison.OrdinalIgnoreCase))
            {
                PresentTargetMenu(family, 0);
                return;
            }

            inputProductName = currentBaseName;
        }

        DeadDropInstance? assignedDrop =
            OrderDropAssignment.ChooseEmpty();

        if (assignedDrop == null)
        {
            Messaging.SendTextMessage(
                MiloDialogue.Line(
                    MiloIntent.NoDropForNewOrder,
                    new MiloLineFacts()),
                new[] { CreateStatusResponse() },
                responseDelay: 0f);
            return;
        }

        _job.Reset();

        _job.State = MixJobState.AwaitingDeposit;
        _job.AssignedDrop = assignedDrop.GUID;
        _reservedDrop = assignedDrop;

        /*
         * A fresh order has told the player nothing yet, so the first
         * message has to name its drop.
         */
        _depositLocationKnown = false;
        _pickupLocationKnown = false;

        _job.InputProductId =
            inputProductId;

        _job.InputProductName =
            inputProductName;

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
            $"Quoted order: {requiredBricks} {inputProductName} bricks -> " +
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

        MiloLineFacts facts =
            DepositFacts(
                _reservedDrop);

        MelonLogger.Msg(
            $"Assigned order: ${_job.QuotedFee:N0} quoted; " +
            $"{_job.RequiredPackages} {_job.InputProductName} {brickWord} -> " +
            $"'{_job.TargetProductName}' at '{_reservedDrop.Name}' " +
            $"GUID={_reservedDrop.GUID}");

        Messaging.SendTextMessage(
            MiloDialogue.DepositBriefing(facts),
            new[] { CreateCancelOrderResponse() },
            responseDelay: 0f);

        // The player now has the drop; every later line for this order can
        // refer back to it instead of spelling it out again.
        _depositLocationKnown = true;
    }

    private void RestoreLegacyAwaitingPayment()
    {
        DeadDropInstance? assignedDrop =
            OrderDropAssignment.ChooseEmpty();

        if (assignedDrop == null)
        {
            Messaging.SendTextMessage(
                MiloDialogue.Line(
                    MiloIntent.NoDropForRestoredOrder,
                    new MiloLineFacts()),
                new[] { CreateStatusResponse(), CreateCancelOrderResponse() },
                responseDelay: 0f);
            return;
        }

        _job.AssignedDrop = assignedDrop.GUID;
        _job.State = MixJobState.AwaitingDeposit;
        _reservedDrop = assignedDrop;
        AttachReservedDropListener();
        if (!ValidateAwaitingWeedOrder())
            return;
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
                MiloDialogue.Line(
                    MiloIntent.LostDrop,
                    new MiloLineFacts()),
                responses: null,
                responseDelay: 0f);

            PresentIdleMenu();
            return;
        }

        AttachReservedDropListener();

        if (!ValidateAwaitingWeedOrder())
            return;

        if (_job.PaymentNeedsReview)
        {
            Messaging.SendTextMessage(
                MiloDialogue.Line(
                    MiloIntent.PaymentNeedsReview,
                    DepositFacts(
                        _reservedDrop)),
                new[] { CreateStatusResponse() },
                responseDelay: 0f);

            _depositLocationKnown = true;
            return;
        }

        string brickWord =
            _job.RequiredPackages == 1
                ? "brick"
                : "bricks";

        MelonLogger.Msg(
            $"Restored awaiting order: " +
            $"{_job.RequiredPackages} {_job.InputProductName} {brickWord} -> " +
            $"'{_job.TargetProductName}' " +
            $"at '{_reservedDrop.Name}'");

        Messaging.SendTextMessage(
            MiloDialogue.DepositStatus(
                DepositFacts(_reservedDrop)),
            new[] { CreateStatusResponse(), CreateCancelOrderResponse() });

        _depositLocationKnown = true;

        if (!_reservedDrop.IsEmpty)
            OnDropContentsChanged();
    }

    private void AttachReservedDropListener()
    {
        if (_reservedDrop == null)
            return;

        _depositAcknowledged = false;
        _rejectionGuard.Clear();

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

        if (!ValidateAwaitingWeedOrder())
            return;

        if (_job.PaymentNeedsReview)
            return;

        if (_reservedDrop.IsEmpty)
        {
            _rejectionGuard.Clear();
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
                $"{_job.InputProductName} is unpackaged; bricks required");

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

        if (!ValidateAwaitingWeedOrder())
            return;

        DeadDropInstance drop =
            _reservedDrop;

        if (_job.PaymentNeedsReview)
        {
            Messaging.SendTextMessage(
                MiloDialogue.Line(
                    MiloIntent.PaymentNeedsReview,
                    DepositFacts(
                        drop)),
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
                    out bool paymentNeedsReview,
                    out MiloPaymentFailure paymentFailure))
            {
                _job.PaymentNeedsReview = paymentNeedsReview;

                MiloLineFacts facts =
                    DepositFacts(
                        drop);

                facts.Failure = paymentFailure;

                Messaging.SendTextMessage(
                    paymentNeedsReview
                        ? MiloDialogue.Line(
                            MiloIntent.PaymentNeedsReview,
                            facts)
                        : MiloDialogue.Line(
                            MiloIntent.CollectionFailed,
                            facts),
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
        _rejectionGuard.Clear();

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
                            out bool refundNeedsReview,
                            out MiloPaymentFailure refundFailure))
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

        _depositLocationKnown = true;
        _pickupLocationKnown = false;

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
            MiloDialogue.Line(
                MiloIntent.ProcessingStarted,
                ProcessingFacts(ProcessingDurationMinutes)),
            new[] { CreateStatusResponse() },
            responseDelay: 0f);
    }

    /// <summary>
    /// Facts for the two "it is on my bench" messages. The product name is
    /// mandatory there; the clock wording comes from the one duration
    /// constant so the two messages can never disagree.
    /// </summary>
    private MiloLineFacts ProcessingFacts(
        int remainingMinutes)
    {
        MiloLineFacts facts = JobFacts();

        facts.Duration = MiloDialogue.DescribeDuration(
            ProcessingDurationMinutes);

        facts.TimeLeft = MiloDialogue.DescribeDuration(
            remainingMinutes);

        return facts;
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
            MiloDialogue.Line(
                MiloIntent.ProcessingStatus,
                ProcessingFacts(remaining)),
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
        return Button(
            MiloIntent.ButtonCheckStatus,
            SendCurrentStatus,
            "mixservice_check_status");
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
                    MiloDialogue.Line(
                        MiloIntent.ProcessingStatus,
                        ProcessingFacts(remaining)),
                    new[] { CreateStatusResponse() },
                    responseDelay: 0f);

                break;
            }

            case MixJobState.ReadyForPickup:
                EnsurePickupReady();
                break;

            case MixJobState.AwaitingDeposit:
                if (!ValidateAwaitingWeedOrder(repeatMessage: true))
                    break;

                if (_job.PaymentNeedsReview)
                {
                    Messaging.SendTextMessage(
                        MiloDialogue.Line(
                            MiloIntent.PaymentNeedsReview,
                            DepositFacts(
                                _reservedDrop)),
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
                    MiloDialogue.DepositStatus(
                        DepositFacts(_reservedDrop)),
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
                    MiloDialogue.Line(
                        MiloIntent.PickupBroken,
                        JobFacts()),
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
                MiloDialogue.Line(
                    MiloIntent.PickupDropUnavailable,
                    JobFacts()),
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
                MiloDialogue.Line(
                    MiloIntent.PackagingFailed,
                    JobFacts()),
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
        MiloLineFacts facts =
            PickupFacts(drop);

        facts.PackageCount = packageCount;

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.PickupReady,
                facts),
            new[] { CreateStatusResponse() },
            responseDelay: 0f);

        // From now on the player knows where the finished batch is, so repeat
        // status messages can say "the drop" instead of naming it again.
        _pickupLocationKnown = true;
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
        /*
         * Reattaching the same drop on every status check must not forget that
         * the player has already been told where it is. Compare GUIDs, not
         * object identity, because a lookup can hand back a fresh wrapper.
         */
        if (_pickupDrop == null ||
            !string.Equals(
                _pickupDrop.GUID,
                drop.GUID,
                StringComparison.OrdinalIgnoreCase))
        {
            _pickupLocationKnown = false;
        }

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

        var another = Button(
            MiloIntent.ButtonAfterPickup,
            PresentDrugMenu,
            "mixservice_after_pickup_new_order");

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.PickupComplete,
                JobFacts()),
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

    private void RejectDeposit(string problem)
    {
        if (_reservedDrop == null)
            return;

        string rejection =
            $"Deposit at '{_reservedDrop.Name}' " +
            $"not accepted: {problem}";

        if (!_rejectionGuard.ShouldEmit(rejection))
            return;

        MelonLogger.Warning(rejection);
    }

    private Response CreateCancelOrderResponse()
    {
        return Button(
            MiloIntent.ButtonCancelOrder,
            CancelOrder,
            "mixservice_cancel_order");
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

        var startAgain = Button(
            MiloIntent.ButtonAfterCancel,
            PresentDrugMenu,
            "mixservice_after_cancel_new_order");

        Messaging.SendTextMessage(
            MiloDialogue.Line(
                MiloIntent.OrderCancelled,
                JobFacts()),
            new[] { startAgain });
    }

    private bool RefundUncollectedOrder()
    {
        if (_job.PaymentNeedsReview)
        {
            Messaging.SendTextMessage(
                MiloDialogue.Line(
                    MiloIntent.RefundNeedsReview,
                    JobFacts()),
                new[] { CreateStatusResponse() },
                responseDelay: 0f);
            return false;
        }

        if (MiloWalletPayment.TryRefund(
                _job.AmountPaid,
                out string problem,
                out bool paymentNeedsReview,
                out MiloPaymentFailure paymentFailure))
            return true;

        _job.PaymentNeedsReview = paymentNeedsReview;
        MelonLogger.Error(problem);

        MiloLineFacts facts = JobFacts();
        facts.Failure = paymentFailure;

        Messaging.SendTextMessage(
            paymentNeedsReview
                ? MiloDialogue.Line(
                    MiloIntent.RefundNeedsReview,
                    facts)
                : MiloDialogue.Line(
                    MiloIntent.RefundFailed,
                    facts),
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
        _rejectionGuard.Clear();
        _depositLocationKnown = false;
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
