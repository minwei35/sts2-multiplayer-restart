using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace MultiplayerRestart.Utils;

/// <summary>
/// 通过反射定位游戏内部 API。
/// 基于 sts2.dll 反编译结果确定的类名和方法名。
/// </summary>
public static class GameApi
{
    // NGame: 游戏主节点，管理 run 生命周期
    private static Type? _nGameType;
    private static object? _nGameInstance;

    // 关键方法
    private static MethodInfo? _abandonRunMethod;
    private static MethodInfo? _returnToMainMenuAfterRunMethod;
    private static MethodInfo? _returnToMainMenuMethod;
    private static MethodInfo? _startNewMultiplayerRunMethod;
    private static MethodInfo? _tryAbandonMultiplayerRunMethod;

    // NPauseMenu
    private static Type? _nPauseMenuType;

    // NMultiplayerSubmenu
    private static Type? _nMultiplayerSubmenuType;

    // 状态字段
    private static FieldInfo? _isInRunField;

    public static Type? NPauseMenuType => _nPauseMenuType;
    public static bool IsReady => _nGameType != null;

    public static void DiscoverApis()
    {
        DiscoverNGame();
        DiscoverPauseMenu();
        DiscoverMultiplayerSubmenu();
        LogDiscoveryResults();
    }

    private static void DiscoverNGame()
    {
        _nGameType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.NGame");
        if (_nGameType == null)
        {
            Log.Warn("[MultiplayerRestart] NGame type not found.");
            return;
        }

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        _abandonRunMethod = _nGameType.GetMethod("AbandonRun", flags)
                         ?? _nGameType.GetMethod("AbandonRunAsync", flags);

        _returnToMainMenuAfterRunMethod = _nGameType.GetMethod("ReturnToMainMenuAfterRun", flags);
        _returnToMainMenuMethod = _nGameType.GetMethod("ReturnToMainMenu", flags);
        _startNewMultiplayerRunMethod = _nGameType.GetMethod("StartNewMultiplayerRun", flags);
        _isInRunField = _nGameType.GetField("_isInRun", flags);
    }

    private static void DiscoverPauseMenu()
    {
        _nPauseMenuType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu");
    }

    private static void DiscoverMultiplayerSubmenu()
    {
        _nMultiplayerSubmenuType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu");

        if (_nMultiplayerSubmenuType != null)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _tryAbandonMultiplayerRunMethod = _nMultiplayerSubmenuType.GetMethod("TryAbandonMultiplayerRun", flags);
        }
    }

    private static void LogDiscoveryResults()
    {
        Log.Info($"[MultiplayerRestart] API Discovery Results:");
        Log.Info($"  NGame: {(_nGameType != null ? "OK" : "MISSING")}");
        Log.Info($"  AbandonRun: {(_abandonRunMethod != null ? _abandonRunMethod.Name : "MISSING")}");
        Log.Info($"  ReturnToMainMenuAfterRun: {(_returnToMainMenuAfterRunMethod != null ? "OK" : "MISSING")}");
        Log.Info($"  ReturnToMainMenu: {(_returnToMainMenuMethod != null ? "OK" : "MISSING")}");
        Log.Info($"  StartNewMultiplayerRun: {(_startNewMultiplayerRunMethod != null ? "OK" : "MISSING")}");
        Log.Info($"  NPauseMenu: {(_nPauseMenuType != null ? "OK" : "MISSING")}");
        Log.Info($"  NMultiplayerSubmenu: {(_nMultiplayerSubmenuType != null ? "OK" : "MISSING")}");
        Log.Info($"  TryAbandonMultiplayerRun: {(_tryAbandonMultiplayerRunMethod != null ? "OK" : "MISSING")}");
    }

    /// <summary>
    /// 获取 NGame 实例（通过场景树查找）。
    /// </summary>
    public static object? GetNGameInstance()
    {
        if (_nGameInstance != null && _nGameInstance is GodotObject go && GodotObject.IsInstanceValid(go))
            return _nGameInstance;

        if (_nGameType == null) return null;

        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            _nGameInstance = FindNodeOfType(tree.Root, _nGameType);
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] Failed to find NGame: {ex.Message}");
        }

        return _nGameInstance;
    }

    private static object? FindNodeOfType(Node root, Type targetType)
    {
        if (root.GetType() == targetType || targetType.IsInstanceOfType(root))
            return root;

        foreach (var child in root.GetChildren())
        {
            var result = FindNodeOfType(child, targetType);
            if (result != null) return result;
        }

        return null;
    }

    /// <summary>
    /// 放弃当前 run。对于多人游戏会自动通知所有玩家。
    /// </summary>
    public static bool TryAbandonRun()
    {
        var nGame = GetNGameInstance();
        if (nGame == null)
        {
            Log.Warn("[MultiplayerRestart] NGame instance not found.");
            return false;
        }

        try
        {
            // 优先使用 AbandonRun
            if (_abandonRunMethod != null)
            {
                var result = _abandonRunMethod.Invoke(nGame, null);
                HandleAsyncResult(result, "AbandonRun");
                return true;
            }

            // 退而使用 ReturnToMainMenuAfterRun
            if (_returnToMainMenuAfterRunMethod != null)
            {
                var result = _returnToMainMenuAfterRunMethod.Invoke(nGame, null);
                HandleAsyncResult(result, "ReturnToMainMenuAfterRun");
                return true;
            }

            Log.Warn("[MultiplayerRestart] No abandon method available.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] AbandonRun invocation failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 返回主菜单。
    /// </summary>
    public static bool TryReturnToMenu()
    {
        var nGame = GetNGameInstance();
        if (nGame == null) return false;

        try
        {
            var method = _returnToMainMenuMethod ?? _returnToMainMenuAfterRunMethod;
            if (method == null) return false;

            var result = method.Invoke(nGame, null);
            HandleAsyncResult(result, method.Name);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] ReturnToMenu failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    private static void HandleAsyncResult(object? result, string methodName)
    {
        if (result is System.Threading.Tasks.Task task)
        {
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Log.Warn($"[MultiplayerRestart] {methodName} async failed: {t.Exception?.InnerException?.Message}");
                else
                    Log.Info($"[MultiplayerRestart] {methodName} completed.");
            });
        }
    }

    /// <summary>
    /// 检查当前是否处于多人游戏中。
    /// </summary>
    public static bool IsMultiplayer()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var peer = tree.GetMultiplayer()?.MultiplayerPeer;
            if (peer == null) return false;
            return peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查本机是否为多人游戏的主机。
    /// </summary>
    public static bool IsHost()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var mp = tree.GetMultiplayer();
            if (mp?.MultiplayerPeer == null) return true;
            if (mp.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return true;
            return mp.IsServer();
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 检查当前是否在一个活跃的 run 中。
    /// </summary>
    public static bool IsInRun()
    {
        // 策略1: 通过 NGame._isInRun 字段
        try
        {
            var nGame = GetNGameInstance();
            if (nGame != null && _isInRunField != null)
            {
                var value = _isInRunField.GetValue(nGame);
                if (value is bool b) return b;
            }
        }
        catch { /* fall through */ }

        // 策略2: 通过场景树判断
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var root = tree.Root;
            string[] runIndicators = { "RunScreen", "CombatScreen", "MapScreen", "Run", "GameScreen" };
            foreach (var name in runIndicators)
            {
                if (root.FindChild(name, true, false) != null)
                    return true;
            }
        }
        catch { /* fall through */ }

        return false;
    }

    /// <summary>
    /// 强制断开多人连接并尝试返回主菜单。
    /// </summary>
    public static void ForceDisconnectAndReturn()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var peer = tree.GetMultiplayer()?.MultiplayerPeer;
            if (peer != null && peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected)
            {
                Log.Info("[MultiplayerRestart] Closing multiplayer peer...");
                peer.Close();
            }

            if (!TryReturnToMenu())
            {
                Log.Info("[MultiplayerRestart] Using scene tree reload as fallback.");
                tree.CallDeferred("reload_current_scene");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] ForceDisconnectAndReturn failed: {ex.Message}");
        }
    }
}
