using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MochaScheduler.JobRunner;

namespace MochaScheduler
{
    public class JobManager
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningJobs = new();
        private readonly NotificationManager _notificationManager;

        public event EventHandler? JobStateChanged;

        public JobManager(NotificationManager notificationManager)
        {
            _notificationManager = notificationManager;
        }

        public bool IsJobRunning(string jobId) => _runningJobs.ContainsKey(jobId);

        public int RunningJobsCount => _runningJobs.Count;

        public void CancelJob(string jobId)
        {
            if (_runningJobs.TryGetValue(jobId, out var cts))
            {
                LogManager.LogApp($"Requesting cancel for job '{jobId}'...");
                cts.Cancel();
            }
        }

        public void CancelAllJobs()
        {
            LogManager.LogApp("Canceling all running jobs...");
            foreach (var key in _runningJobs.Keys)
            {
                CancelJob(key);
            }
        }

        public async Task TriggerJobAsync(JobConfig job, bool isManual = false)
        {
            if (string.IsNullOrEmpty(job.Id))
            {
                return;
            }

            // 多重起動防止
            if (_runningJobs.ContainsKey(job.Id))
            {
                LogManager.LogApp($"Job '{job.Id}' ({job.Name}) is already running. Skipped.", "WARNING");
                LogManager.LogJob(job.Id, $"Job execution skipped because it is already running.");
                return;
            }

            var cts = new CancellationTokenSource();
            if (!_runningJobs.TryAdd(job.Id, cts))
            {
                cts.Dispose();
                return;
            }

            // 状態変更通知 (実行開始)
            JobStateChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                LogManager.LogApp($"Starting job '{job.Id}' ({job.Name}) [Manual={isManual}]");
                LogManager.LogJob(job.Id, $"=== Job Started ({job.Name}) ===");

                await ExecuteJobWithRetryAsync(job, cts, isManual);
            }
            finally
            {
                _runningJobs.TryRemove(job.Id, out _);
                cts.Dispose();
                // 状態変更通知 (実行終了)
                JobStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task ExecuteJobWithRetryAsync(JobConfig job, CancellationTokenSource cts, bool isManual)
        {
            int maxAttempts = job.RetryCount + 1;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                
                // タイムアウト監視の設定
                if (job.TimeoutSeconds > 0)
                {
                    attemptCts.CancelAfter(TimeSpan.FromSeconds(job.TimeoutSeconds));
                }

                if (attempt > 1)
                {
                    LogManager.LogJob(job.Id, $"--- Retry Attempt {attempt} of {maxAttempts} ---");
                }

                var runner = new PowerShellRunner();
                var startTime = DateTime.Now;

                // ジョブログへの非同期リアルタイム出力コールバックを指定して実行
                var result = await runner.RunAsync(
                    job,
                    stdout => LogManager.LogJob(job.Id, stdout),
                    stderr => LogManager.LogJob(job.Id, stderr, isError: true),
                    attemptCts.Token
                );

                var duration = DateTime.Now - startTime;

                if (result.Success)
                {
                    LogManager.LogApp($"Job '{job.Id}' completed successfully on attempt {attempt}. Duration: {duration:g}");
                    LogManager.LogJob(job.Id, $"=== Job Succeeded (Duration: {duration:g}) ===");
                    
                    _notificationManager.ShowJobSuccess(job.Name, isManual);
                    return;
                }

                string errorDetails = result.Exception != null ? result.Exception.Message : result.Message;
                LogManager.LogApp($"Job '{job.Id}' failed on attempt {attempt}: {errorDetails}", "ERROR");
                LogManager.LogJob(job.Id, $"Attempt {attempt} failed: {errorDetails}", isError: true);

                if (cts.Token.IsCancellationRequested || attempt >= maxAttempts)
                {
                    LogManager.LogApp($"Job '{job.Id}' execution failed definitively.");
                    LogManager.LogJob(job.Id, $"=== Job Failed Definitively ===", isError: true);
                    
                    _notificationManager.ShowJobFailure(job.Name, errorDetails, isManual);
                    return;
                }

                LogManager.LogApp($"Waiting {job.RetryDelaySeconds} seconds before retry...");
                LogManager.LogJob(job.Id, $"Waiting {job.RetryDelaySeconds} seconds before next attempt...", isError: true);
                
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(job.RetryDelaySeconds), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    LogManager.LogApp($"Retry delay for job '{job.Id}' was canceled.");
                    LogManager.LogJob(job.Id, $"Retry delay canceled.", isError: true);
                    return;
                }
            }
        }
    }
}
