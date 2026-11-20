using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>A session tag (FR.24) shown as a chip under the header; clicking it opens its URL in the system browser.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionTagViewModel
{
	private readonly IUrlLauncher _launcher;
	private readonly ISystemUrlLauncher _systemLauncher;

	public SessionTagViewModel(SessionTagDto tag, IUrlLauncher launcher, ISystemUrlLauncher systemLauncher)
	{
		_launcher = launcher;
		_systemLauncher = systemLauncher;
		Label = tag.Label;
		Url = tag.Url;
		KindText = tag.Kind switch
		{
			SessionTagKind.LinearIssue => "Linear",
			SessionTagKind.GitHubPullRequest => "PR",
			SessionTagKind.GitHubRepository => "Repo",
			_ => string.Empty,
		};
	}

	public string Label { get; }

	public string Url { get; }

	/// <summary>The chip's tooltip: the address and the FR.26.9 gesture line.</summary>
	public string ToolTipText => Url + "\n" + Constants.SystemBrowser.CtrlClickLine;

	public string KindText { get; }

	[RelayCommand]
	private void Open() => _launcher.TryOpen(Url);

	/// <summary>FR.26.9: Ctrl+click on the chip; the system browser, never routed (the tag url already passed <c>SessionTagUrlPolicy</c> in the daemon).</summary>
	[RelayCommand]
	private void OpenInSystemBrowser() => _systemLauncher.TryOpen(Url);
}
