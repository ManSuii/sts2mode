using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2ClickOptimizer.Patches;

internal enum PreviewInputMode
{
    Mouse,
    Controller
}

internal static class AutomaticTargetPreview
{
    private static PreviewInputMode _inputMode = PreviewInputMode.Mouse;

    private static CardModel? _cardSource;
    private static PotionModel? _potionSource;
    private static Creature? _target;
    private static bool _sourceActive;
    private static bool _activePlay;
    private static bool _manualTargetActive;

    private static NCreature? _visualCreature;

    public static void TrackInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton or InputEventMouseMotion)
        {
            _inputMode = PreviewInputMode.Mouse;
            return;
        }

        if (inputEvent is InputEventJoypadButton)
        {
            _inputMode = PreviewInputMode.Controller;
            return;
        }

        if (inputEvent is InputEventJoypadMotion motion && Math.Abs(motion.AxisValue) > 0.35f)
        {
            _inputMode = PreviewInputMode.Controller;
        }
    }

    public static void BeginCardFocus(CardModel? card)
    {
        if (_activePlay || card == null)
            return;

        _cardSource = card;
        _potionSource = null;
        _sourceActive = true;
        _target = ResolveCardTarget(card, NCombatRoom.Instance);
        Refresh();
    }

    public static void EndCardFocus(CardModel? card)
    {
        if (_activePlay || card == null || _cardSource != card)
            return;

        Clear();
    }

    public static void BeginCardPlay(CardModel? card, Creature? target)
    {
        if (card == null || target == null)
            return;

        _cardSource = card;
        _potionSource = null;
        _sourceActive = true;
        _activePlay = true;
        _manualTargetActive = false;
        _target = target;
        Refresh();
    }

    public static void ClearCard(CardModel? card = null)
    {
        if (card != null && _cardSource != card)
            return;

        Clear();
    }

    public static void BeginPotionFocus(PotionModel? potion)
    {
        if (_activePlay || potion == null)
            return;

        _cardSource = null;
        _potionSource = potion;
        _sourceActive = true;
        _target = ResolvePotionTarget(potion, NCombatRoom.Instance);
        Refresh();
    }

    public static void BeginPotionPlay(PotionModel? potion, Creature? target)
    {
        if (potion == null || target == null)
            return;

        _cardSource = null;
        _potionSource = potion;
        _sourceActive = true;
        _activePlay = true;
        _manualTargetActive = false;
        _target = target;
        Refresh();
    }

    public static void ClearPotion(PotionModel? potion = null)
    {
        if (potion != null && _potionSource != potion)
            return;

        Clear();
    }

    public static void BeginManualTarget()
    {
        _manualTargetActive = true;
        HideVisuals(hideNativeReticle: true);
    }

    public static void EndManualTarget()
    {
        if (!_manualTargetActive)
            return;

        _manualTargetActive = false;
        Refresh();
    }

    public static Creature? ResolveCardTarget(CardModel card, NCombatRoom? room)
    {
        if (room == null)
            return null;

        if (card.TargetType == TargetType.AnyAlly)
            return FindSingleAliveCreature(room, wantEnemy: false);

        if (card.TargetType != TargetType.AnyEnemy)
            return null;

        var singleEnemy = FindSingleAliveCreature(room, wantEnemy: true);
        if (singleEnemy != null)
            return singleEnemy;

        if (_inputMode != PreviewInputMode.Mouse)
            return null;

        var lastTarget = room.LastTargetedCreature;
        return lastTarget is { IsAlive: true, IsEnemy: true } ? lastTarget : null;
    }

    public static Creature? ResolvePotionTarget(PotionModel potion, NCombatRoom? room)
    {
        if (potion.TargetType is TargetType.Self or TargetType.AnyPlayer)
        {
            var category = potion.TargetType == TargetType.Self
                ? PotionTargetCategory.Self
                : PotionTargetCategory.AnyPlayer;
            var decision = PotionUsePolicy.Decide(
                category,
                potion.TargetType == TargetType.AnyPlayer && potion.CanThrowAtAlly());

            return decision.Mode == PotionUseMode.UseImmediately
                ? potion.Owner?.Creature
                : null;
        }

        if (room == null)
            return null;

        if (potion.TargetType == TargetType.AnyEnemy)
            return FindSingleAliveCreature(room, wantEnemy: true);

        return potion.CanThrowAtAlly()
            ? FindSingleAliveCreature(room, wantEnemy: false)
            : null;
    }

    private static Creature? FindSingleAliveCreature(NCombatRoom room, bool wantEnemy)
    {
        Creature? result = null;

        foreach (var node in room.CreatureNodes)
        {
            var creature = node.Entity;
            if (!creature.IsAlive || creature.IsEnemy != wantEnemy)
                continue;

            if (result != null)
                return null;

            result = creature;
        }

        return result;
    }

    private static void Refresh()
    {
        if (!_sourceActive ||
            _manualTargetActive ||
            _target == null ||
            !_target.IsAlive ||
            NCombatRoom.Instance == null)
        {
            HideVisuals(hideNativeReticle: true);
            return;
        }

        var creatureNode = NCombatRoom.Instance.GetCreatureNode(_target);
        if (creatureNode == null)
        {
            HideVisuals(hideNativeReticle: true);
            return;
        }

        if (_visualCreature != creatureNode)
            HideVisuals(hideNativeReticle: true);

        _visualCreature = creatureNode;
        creatureNode.ShowSingleSelectReticle();
    }

    private static void Clear()
    {
        _cardSource = null;
        _potionSource = null;
        _target = null;
        _sourceActive = false;
        _activePlay = false;
        _manualTargetActive = false;
        HideVisuals(hideNativeReticle: true);
    }

    private static void HideVisuals(bool hideNativeReticle)
    {
        if (hideNativeReticle)
            _visualCreature?.HideSingleSelectReticle();

        _visualCreature = null;
    }
}
