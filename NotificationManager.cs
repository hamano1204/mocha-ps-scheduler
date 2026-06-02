using System;
using System.Windows.Forms;

namespace MochaScheduler
{
    public class NotificationManager
    {
        public class NotificationEventArgs : EventArgs
        {
            public string Title { get; }
            public string Message { get; }
            public ToolTipIcon Icon { get; }

            public NotificationEventArgs(string title, string message, ToolTipIcon icon)
            {
                Title = title;
                Message = message;
                Icon = icon;
            }
        }

        public event EventHandler<NotificationEventArgs>? NotificationRequested;
        
        private readonly ConfigManager _configManager;

        public NotificationManager(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public void ShowJobSuccess(string jobName, bool isManual)
        {
            if (isManual || _configManager.Config.Notification.OnSuccess)
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
            if (isManual || _configManager.Config.Notification.OnFailure)
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
}
