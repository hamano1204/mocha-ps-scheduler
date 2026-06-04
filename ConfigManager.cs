using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace MochaScheduler;

public class ConfigManager : IDisposable
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    private readonly FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private readonly object _lock = new();

    private AppConfig _config = new();

    public AppConfig Config
    {
        get
        {
            lock (_lock)
            {
                return _config.Clone();
            }
        }
    }

    public event EventHandler<AppConfig>? ConfigChanged;

    public ConfigManager()
    {
        LoadOrCreateConfig();

        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir is not null)
            {
                _watcher = new FileSystemWatcher(dir, "config.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                _watcher.Changed += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            LogManager.LogApp($"Failed to start config file watcher: {ex.Message}", "ERROR");
        }
    }

    public string GetConfigPath() => ConfigPath;

    public void LoadOrCreateConfig()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    _config = new AppConfig();
                    // デフォルト設定にサンプルジョブを1つ追加しておく
                    _config.Jobs.Add(new JobConfig
                    {
                        Id = "sample-ps",
                        Name = "サンプルPowerShellジョブ",
                        Type = "PowerShell",
                        ScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.ps1"),
                        Schedule = "*/5 * * * * *", // 5秒ごと (デバッグ用)
                        TimeoutSeconds = 60,
                        RetryCount = 1
                    });

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(_config, options);
                    File.WriteAllText(ConfigPath, json);
                    LogManager.LogApp("Created default config.json");

                    // サンプルスクリプトも自動生成する
                    var sampleScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.ps1");
                    if (!File.Exists(sampleScript))
                    {
                        File.WriteAllText(sampleScript, "Write-Output \"Hello, Mocha Scheduler! Current time is $(Get-Date)\"");
                    }
                    return;
                }

                var content = File.ReadAllText(ConfigPath);
                var parsed = JsonSerializer.Deserialize<AppConfig>(content);
                if (parsed is not null)
                {
                    _config = parsed;
                    LogManager.LogApp("Loaded config.json successfully.");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogApp($"Error loading config.json: {ex.Message}", "ERROR");
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcherは二重に発火しやすいため、150msのデバウンスを行う
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                LogManager.LogApp("config.json change detected. Reloading...");
                LoadOrCreateConfig();
                ConfigChanged?.Invoke(this, Config);
            }, null, 150, Timeout.Infinite);
        }
    }

    public void SaveConfig(AppConfig config)
    {
        AppConfig? configToNotify = null;
        lock (_lock)
        {
            try
            {
                if (_watcher is not null)
                {
                    _watcher.EnableRaisingEvents = false;
                }

                _config = config;
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(ConfigPath, json);

                LogManager.LogApp("Config saved successfully.");
                configToNotify = _config.Clone();
            }
            catch (Exception ex)
            {
                LogManager.LogApp($"Error saving config: {ex.Message}", "ERROR");
            }
            finally
            {
                if (_watcher is not null)
                {
                    _watcher.EnableRaisingEvents = true;
                }
            }
        }

        if (configToNotify is not null)
        {
            ConfigChanged?.Invoke(this, configToNotify);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        GC.SuppressFinalize(this);
    }
}
