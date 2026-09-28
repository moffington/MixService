using System.Collections.Generic;
using MixService.Mixing;

namespace MixService.Dialogue;

/// <summary>
/// Every word Milo says, grouped by what he is trying to say.
///
/// THIS IS THE FILE TO EDIT WHEN YOU WANT MORE LINES. Find the intent, add a
/// string to the tier, done. Nothing else has to change: the picker reads the
/// array length, so more lines means more variety for free.
///
/// House rules, enforced by Tests/DialogueTests.cs:
///   * A line is 20 words or fewer. Milo texts. He does not make speeches.
///   * Fragments are written short on purpose. A message can be two fragments
///     (see MiloDialogue.DepositBriefing), so 20 words of quote plus 20 words
///     of instruction is already an essay.
///   * A drop location goes in a !LocationKnown line and nowhere else. Once the
///     player has it, refer back to it instead of repeating it.
///   * No line may state something the facts do not support. {due} is the total
///     still owed, NOT the gap between what the player is carrying and that
///     total, so never word it as a shortfall.
///   * Button banks hold exactly one line, which is what Label() returns. Button
///     text is deterministic on purpose; see MiloDialogue for why.
///
/// Placeholders a line may use:
///   {product}   the mix being made          {drop}     where the drop is
///   {bricks}    how many bricks             {brick}    brick / bricks to match
///   {count}     how many finished packages  {packaging}   packaging name
///   {quality}   the input quality           {packagings}  packaging name + s
///   {fee}       Milo's full quote           {due}      still owed at handoff
///   {duration}  the full run time           {left}     time remaining
///
/// Tier rules, evaluated top to bottom; the last tier always has a null rule
/// and is the fallback:
///   !LocationKnown         Milo has not put this drop in front of the player
///                          yet, so the line has to name it.
///   SameSpotAsDeposit      the finished batch is in the drop the player was
///                          already given, so "same spot" is true.
///   !ProductCollected      the player still has their product sitting there.
///   Payment                Unpaid / Partial / Paid on the current order.
///   IsLargeOrder           BrickCount >= MiloLineFacts.LargeOrderBricks.
///   Failure                why a wallet call failed, see MiloPaymentFailure.
///
/// Milo is tired. He is not doing a bit. Let most of it be flat.
/// </summary>
public static class MiloDialogueBank
{
    public static Dictionary<MiloIntent, MiloBank> Build()
    {
        return new Dictionary<MiloIntent, MiloBank>
        {
            // ---------------------------------------------------------
            // Conversation openers and menus
            // ---------------------------------------------------------
            [MiloIntent.Greeting] = new MiloBank(
                new MiloBankTier(
                    null,
                    "yo. need something?",
                    "hey. you need a mix?",
                    "sup. need a batch?",
                    "you again.",
                    "milo. what do you need.",
                    "hi. quickly.")),

            [MiloIntent.NoTargetsDiscovered] = new MiloBank(
                new MiloBankTier(
                    null,
                    "no mixes figured out yet.",
                    "figure out a cocaine mix first.",
                    "nothing for me to make.")),

            [MiloIntent.TargetPrompt] = new MiloBank(
                new MiloBankTier(
                    null,
                    "what're we making?",
                    "which one?",
                    "pick a mix.",
                    "what do you want mixed.",
                    "your pick.")),

            [MiloIntent.QuantityPrompt] = new MiloBank(
                new MiloBankTier(
                    null,
                    "how much {product}?",
                    "{product}. how many?",
                    "how many {product}?",
                    "{product}. how much.",
                    "how many bricks?")),

            // ---------------------------------------------------------
            // Taking the order
            // ---------------------------------------------------------
            [MiloIntent.OrderQuote] = new MiloBank(
                new MiloBankTier(
                    facts => facts.Payment == MiloPaymentState.Paid,
                    "{fee}, already paid.",
                    "you're {fee} clear.",
                    "{fee}. done."),

                new MiloBankTier(
                    facts => facts.Payment == MiloPaymentState.Partial,
                    "{fee} total, {due} when i collect.",
                    "{due} left of {fee}, at handoff.",
                    "{fee} all told. {due} when i collect."),

                new MiloBankTier(
                    facts => facts.IsLargeOrder,
                    "{bricks} bricks. {fee}, due when i collect.",
                    "you want a lot. {fee} when i collect.",
                    "big run. {fee} when i collect it.",
                    "{bricks} at once. {fee} when i collect it.",
                    "fine. {bricks} bricks, {fee} at handoff."),

                new MiloBankTier(
                    null,
                    "{fee}, due when i collect.",
                    "my cut's {fee}, at handoff.",
                    "{fee} when i collect it.",
                    "my cut's {fee}. you pay when i collect.",
                    "that's {fee}, at handoff.")),

            [MiloIntent.DepositInstructions] = new MiloBank(
                new MiloBankTier(
                    facts => facts.IsLargeOrder,
                    "{bricks} cocaine {brick} at {drop}. one quality, no sorting.",
                    "{drop}. {bricks} cocaine {brick}, all identical quality.",
                    "put {bricks} cocaine {brick} in {drop}. " +
                        "same quality on all of it."),

                new MiloBankTier(
                    null,
                    "{bricks} cocaine {brick} at {drop}. one quality.",
                    "drop {bricks} cocaine {brick} at {drop}. same quality throughout.",
                    "{drop}. {bricks} cocaine {brick}, one quality.",
                    "leave {bricks} cocaine {brick} at {drop}. don't vary the quality.")),

            // ---------------------------------------------------------
            // Waiting on the player
            // ---------------------------------------------------------
            [MiloIntent.AwaitingDeposit] = new MiloBank(
                new MiloBankTier(
                    facts => !facts.LocationKnown,
                    "still waiting. {bricks} cocaine {brick} at {drop}.",
                    "nothing yet. {drop}, {bricks} cocaine {brick}.",
                    "{drop}. {bricks} cocaine {brick}.",
                    "i don't have the goods. {drop}, {bricks} cocaine {brick}."),

                new MiloBankTier(
                    facts => facts.IsLargeOrder,
                    "still nothing. {bricks} bricks, same spot.",
                    "drop's empty. {bricks} bricks, where i told you.",
                    "{bricks} bricks. same spot."),

                new MiloBankTier(
                    null,
                    "{bricks} cocaine {brick}. where i told you.",
                    "still waiting. {bricks} {brick}, same spot.",
                    "nothing from you. {bricks} {brick}, same place.",
                    "drop's still empty. {bricks} {brick}.",
                    "{bricks} {brick}. where i told you.")),

            [MiloIntent.SettlementNote] = new MiloBank(
                new MiloBankTier(
                    facts => facts.Payment == MiloPaymentState.Paid,
                    "already paid.",
                    "we're square.",
                    "nothing owed."),

                new MiloBankTier(
                    null,
                    "{due} when i collect.",
                    "{due} at handoff.",
                    "i need {due}.")),

            // ---------------------------------------------------------
            // Working on the order
            // ---------------------------------------------------------
            [MiloIntent.ProcessingStarted] = new MiloBank(
                new MiloBankTier(
                    null,
                    "got it. {product} in {duration}.",
                    "{product}. {duration}.",
                    "got it. {duration} on {product}.",
                    "it's on. {product}, {duration}.")),

            [MiloIntent.ProcessingStatus] = new MiloBank(
                new MiloBankTier(
                    null,
                    "{product}. {left} left.",
                    "still going. {left} on {product}.",
                    "{left} to go.",
                    "not done. {left}.",
                    "still on it. {left} left.")),

            [MiloIntent.CollectionFailed] = new MiloBank(
                new MiloBankTier(
                    facts => !facts.LocationKnown,
                    "can't collect yet. it's at {drop}.",
                    "not yet. {drop}, still untouched.",
                    "it's still at {drop}."),

                new MiloBankTier(
                    facts =>
                        facts.Failure ==
                            MiloPaymentFailure.InsufficientCash,
                    "you don't have {due} on you. product's still there.",
                    "come back with {due}. nothing's taken.",
                    "need {due} first. it stays put.",
                    "i need {due} and you don't have it."),

                new MiloBankTier(
                    facts =>
                        facts.Failure ==
                                MiloPaymentFailure.ChangeUnverifiable ||
                        facts.Failure ==
                                MiloPaymentFailure.BalanceUnavailable,
                    "something's off with your cash.",
                    "cash isn't reading right. nothing's taken.",
                    "i can't read your balance."),

                new MiloBankTier(
                    null,
                    "can't collect yet.",
                    "not yet. product's still there.",
                    "no.")),

            [MiloIntent.PaymentNeedsReview] = new MiloBank(
                new MiloBankTier(
                    facts =>
                        !facts.LocationKnown && !facts.ProductCollected,
                    "the cash needs review. your drop is {drop}.",
                    "review needed on the cash. {drop}."),

                new MiloBankTier(
                    facts => !facts.ProductCollected,
                    "the cash needs review. your stuff's in the drop.",
                    "review needed. i haven't taken the product."),

                new MiloBankTier(
                    null,
                    "cash needs review. product's in the drop.",
                    "the cash needs review. your product's in the drop.")),

            // ---------------------------------------------------------
            // Handing the order over
            // ---------------------------------------------------------
            [MiloIntent.PickupReady] = new MiloBank(
                new MiloBankTier(
                    facts => !facts.LocationKnown,
                    "{product} is done. {count} {quality} {packagings} at {drop}.",
                    "{product}'s ready. {count} {quality} {packagings} in {drop}.",
                    "done. {count} {quality} {packagings} at {drop}."),

                new MiloBankTier(
                    facts => facts.SameSpotAsDeposit,
                    "{product}'s ready. {count} {quality} {packagings}, same spot.",
                    "done. {count} {quality} {packagings}, same drop.",
                    "{count} {quality} {packagings}, where i told you."),

                new MiloBankTier(
                    null,
                    "{product}'s ready. {count} {quality} {packagings}, in the drop.",
                    "done. {count} {quality} {packagings}, check the drop.")),

            [MiloIntent.PickupDropUnavailable] = new MiloBank(
                new MiloBankTier(
                    null,
                    "{product} is done. no clean drop. check back.",
                    "{product}'s ready. nowhere to put it. check back.")),

            [MiloIntent.PickupBroken] = new MiloBank(
                new MiloBankTier(
                    null,
                    "pickup's busted. don't order again yet.",
                    "i can't find the pickup. don't order again.",
                    "something's off with the pickup. wait on it.")),

            [MiloIntent.PackagingFailed] = new MiloBank(
                new MiloBankTier(
                    null,
                    "couldn't bag your batch. don't order again yet.",
                    "packaging failed. don't order again yet.")),

            [MiloIntent.PickupComplete] = new MiloBank(
                new MiloBankTier(
                    null,
                    "all yours.",
                    "that's yours. shout if you need more.",
                    "gone. come back when you need more.")),

            // ---------------------------------------------------------
            // Walking away from the order
            // ---------------------------------------------------------
            [MiloIntent.OrderCancelled] = new MiloBank(
                new MiloBankTier(
                    null,
                    "cancelled.",
                    "done. it's off my books.",
                    "forget it.")),

            [MiloIntent.RefundNeedsReview] = new MiloBank(
                new MiloBankTier(
                    null,
                    "cash needs review. i can't cancel it yet.",
                    "i can't cancel that yet. cash needs review.")),

            [MiloIntent.RefundFailed] = new MiloBank(
                new MiloBankTier(
                    null,
                    "refund's stuck. order's still open.",
                    "can't refund yet. order's still open.")),

            [MiloIntent.LostDrop] = new MiloBank(
                new MiloBankTier(
                    null,
                    "lost the drop. start over.",
                    "that drop's gone. new order.")),

            [MiloIntent.NoDropForNewOrder] = new MiloBank(
                new MiloBankTier(
                    null,
                    "no clean drop. try me later.",
                    "nowhere clean to work. later.")),

            [MiloIntent.NoDropForRestoredOrder] = new MiloBank(
                new MiloBankTier(
                    null,
                    "no clean drop for your order. check back.",
                    "need a clean drop. check back soon.")),

            // ---------------------------------------------------------
            // Response button labels
            //
            // One line per tier, on purpose. MiloDialogue.Label() returns it
            // verbatim, so a label is stable and never disturbs the repeat
            // latch that guards Milo's actual messages.
            // ---------------------------------------------------------
            [MiloIntent.ButtonNewOrder] = new MiloBank(
                new MiloBankTier(
                    null,
                    "need a batch mixed")),

            [MiloIntent.ButtonNeverMind] = new MiloBank(
                new MiloBankTier(
                    null,
                    "never mind")),

            [MiloIntent.ButtonMore] = new MiloBank(
                new MiloBankTier(
                    null,
                    "more...")),

            [MiloIntent.ButtonQuantity] = new MiloBank(
                new MiloBankTier(
                    facts => facts.BrickCount == 1,
                    "1 brick"),
                new MiloBankTier(
                    facts => facts.BrickCount == 5,
                    "5 bricks"),
                new MiloBankTier(
                    facts => facts.BrickCount == 10,
                    "10 bricks"),
                new MiloBankTier(
                    facts => facts.BrickCount == 20,
                    "20 bricks"),
                new MiloBankTier(
                    null,
                    "{bricks} {brick}")),

            [MiloIntent.ButtonDifferentMix] = new MiloBank(
                new MiloBankTier(
                    null,
                    "different mix")),

            [MiloIntent.ButtonCheckStatus] = new MiloBank(
                new MiloBankTier(
                    null,
                    "check my batch")),

            [MiloIntent.ButtonAfterPickup] = new MiloBank(
                new MiloBankTier(
                    null,
                    "got another batch")),

            [MiloIntent.ButtonCancelOrder] = new MiloBank(
                new MiloBankTier(
                    null,
                    "cancel it")),

            [MiloIntent.ButtonAfterCancel] = new MiloBank(
                new MiloBankTier(
                    null,
                    "set up another"))
        };
    }
}
