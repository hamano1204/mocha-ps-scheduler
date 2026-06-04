using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NCrontab;

namespace MochaScheduler;

public class SchedulerEngine : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly JobManager _jobManager;
    private readonly object _lock = new();
    private readonly Dictionary<string, CrontabSchedule> _schedules = [];
    private readonly Dictionary<string, DateTime> _nextRunTimes = [];
    private CancellationTokenSource? _cts;
    private Task? _schedulerTask;

    public SchedulerEngine(ConfigManager configManager, JobManager jobManager)
    {
        _configManager = configManager;
        _jobManager = jobManager;
        _configManager.ConfigChanged += OnConfigChanged;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_cts is not null)
            {
                return; // すでに稼働中
            }

            _cts = new CancellationTokenSource();
            RebuildSchedules();
            
            _schedulerTask = Task.Run(() => RunSchedulerLoopAsync(_cts.Token));
            LogManager.LogApp("Scheduler engine started successfully.");
        }
    }

    public void Stop()
    {
        CancellationTokenSource? localCts;
        Task? localTask;

        lock (_lock)
        {
            localCts = _cts;
            localTask = _schedulerTask;

            _cts = null;
            _schedulerTask = null;
        }

        if (localCts is not null)
        {
            localCts.Cancel();
            try
            {
                localTask?.Wait();
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                // キャンセルによる正常終了
            }
            catch (Exception ex)
            {
                LogManager.LogApp($"Error waiting for scheduler thread to exit: {ex.Message}", "ERROR");
            }
            finally
            {
                localCts.Dispose();
            }
            LogManager.LogApp("Scheduler engine stopped.");
        }
    }

    private void RebuildSchedules()
    {
        lock (_lock)
        {
            _schedules.Clear();
            _nextRunTimes.Clear();

            var now = DateTime.Now;
            foreach (var job in _configManager.Config.Jobs)
            {
                if (string.IsNullOrEmpty(job.Id) || string.IsNullOrEmpty(job.Schedule) || !job.Enabled)
                {
                    continue;
                }

                try
                {
                    // NCrontab で秒を含む6フィールドでのパースを試みる
                    var options = new CrontabSchedule.ParseOptions { IncludingSeconds = true };
                    var schedule = CrontabSchedule.Parse(job.Schedule, options);
                    _schedules[job.Id] = schedule;
                    
                    var next = schedule.GetNextOccurrence(now);
                    _nextRunTimes[job.Id] = next;
                    LogManager.LogApp($"Scheduled job '{job.Id}' - Next execution: {next:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    LogManager.LogApp($"Failed to parse schedule '{job.Schedule}' for job '{job.Id}': {ex.Message}", "ERROR");
                }
            }
        }
    }

    private async Task RunSchedulerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var jobsToRun = new List<JobConfig>();

                lock (_lock)
                {
                    foreach (var job in _configManager.Config.Jobs)
                    {
                        if (string.IsNullOrEmpty(job.Id))
                        {
                            continue;
                        }

                        if (_nextRunTimes.TryGetValue(job.Id, out var nextRun) && now >= nextRun)
                        {
                            jobsToRun.Add(job);
                            
                            // 次の実行予定時刻を計算して更新 (現在時刻nowではなく予定時刻nextRunを基準に算出することで時間の累積ドリフトを防ぐ)
                            if (_schedules.TryGetValue(job.Id, out var schedule))
                            {
                                _nextRunTimes[job.Id] = schedule.GetNextOccurrence(nextRun);
                                LogManager.LogApp($"Job '{job.Id}' triggered. Next execution: {_nextRunTimes[job.Id]:yyyy-MM-dd HH:mm:ss}");
                            }
                        }
                    }
                }

                // トリガーは非同期で実行 (呼び出し側の待機ループをブロックしない)
                foreach (var job in jobsToRun)
                {
                    _ = _jobManager.TriggerJobAsync(job);
                }

                // 次の秒の開始境界まで待機 (時間の累積ドリフトを防ぐ)
                var delay = 1000 - DateTime.Now.Millisecond;
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogManager.LogApp($"Error in scheduler execution loop: {ex.Message}", "ERROR");
                await Task.Delay(5000, cancellationToken); // エラー時は5秒ウェイトを入れる
            }
        }
    }

    private void OnConfigChanged(object? sender, AppConfig newConfig)
    {
        LogManager.LogApp("Config change detected. Rebuilding scheduler execution queues...");
        RebuildSchedules();
    }

    public void Dispose()
    {
        _configManager.ConfigChanged -= OnConfigChanged;
        Stop();
        GC.SuppressFinalize(this);
    }
}
