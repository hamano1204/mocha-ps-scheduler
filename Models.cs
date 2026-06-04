using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MochaScheduler;

public class AppConfig
{
    [JsonPropertyName("startup")]
    public bool Startup { get; set; } = true;

    [JsonPropertyName("notification")]
    public NotificationConfig Notification { get; set; } = new();

    [JsonPropertyName("jobs")]
    public List<JobConfig> Jobs { get; set; } = [];

    public AppConfig Clone()
    {
        var clone = new AppConfig
        {
            Startup = this.Startup,
            Notification = this.Notification?.Clone() ?? new NotificationConfig()
        };
        if (this.Jobs is not null)
        {
            foreach (var job in this.Jobs)
            {
                clone.Jobs.Add(job.Clone());
            }
        }
        return clone;
    }
}

public class NotificationConfig
{
    [JsonPropertyName("onSuccess")]
    public bool OnSuccess { get; set; } = false;

    [JsonPropertyName("onFailure")]
    public bool OnFailure { get; set; } = true;

    public NotificationConfig Clone()
    {
        return new NotificationConfig
        {
            OnSuccess = this.OnSuccess,
            OnFailure = this.OnFailure
        };
    }
}

public class JobConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "PowerShell"; // PowerShell, Batch, Python

    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("schedule")]
    public string Schedule { get; set; } = string.Empty;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 300;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("retryDelaySeconds")]
    public int RetryDelaySeconds { get; set; } = 10;

    [JsonPropertyName("executablePath")]
    public string ExecutablePath { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    public JobConfig Clone()
    {
        return new JobConfig
        {
            Id = this.Id,
            Name = this.Name,
            Type = this.Type,
            ScriptPath = this.ScriptPath,
            Arguments = this.Arguments,
            Schedule = this.Schedule,
            TimeoutSeconds = this.TimeoutSeconds,
            RetryCount = this.RetryCount,
            RetryDelaySeconds = this.RetryDelaySeconds,
            ExecutablePath = this.ExecutablePath,
            Enabled = this.Enabled
        };
    }
}

public class JobResult
{
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
