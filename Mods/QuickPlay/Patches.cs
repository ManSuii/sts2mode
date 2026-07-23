using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
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
        // --- Patch 1: NMouseCardPlay.Start() — single enemy & non-targeting auto-play ---
        var mousePlayType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay");
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

        // --- Patch 3: NPotionPopup.OnUseButtonPressed() — skip targeting when single target ---
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
                    TryPlayCard(__instance, aliveAllies[0]);
                    return false;
                }
                return true;
            }

            if (targetType == TargetType.AnyEnemy)
            {
                var aliveEnemies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && !n.Entity.IsPlayer)
                    .Select(n => n.Entity).ToList();
                if (aliveEnemies.Count == 1)
                {
                    TryPlayCard(__instance, aliveEnemies[0]);
                    return false;
                }
                return true;
            }

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

            var cts = _ctsField?.GetValue(__instance) as CancellationTokenSource;
            cts?.Cancel();

            var lastTarget = combatRoom.LastTargetedCreature;
            if (lastTarget != null && lastTarget.IsAlive)
            {
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

            // --- Self -> already handled by the game, let normal flow go ---
            if (targetType == TargetType.Self)
                return true;

            // For creature-dependent potions, need combat room
            if (combatRoom == null) return true;

            // --- AnyEnemy -> single enemy auto-use ---
            if (targetType == TargetType.AnyEnemy)
            {
                var aliveEnemies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && !n.Entity.IsPlayer)
                    .Select(n => n.Entity).ToList();

                if (aliveEnemies.Count == 1)
                {
                    potion.EnqueueManualUse(aliveEnemies[0]);
                    return false;
                }
                return true;
            }

            // --- CanThrowAtAlly potions -> single player = use on self ---
            if (potion.CanThrowAtAlly())
            {
                var aliveAllies = combatRoom.CreatureNodes
                    .Where(n => n.Entity.IsAlive && n.Entity.IsPlayer)
                    .Select(n => n.Entity).ToList();

                if (aliveAllies.Count == 1)
                {
                    potion.EnqueueManualUse(aliveAllies[0]);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[ClickOptimizer] PotionUsePrefix error: {ex.Message}");
        }

        return true; // normal targeting flow
    }

    private static void TryPlayCard(NCardPlay instance, Creature? target)
    {
        _tryPlayCardMethod?.Invoke(instance, new object?[] { target });
    }
}
