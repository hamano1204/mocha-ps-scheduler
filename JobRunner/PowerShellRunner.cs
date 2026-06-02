using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MochaScheduler.JobRunner
{
    public class PowerShellRunner
    {
        public async Task<JobResult> RunAsync(
            JobConfig config, 
            Action<string> onOutputReceived, 
            Action<string> onErrorReceived, 
            CancellationToken cancellationToken)
        {
            var result = new JobResult();
            
            // config でパスが明示されていればそれを使用。なければ pwsh.exe を動的探索して使用
            string shell = "powershell.exe";
            if (!string.IsNullOrEmpty(config.ExecutablePath))
            {
                shell = config.ExecutablePath;
            }
            else
            {
                shell = GetLatestPwshPath();
            }

            string scriptPath = config.ScriptPath;
            // 引数の組み立て
            string args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" {config.Arguments}".Trim();

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    onOutputReceived(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    onErrorReceived(e.Data);
                }
            };

            try
            {
                if (!process.Start())
                {
                    result.Success = false;
                    result.Message = "Failed to start process.";
                    return result;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // CancellationToken によるプロセス終了待機
                await process.WaitForExitAsync(cancellationToken);

                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                result.Message = $"Process exited with code {process.ExitCode}";
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        // プロセスツリーごと終了
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // すでに終了している場合などの例外を無視
                }
                
                result.Success = false;
                result.Message = "Job was canceled.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Exception = ex;
                result.Message = $"Error starting process: {ex.Message}";
            }

            return result;
        }

        private static string GetLatestPwshPath()
        {
            try
            {
                string pfPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell");
                if (Directory.Exists(pfPowerShell))
                {
                    var pwshFiles = Directory.GetFiles(pfPowerShell, "pwsh.exe", SearchOption.AllDirectories);
                    if (pwshFiles.Length > 0)
                    {
                        // Sort descending to prefer newer versions, and prioritize non-preview releases
                        Array.Sort(pwshFiles, (a, b) =>
                        {
                            bool aPreview = a.Contains("preview", StringComparison.OrdinalIgnoreCase);
                            bool bPreview = b.Contains("preview", StringComparison.OrdinalIgnoreCase);
                            if (aPreview != bPreview)
                            {
                                return aPreview.CompareTo(bPreview); // false (non-preview) comes first
                            }
                            return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
                        });
                        return pwshFiles[0];
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogApp($"Failed dynamically locating pwsh.exe: {ex.Message}", "WARNING");
            }
            return "powershell.exe"; // Fallback to System PowerShell
        }
    }
}
