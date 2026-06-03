using System;
using System.Threading;
using System.Windows.Forms;

namespace MochaScheduler;

static class Program
{
    private static Mutex? _mutex;
    private static System.Drawing.Icon? _appIcon;
    public static System.Drawing.Icon AppIcon => _appIcon ??= LoadAppIcon();

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(icoPath))
            {
                return new System.Drawing.Icon(icoPath);
            }
            
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(processPath) ?? System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
            // Fallback
        }
        return System.Drawing.SystemIcons.Application;
    }

    [STAThread]
    static void Main()
    {
        // 二重起動防止用 Mutex (マシン全体で一意)
        const string mutexName = "Global\\MochaPSScheduler_Mutex_2026";
        _mutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show(
                "Mocha PS Scheduler はすでに起動しています。\nシステムトレイのアイコンを確認してください。",
                "二重起動防止",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        // Windows Forms の高DPIなどの設定初期化
        ApplicationConfiguration.Initialize();

        // 同期コンテキストを明示的にインストールして、Application.Run 起動前でも UI スレッドへの Post が正しく行われるようにする
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        // 未処理例外のグローバルハンドラを設定してログに出力できるようにする
        Application.ThreadException += (sender, e) =>
        {
            LogManager.LogApp($"Unhandled UI thread exception: {e.Exception}", "FATAL");
            MessageBox.Show(
                $"アプリケーションのエラーが発生しました:\n{e.Exception.Message}\n\n詳細はlogs\\app.logを確認してください。",
                "エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            LogManager.LogApp($"Unhandled AppDomain exception: {e.ExceptionObject}", "FATAL");
        };

        try
        {
            // コア管理モジュールの初期化
            using var configManager = new ConfigManager();
            var notificationManager = new NotificationManager(configManager);
            var jobManager = new JobManager(notificationManager);
            
            using var schedulerEngine = new SchedulerEngine(configManager, jobManager);

            // 定期実行スケジューラの開始
            schedulerEngine.Start();

            // トレイ常駐UIの開始
            using var trayContext = new SystemTrayContext(
                configManager,
                jobManager,
                schedulerEngine,
                notificationManager
            );

            // メッセージループの開始
            Application.Run(trayContext);
        }
        catch (Exception ex)
        {
            LogManager.LogApp($"Fatal error in application entry point: {ex}", "FATAL");
            MessageBox.Show(
                $"アプリケーションで重大なエラーが発生しました:\n{ex.Message}\n\n詳細はlogs\\app.logを確認してください。",
                "致命的なエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        finally
        {
            // Mutexの解放
            if (_mutex is not null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // 放出失敗は無視
                }
                _mutex.Dispose();
            }
        }
    }
}