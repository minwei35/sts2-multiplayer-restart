using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace MultiplayerRestart.Patches;

/// <summary>
/// 在暂停菜单 (NPauseMenu) 中注入"重开一局"按钮。
/// 目标类: MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu
/// </summary>
public static class PauseMenuPatch
{
    private static bool _patched;
    private static readonly HashSet<ulong> _injectedNodes = new();

    public static void TryApplyManualPatch(Harmony harmony)
    {
        if (_patched) return;

        // 目标: NPauseMenu 的打开/显示方法
        var pauseType = Utils.GameApi.NPauseMenuType;
        if (pauseType == null)
        {
            Log.Info("[MultiplayerRestart] NPauseMenu type not found, using scene tree watcher.");
            SetupSceneTreeWatcher();
            return;
        }

        // 尝试 patch 暂停菜单的可能方法
        string[] candidates = { "openPauseMenu", "Open", "_Ready", "Initialize", "Show" };
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var methodName in candidates)
        {
            var method = pauseType.GetMethod(methodName, flags);
            if (method == null) continue;

            try
            {
                harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(typeof(PauseMenuPatch), nameof(OnPauseMenuOpened))
                );
                _patched = true;
                Log.Info($"[MultiplayerRestart] Patched NPauseMenu.{methodName}");
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Failed to patch NPauseMenu.{methodName}: {ex.Message}");
            }
        }

        if (!_patched)
        {
            Log.Info("[MultiplayerRestart] Could not patch NPauseMenu methods, using scene tree watcher.");
            SetupSceneTreeWatcher();
        }
    }

    private static void SetupSceneTreeWatcher()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            tree.Connect("node_added", Callable.From(new Action<Node>(OnNodeAdded)));
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] Scene tree watcher setup failed: {ex.Message}");
        }
    }

    private static void OnNodeAdded(Node node)
    {
        var nodeName = node.Name.ToString().ToLowerInvariant();
        if (!nodeName.Contains("pause") && !nodeName.Contains("pausemenu")) return;
        if (node is not Control control) return;

        // 延迟注入，等 UI 树构建完成
        var tree = node.GetTree();
        var timer = tree.CreateTimer(0.15);
        timer.Connect("timeout", Callable.From(new Action(() => TryInjectButton(control))));
    }

    /// <summary>
    /// Harmony Postfix: NPauseMenu 打开时注入按钮。
    /// </summary>
    public static void OnPauseMenuOpened(object __instance)
    {
        if (__instance is not Control control) return;

        // 延迟执行以确保按钮容器已完成布局
        var tree = control.GetTree();
        var timer = tree.CreateTimer(0.1);
        timer.Connect("timeout", Callable.From(new Action(() => TryInjectButton(control))));
    }

    private static void TryInjectButton(Control parentControl)
    {
        var instanceId = parentControl.GetInstanceId();
        if (_injectedNodes.Contains(instanceId)) return;

        // 查找包含按钮的容器
        var container = FindButtonContainer(parentControl);
        if (container == null)
        {
            Log.Info("[MultiplayerRestart] No button container found in pause menu.");
            return;
        }

        // 检查是否已经注入
        foreach (var child in container.GetChildren())
        {
            if (child is Button btn && btn.Name == "RestartRunButton")
                return;
        }

        _injectedNodes.Add(instanceId);

        var restartButton = new Button();
        restartButton.Name = "RestartRunButton";
        restartButton.Text = "Restart Run";
        restartButton.TooltipText = "Restart the current run (Ctrl+Shift+R)";

        CopyButtonStyle(container, restartButton);

        restartButton.Connect("pressed", Callable.From(new Action(() =>
        {
            RestartManager.TriggerRestart();
        })));

        // 在 Abandon Run 按钮之前插入
        int insertIdx = FindAbandonButtonIndex(container);
        container.AddChild(restartButton);
        if (insertIdx >= 0)
        {
            container.MoveChild(restartButton, insertIdx);
        }

        Log.Info("[MultiplayerRestart] Restart button injected into pause menu.");
    }

    private static Container? FindButtonContainer(Control root)
    {
        if (root is Container c && HasButtonChildren(c))
            return c;

        foreach (var child in root.GetChildren())
        {
            if (child is not Control childControl) continue;
            var found = FindButtonContainer(childControl);
            if (found != null) return found;
        }

        return null;
    }

    private static bool HasButtonChildren(Container container)
    {
        int buttonCount = 0;
        foreach (var child in container.GetChildren())
        {
            if (child is Button) buttonCount++;
        }
        return buttonCount >= 2;
    }

    private static int FindAbandonButtonIndex(Container container)
    {
        int index = 0;
        foreach (var child in container.GetChildren())
        {
            if (child is Button button)
            {
                var text = button.Text?.ToLowerInvariant() ?? "";
                var name = button.Name.ToString().ToLowerInvariant();
                if (text.Contains("abandon") || text.Contains("quit") ||
                    name.Contains("abandon") || name.Contains("quit"))
                    return index;
            }
            index++;
        }
        return -1;
    }

    private static void CopyButtonStyle(Container container, Button target)
    {
        foreach (var child in container.GetChildren())
        {
            if (child is not Button existingButton) continue;

            target.CustomMinimumSize = existingButton.CustomMinimumSize;
            target.SizeFlagsHorizontal = existingButton.SizeFlagsHorizontal;
            target.SizeFlagsVertical = existingButton.SizeFlagsVertical;

            if (existingButton.HasThemeStyleboxOverride("normal"))
                target.AddThemeStyleboxOverride("normal", existingButton.GetThemeStylebox("normal"));
            if (existingButton.HasThemeStyleboxOverride("hover"))
                target.AddThemeStyleboxOverride("hover", existingButton.GetThemeStylebox("hover"));
            if (existingButton.HasThemeStyleboxOverride("pressed"))
                target.AddThemeStyleboxOverride("pressed", existingButton.GetThemeStylebox("pressed"));

            break;
        }
    }
}
