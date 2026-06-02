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

            if (cancellationToken.IsCancellationRequested)
            {
                result.Success = false;
                result.Message = "Job was canceled before starting.";
                return result;
            }

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

                // キャンセル要求時にプロセスツリーごと強制終了するコールバックを登録
                using var registration = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // 既にプロセスが終了している等の場合は例外を無視
                    }
                });

                // CancellationToken によるプロセス終了待機
                await process.WaitForExitAsync(cancellationToken);
                
                // 非同期イベントハンドラがすべて完了するのを保証するため、引数なしの WaitForExit を呼び出す
                process.WaitForExit();

                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                result.Message = $"Process exited with code {process.ExitCode}";
            }
            catch (OperationCanceledException)
            {
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
                    // バージョン名（例: 7, 7-preview）のディレクトリのみを探索して pwsh.exe を探す（全階層の再帰探索を避けて高速化）
                    var pwshFilesList = new System.Collections.Generic.List<string>();
                    foreach (var verDir in Directory.GetDirectories(pfPowerShell))
                    {
                        string pwshPath = Path.Combine(verDir, "pwsh.exe");
                        if (File.Exists(pwshPath))
                        {
                            pwshFilesList.Add(pwshPath);
                        }
                    }

                    if (pwshFilesList.Count > 0)
                    {
                        var pwshFiles = pwshFilesList.ToArray();
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
