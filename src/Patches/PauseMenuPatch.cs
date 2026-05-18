using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace MultiplayerRestart.Patches;

public static class PauseMenuPatch
{
    private static bool _patched;
    private static readonly HashSet<ulong> _injectedNodes = new();

    private static Type? _pauseMenuButtonType;
    private static Type? _nClickableControlType;
    private static FieldInfo? _buttonContainerField;
    private static EventInfo? _releasedEvent;

    public static void TryApplyManualPatch(Harmony harmony)
    {
        if (_patched) return;

        var pauseType = Utils.GameApi.NPauseMenuType;
        if (pauseType == null)
        {
            Log.Warn("[MultiplayerRestart] NPauseMenu type not found.");
            return;
        }

        _pauseMenuButtonType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenuButton");
        _nClickableControlType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl");
        _buttonContainerField = pauseType.GetField("_buttonContainer",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        // NClickableControl.Released 事件
        if (_nClickableControlType != null)
            _releasedEvent = _nClickableControlType.GetEvent("Released",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Log.Info($"[MultiplayerRestart] NPauseMenuButton: {(_pauseMenuButtonType != null ? "OK" : "MISSING")}");
        Log.Info($"[MultiplayerRestart] NClickableControl: {(_nClickableControlType != null ? "OK" : "MISSING")}");
        Log.Info($"[MultiplayerRestart] _buttonContainer: {(_buttonContainerField != null ? "OK" : "MISSING")}");
        Log.Info($"[MultiplayerRestart] Released event: {(_releasedEvent != null ? "OK" : "MISSING")}");

        var readyMethod = pauseType.GetMethod("_Ready",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (readyMethod != null)
        {
            try
            {
                harmony.Patch(readyMethod,
                    postfix: new HarmonyMethod(typeof(PauseMenuPatch), nameof(OnPauseMenuReady)));
                _patched = true;
                Log.Info("[MultiplayerRestart] Patched NPauseMenu._Ready");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Failed to patch _Ready: {ex.Message}");
            }
        }

        var openMethod = pauseType.GetMethod("openPauseMenu",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (openMethod != null)
        {
            try
            {
                harmony.Patch(openMethod,
                    postfix: new HarmonyMethod(typeof(PauseMenuPatch), nameof(OnPauseMenuOpened)));
                Log.Info("[MultiplayerRestart] Patched NPauseMenu.openPauseMenu");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Failed to patch openPauseMenu: {ex.Message}");
            }
        }
    }

    public static void OnPauseMenuReady(object __instance)
    {
        if (__instance is not Node node) return;
        var timer = node.GetTree().CreateTimer(0.2);
        timer.Connect("timeout", Callable.From(new Action(() => TryInjectButton(node))));
    }

    public static void OnPauseMenuOpened(object __instance)
    {
        if (__instance is not Node node) return;
        var timer = node.GetTree().CreateTimer(0.1);
        timer.Connect("timeout", Callable.From(new Action(() => TryInjectButton(node))));
    }

    private static void TryInjectButton(Node pauseMenuNode)
    {
        var instanceId = pauseMenuNode.GetInstanceId();
        if (_injectedNodes.Contains(instanceId)) return;

        Node? container = null;

        // 策略1: 反射获取 _buttonContainer
        if (_buttonContainerField != null)
        {
            try
            {
                container = _buttonContainerField.GetValue(pauseMenuNode) as Node;
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] _buttonContainer reflection failed: {ex.Message}");
            }
        }

        // 策略2: 按名称搜索
        if (container == null)
            container = FindNodeByNamePattern(pauseMenuNode, "buttoncontainer", "button_container");

        // 策略3: 查找包含 NPauseMenuButton 子节点的节点
        if (container == null)
            container = FindContainerWithPauseButtons(pauseMenuNode);

        if (container == null)
        {
            Log.Warn("[MultiplayerRestart] Button container not found. Node tree:");
            DumpNodeTree(pauseMenuNode, 0, 3);
            return;
        }

        // 检查是否已注入
        foreach (var child in container.GetChildren())
        {
            if (child.Name.ToString() == "RestartRunButton") return;
        }

        _injectedNodes.Add(instanceId);

        // 找一个模板按钮来克隆
        Node? templateButton = FindTemplateButton(container);

        Node? restartButton = null;

        // 策略A: 克隆已有按钮
        if (templateButton != null)
        {
            try
            {
                restartButton = (Node)templateButton.Duplicate();
                restartButton.Name = "RestartRunButton";
                SetButtonText(restartButton, "Restart Run");
                RemoveExistingEventHandlers(restartButton);
                SubscribeReleasedEvent(restartButton);
                Log.Info("[MultiplayerRestart] Restart button created (cloned).");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Clone failed: {ex.Message}");
                restartButton = null;
            }
        }

        // 策略B: 实例化 NPauseMenuButton
        if (restartButton == null && _pauseMenuButtonType != null)
        {
            try
            {
                restartButton = (Node)Activator.CreateInstance(_pauseMenuButtonType)!;
                restartButton.Name = "RestartRunButton";
                SetButtonText(restartButton, "Restart Run");
                SubscribeReleasedEvent(restartButton);
                Log.Info("[MultiplayerRestart] Restart button created (new instance).");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Instantiation failed: {ex.Message}");
                restartButton = null;
            }
        }

        // 策略C: 普通 Button 回退
        if (restartButton == null)
        {
            var btn = new Button();
            btn.Name = "RestartRunButton";
            btn.Text = "Restart Run";
            btn.Connect("pressed", Callable.From(new Action(OnRestartButtonPressed)));
            CopyStyleFromSibling(container, btn);
            restartButton = btn;
            Log.Info("[MultiplayerRestart] Restart button created (plain Button fallback).");
        }

        int insertIdx = FindTargetButtonIndex(container);
        container.AddChild(restartButton);
        if (insertIdx >= 0)
            container.MoveChild(restartButton, insertIdx);

        TryRebuildFocusNeighbors(pauseMenuNode);
        Log.Info("[MultiplayerRestart] Restart button injected.");
    }

    private static void OnRestartButtonPressed()
    {
        RestartManager.TriggerRestart();
    }

    private static void SubscribeReleasedEvent(Node button)
    {
        // 方式1: 通过 EventInfo 订阅 Released 事件
        if (_releasedEvent != null)
        {
            try
            {
                var handler = Delegate.CreateDelegate(_releasedEvent.EventHandlerType!,
                    typeof(PauseMenuPatch).GetMethod(nameof(OnRestartButtonReleased),
                        BindingFlags.NonPublic | BindingFlags.Static)!);
                _releasedEvent.AddEventHandler(button, handler);
                Log.Info("[MultiplayerRestart] Subscribed to Released event via EventInfo.");
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] EventInfo subscription failed: {ex.Message}");
            }
        }

        // 方式2: 通过反射直接调用 add_Released
        try
        {
            var addMethod = button.GetType().GetMethod("add_Released",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (addMethod != null)
            {
                var paramType = addMethod.GetParameters()[0].ParameterType;
                var handler = Delegate.CreateDelegate(paramType,
                    typeof(PauseMenuPatch).GetMethod(nameof(OnRestartButtonReleased),
                        BindingFlags.NonPublic | BindingFlags.Static)!);
                addMethod.Invoke(button, new object[] { handler });
                Log.Info("[MultiplayerRestart] Subscribed to Released event via add_Released.");
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] add_Released failed: {ex.Message}");
        }

        // 方式3: 尝试 Godot 信号 "released"
        try
        {
            if (button is GodotObject go)
            {
                go.Connect("released", Callable.From(new Action(OnRestartButtonPressed)));
                Log.Info("[MultiplayerRestart] Connected via Godot 'released' signal.");
                return;
            }
        }
        catch { }

        Log.Warn("[MultiplayerRestart] Could not subscribe to any click event on button.");
    }

    private static void OnRestartButtonReleased()
    {
        RestartManager.TriggerRestart();
    }

    private static void RemoveExistingEventHandlers(Node button)
    {
        // 清除克隆按钮上的旧 Released 事件处理器
        if (_releasedEvent != null)
        {
            try
            {
                // 通过反射获取 backing field 并清空
                var backingField = button.GetType().GetField("backing_Released",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (backingField != null)
                {
                    backingField.SetValue(button, null);
                    return;
                }
            }
            catch { }
        }

        // 清除 Pressed 事件
        try
        {
            var backingField = button.GetType().GetField("backing_Pressed",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (backingField != null)
                backingField.SetValue(button, null);
        }
        catch { }
    }

    private static Node? FindTemplateButton(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            if (_pauseMenuButtonType != null && _pauseMenuButtonType.IsInstanceOfType(child))
                return child;
        }
        return null;
    }

    private static void SetButtonText(Node button, string text)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // 尝试 Text 属性
        var textProp = button.GetType().GetProperty("Text", flags);
        if (textProp != null && textProp.CanWrite)
        {
            try { textProp.SetValue(button, text); return; } catch { }
        }

        // 通过 Godot Set
        if (button is GodotObject go)
        {
            try { go.Set("text", text); return; } catch { }
        }

        // 查找 MegaLabel 子节点
        foreach (var child in button.GetChildren())
        {
            var childTypeName = child.GetType().Name;
            if (childTypeName.Contains("MegaLabel") || childTypeName.Contains("Label"))
            {
                var labelProp = child.GetType().GetProperty("Text", flags);
                if (labelProp != null && labelProp.CanWrite)
                {
                    try { labelProp.SetValue(child, text); return; } catch { }
                }
                if (child is GodotObject lgo)
                {
                    try { lgo.Set("text", text); return; } catch { }
                }
            }
        }
    }

    private static Node? FindNodeByNamePattern(Node root, params string[] patterns)
    {
        var name = root.Name.ToString().ToLowerInvariant();
        foreach (var p in patterns)
        {
            if (name.Contains(p)) return root;
        }
        foreach (var child in root.GetChildren())
        {
            var found = FindNodeByNamePattern(child, patterns);
            if (found != null) return found;
        }
        return null;
    }

    private static Node? FindContainerWithPauseButtons(Node root)
    {
        if (_pauseMenuButtonType != null)
        {
            int count = 0;
            foreach (var child in root.GetChildren())
            {
                if (_pauseMenuButtonType.IsInstanceOfType(child)) count++;
            }
            if (count >= 2) return root;
        }
        foreach (var child in root.GetChildren())
        {
            var found = FindContainerWithPauseButtons(child);
            if (found != null) return found;
        }
        return null;
    }

    private static int FindTargetButtonIndex(Node container)
    {
        int index = 0;
        foreach (var child in container.GetChildren())
        {
            var name = child.Name.ToString().ToLowerInvariant();
            if (name.Contains("saveandquit") || name.Contains("giveup") ||
                name.Contains("abandon") || name.Contains("quit"))
                return index;
            index++;
        }
        return -1;
    }

    private static void TryRebuildFocusNeighbors(Node pauseMenuNode)
    {
        try
        {
            var method = pauseMenuNode.GetType().GetMethod("RebuildFocusNeighbors",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(pauseMenuNode, null);
        }
        catch { }
    }

    private static void CopyStyleFromSibling(Node container, Button target)
    {
        foreach (var child in container.GetChildren())
        {
            if (child is not Control sibling) continue;
            target.CustomMinimumSize = sibling.CustomMinimumSize;
            target.SizeFlagsHorizontal = sibling.SizeFlagsHorizontal;
            target.SizeFlagsVertical = sibling.SizeFlagsVertical;
            break;
        }
    }

    private static void DumpNodeTree(Node node, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        var indent = new string(' ', depth * 2);
        Log.Info($"[MultiplayerRestart] {indent}{node.Name} ({node.GetType().Name}) children={node.GetChildCount()}");
        foreach (var child in node.GetChildren())
            DumpNodeTree(child, depth + 1, maxDepth);
    }
}
