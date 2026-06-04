using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MochaScheduler.JobRunner;

public class PowerShellRunner : IJobRunner
{
    public async Task<JobResult> RunAsync(
        JobConfig config, 
        Action<string> onOutputReceived, 
        Action<string> onErrorReceived, 
        CancellationToken cancellationToken)
    {
        var result = new JobResult();
        
        string shell = !string.IsNullOrEmpty(config.ExecutablePath) ? config.ExecutablePath : "powershell.exe";
        string scriptPath = config.ScriptPath;
        string args = $"-NoProfile -NonInteractive -InputFormat None -ExecutionPolicy Bypass -File \"{scriptPath}\" {config.Arguments}".Trim();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.EnableRaisingEvents = false;

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

            // 標準入力を即座に閉じて、入力待ちでのハングを確実に防ぐ
            process.StandardInput.Close();

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

            // 標準出力/標準エラーの非同期読み取りタスクを開始
            var outputTask = ReadStreamAsync(process.StandardOutput, onOutputReceived, "stdout", cancellationToken);
            var errorTask = ReadStreamAsync(process.StandardError, onErrorReceived, "stderr", cancellationToken);

            // プロセスの終了をポーリングで待機
            await WaitForProcessExitAsync(process, cancellationToken).ConfigureAwait(false);

            // 標準出力・標準エラーのストリームを明示的に Dispose して、非同期I/Oブロックを強制解除
            try { process.StandardOutput.Dispose(); } catch { }
            try { process.StandardError.Dispose(); } catch { }

            if (cancellationToken.IsCancellationRequested)
            {
                await WaitForCancellationCleanupAsync(process).ConfigureAwait(false);
                result.Success = false;
                result.Message = "Job was canceled.";
            }
            else if (process.HasExited)
            {
                await WaitForOutputTasksAsync(outputTask, errorTask, config.Id).ConfigureAwait(false);

                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                result.Message = $"Process exited with code {process.ExitCode}";
            }
            else
            {
                result.Success = false;
                result.Message = "Process did not exit within expected time.";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Exception = ex;
            result.Message = $"Error starting process: {ex.Message}";
        }

        return result;
    }

    private static Task ReadStreamAsync(
        StreamReader reader, 
        Action<string> onLineReceived, 
        string streamName, 
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null) break;
                    onLineReceived(line);
                }
            }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogManager.LogApp($"Error reading {streamName}: {ex.Message}", "ERROR");
            }
        });
    }

    private static async Task WaitForProcessExitAsync(Process process, CancellationToken cancellationToken)
    {
        while (true)
        {
            try { process.Refresh(); } catch { }
            if (process.HasExited) break;

            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private const int CancellationWaitIntervalMs = 50;
    private const int CancellationMaxWaitMs = 2000;

    private static async Task WaitForCancellationCleanupAsync(Process process)
    {
        int waitTries = 0;
        int maxTries = CancellationMaxWaitMs / CancellationWaitIntervalMs;
        while (waitTries < maxTries)
        {
            try { process.Refresh(); } catch { }
            if (process.HasExited) break;
            await Task.Delay(CancellationWaitIntervalMs).ConfigureAwait(false);
            waitTries++;
        }
    }

    private static async Task WaitForOutputTasksAsync(Task outputTask, Task errorTask, string jobId)
    {
        try
        {
            var delayTask = Task.Delay(200);
            var completedTask = await Task.WhenAny(Task.WhenAll(outputTask, errorTask), delayTask).ConfigureAwait(false);
            if (completedTask == delayTask)
            {
                LogManager.LogApp($"Reading output for job '{jobId}' timed out after process exit (possible .NET stream hang).", "WARNING");
            }
        }
        catch
        {
            // タスク完了時の例外は無視
        }
    }
}
