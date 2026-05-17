using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MultiplayerRestart.Patches;
using MultiplayerRestart.Utils;

namespace MultiplayerRestart;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "MultiplayerRestart";
    private static Harmony? _harmony;

    public static void Initialize()
    {
        Log.Info($"[{ModId}] Initializing v1.0.0...");

        _harmony = new Harmony(ModId);

        GameApi.DiscoverApis();
        ApplyPatches();
        InputHandler.Setup();

        Log.Info($"[{ModId}] Loaded. Press Ctrl+Shift+R to restart run.");
    }

    private static void ApplyPatches()
    {
        _harmony!.PatchAll(Assembly.GetExecutingAssembly());
        PauseMenuPatch.TryApplyManualPatch(_harmony);
    }
}
