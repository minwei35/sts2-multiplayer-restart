using Godot;
using MegaCrit.Sts2.Core.Logging;
using MultiplayerRestart.Utils;

namespace MultiplayerRestart;

/// <summary>
/// 管理重开流程：权限校验 → 确认弹窗 → 执行重开。
/// </summary>
public static class RestartManager
{
    private static bool _dialogOpen;
    private static bool _restarting;

    public static void TriggerRestart()
    {
        if (_dialogOpen || _restarting) return;

        if (!GameApi.IsInRun())
        {
            Log.Info("[MultiplayerRestart] Not in an active run, ignoring restart request.");
            return;
        }

        if (GameApi.IsMultiplayer() && !GameApi.IsHost())
        {
            ShowNotification("Only the host can restart the run.\n只有房主可以发起重开。");
            return;
        }

        ShowConfirmDialog();
    }

    private static void ShowConfirmDialog()
    {
        _dialogOpen = true;

        var tree = (SceneTree)Engine.GetMainLoop();

        var dialog = new ConfirmationDialog();
        dialog.Title = "Restart Run / 重开一局";
        dialog.DialogText = GameApi.IsMultiplayer()
            ? "Restart the multiplayer run?\nAll players will return to the main menu.\n\n确定要重开吗？所有玩家将返回主菜单。"
            : "Restart the current run?\n\n确定要重开当前局吗？";

        dialog.OkButtonText = "Restart / 重开";
        dialog.CancelButtonText = "Cancel / 取消";

        dialog.MinSize = new Vector2I(420, 180);
        dialog.ProcessMode = Node.ProcessModeEnum.Always;

        dialog.Connect("confirmed", Callable.From(new Action(OnConfirmed)));
        dialog.Connect("canceled", Callable.From(new Action(OnCanceled)));
        dialog.Connect("tree_exited", Callable.From(new Action(OnDialogExited)));

        tree.Root.CallDeferred("add_child", dialog);
        dialog.CallDeferred("popup_centered");
    }

    private static void ShowNotification(string message)
    {
        var tree = (SceneTree)Engine.GetMainLoop();

        var dialog = new AcceptDialog();
        dialog.Title = "Multiplayer Restart";
        dialog.DialogText = message;
        dialog.ProcessMode = Node.ProcessModeEnum.Always;
        dialog.Connect("tree_exited", Callable.From(new Action(() =>
        {
            dialog.QueueFree();
        })));

        tree.Root.CallDeferred("add_child", dialog);
        dialog.CallDeferred("popup_centered");
    }

    private static void OnConfirmed()
    {
        _dialogOpen = false;
        ExecuteRestart();
    }

    private static void OnCanceled()
    {
        _dialogOpen = false;
    }

    private static void OnDialogExited()
    {
        _dialogOpen = false;
    }

    private static void ExecuteRestart()
    {
        _restarting = true;

        Log.Info("[MultiplayerRestart] Executing restart...");

        // 策略1: 通过游戏 API 放弃当前 run
        if (GameApi.TryAbandonRun())
        {
            Log.Info("[MultiplayerRestart] Run abandoned via game API.");
            _restarting = false;
            return;
        }

        // 策略2: 通过游戏 API 返回主菜单
        if (GameApi.TryReturnToMenu())
        {
            Log.Info("[MultiplayerRestart] Returned to menu via game API.");
            _restarting = false;
            return;
        }

        // 策略3: 强制断开并返回
        Log.Info("[MultiplayerRestart] Using fallback: force disconnect and return.");
        GameApi.ForceDisconnectAndReturn();
        _restarting = false;
    }
}
