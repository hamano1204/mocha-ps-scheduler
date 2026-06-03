using System;
using System.Threading;
using System.Threading.Tasks;

namespace MochaScheduler.JobRunner;

public interface IJobRunner
{
    Task<JobResult> RunAsync(
        JobConfig config, 
        Action<string> onOutputReceived, 
        Action<string> onErrorReceived, 
        CancellationToken cancellationToken);
}
