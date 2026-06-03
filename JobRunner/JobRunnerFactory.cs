using System;

namespace MochaScheduler.JobRunner;

public static class JobRunnerFactory
{
    public static IJobRunner CreateRunner(string jobType)
    {
        return jobType.ToLowerInvariant() switch
        {
            "powershell" => new PowerShellRunner(),
            // 将来的に "batch" や "python" を追加可能
            _ => throw new NotSupportedException($"Job type '{jobType}' is not supported.")
        };
    }
}
