using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace MultiplayerRestart;

/// <summary>
/// 键盘快捷键输入检测。
/// 使用 Timer 轮询 Input 状态，避免创建自定义 Godot Node 类型。
/// </summary>
public static class InputHandler
{
    private static Godot.Timer? _pollTimer;
    private static bool _keyWasDown;
    private static bool _initialized;

    // Ctrl+Shift+R 触发重开
    private const Key TriggerKey = Key.R;

    public static void Setup()
    {
        if (_initialized) return;
        _initialized = true;

        var tree = (SceneTree)Engine.GetMainLoop();
        if (tree?.Root == null)
        {
            Log.Warn("[MultiplayerRestart] SceneTree not ready, deferring input setup.");
            return;
        }

        _pollTimer = new Godot.Timer();
        _pollTimer.Name = "MultiplayerRestart_InputPoll";
        _pollTimer.WaitTime = 0.05; // 50ms, 20Hz
        _pollTimer.Autostart = true;
        _pollTimer.ProcessCallback = Godot.Timer.TimerProcessCallback.Idle;
        _pollTimer.ProcessMode = Node.ProcessModeEnum.Always;
        _pollTimer.Connect("timeout", Callable.From(new Action(PollInput)));

        tree.Root.CallDeferred("add_child", _pollTimer);
        Log.Info("[MultiplayerRestart] Input handler registered (Ctrl+Shift+R).");
    }

    private static void PollInput()
    {
        bool isDown = Input.IsKeyPressed(TriggerKey)
                   && Input.IsKeyPressed(Key.Ctrl)
                   && Input.IsKeyPressed(Key.Shift);

        // 只在按下瞬间触发（边沿检测）
        if (isDown && !_keyWasDown)
        {
            RestartManager.TriggerRestart();
        }

        _keyWasDown = isDown;
    }

    public static void Cleanup()
    {
        if (_pollTimer != null && GodotObject.IsInstanceValid(_pollTimer))
        {
            _pollTimer.Stop();
            _pollTimer.QueueFree();
            _pollTimer = null;
        }
        _initialized = false;
    }
}
