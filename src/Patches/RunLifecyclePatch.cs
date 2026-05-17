using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerRestart.Patches;

/// <summary>
/// 监听 run 生命周期事件，跟踪当前 run 状态。
/// </summary>
[HarmonyPatch]
public static class RunLifecyclePatch
{
    private static bool _inRun;
    private static IRunState? _currentRunState;

    public static bool IsInRun => _inRun;
    public static IRunState? CurrentRunState => _currentRunState;

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    [HarmonyPostfix]
    public static void OnBeforeRoomEntered(IRunState runState)
    {
        _inRun = true;
        _currentRunState = runState;
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    [HarmonyPostfix]
    public static void OnAfterCombatEnd(IRunState runState)
    {
        _currentRunState = runState;
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
    [HarmonyPostfix]
    public static void OnAfterCombatVictory(IRunState runState)
    {
        _currentRunState = runState;
    }

    public static void Reset()
    {
        _inRun = false;
        _currentRunState = null;
    }
}
