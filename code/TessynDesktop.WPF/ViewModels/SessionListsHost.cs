using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Owns the Recent list and the Tag search for the whole window and ties them to the shell: it attaches them to the client when the
/// connection is ready, opens a picked session through the tree and the pane, and sets favourites. One instance exists, so the Recent tab
/// and the title-bar dropdown show the same list. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListsHost : ISessionListActions
{
	private readonly MainViewModel _main;
	private readonly ILogger<SessionListsHost> _logger;
	private ITessynClient? _client;

	public SessionListsHost(ConnectionViewModel connection, MainViewModel main, IUiDispatcher ui, ILoggerFactory loggers)
	{
		_main = main;
		_logger = loggers.CreateLogger<SessionListsHost>();
		Recent = new RecentViewModel(this, ui, loggers.CreateLogger<RecentViewModel>());
		Tags = new TagSearchViewModel(this, ui, loggers.CreateLogger<TagSearchViewModel>());
		connection.ClientReady += OnClientReady;
	}

	public RecentViewModel Recent { get; }

	public TagSearchViewModel Tags { get; }

	private void OnClientReady(ITessynClient client)
	{
		_client = client;
		_ = Recent.AttachAsync(client);
		_ = Tags.AttachAsync(client);
	}

	/// <inheritdoc />
	public void Open(string nodeId, string name)
	{
		var session = _main.Session;
		if (session == null)
			return;

		// The tree selection opens the session (and expands its parents); a session the tree does not know is opened directly.
		var selected = _main.Tree?.SelectNodeById(nodeId) == true;
		if (session.NodeId != nodeId)
			_ = session.OpenAsync(nodeId, name);

		_logger.LogInformation("Opened {NodeId} from a session list (tree row {InTree})", nodeId, selected);
	}

	/// <inheritdoc />
	public async Task<bool?> SetFavoriteAsync(string nodeId, bool value)
	{
		var client = _client;
		if (client == null)
			return null;

		try
		{
			var result = await client.SessionSetFavoriteAsync(nodeId, value);
			return result.Value;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "session.setFavorite failed for {NodeId}", nodeId);
			return null;
		}
	}
}
