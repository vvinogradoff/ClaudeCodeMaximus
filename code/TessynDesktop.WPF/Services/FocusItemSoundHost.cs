using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>Starts the <see cref="FocusItemSoundService"/> for the client once the connection delivers it (the client itself registers the sound channel in its hello).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusItemSoundHost : IHostedService, IDisposable
{
	private readonly ConnectionViewModel _connection;
	private readonly ISoundPlayer _player;
	private readonly ILoggerFactory _loggers;
	private FocusItemSoundService? _service;

	public FocusItemSoundHost(ConnectionViewModel connection, ISoundPlayer player, ILoggerFactory loggers)
	{
		_connection = connection;
		_player = player;
		_loggers = loggers;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_connection.ClientReady += OnClientReady;
		if (_connection.Client is { } client)
			OnClientReady(client);

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	private void OnClientReady(ITessynClient client)
	{
		_service?.Dispose();
		_service = new FocusItemSoundService(client, _player, TimeProvider.System, _loggers.CreateLogger<FocusItemSoundService>());
		_service.Attach();
	}

	public void Dispose()
	{
		_connection.ClientReady -= OnClientReady;
		_service?.Dispose();
		_service = null;
	}
}
