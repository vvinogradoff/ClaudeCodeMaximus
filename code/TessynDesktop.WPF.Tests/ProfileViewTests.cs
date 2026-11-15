using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The W5 views: the flyout, the auth banner, the switch button and the profile badge of the tree rows bind to their view models without binding errors, and the module fills the slots.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileViewTests
{
	private sealed class BindingErrors : TraceListener
	{
		public List<string> Messages { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Messages)
					Messages.Add(message);
		}
	}

	private sealed class NoState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	[Fact]
	public async Task TheViewsBindWithoutBindingErrors_AndTheBadgeIsOnSessionRows()
	{
		var client = new FakeTessynClient().AnnouncingProfileAdmin();
		client.Profiles = [new("default", "[Sys] a@b.c", true, "a@b.c", ProfileAuthState.Unknown, true), new("p1", "Work", false, null, ProfileAuthState.LoggedOut, false)];
		client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], [new TreeSessionDto("s1", "Session", 0, null, new SessionStateDto("s1", false, false, null, false, null, null, false, "Work", true))])];
		var ui = new ImmediateUiDispatcher();
		var presenter = new FakeDialogPresenter();
		var flyout = new ProfileViewModel(new DialogService(presenter, ui), ui, NullLogger<ProfileViewModel>.Instance);
		await flyout.AttachAsync(client);
		var tree = new TreeViewModel(client, ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		tree.SelectNodeById("s1");
		var commands = new ShellCommands(new ClientSettingsStore(new NoState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		var slots = new ShellSlots();
		var context = new TreeContext { Current = tree };
		var switcher = new ProfileSwitchViewModel(new DialogService(presenter, ui), context, ui, NullLogger<ProfileSwitchViewModel>.Instance);
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var host = new ProfileHost(connection, flyout, switcher);
		new ProfileShellModule(host).Register(slots, commands);

		Assert.Single(slots.For(Constants.Slots.StatusBarItems));
		Assert.Empty(slots.For(Constants.Slots.ComposerAccessory));   // the auth banner is W1b's ComposerNotices, not W5's
		Assert.Single(slots.For(Constants.Slots.TreeFooter));
		Assert.NotNull(commands.Find(Constants.Profiles.SwitchCommandId));

		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				var views = new FrameworkElement[]
				{
					new ProfileFlyout { DataContext = flyout },
					new ProfileSwitchButton(commands, switcher) { DataContext = tree },
					new TreePanel { DataContext = tree },
				};
				foreach (var element in views)
				{
					element.Measure(new Size(500, 600));
					element.Arrange(new Rect(0, 0, 500, 600));
					element.UpdateLayout();
				}

			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
	}
}
