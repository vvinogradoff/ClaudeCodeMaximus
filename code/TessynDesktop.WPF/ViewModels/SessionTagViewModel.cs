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

	public SessionTagViewModel(SessionTagDto tag, IUrlLauncher launcher)
	{
		_launcher = launcher;
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

	public string KindText { get; }

	[RelayCommand]
	private void Open() => _launcher.TryOpen(Url);
}
