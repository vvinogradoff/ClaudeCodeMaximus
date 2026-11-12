using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>A fixed two-model catalogue.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeModelService : IClaudeModelService
{
	public const string SonnetId = "claude-sonnet-e2e";
	public const string OpusId = "claude-opus-e2e";

	private static readonly ClaudeModelInfo[] Models =
	[
		new(SonnetId, "sonnet", "Sonnet E2E"),
		new(OpusId, "opus", "Opus E2E"),
	];

	public event EventHandler? ModelsUpdated { add { } remove { } }

	public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => Models;

	public Task EnsureModelsLoadedAsync() => Task.CompletedTask;
}
