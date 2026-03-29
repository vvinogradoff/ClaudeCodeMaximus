using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class TessynRunService : ITessynRunService
{
    private readonly ITessynDaemonService _daemon;

    public TessynRunService(ITessynDaemonService daemon)
    {
        _daemon = daemon;
    }

    public IObservable<TessynRunEvent> RunEvents => _daemon.RunEvents;

    public Task<string> SendAsync(
        string projectPath, string prompt, string? externalId, string? model,
        CancellationToken cancellationToken)
    {
        return _daemon.RunSendAsync(prompt, projectPath, externalId, model, cancellationToken);
    }

    public Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        return _daemon.RunCancelAsync(runId, cancellationToken);
    }

    public Task<List<TessynActiveRun>> ListActiveAsync(CancellationToken cancellationToken)
    {
        return _daemon.RunListAsync(cancellationToken);
    }
}
