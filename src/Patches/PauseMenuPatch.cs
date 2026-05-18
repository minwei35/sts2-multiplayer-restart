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
    private static FieldInfo? _buttonContainerField;

    public static void TryApplyManualPatch(Harmony harmony)
    {
        if (_patched) return;

        var pauseType = Utils.GameApi.NPauseMenuType;
        if (pauseType == null)
        {
            Log.Warn("[MultiplayerRestart] NPauseMenu type not found.");
            return;
        }

        // 发现 NPauseMenuButton 类型和 _buttonContainer 字段
        _pauseMenuButtonType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenuButton");
        _buttonContainerField = pauseType.GetField("_buttonContainer",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Log.Info($"[MultiplayerRestart] NPauseMenuButton type: {(_pauseMenuButtonType != null ? "OK" : "MISSING")}");
        Log.Info($"[MultiplayerRestart] _buttonContainer field: {(_buttonContainerField != null ? "OK" : "MISSING")}");

        // Patch _Ready 方法
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

        // 同时 patch openPauseMenu 以便每次打开时检查
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
        // 延迟注入，等子节点加载完成
        var timer = node.GetTree().CreateTimer(0.2);
        timer.Connect("timeout", Callable.From(new Action(() => TryInjectButton(node))));
    }

    public static void OnPauseMenuOpened(object __instance)
    {
        if (__instance is not Node node) return;
        // 每次打开时检查
        node.CallDeferred(new StringName("_inject_restart_check"));
        var timer = node.GetTree().CreateTimer(0.1);
        timer.Connect("timeout", Callable.From(new Action(() => TryInjectButton(node))));
    }

    private static void TryInjectButton(Node pauseMenuNode)
    {
        var instanceId = pauseMenuNode.GetInstanceId();
        if (_injectedNodes.Contains(instanceId)) return;

        // 策略1: 通过反射获取 _buttonContainer 字段
        Node? container = null;
        if (_buttonContainerField != null)
        {
            try
            {
                container = _buttonContainerField.GetValue(pauseMenuNode) as Node;
                Log.Info($"[MultiplayerRestart] Got _buttonContainer via reflection: {container != null}");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Failed to get _buttonContainer: {ex.Message}");
            }
        }

        // 策略2: 遍历场景树查找 buttonContainer 节点
        if (container == null)
        {
            container = FindNodeByNamePattern(pauseMenuNode, "buttoncontainer", "button_container", "buttons");
            if (container != null)
                Log.Info($"[MultiplayerRestart] Found container by name: {container.Name}");
        }

        // 策略3: 查找包含多个 NPauseMenuButton 子节点的容器
        if (container == null)
        {
            container = FindContainerWithPauseButtons(pauseMenuNode);
            if (container != null)
                Log.Info($"[MultiplayerRestart] Found container by button children: {container.Name}");
        }

        if (container == null)
        {
            Log.Warn("[MultiplayerRestart] Could not find button container. Dumping tree:");
            DumpNodeTree(pauseMenuNode, 0, 3);
            return;
        }

        // 检查是否已注入
        foreach (var child in container.GetChildren())
        {
            if (child.Name.ToString() == "RestartRunButton") return;
        }

        _injectedNodes.Add(instanceId);

        // 创建按钮: 优先使用 NPauseMenuButton，否则克隆已有按钮
        Node? restartButton = null;

        // 策略A: 克隆一个已有的 NPauseMenuButton
        Node? templateButton = null;
        foreach (var child in container.GetChildren())
        {
            if (_pauseMenuButtonType != null && _pauseMenuButtonType.IsInstanceOfType(child))
            {
                templateButton = child;
                break;
            }
        }

        if (templateButton != null)
        {
            try
            {
                restartButton = (Node)templateButton.Duplicate();
                restartButton.Name = "RestartRunButton";

                // 通过反射设置按钮文本 (NPauseMenuButton 可能使用 MegaLabel)
                SetButtonText(restartButton, "Restart Run");

                // 断开原有信号，连接新的
                DisconnectAllSignals(restartButton, "pressed");
                DisconnectAllSignals(restartButton, "Pressed");

                if (restartButton is Control ctrl)
                {
                    ctrl.Connect("pressed", Callable.From(new Action(() =>
                    {
                        RestartManager.TriggerRestart();
                    })));
                }

                Log.Info("[MultiplayerRestart] Created restart button by cloning NPauseMenuButton.");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Failed to clone button: {ex.Message}");
                restartButton = null;
            }
        }

        // 策略B: 直接实例化 NPauseMenuButton
        if (restartButton == null && _pauseMenuButtonType != null)
        {
            try
            {
                restartButton = (Node)Activator.CreateInstance(_pauseMenuButtonType)!;
                restartButton.Name = "RestartRunButton";
                SetButtonText(restartButton, "Restart Run");

                if (restartButton is Control ctrl)
                {
                    ctrl.Connect("pressed", Callable.From(new Action(() =>
                    {
                        RestartManager.TriggerRestart();
                    })));
                }

                Log.Info("[MultiplayerRestart] Created restart button via Activator.");
            }
            catch (Exception ex)
            {
                Log.Warn($"[MultiplayerRestart] Failed to instantiate NPauseMenuButton: {ex.Message}");
                restartButton = null;
            }
        }

        // 策略C: 使用普通 Button 作为最后手段
        if (restartButton == null)
        {
            var btn = new Button();
            btn.Name = "RestartRunButton";
            btn.Text = "Restart Run";
            btn.Connect("pressed", Callable.From(new Action(() =>
            {
                RestartManager.TriggerRestart();
            })));
            CopyStyleFromSibling(container, btn);
            restartButton = btn;
            Log.Info("[MultiplayerRestart] Created restart button as plain Button (fallback).");
        }

        // 插入到 SaveAndQuit/GiveUp 按钮之前
        int insertIdx = FindTargetButtonIndex(container);
        container.AddChild(restartButton);
        if (insertIdx >= 0)
        {
            container.MoveChild(restartButton, insertIdx);
        }

        // 重建焦点邻居
        TryRebuildFocusNeighbors(pauseMenuNode);

        Log.Info("[MultiplayerRestart] Restart button injected into pause menu.");
    }

    private static void SetButtonText(Node button, string text)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // 尝试 Text 属性 (如果继承自 Button)
        var textProp = button.GetType().GetProperty("Text", flags);
        if (textProp != null && textProp.CanWrite)
        {
            textProp.SetValue(button, text);
            return;
        }

        // 尝试 SetText 方法
        var setTextMethod = button.GetType().GetMethod("SetText", flags);
        if (setTextMethod != null)
        {
            try { setTextMethod.Invoke(button, new object[] { text }); return; } catch { }
        }

        // 尝试查找 MegaLabel 子节点并设置文本
        foreach (var child in button.GetChildren())
        {
            var childType = child.GetType();
            if (childType.Name.Contains("MegaLabel") || childType.Name.Contains("Label"))
            {
                var labelTextProp = childType.GetProperty("Text", flags);
                if (labelTextProp != null && labelTextProp.CanWrite)
                {
                    labelTextProp.SetValue(child, text);
                    return;
                }
            }
        }

        // 尝试通过 Godot 的 Set 方法
        if (button is GodotObject go)
        {
            try { go.Set("text", text); } catch { }
        }
    }

    private static void DisconnectAllSignals(Node node, string signalName)
    {
        try
        {
            if (node is GodotObject go)
            {
                var signals = go.GetSignalConnectionList(signalName);
                foreach (var dict in signals)
                {
                    if (dict.TryGetValue("callable", out var callable))
                    {
                        go.Disconnect(signalName, (Callable)callable);
                    }
                }
            }
        }
        catch { }
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
            if (method != null)
            {
                method.Invoke(pauseMenuNode, null);
                Log.Info("[MultiplayerRestart] RebuildFocusNeighbors called.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[MultiplayerRestart] RebuildFocusNeighbors failed: {ex.Message}");
        }
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
        var typeName = node.GetType().Name;
        Log.Info($"[MultiplayerRestart] {indent}{node.Name} ({typeName}) children={node.GetChildCount()}");
        foreach (var child in node.GetChildren())
        {
            DumpNodeTree(child, depth + 1, maxDepth);
        }
    }
}
