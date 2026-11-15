using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>Starts the <see cref="CompletionSoundService"/> for the client once the connection delivers it (the client itself registers the sound channel in its hello).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CompletionSoundHost : IHostedService, IDisposable
{
	private readonly ConnectionViewModel _connection;
	private readonly ISoundPlayer _player;
	private readonly ILoggerFactory _loggers;
	private CompletionSoundService? _service;

	public CompletionSoundHost(ConnectionViewModel connection, ISoundPlayer player, ILoggerFactory loggers)
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
		_service = new CompletionSoundService(client, _player, TimeProvider.System, _loggers.CreateLogger<CompletionSoundService>());
		_service.Attach();
	}

	public void Dispose()
	{
		_connection.ClientReady -= OnClientReady;
		_service?.Dispose();
		_service = null;
	}
}
