using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The model refresh behind <c>models.refresh</c> (package J3 part 2): unlike the first load it asks Ollama again every time.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeModelServiceRefreshTests : IDisposable
{
	private sealed class ScriptedOllama : IOllamaModelService
	{
		public List<string> Asked { get; } = [];
		public Func<string, CancellationToken, Task<IReadOnlyList<ClaudeModelInfo>>> Answer { get; set; } = (_, _) => Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([]);

		public Task<IReadOnlyList<ClaudeModelInfo>> GetModelsAsync(string baseUrl, CancellationToken cancellationToken = default)
		{
			lock (Asked) Asked.Add(baseUrl);
			return Answer(baseUrl, cancellationToken);
		}
	}

	private static ClaudeModelInfo Local(string id) => new(id, string.Empty, id, ModelProvider.Ollama, SupportsTools: true);

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "model-refresh-" + Guid.NewGuid().ToString("N")[..8]);
	private readonly AppSettingsService _settings;
	private readonly ScriptedOllama _ollama = new();
	private readonly QueueingUiDispatcher _ui = new();
	private readonly ClaudeModelService _service;

	public ClaudeModelServiceRefreshTests()
	{
		Directory.CreateDirectory(_dir);
		_settings = new AppSettingsService(_dir);
		_service = new ClaudeModelService(_ollama, _settings, _ui);
	}

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	[Fact]
	public async Task EnsureModelsLoaded_AsksOllamaOncePerLaunch_ButRefreshAsksEveryTime()
	{
		await _service.EnsureModelsLoadedAsync();
		await _service.EnsureModelsLoadedAsync();
		Assert.Single(_ollama.Asked);

		await _service.RefreshAsync();
		await _service.RefreshAsync();

		Assert.Equal(3, _ollama.Asked.Count);
	}

	[Fact]
	public async Task Refresh_PicksUpAModelPulledSinceTheLaunch_AndDropsOneThatIsGone()
	{
		var curated = _service.GetCachedModels().Count;
		_ollama.Answer = (_, _) => Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([Local("gemma4:26b")]);
		await _service.EnsureModelsLoadedAsync();

		_ollama.Answer = (_, _) => Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([Local("qwen3:30b"), Local("llama4:17b")]);
		await _service.RefreshAsync();

		Assert.Equal(curated + 2, _service.GetCachedModels().Count);
		Assert.DoesNotContain(_service.GetCachedModels(), m => m.Id == "gemma4:26b");
		Assert.Contains(_service.GetCachedModels(), m => m.Id == "qwen3:30b");
	}

	[Fact]
	public async Task Refresh_UsesTheOllamaUrlOfTheMoment_AndRaisesModelsUpdated()
	{
		var raised = 0;
		_service.ModelsUpdated += (_, _) => raised++;
		_settings.Settings.OllamaBaseUrl = "http://127.0.0.1:11555";

		await _service.RefreshAsync();
		_ui.RunPending();

		Assert.Equal(["http://127.0.0.1:11555"], _ollama.Asked);
		Assert.Equal(1, raised);
	}

	[Fact]
	public async Task ACancelledRefresh_KeepsTheListItHad()
	{
		_ollama.Answer = (_, _) => Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([Local("gemma4:26b")]);
		await _service.RefreshAsync();
		var before = _service.GetCachedModels();
		using var cts = new CancellationTokenSource();
		_ollama.Answer = (_, ct) =>
		{
			cts.Cancel();                                                 // the real lookup swallows a cancel and answers "no models"
			return Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([]);
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RefreshAsync(cts.Token));

		Assert.Same(before, _service.GetCachedModels());
	}

	[Fact]
	public async Task L5_ACancelledRefresh_NeverBecomesTheOncePerLaunchTask()
	{
		using var cts = new CancellationTokenSource();
		_ollama.Answer = (_, _) =>
		{
			cts.Cancel();
			return Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([]);
		};
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RefreshAsync(cts.Token));
		_ollama.Answer = (_, _) => Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([Local("gemma4:26b")]);

		await _service.EnsureModelsLoadedAsync();                         // completes normally, and really asks

		Assert.Equal(2, _ollama.Asked.Count);
		Assert.Contains(_service.GetCachedModels(), m => m.Id == "gemma4:26b");
	}

	[Fact]
	public async Task L5_AFaultedRefresh_NeverBecomesTheOncePerLaunchTask()
	{
		_ollama.Answer = (_, _) => throw new InvalidOperationException("Ollama blew up");
		await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RefreshAsync());
		_ollama.Answer = (_, _) => Task.FromResult<IReadOnlyList<ClaudeModelInfo>>([]);

		await _service.EnsureModelsLoadedAsync();

		Assert.Equal(2, _ollama.Asked.Count);
	}

	[Fact]
	public async Task L5_ASuccessfulRefresh_SatisfiesTheOncePerLaunchLoad()
	{
		await _service.RefreshAsync();

		await _service.EnsureModelsLoadedAsync();

		Assert.Single(_ollama.Asked);
	}
}
