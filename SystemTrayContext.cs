using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MochaScheduler
{
    public class SystemTrayContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ConfigManager _configManager;
        private readonly JobManager _jobManager;
        private readonly SchedulerEngine _schedulerEngine;
        private readonly NotificationManager _notificationManager;
        private readonly JobListForm _jobListForm;
        private readonly SynchronizationContext? _syncContext;

        public SystemTrayContext(
            ConfigManager configManager,
            JobManager jobManager,
            SchedulerEngine schedulerEngine,
            NotificationManager notificationManager)
        {
            _syncContext = SynchronizationContext.Current;
            _configManager = configManager;
            _jobManager = jobManager;
            _schedulerEngine = schedulerEngine;
            _notificationManager = notificationManager;

            // ジョブ一覧ウィンドウの初期化
            _jobListForm = new JobListForm(_configManager, _jobManager);

            // 各種マネージャーのイベント購読
            _notificationManager.NotificationRequested += OnNotificationRequested;
            _configManager.ConfigChanged += OnConfigChanged;
            
            // ジョブの実行状態変化時にメニューを再構築
            _jobManager.JobStateChanged += OnJobStateChanged;

            // NotifyIcon の作成
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Mocha PS Scheduler (待機中)",
                Visible = true
            };

            // ダブルクリック時にジョブ一覧ウィンドウを表示する
            _notifyIcon.DoubleClick += (s, e) => ShowJobListForm();

            // 左クリック時にジョブ一覧ウィンドウを表示する (MouseClickより信頼性の高いMouseUpを使用)
            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowJobListForm();
                }
            };

            // 初期メニューの構築
            BuildContextMenu();
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            // ステータス表示
            var runningCount = _jobManager.RunningJobsCount;
            var statusText = runningCount > 0 ? $"実行中 ({runningCount}個のジョブが動作中)" : "ステータス: 待機中";
            var statusItem = new ToolStripMenuItem(statusText) { Enabled = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());

            // 「スケジュール一覧」ウィンドウを開くメニュー
            var showListItem = new ToolStripMenuItem("スケジュール一覧を開く", null, (s, e) => ShowJobListForm());
            menu.Items.Add(showListItem);

            // 「今すぐ実行」サブメニューの構築
            var runSubMenu = new ToolStripMenuItem("今すぐ実行");
            if (_configManager.Config.Jobs.Count == 0)
            {
                runSubMenu.DropDownItems.Add(new ToolStripMenuItem("(ジョブなし)") { Enabled = false });
            }
            else
            {
                foreach (var job in _configManager.Config.Jobs)
                {
                    if (string.IsNullOrEmpty(job.Id)) continue;
                    
                    var jobName = string.IsNullOrEmpty(job.Name) ? job.Id : job.Name;
                    var runItem = new ToolStripMenuItem(jobName);
                    
                    if (_jobManager.IsJobRunning(job.Id))
                    {
                        runItem.Enabled = false;
                        runItem.Text += " (実行中)";
                    }
                    
                    runItem.Click += (s, e) =>
                    {
                        _ = _jobManager.TriggerJobAsync(job, isManual: true);
                    };
                    runSubMenu.DropDownItems.Add(runItem);
                }
            }
            menu.Items.Add(runSubMenu);

            // 「実行中のジョブを停止」サブメニュー (実行中のものがある場合のみ)
            if (runningCount > 0)
            {
                var stopSubMenu = new ToolStripMenuItem("実行中のジョブを停止");
                foreach (var job in _configManager.Config.Jobs)
                {
                    if (_jobManager.IsJobRunning(job.Id))
                    {
                        var jobName = string.IsNullOrEmpty(job.Name) ? job.Id : job.Name;
                        var stopItem = new ToolStripMenuItem(jobName);
                        stopItem.Click += (s, e) => _jobManager.CancelJob(job.Id);
                        stopSubMenu.DropDownItems.Add(stopItem);
                    }
                }
                menu.Items.Add(stopSubMenu);
            }

            menu.Items.Add(new ToolStripSeparator());

            // ログフォルダを開く
            var openLogDirItem = new ToolStripMenuItem("ログフォルダを開く", null, (s, e) => OpenLogDirectory());
            menu.Items.Add(openLogDirItem);

            // スタートアップ自動起動の切り替え
            var isStartupEnabled = StartupManager.IsStartupEnabled();
            var startupItem = new ToolStripMenuItem("スタートアップ自動起動", null, (s, e) => ToggleStartup(s as ToolStripMenuItem))
            {
                Checked = isStartupEnabled
            };
            menu.Items.Add(startupItem);

            menu.Items.Add(new ToolStripSeparator());

            // 終了
            var exitItem = new ToolStripMenuItem("終了", null, (s, e) => ExitApplication());
            menu.Items.Add(exitItem);

            // 古いメニューがある場合は破棄してメモリリークを防ぐ
            var oldMenu = _notifyIcon.ContextMenuStrip;
            _notifyIcon.ContextMenuStrip = menu;
            oldMenu?.Dispose();

            // ツールチップの文言も更新
            _notifyIcon.Text = $"Mocha PS Scheduler\n{statusText}";
        }

        private void ShowJobListForm()
        {
            if (_jobListForm.WindowState == FormWindowState.Minimized)
            {
                _jobListForm.WindowState = FormWindowState.Normal;
            }
            _jobListForm.Show();
            _jobListForm.Activate();
        }

        private void OpenLogDirectory()
        {
            try
            {
                var dir = LogManager.GetLogDirectory();
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ログフォルダを開くことができませんでした:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleStartup(ToolStripMenuItem? item)
        {
            if (item == null) return;

            bool targetState = !item.Checked;
            if (StartupManager.SetStartup(targetState))
            {
                item.Checked = targetState;
                var msg = targetState ? "PC起動時のバックグラウンド自動起動を登録しました。" : "自動起動を解除しました。";
                _notificationManager.ShowAppNotification("スタートアップ設定変更", msg);
            }
            else
            {
                MessageBox.Show(
                    "スタートアップ設定の適用に失敗しました。\n管理者としてアプリが起動しているか確認してください。",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void OnNotificationRequested(object? sender, NotificationManager.NotificationEventArgs e)
        {
            SafeInvoke(() => 
            {
                _notifyIcon.ShowBalloonTip(3000, e.Title, e.Message, e.Icon);
            });
        }

        private void OnConfigChanged(object? sender, AppConfig e)
        {
            SafeInvoke(BuildContextMenu);
        }

        private void OnJobStateChanged(object? sender, EventArgs e)
        {
            SafeInvoke(BuildContextMenu);
        }

        private void SafeInvoke(Action action)
        {
            var syncContext = _syncContext ?? SynchronizationContext.Current;
            if (syncContext != null)
            {
                syncContext.Post(_ => action(), null);
                return;
            }

            if (_jobListForm != null && !_jobListForm.IsDisposed)
            {
                try
                {
                    if (_jobListForm.InvokeRequired)
                    {
                        _jobListForm.BeginInvoke(action);
                        return;
                    }
                }
                catch
                {
                    // ハンドル未作成などの例外対策
                }
            }

            action();
        }

        private void ExitApplication()
        {
            LogManager.LogApp("Application stop request received. Shutting down...");
            
            // ジョブのキャンセルとスケジューラの停止
            _jobManager.CancelAllJobs();
            _schedulerEngine.Stop();

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            // ウィンドウのクローズと破棄
            _jobListForm.Close();
            
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _notificationManager.NotificationRequested -= OnNotificationRequested;
                _configManager.ConfigChanged -= OnConfigChanged;
                _jobManager.JobStateChanged -= OnJobStateChanged;
                _notifyIcon?.Dispose();
                _jobListForm?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
