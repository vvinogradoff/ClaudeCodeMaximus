using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Root view model of the main window: the connection banner, the session tree and the status bar. The tree is created when the
/// client is ready. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class MainViewModel : ObservableObject
{
	private readonly Func<ITessynClient, TreeViewModel> _treeFactory;
	private readonly ILogger<MainViewModel> _logger;

	[ObservableProperty]
	private TreeViewModel? _tree;

	[ObservableProperty]
	private string _statusText = "Not connected";

	public MainViewModel(ConnectionViewModel connection, Func<ITessynClient, TreeViewModel> treeFactory, ILogger<MainViewModel> logger)
	{
		Connection = connection;
		_treeFactory = treeFactory;
		_logger = logger;
		Connection.ClientReady += OnClientReady;
		Connection.PropertyChanged += (_, _) => UpdateStatus();
	}

	public ConnectionViewModel Connection { get; }

	/// <summary>Finds the daemon and connects; returns when connected or on a terminal state.</summary>
	public Task StartAsync(CancellationToken cancellationToken) => Connection.RunAsync(cancellationToken);

	private async void OnClientReady(ITessynClient client)
	{
		try
		{
			var tree = _treeFactory(client);
			tree.PropertyChanged += (_, _) => UpdateStatus();
			Tree = tree;
			await tree.LoadAsync();
			UpdateStatus();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Loading the tree failed");
		}
	}

	private void UpdateStatus()
	{
		if (Tree == null || Tree.Version < 0)
		{
			StatusText = Connection.BannerText;
			return;
		}

		StatusText = $"{Tree.SessionCount} sessions   tree v{Tree.Version}   {Connection.State}";
	}
}
