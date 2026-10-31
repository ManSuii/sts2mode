namespace Sts2ClickOptimizer.Patches;

internal enum PotionTargetCategory
{
    Self,
    AnyPlayer,
    SingleEnemy,
    MultipleEnemies,
    SingleAlly,
    MultipleAllies,
    Unsupported
}

internal enum PotionUseMode
{
    NormalFlow,
    UseImmediately
}

internal enum PotionUseTarget
{
    None,
    Owner,
    Enemy,
    Ally
}

internal readonly record struct PotionUseDecision(PotionUseMode Mode, PotionUseTarget Target);

internal static class PotionUsePolicy
{
    public static PotionUseDecision Decide(PotionTargetCategory category, bool canThrowAtAlly)
    {
        if (category == PotionTargetCategory.Self)
            return new PotionUseDecision(PotionUseMode.UseImmediately, PotionUseTarget.Owner);

        if (category == PotionTargetCategory.AnyPlayer && !canThrowAtAlly)
            return new PotionUseDecision(PotionUseMode.UseImmediately, PotionUseTarget.Owner);

        return new PotionUseDecision(PotionUseMode.NormalFlow, PotionUseTarget.None);
    }
}
