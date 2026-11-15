using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The auth-error banner above the prompt box (W5): shown while the selected session's state carries <c>authError</c>, with Re-authenticate and Remove for the
/// profile the session runs on. Follows the tree selection and the selected row's state; contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class AuthBannerViewModel : ObservableObject, IDisposable
{
	private readonly ProfileViewModel _profiles;
	private TreeViewModel? _tree;
	private TreeNodeViewModel? _node;

	[ObservableProperty]
	private bool _isVisible;

	[ObservableProperty]
	private string _profileName = string.Empty;

	public AuthBannerViewModel(ProfileViewModel profiles)
	{
		_profiles = profiles;
		_profiles.Profiles.CollectionChanged += (_, _) =>
		{
			ReauthenticateCommand.NotifyCanExecuteChanged();
			RemoveCommand.NotifyCanExecuteChanged();
		};
	}

	public string Text => Constants.Profiles.AuthBannerText;

	/// <summary>Follows the selection of this tree from now on.</summary>
	public void AttachTree(TreeViewModel tree)
	{
		if (ReferenceEquals(tree, _tree))
			return;

		if (_tree != null)
			_tree.SelectionChanged -= OnSelectionChanged;

		_tree = tree;
		tree.SelectionChanged += OnSelectionChanged;
		Follow(tree.SelectedNodeId == null ? null : tree.FindNode(tree.SelectedNodeId));
	}

	[RelayCommand(CanExecute = nameof(CanAct))]
	private Task ReauthenticateAsync() => _profiles.AuthenticateAsync(CurrentRow());

	[RelayCommand(CanExecute = nameof(CanAct))]
	private Task RemoveAsync() => _profiles.RemoveAsync(CurrentRow());

	private bool CanAct() => CurrentRow() != null;

	private ProfileRowViewModel? CurrentRow() => _profiles.FindByName(ProfileName);

	private void OnSelectionChanged(object? sender, TreeNodeViewModel node) => Follow(node);

	private void Follow(TreeNodeViewModel? node)
	{
		if (_node != null)
			_node.PropertyChanged -= OnNodeChanged;

		_node = node;
		if (node != null)
			node.PropertyChanged += OnNodeChanged;

		Refresh();
	}

	private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(TreeNodeViewModel.State))
			Refresh();
	}

	private void Refresh()
	{
		var state = _node is { IsSessionLike: true } ? _node.State : null;
		ProfileName = state?.ProfileName ?? string.Empty;
		IsVisible = state?.AuthError == true;
		ReauthenticateCommand.NotifyCanExecuteChanged();
		RemoveCommand.NotifyCanExecuteChanged();
	}

	public void Dispose()
	{
		if (_tree != null)
			_tree.SelectionChanged -= OnSelectionChanged;

		if (_node != null)
			_node.PropertyChanged -= OnNodeChanged;
	}
}
