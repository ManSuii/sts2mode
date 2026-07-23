using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2ClickOptimizer;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony _harmony = null!;

    public static void Init()
    {
        _harmony = new Harmony("Sts2ClickOptimizer");
        Patches.ClickOptimizerPatch.Apply(_harmony);
        Godot.GD.Print("[ClickOptimizer] Initialized");
    }
}
