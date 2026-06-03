using System;
using System.Windows.Forms;

namespace MochaScheduler;

public class NotificationManager(ConfigManager configManager)
{
    public class NotificationEventArgs(string title, string message, ToolTipIcon icon) : EventArgs
    {
        public string Title { get; } = title;
        public string Message { get; } = message;
        public ToolTipIcon Icon { get; } = icon;
    }

    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public void ShowJobSuccess(string jobName, bool isManual)
    {
        if (isManual || configManager.Config.Notification.OnSuccess)
        {
            NotificationRequested?.Invoke(this, new NotificationEventArgs(
                "ジョブ実行成功",
                $"ジョブ '{jobName}' が正常に完了しました。",
                ToolTipIcon.Info
            ));
        }
    }

    public void ShowJobFailure(string jobName, string error, bool isManual)
    {
        if (isManual || configManager.Config.Notification.OnFailure)
        {
            NotificationRequested?.Invoke(this, new NotificationEventArgs(
                "ジョブ実行失敗",
                $"ジョブ '{jobName}' の実行に失敗しました。\n{error}",
                ToolTipIcon.Error
            ));
        }
    }

    public void ShowAppNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, icon));
    }
}
