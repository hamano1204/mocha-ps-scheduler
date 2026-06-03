using System;
using System.IO;
using Microsoft.Win32;

namespace MochaScheduler;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MochaPSScheduler";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key is null)
            {
                return false;
            }

            var value = key.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            // 登録されているパスと現在のプロセスパスを比較
            string currentExePath = Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory;
            if (currentExePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                currentExePath = Path.ChangeExtension(currentExePath, ".exe");
            }

            // 引用符を除去して正規化比較
            var cleanValue = value.Replace("\"", "").Trim();
            var cleanExe = currentExePath.Trim();

            return string.Equals(cleanValue, cleanExe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogManager.LogApp($"Failed to read startup configuration from registry: {ex.Message}", "ERROR");
            return false;
        }
    }

    public static bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key is null)
            {
                LogManager.LogApp("Startup registry key could not be opened.", "ERROR");
                return false;
            }

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory;
                if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    exePath = Path.ChangeExtension(exePath, ".exe");
                }

                // パス内のスペースに対応するため二重引用符で囲む
                string commandLine = $"\"{exePath}\"";
                key.SetValue(AppName, commandLine);
                LogManager.LogApp($"Successfully configured startup registry for MochaPSScheduler: {commandLine}");
            }
            else
            {
                key.DeleteValue(AppName, false);
                LogManager.LogApp("Successfully removed startup configuration from registry.");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogManager.LogApp($"Failed to write startup configuration to registry: {ex.Message}", "ERROR");
            return false;
        }
    }
}
