using System.Text.Json;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.State;

/// <summary>
/// <see cref="IClientStateStore"/> over a JSON file. Changes are coalesced: the file is written once per debounce window on a
/// background thread, through a temp file and an atomic replace, so a crash never leaves a half-written state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonClientStateStore : IClientStateStore
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	private readonly AppPaths _paths;
	private readonly ILogger<JsonClientStateStore> _logger;
	private readonly TimeSpan _debounce;
	private readonly object _gate = new();

	private CancellationTokenSource? _pendingWrite;
	private Task _writeTask = Task.CompletedTask;
	private bool _dirty;

	public JsonClientStateStore(AppPaths paths, ILogger<JsonClientStateStore> logger)
		: this(paths, logger, Constants.Timeouts.StateSaveDebounce)
	{
	}

	public JsonClientStateStore(AppPaths paths, ILogger<JsonClientStateStore> logger, TimeSpan debounce)
	{
		_paths = paths;
		_logger = logger;
		_debounce = debounce;
	}

	public ClientStateModel State { get; private set; } = new();

	public async Task LoadAsync()
	{
		var path = _paths.ClientStateFilePath;
		try
		{
			if (!File.Exists(path))
				return;

			await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			var loaded = await JsonSerializer.DeserializeAsync<ClientStateModel>(stream, Json).ConfigureAwait(false);
			if (loaded != null)
			{
				loaded.TreeWidth = ClampTreeWidth(loaded.TreeWidth);
				loaded.Sections ??= new();
				State = loaded;
			}
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "Client state at {Path} is unreadable; keeping it as .bad and starting with defaults", path);
			State = new ClientStateModel();
			KeepCorruptFile(path);
		}
	}

	/// <summary>A tree width from a hand-edited or damaged file is brought into a usable range.</summary>
	public static double ClampTreeWidth(double width) =>
		double.IsNaN(width) || double.IsInfinity(width) ? Constants.Limits.DefaultTreeWidth : Math.Clamp(width, Constants.Limits.MinTreeWidth, Constants.Limits.MaxTreeWidth);

	private static void KeepCorruptFile(string path)
	{
		try
		{
			File.Move(path, path + Constants.Paths.BadFileSuffix, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Nothing more to do: the next save replaces the file.
		}
	}

	public void Update(Action<ClientStateModel> change)
	{
		lock (_gate)
		{
			change(State);
			_dirty = true;
			_pendingWrite?.Cancel();
			_pendingWrite?.Dispose();
			var cts = new CancellationTokenSource();
			_pendingWrite = cts;
			_writeTask = WriteAfterDelayAsync(cts.Token);
		}
	}

	public async Task FlushAsync()
	{
		Task pending;
		lock (_gate)
		{
			_pendingWrite?.Cancel();
			pending = _writeTask;
		}

		await pending.ConfigureAwait(false);
		await WriteNowAsync().ConfigureAwait(false);
	}

	private async Task WriteAfterDelayAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		await WriteNowAsync().ConfigureAwait(false);
	}

	private async Task WriteNowAsync()
	{
		string json;
		lock (_gate)
		{
			if (!_dirty)
				return;

			_dirty = false;
			json = JsonSerializer.Serialize(State, Json);
		}

		var path = _paths.ClientStateFilePath;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			var temp = path + ".tmp";
			await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
			File.Move(temp, path, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "Writing the client state failed");
			lock (_gate)
				_dirty = true;
		}
	}
}
