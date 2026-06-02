using System;
using System.IO;

namespace MochaScheduler
{
    public static class LogManager
    {
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly object LockObj = new();
        private const long MaxLogSize = 5 * 1024 * 1024; // 5MB

        static LogManager()
        {
            try
            {
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }
            }
            catch
            {
                // 静的コンストラクタでの例外を防止
            }
        }

        public static string GetLogDirectory() => LogDir;

        public static void LogApp(string message, string level = "INFO")
        {
            var formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            WriteToFile(Path.Combine(LogDir, "app.log"), formatted);
        }

        public static void LogJob(string jobId, string message, bool isError = false)
        {
            var prefix = isError ? "[ERROR] " : "[INFO]  ";
            var formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {prefix}{message}";
            WriteToFile(Path.Combine(LogDir, $"job-{jobId}.log"), formatted);
        }

        private static void WriteToFile(string filePath, string content)
        {
            lock (LockObj)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var info = new FileInfo(filePath);
                        if (info.Length > MaxLogSize)
                        {
                            RotateLog(filePath);
                        }
                    }

                    using var writer = new StreamWriter(filePath, append: true);
                    writer.WriteLine(content);
                }
                catch
                {
                    // ログ書き出し自体の失敗は無視する
                }
            }
        }

        private static void RotateLog(string filePath)
        {
            try
            {
                var backupPath = filePath + ".bak";
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                File.Move(filePath, backupPath);
            }
            catch
            {
                // ファイルロックなどの原因で移動できない場合はローテーションを見送る
            }
        }
    }
}
