using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>A router that writes <see cref="ResolvedMarker"/> into a shared log whenever a session's engine is resolved, then delegates.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class OrderRecordingRouter : ISessionEngineRouter
{
	public const string ResolvedMarker = "engine:resolved";

	private readonly ISessionEngineRouter _inner;
	private readonly List<string> _log;

	public OrderRecordingRouter(ISessionEngineRouter inner, List<string> log)
	{
		_inner = inner;
		_log   = log;
	}

	public IReadOnlyList<ISessionEngine> Engines => _inner.Engines;
	public ISessionEngine Get(string? engineId) => _inner.Get(engineId);

	public ISessionEngine For(SessionNodeModel node)
	{
		lock (_log) _log.Add(ResolvedMarker);
		return _inner.For(node);
	}

	public string? ApplyEngineChange(SessionNodeModel node, ISessionEngine engine) => _inner.ApplyEngineChange(node, engine);
	public bool IsAlive(string nodeId) => _inner.IsAlive(nodeId);
	public void Stop(string nodeId) => _inner.Stop(nodeId);
	public void TerminateAll() => _inner.TerminateAll();
}
