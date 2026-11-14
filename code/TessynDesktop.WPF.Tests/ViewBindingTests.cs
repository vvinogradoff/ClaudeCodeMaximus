using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The real views bound to the real view models on an STA thread: a command bound with a parameter of the wrong type throws when the
/// DataContext is set (the live-smoke failure of FindStepCommand), so these tests catch that class of bug without a daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ViewBindingTests
{
	private sealed class BindingErrorListener : TraceListener
	{
		public List<string> Errors { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Errors)
					Errors.Add(message);
		}
	}

	private static void RunSta(Action action)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null)
			ExceptionDispatchInfo.Capture(failure).Throw();
	}

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(900, 700));
		element.Arrange(new Rect(0, 0, 900, 700));
		element.UpdateLayout();
	}

	private static SessionPaneViewModel NewPane(FakeTessynClient client) =>
		new(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);

	[Fact]
	public void SessionPane_BindsToItsViewModelWithoutExceptionsOrBindingErrors()
	{
		var listener = new BindingErrorListener();
		RunSta(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(3);
			var viewModel = NewPane(client);
			viewModel.OpenAsync("a", "A").GetAwaiter().GetResult();

			var pane = new SessionPane { DataContext = viewModel };
			Layout(pane);
			viewModel.ShowFindCommand.Execute(null);
			Layout(pane);

			Assert.True(viewModel.FindNextCommand.CanExecute(null));
			Assert.True(viewModel.FindPreviousCommand.CanExecute(null));
		});

		Assert.Empty(listener.Errors);
	}

	[Fact]
	public void SessionPane_BindsWithoutAViewModelYet()
	{
		RunSta(() => Layout(new SessionPane()));
	}

	[Fact]
	public void TreePanel_BindsToALoadedTreeWithoutExceptions()
	{
		RunSta(() =>
		{
			var client = new FakeTessynClient();
			var builder = new TreeBuilder();
			client.Directories = [builder.Directory("d1", "Repo", [], builder.Session("s1", "One", workers: 1))];
			var tree = new TreeViewModel(client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
			tree.LoadAsync().GetAwaiter().GetResult();

			var panel = new TreePanel { DataContext = tree };
			Layout(panel);
			tree.ToggleExpandCommand.Execute(tree.Rows[0]);
			Layout(panel);
		});
	}

	[Fact]
	public void Xaml_NeverPassesALiteralCommandOrConverterParameterToATypedTarget()
	{
		// A literal string parameter reaches a typed command (RelayCommand<int>) as a string and throws. Only "{Binding ...}" parameters are allowed.
		var root = FindProjectDirectory();
		var offenders = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
			.Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			.SelectMany(f => Regex.Matches(File.ReadAllText(f), "(CommandParameter|ConverterParameter)\\s*=\\s*\"(?!\\{)[^\"]*\"").Select(m => Path.GetFileName(f) + ": " + m.Value))
			.ToList();

		Assert.Empty(offenders);
	}

	private static string FindProjectDirectory()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var candidate = Path.Combine(dir.FullName, "TessynDesktop.WPF");
			if (File.Exists(Path.Combine(candidate, "TessynDesktop.WPF.csproj")))
				return candidate;
		}

		throw new DirectoryNotFoundException("TessynDesktop.WPF project folder not found above " + AppContext.BaseDirectory);
	}
}
