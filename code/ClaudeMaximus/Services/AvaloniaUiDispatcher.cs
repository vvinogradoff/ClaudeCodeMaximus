using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ClaudeMaximus.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
}
