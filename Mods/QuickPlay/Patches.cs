using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace Sts2ClickOptimizer.Patches;

internal static class ClickOptimizerPatch
{
    private static MethodInfo? _tryPlayCardMethod;
    private static FieldInfo? _ctsField;

    // Potion optimization: cached reflected access
    private static FieldInfo? _potionPopupHolderField;

    public static void Apply(Harmony harmony)
    {
        var mousePlayType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay");
        var controllerPlayType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Combat.NControllerCardPlay");

        // --- Patch 1: NMouseCardPlay.Start() — single enemy & non-targeting auto-play (mouse) ---
        var startMethod = AccessTools.Method(mousePlayType, "Start");
        if (startMethod != null)
        {
            harmony.Patch(startMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(StartPrefix)));
            Godot.GD.Print("[ClickOptimizer] Patched NMouseCardPlay.Start");
        }
        else
        {
            Godot.GD.PrintErr("[ClickOptimizer] FAILED to find NMouseCardPlay.Start");
        }

        // --- Patch 2: NMouseCardPlay._Input() — click detection for last-target feature ---
        var inputMethod = AccessTools.Method(mousePlayType, "_Input");
        if (inputMethod != null)
        {
            harmony.Patch(inputMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(InputPrefix)));
            Godot.GD.Print("[ClickOptimizer] Patched NMouseCardPlay._Input");
        }

        // --- Patch 3: NControllerCardPlay.Start() — auto-target for gamepad card play ---
        var ctrlStartMethod = AccessTools.Method(controllerPlayType, "Start");
        if (ctrlStartMethod != null)
        {
            harmony.Patch(ctrlStartMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(ControllerStartPrefix)));
            Godot.GD.Print("[ClickOptimizer] Patched NControllerCardPlay.Start");
        }
        else
        {
            Godot.GD.PrintErr("[ClickOptimizer] FAILED to find NControllerCardPlay.Start");
        }

        // --- Patch 4: NPlayerHand focus events — automatic target preview ---
        var playerHandType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand");
        var holderFocusedMethod = AccessTools.Method(playerHandType, "OnHolderFocused");
        if (holderFocusedMethod != null)
        {
            harmony.Patch(holderFocusedMethod,
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(HolderFocusedPostfix)));
            Godot.GD.Print("[ClickOptimizer] Patched NPlayerHand.OnHolderFocused");
        }

        var holderUnfocusedMethod = AccessTools.Method(playerHandType, "OnHolderUnfocused");
        if (holderUnfocusedMethod != null)
        {
            harmony.Patch(holderUnfocusedMethod,
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(HolderUnfocusedPostfix)));
            Godot.GD.Print("[ClickOptimizer] Patched NPlayerHand.OnHolderUnfocused");
        }

        // --- Patch 5: NCardPlay hover events — manual target selection takes priority ---
        var creatureHoverMethod = AccessTools.Method(typeof(NCardPlay), "OnCreatureHover");
        if (creatureHoverMethod != null)
        {
            harmony.Patch(creatureHoverMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(CardHoverPrefix)));
            Godot.GD.Print("[ClickOptimizer] Patched NCardPlay.OnCreatureHover");
        }

        var creatureUnhoverMethod = AccessTools.Method(typeof(NCardPlay), "OnCreatureUnhover");
        if (creatureUnhoverMethod != null)
        {
            harmony.Patch(creatureUnhoverMethod,
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(CardUnhoverPostfix)));
            Godot.GD.Print("[ClickOptimizer] Patched NCardPlay.OnCreatureUnhover");
        }

        var cleanupMethod = AccessTools.Method(typeof(NCardPlay), "Cleanup");
        if (cleanupMethod != null)
        {
            harmony.Patch(cleanupMethod,
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(CardPlayCleanupPostfix)));
        }

        var cancelCardPlayMethod = AccessTools.Method(typeof(NCardPlay), "OnCancelPlayCard");
        if (cancelCardPlayMethod != null)
        {
            harmony.Patch(cancelCardPlayMethod,
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(CardPlayCleanupPostfix)));
        }

        // --- Patch 6: NPotionPopup.OnUseButtonPressed() — skip targeting when single target ---
        var popupType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Potions.NPotionPopup");
        var useMethod = AccessTools.Method(popupType, "OnUseButtonPressed");
        if (useMethod != null)
        {
            harmony.Patch(useMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(PotionUsePrefix)));
            Godot.GD.Print("[ClickOptimizer] Patched NPotionPopup.OnUseButtonPressed");
        }
        else
        {
            Godot.GD.PrintErr("[ClickOptimizer] FAILED to find NPotionPopup.OnUseButtonPressed");
        }

        var popupExitMethod = AccessTools.Method(popupType, "_ExitTree");
        if (popupExitMethod != null)
        {
            harmony.Patch(popupExitMethod,
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(PotionPopupExitPostfix)));
        }

        // --- Patch 7: NPotionHolder.OpenPotionPopup() — preview the automatic potion target ---
        var potionHolderType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Potions.NPotionHolder");
        var openPopupMethod = AccessTools.Method(potionHolderType, "OpenPotionPopup");
        if (openPopupMethod != null)
        {
            harmony.Patch(openPopupMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(PotionOpenPrefix)),
                postfix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(PotionOpenedPostfix)));
        }

        // --- Patch 8: NTargetManager._Input() — track mouse vs controller input ---
        var targetManagerType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager");
        var targetManagerInputMethod = AccessTools.Method(targetManagerType, "_Input");
        if (targetManagerInputMethod != null)
        {
            harmony.Patch(targetManagerInputMethod,
                prefix: new HarmonyMethod(typeof(ClickOptimizerPatch), nameof(TargetManagerInputPrefix)));
        }

        // Cache accessed members
        _tryPlayCardMethod = AccessTools.Method(
            typeof(NCardPlay), "TryPlayCard", new[] { typeof(Creature) });
        _ctsField = AccessTools.Field(mousePlayType, "_cancellationTokenSource");
        _potionPopupHolderField = AccessTools.Field(popupType, "_holder");
    }

    public static bool StartPrefix(NMouseCardPlay __instance)
    {
        try
        {
            var holder = __instance.Holder;
            var card = holder?.CardModel;
            if (card == null) return true;

            var combatRoom = NCombatRoom.Instance;
            if (combatRoom == null) return true;

            var targetType = card.TargetType;

            if (targetType == TargetType.AnyAlly)
            {
                var aliveAllies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && n.Entity.IsPlayer)
                    .Select(n => n.Entity).ToList();
                if (aliveAllies.Count == 1)
                {
                    AutomaticTargetPreview.BeginCardPlay(card, aliveAllies[0]);
                    TryPlayCard(__instance, aliveAllies[0]);
                    return false;
                }
                AutomaticTargetPreview.ClearCard(card);
                return true;
            }

            if (targetType == TargetType.AnyEnemy)
            {
                var aliveEnemies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && n.Entity.IsEnemy)
                    .Select(n => n.Entity).ToList();
                if (aliveEnemies.Count == 1)
                {
                    AutomaticTargetPreview.BeginCardPlay(card, aliveEnemies[0]);
                    TryPlayCard(__instance, aliveEnemies[0]);
                    return false;
                }
                return true;
            }

            AutomaticTargetPreview.ClearCard(card);
            TryPlayCard(__instance, null);
            return false;
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[ClickOptimizer] StartPrefix error: {ex.Message}");
            return true;
        }
    }

    public static bool InputPrefix(NMouseCardPlay __instance, InputEvent inputEvent)
    {
        try
        {
            // Mouse click (release) in play zone: auto-target last target
            if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseEvent)
                return true;
            if (!mouseEvent.IsReleased())
                return true;

            var holder = __instance.Holder;
            var card = holder?.CardModel;
            if (card == null || card.TargetType != TargetType.AnyEnemy)
                return true;

            var combatRoom = NCombatRoom.Instance;
            if (combatRoom == null) return true;

            var viewport = AccessTools.Field(typeof(NCardPlay), "_viewport")?.GetValue(__instance) as Viewport;
            var mouseY = viewport?.GetMousePosition().Y ?? 0f;
            var screenHeight = viewport?.GetVisibleRect().Size.Y ?? 1080f;
            if (mouseY < screenHeight * 0.75f)
                return true;

            var lastTarget = combatRoom.LastTargetedCreature;
            if (lastTarget != null && lastTarget.IsAlive)
            {
                var cts = _ctsField?.GetValue(__instance) as CancellationTokenSource;
                cts?.Cancel();
                AutomaticTargetPreview.BeginCardPlay(card, lastTarget);
                TryPlayCard(__instance, lastTarget);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[ClickOptimizer] InputPrefix error: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Prefix on NControllerCardPlay.Start().
    /// Same auto-target logic as the mouse version, but for gamepad card play.
    /// </summary>
    public static bool ControllerStartPrefix(NCardPlay __instance)
    {
        try
        {
            var holder = __instance.Holder;
            var card = holder?.CardModel;
            if (card == null) return true;

            var combatRoom = NCombatRoom.Instance;
            if (combatRoom == null) return true;

            var targetType = card.TargetType;

            if (targetType == TargetType.AnyAlly)
            {
                var aliveAllies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && n.Entity.IsPlayer)
                    .Select(n => n.Entity).ToList();
                if (aliveAllies.Count == 1)
                {
                    AutomaticTargetPreview.BeginCardPlay(card, aliveAllies[0]);
                    TryPlayCard(__instance, aliveAllies[0]);
                    return false;
                }
                AutomaticTargetPreview.ClearCard(card);
                return true;
            }

            if (targetType == TargetType.AnyEnemy)
            {
                var aliveEnemies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && n.Entity.IsEnemy)
                    .Select(n => n.Entity).ToList();
                if (aliveEnemies.Count == 1)
                {
                    AutomaticTargetPreview.BeginCardPlay(card, aliveEnemies[0]);
                    TryPlayCard(__instance, aliveEnemies[0]);
                    return false;
                }
                AutomaticTargetPreview.ClearCard(card);
                return true;
            }

            AutomaticTargetPreview.ClearCard(card);
            TryPlayCard(__instance, null);
            return false;
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[ClickOptimizer] ControllerStartPrefix error: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Prefix on NPotionPopup.OnUseButtonPressed().
    /// When there's only one valid target, uses the potion directly
    /// without going through the targeting sequence.
    /// The popup still appears (so you can discard), this only optimizes
    /// what happens after clicking "Use".
    /// </summary>
    public static bool PotionUsePrefix(NPotionPopup __instance)
    {
        try
        {
            // Get the potion holder from the popup
            var holder = _potionPopupHolderField?.GetValue(__instance) as NPotionHolder;
            if (holder == null) return true;

            var potionProp = AccessTools.Property(holder.GetType(), "Potion");
            var potionNode = potionProp?.GetValue(holder);
            if (potionNode == null) return true;

            var modelProp = AccessTools.Property(potionNode.GetType(), "Model");
            var potion = modelProp?.GetValue(potionNode) as PotionModel;
            if (potion == null) return true;

            var combatRoom = NCombatRoom.Instance;
            var targetType = potion.TargetType;

            // --- Self and single-player AnyPlayer potions use the owner directly ---
            if (targetType is TargetType.Self or TargetType.AnyPlayer)
            {
                var category = targetType == TargetType.Self
                    ? PotionTargetCategory.Self
                    : PotionTargetCategory.AnyPlayer;
                var decision = PotionUsePolicy.Decide(
                    category,
                    targetType == TargetType.AnyPlayer && potion.CanThrowAtAlly());
                var ownerCreature = potion.Owner?.Creature;

                if (decision.Mode == PotionUseMode.UseImmediately &&
                    decision.Target == PotionUseTarget.Owner &&
                    ownerCreature != null)
                {
                    AutomaticTargetPreview.BeginPotionPlay(potion, ownerCreature);
                    potion.EnqueueManualUse(ownerCreature);
                    return false;
                }

                return true;
            }

            // For creature-dependent potions, need combat room
            if (combatRoom == null) return true;

            // --- AnyEnemy -> single enemy auto-use ---
            if (targetType == TargetType.AnyEnemy)
            {
                var aliveEnemies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && n.Entity.IsEnemy)
                    .Select(n => n.Entity).ToList();

                if (aliveEnemies.Count == 1)
                {
                    AutomaticTargetPreview.BeginPotionPlay(potion, aliveEnemies[0]);
                    potion.EnqueueManualUse(aliveEnemies[0]);
                    return false;
                }
                return true;
            }

        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[ClickOptimizer] PotionUsePrefix error: {ex.Message}");
        }

        return true; // normal targeting flow
    }

    public static void HolderFocusedPostfix(NHandCardHolder holder)
    {
        AutomaticTargetPreview.BeginCardFocus(holder.CardModel);
    }

    public static void HolderUnfocusedPostfix(NHandCardHolder holder)
    {
        AutomaticTargetPreview.EndCardFocus(holder.CardModel);
    }

    public static void CardHoverPrefix()
    {
        AutomaticTargetPreview.BeginManualTarget();
    }

    public static void CardUnhoverPostfix()
    {
        AutomaticTargetPreview.EndManualTarget();
    }

    public static void CardPlayCleanupPostfix()
    {
        AutomaticTargetPreview.ClearCard();
    }

    public static void PotionOpenedPostfix(NPotionHolder __instance)
    {
        AutomaticTargetPreview.BeginPotionFocus(__instance.Potion?.Model);
    }

    public static bool PotionOpenPrefix(NPotionHolder __instance)
    {
        var potion = __instance.Potion?.Model;
        if (potion == null)
            return true;

        var target = AutomaticTargetPreview.ResolvePotionTarget(potion, NCombatRoom.Instance);
        if (target == null)
            return true;

        AutomaticTargetPreview.BeginPotionPlay(potion, target);
        potion.EnqueueManualUse(target);
        return false;
    }

    public static void PotionPopupExitPostfix()
    {
        AutomaticTargetPreview.ClearPotion();
    }

    public static bool TargetManagerInputPrefix(InputEvent inputEvent)
    {
        AutomaticTargetPreview.TrackInput(inputEvent);
        return true;
    }

    private static void TryPlayCard(NCardPlay instance, Creature? target)
    {
        _tryPlayCardMethod?.Invoke(instance, new object?[] { target });
    }
}
