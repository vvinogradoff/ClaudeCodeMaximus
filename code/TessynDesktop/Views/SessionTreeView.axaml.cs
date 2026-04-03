using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TessynDesktop.Services;
using TessynDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Serilog;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class SessionTreeView : UserControl
{
	private readonly DispatcherTimer _recencyTimer;
	private Point _dragStartPoint;
	private bool _isDragPending;
	private bool _suppressSelectionSync;
	private IDisposable? _selectedSessionSub;

	// ── Inline rename state ─────────────────────────────────────────────────
	private TextBox? _renameTextBox;
	private ViewModelBase? _renamingNode;

	public SessionTreeView()
	{
		InitializeComponent();

		// Refresh recency bars every 60 seconds so colors update as time passes
		_recencyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
		_recencyTimer.Tick += (_, _) => RefreshAllRecencyBrushes();
		_recencyTimer.Start();

		AddHandler(DragDrop.DragOverEvent, OnDragOver);
		AddHandler(DragDrop.DropEvent, OnDrop);

		// Watch for DataContext changes to subscribe to SelectedSession
		DataContextChanged += OnDataContextChanged;
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_selectedSessionSub?.Dispose();
		_selectedSessionSub = null;

		if (DataContext is SessionTreeViewModel vm)
		{
			_selectedSessionSub = vm.WhenAnyValue(x => x.SelectedSession)
				.Subscribe(SyncTreeSelection);
		}
	}

	/// <summary>
	/// When SelectedSession is set programmatically (e.g. restore on startup),
	/// sync the TreeView's visual selection to match.
	/// </summary>
	private void SyncTreeSelection(SessionNodeViewModel? session)
	{
		if (_suppressSelectionSync || session == null)
			return;

		if (Tree.SelectedItem == session)
			return;

		// Expand parent nodes so the TreeView can select the item
		if (DataContext is SessionTreeViewModel vm)
			ExpandParentsOf(session, vm);

		Tree.SelectedItem = session;
	}

	private static void ExpandParentsOf(SessionNodeViewModel target, SessionTreeViewModel tree)
	{
		foreach (var dir in tree.Directories)
		{
			if (ExpandIfContains(dir.Children, target))
			{
				dir.IsExpanded = true;
				return;
			}
		}
	}

	private static bool ExpandIfContains(ObservableCollection<ViewModelBase> children, SessionNodeViewModel target)
	{
		foreach (var child in children)
		{
			if (child == target)
				return true;
			if (child is GroupNodeViewModel group && ExpandIfContains(group.Children, target))
			{
				group.IsExpanded = true;
				return true;
			}
		}
		return false;
	}

	private void RefreshAllRecencyBrushes()
	{
		if (DataContext is not SessionTreeViewModel vm)
			return;

		foreach (var dir in vm.Directories)
			RefreshRecencyInChildren(dir.Children);
	}

	private static void RefreshRecencyInChildren(ObservableCollection<ViewModelBase> children)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session)
				session.RefreshRecencyBrush();
			else if (child is GroupNodeViewModel group)
				RefreshRecencyInChildren(group.Children);
		}
	}

	// ── Refresh git origins ─────────────────────────────────────────────────

	private void OnRefreshGitOriginsClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is SessionTreeViewModel vm)
			vm.RefreshGitOrigins();
	}

	// ── Add Directory ────────────────────────────────────────────────────────

	private async void OnAddDirectoryClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SessionTreeViewModel vm)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
			return;

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
			new Avalonia.Platform.Storage.FolderPickerOpenOptions
			{
				Title       = "Select Working Directory",
				AllowMultiple = false,
			});

		if (folders.Count == 0)
			return;

		vm.AddDirectory(folders[0].Path.LocalPath);
	}

	// ── Tree selection ───────────────────────────────────────────────────────

	private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (DataContext is not SessionTreeViewModel vm)
			return;

		var selected = Tree.SelectedItem;

		// During move mode, update the moving session's position as selection changes
		if (vm.IsMoveModeActive && selected is ViewModelBase selectedVm)
		{
			vm.UpdateMovePosition(selectedVm);
			return;
		}

		_suppressSelectionSync = true;
		vm.SelectedSession = selected is SessionNodeViewModel session ? session : null;
		vm.SelectedTreeItem = selected as ViewModelBase;
		_suppressSelectionSync = false;
	}

	// ── Keyboard shortcuts (F2, F6, Enter, Escape) ──────────────────────────

	private void OnTreeKeyDown(object? sender, KeyEventArgs e)
	{
		if (DataContext is not SessionTreeViewModel vm)
			return;

		switch (e.Key)
		{
			case Key.F2:
				e.Handled = true;
				StartInlineRename();
				break;

			case Key.F6:
				e.Handled = true;
				if (Tree.SelectedItem is SessionNodeViewModel sessionToMove)
					vm.StartMoveMode(sessionToMove);
				break;

			case Key.Enter when vm.IsMoveModeActive:
				e.Handled = true;
				vm.ConfirmMoveMode();
				break;

			case Key.Escape when vm.IsMoveModeActive:
				e.Handled = true;
				vm.CancelMoveMode();
				break;

			case Key.Escape when _renamingNode is not null:
				e.Handled = true;
				CancelInlineRename();
				break;
		}
	}

	// ── Inline rename (F2) ──────────────────────────────────────────────────

	private void StartInlineRename()
	{
		var selected = Tree.SelectedItem;
		if (selected is not (SessionNodeViewModel or GroupNodeViewModel))
			return;

		var selectedVm = (ViewModelBase)selected;
		var treeViewItem = FindTreeViewItemForDataContext(selectedVm);
		if (treeViewItem is null)
			return;

		// Find the name TextBlock in the tree item
		var nameTextBlock = FindNameTextBlock(treeViewItem);
		if (nameTextBlock is null)
			return;

		_renamingNode = selectedVm;

		// Create and show a TextBox in place of the TextBlock
		var currentName = selectedVm switch
		{
			SessionNodeViewModel s => s.Name,
			GroupNodeViewModel g => g.Name,
			_ => string.Empty,
		};

		_renameTextBox = new TextBox
		{
			Text = currentName,
			FontSize = nameTextBlock.FontSize,
			Padding = new Thickness(0),
			Margin = new Thickness(0),
			MinWidth = 80,
		};

		_renameTextBox.KeyDown += OnRenameTextBoxKeyDown;
		_renameTextBox.LostFocus += OnRenameTextBoxLostFocus;

		// Replace TextBlock with TextBox in its parent
		if (nameTextBlock.Parent is Panel panel)
		{
			var idx = panel.Children.IndexOf(nameTextBlock);
			if (idx >= 0)
			{
				nameTextBlock.IsVisible = false;
				panel.Children.Insert(idx + 1, _renameTextBox);
				_renameTextBox.Focus();
				_renameTextBox.SelectAll();
			}
		}
		else if (nameTextBlock.Parent is StackPanel stackPanel)
		{
			var idx = stackPanel.Children.IndexOf(nameTextBlock);
			if (idx >= 0)
			{
				nameTextBlock.IsVisible = false;
				stackPanel.Children.Insert(idx + 1, _renameTextBox);
				_renameTextBox.Focus();
				_renameTextBox.SelectAll();
			}
		}
	}

	private void OnRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			e.Handled = true;
			ConfirmInlineRename();
		}
		else if (e.Key == Key.Escape)
		{
			e.Handled = true;
			CancelInlineRename();
		}
	}

	private void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
	{
		// Confirm on focus loss (clicking elsewhere)
		ConfirmInlineRename();
	}

	private void ConfirmInlineRename()
	{
		if (_renameTextBox is null || _renamingNode is null)
			return;

		if (DataContext is not SessionTreeViewModel vm)
		{
			CancelInlineRename();
			return;
		}

		var newName = _renameTextBox.Text?.Trim();
		if (!string.IsNullOrEmpty(newName))
		{
			switch (_renamingNode)
			{
				case SessionNodeViewModel session:
					vm.RenameSession(session, newName);
					break;
				case GroupNodeViewModel group:
					vm.RenameGroup(group, newName);
					break;
			}
		}

		CleanupRenameTextBox();
	}

	private void CancelInlineRename()
	{
		CleanupRenameTextBox();
	}

	private void CleanupRenameTextBox()
	{
		if (_renameTextBox is null)
			return;

		_renameTextBox.KeyDown -= OnRenameTextBoxKeyDown;
		_renameTextBox.LostFocus -= OnRenameTextBoxLostFocus;

		// Remove TextBox and restore TextBlock visibility
		if (_renameTextBox.Parent is Panel panel)
		{
			panel.Children.Remove(_renameTextBox);
			// Restore the hidden TextBlock
			foreach (var child in panel.Children)
			{
				if (child is TextBlock tb && !tb.IsVisible)
				{
					tb.IsVisible = true;
					break;
				}
			}
		}
		else if (_renameTextBox.Parent is StackPanel stackPanel)
		{
			stackPanel.Children.Remove(_renameTextBox);
			foreach (var child in stackPanel.Children)
			{
				if (child is TextBlock tb && !tb.IsVisible)
				{
					tb.IsVisible = true;
					break;
				}
			}
		}

		_renameTextBox = null;
		_renamingNode = null;
	}

	private TreeViewItem? FindTreeViewItemForDataContext(object dataContext)
	{
		return FindTreeViewItemRecursive(Tree, dataContext);
	}

	private static TreeViewItem? FindTreeViewItemRecursive(ItemsControl parent, object dataContext)
	{
		foreach (var item in parent.GetRealizedContainers())
		{
			if (item is TreeViewItem tvi)
			{
				if (tvi.DataContext == dataContext)
					return tvi;

				var found = FindTreeViewItemRecursive(tvi, dataContext);
				if (found is not null)
					return found;
			}
		}
		return null;
	}

	private static TextBlock? FindNameTextBlock(TreeViewItem tvi)
	{
		// Walk the visual tree to find the TextBlock bound to "Name" or "Label"
		// It's the second TextBlock in the StackPanel (first is the icon emoji)
		return FindSecondTextBlock(tvi);
	}

	private static TextBlock? FindSecondTextBlock(Visual parent)
	{
		var count = 0;
		foreach (var child in parent.GetVisualDescendants())
		{
			if (child is TextBlock tb)
			{
				count++;
				// Skip the icon TextBlock (emoji), return the name TextBlock
				if (count == 2)
					return tb;
			}
		}
		return null;
	}

	// ── Drag and drop ───────────────────────────────────────────────────────

	private void OnSessionPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Border border || border.DataContext is not SessionNodeViewModel)
			return;

		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		_dragStartPoint = e.GetPosition(this);
		_isDragPending = true;

		border.PointerMoved += OnSessionPointerMoved;
		border.PointerReleased += OnSessionPointerReleased;
	}

	private async void OnSessionPointerMoved(object? sender, PointerEventArgs e)
	{
		if (!_isDragPending || sender is not Border border)
			return;

		var currentPos = e.GetPosition(this);
		var diff = currentPos - _dragStartPoint;

		// Minimum drag distance threshold
		if (Math.Abs(diff.X) < 8 && Math.Abs(diff.Y) < 8)
			return;

		_isDragPending = false;
		border.PointerMoved -= OnSessionPointerMoved;
		border.PointerReleased -= OnSessionPointerReleased;

		if (border.DataContext is not SessionNodeViewModel session)
			return;

#pragma warning disable CS0618 // DataObject obsolete in favour of DataTransfer
		var data = new DataObject();
		data.Set("SessionNode", session);

		session.IsBeingMoved = true;

		await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618

		session.IsBeingMoved = false;
	}

	private void OnSessionPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		_isDragPending = false;
		if (sender is Border border)
		{
			border.PointerMoved -= OnSessionPointerMoved;
			border.PointerReleased -= OnSessionPointerReleased;
		}
	}

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		if (DataContext is not SessionTreeViewModel vm)
			return;

		e.DragEffects = DragDropEffects.None;

#pragma warning disable CS0618
		var transfer = e.Data;
#pragma warning restore CS0618
		if (!transfer.Contains("SessionNode"))
			return;

		var session = transfer.Get("SessionNode") as SessionNodeViewModel;
		if (session is null)
			return;

		// Determine the target node from the event source
		var targetVm = GetDataContextFromEventSource(e.Source);
		if (targetVm is null)
			return;

		var sourceOrigin = vm.GetGitOriginForNode(session);

		// Determine the container for the target
		ViewModelBase container;
		if (targetVm is SessionNodeViewModel)
		{
			var parentInfo = vm.FindParentOf(targetVm);
			if (parentInfo.parent is null)
				return;
			container = parentInfo.parent;
		}
		else if (targetVm is DirectoryNodeViewModel or GroupNodeViewModel)
			container = targetVm;
		else
			return;

		if (vm.CanMoveSessionTo(sourceOrigin, container))
			e.DragEffects = DragDropEffects.Move;
	}

	private void OnDrop(object? sender, DragEventArgs e)
	{
		if (DataContext is not SessionTreeViewModel vm)
			return;

#pragma warning disable CS0618
		var transfer = e.Data;
#pragma warning restore CS0618
		if (!transfer.Contains("SessionNode"))
			return;

		var session = transfer.Get("SessionNode") as SessionNodeViewModel;
		if (session is null)
			return;

		var targetVm = GetDataContextFromEventSource(e.Source);
		if (targetVm is null)
			return;

		vm.MoveSessionTo(session, targetVm);
	}

	private static ViewModelBase? GetDataContextFromEventSource(object? source)
	{
		var visual = source as Visual;
		while (visual is not null)
		{
			if (visual.DataContext is ViewModelBase vmBase)
				return vmBase;
			visual = visual.GetVisualParent();
		}
		return null;
	}

	// ── Context menu ─────────────────────────────────────────────────────────

	private async void OnContextMenuClicked(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem mi || DataContext is not SessionTreeViewModel vm)
			return;

		var action    = mi.Tag as string;
		var ownerVm   = GetContextMenuOwnerDataContext(mi);
		var ownerWindow = TopLevel.GetTopLevel(this) as Window;

		switch (action)
		{
			case "AddGroupToDirectory" when ownerVm is DirectoryNodeViewModel dir:
				await AddGroupToDirectoryAsync(vm, dir, ownerWindow);
				break;

			case "AddSessionToDirectory" when ownerVm is DirectoryNodeViewModel dir:
				await AddSessionToDirectoryAsync(vm, dir, ownerWindow);
				break;

			case "ImportToDirectory" when ownerVm is DirectoryNodeViewModel dir:
				await ImportToDirectoryAsync(vm, dir, ownerWindow);
				break;

			case "AddGroupToGroup" when ownerVm is GroupNodeViewModel grp:
				await AddGroupToGroupAsync(vm, grp, ownerWindow);
				break;

			case "AddSessionToGroup" when ownerVm is GroupNodeViewModel grp:
				await AddSessionToGroupAsync(vm, grp, ownerWindow);
				break;

			case "ImportToGroup" when ownerVm is GroupNodeViewModel grp:
				await ImportToGroupAsync(vm, grp, ownerWindow);
				break;

			case "RenameGroup" when ownerVm is GroupNodeViewModel grp:
				await RenameGroupAsync(vm, grp, ownerWindow);
				break;

			case "RenameSession" when ownerVm is SessionNodeViewModel session:
				await RenameSessionAsync(vm, session, ownerWindow);
				break;

			case "MoveSession" when ownerVm is SessionNodeViewModel session:
				vm.StartMoveMode(session);
				break;

			case "DeleteDirectory" when ownerVm is DirectoryNodeViewModel dir:
				vm.TryDeleteDirectory(dir);
				break;

			case "DeleteGroup" when ownerVm is GroupNodeViewModel grp:
				DeleteGroupFromTree(vm, grp);
				break;

			case "DeleteSession" when ownerVm is SessionNodeViewModel session:
				DeleteSessionFromTree(vm, session);
				break;
		}
	}

	// ── Context menu helpers ─────────────────────────────────────────────────

	private static object? GetContextMenuOwnerDataContext(MenuItem mi)
	{
		// MenuItem → ContextMenu → StackPanel (PlacementTarget) → DataContext
		if (mi.Parent is ContextMenu cm)
			return cm.PlacementTarget?.DataContext ?? cm.DataContext;
		return null;
	}

	private static Task<string?> ShowInputAsync(Window? owner, string title, string prompt, string? initial = null)
		=> (owner as MainWindow)?.ShowInputOverlayAsync(title, prompt, initial)
		   ?? Task.FromResult<string?>(null);

	private static async Task AddGroupToDirectoryAsync(
		SessionTreeViewModel vm, DirectoryNodeViewModel dir, Window? owner)
	{
		var name = await ShowInputAsync(owner, "New Group", "Group name:");
		if (!string.IsNullOrEmpty(name))
			vm.AddGroup(dir, name);
	}

	private static async Task AddSessionToDirectoryAsync(
		SessionTreeViewModel vm, DirectoryNodeViewModel dir, Window? owner)
	{
		var name = await ShowInputAsync(owner, "New Session", "Session name:");
		if (!string.IsNullOrEmpty(name))
			vm.AddSession(dir, name);
	}

	private static async Task AddGroupToGroupAsync(
		SessionTreeViewModel vm, GroupNodeViewModel grp, Window? owner)
	{
		var name = await ShowInputAsync(owner, "New Group", "Group name:");
		if (!string.IsNullOrEmpty(name))
			vm.AddGroupToGroup(grp, name);
	}

	private static async Task AddSessionToGroupAsync(
		SessionTreeViewModel vm, GroupNodeViewModel grp, Window? owner)
	{
		var name = await ShowInputAsync(owner, "New Session", "Session name:");
		if (!string.IsNullOrEmpty(name))
			vm.AddSessionToGroup(grp, name);
	}

	private static async Task RenameGroupAsync(
		SessionTreeViewModel vm, GroupNodeViewModel grp, Window? owner)
	{
		var name = await ShowInputAsync(owner, "Rename Group", "New name:", grp.Name);
		if (!string.IsNullOrEmpty(name))
			vm.RenameGroup(grp, name);
	}

	private static async Task RenameSessionAsync(
		SessionTreeViewModel vm, SessionNodeViewModel session, Window? owner)
	{
		var name = await ShowInputAsync(owner, "Rename Session", "New name:", session.Name);
		if (!string.IsNullOrEmpty(name))
			vm.RenameSession(session, name);
	}

	private void DeleteGroupFromTree(SessionTreeViewModel vm, GroupNodeViewModel grp)
	{
		// Find the parent and delete from it
		foreach (var dir in vm.Directories)
		{
			if (TryDeleteGroupFromParent(vm, dir, grp))
				return;
		}
	}

	private static bool TryDeleteGroupFromParent(
		SessionTreeViewModel vm, DirectoryNodeViewModel dir, GroupNodeViewModel target)
	{
		foreach (var child in dir.Children)
		{
			if (child == target)
				return vm.TryDeleteGroup(dir, target);

			if (child is GroupNodeViewModel grp && TryDeleteGroupFromGroup(vm, grp, target))
				return true;
		}
		return false;
	}

	private static bool TryDeleteGroupFromGroup(
		SessionTreeViewModel vm, GroupNodeViewModel parent, GroupNodeViewModel target)
	{
		foreach (var child in parent.Children)
		{
			if (child == target)
				return vm.TryDeleteGroupFromGroup(parent, target);

			if (child is GroupNodeViewModel grp && TryDeleteGroupFromGroup(vm, grp, target))
				return true;
		}
		return false;
	}

	private async void DeleteSessionFromTree(SessionTreeViewModel vm, SessionNodeViewModel session)
	{
		var fileService = App.Services.GetRequiredService<ISessionFileService>();
		var fileExists = fileService.SessionFileExists(session.FileName);

		if (fileExists)
		{
			// Session file exists — confirm deletion with user
			var ownerWindow = TopLevel.GetTopLevel(this) as MainWindow;
			if (ownerWindow == null)
				return;

			var confirmed = await ownerWindow.ShowConfirmOverlayAsync(
				$"Delete session \"{session.Name}\"?\n\nThis removes the session from Maximus and deletes its local file. The original Claude Code session is not affected.",
				"Delete");

			if (!confirmed)
				return;
		}

		// Try with forceDelete when file exists
		foreach (var dir in vm.Directories)
		{
			if (TryDeleteSessionFromParent(vm, dir, session, fileExists))
				return;
		}
	}

	private static bool TryDeleteSessionFromParent(
		SessionTreeViewModel vm, DirectoryNodeViewModel dir, SessionNodeViewModel target, bool forceDelete = false)
	{
		foreach (var child in dir.Children)
		{
			if (child == target)
				return vm.TryDeleteSession(dir, target, forceDelete);

			if (child is GroupNodeViewModel grp && TryDeleteSessionFromGroup(vm, grp, target, forceDelete))
				return true;
		}
		return false;
	}

	private static bool TryDeleteSessionFromGroup(
		SessionTreeViewModel vm, GroupNodeViewModel parent, SessionNodeViewModel target, bool forceDelete = false)
	{
		foreach (var child in parent.Children)
		{
			if (child == target)
				return vm.TryDeleteSessionFromGroup(parent, target, forceDelete);

			if (child is GroupNodeViewModel grp && TryDeleteSessionFromGroup(vm, grp, target, forceDelete))
				return true;
		}
		return false;
	}

	// ── Session import ──────────────────────────────────────────────────────

	private static async Task ImportToDirectoryAsync(
		SessionTreeViewModel vm, DirectoryNodeViewModel dir, Window? ownerWindow)
	{
		var result = await ShowImportPickerAsync(dir.Path, dir.Path, vm, ownerWindow);
		if (result == null)
			return;

		var fileService = App.Services.GetRequiredService<ISessionFileService>();
		var importService = App.Services.GetRequiredService<IClaudeSessionImportService>();
		var (dirTarget, grpTarget) = vm.FindTargetByKey(result.Value.TargetKey);

		// If target is a new directory not yet in the tree, create it
		if (dirTarget == null && grpTarget == null && !string.IsNullOrEmpty(result.Value.TargetKey))
			dirTarget = vm.AddDirectory(result.Value.TargetKey);

		foreach (var item in result.Value.Items)
			ExecuteImport(vm, fileService, importService, item, dirTarget, grpTarget);
	}

	private static async Task ImportToGroupAsync(
		SessionTreeViewModel vm, GroupNodeViewModel grp, Window? ownerWindow)
	{
		var targetKey = $"{grp.WorkingDirectory}|{grp.Name}";
		var result = await ShowImportPickerAsync(grp.WorkingDirectory, targetKey, vm, ownerWindow);
		if (result == null)
			return;

		var fileService = App.Services.GetRequiredService<ISessionFileService>();
		var importService = App.Services.GetRequiredService<IClaudeSessionImportService>();
		var (dirTarget, grpTarget) = vm.FindTargetByKey(result.Value.TargetKey);

		// If target is a new directory not yet in the tree, create it
		if (dirTarget == null && grpTarget == null && !string.IsNullOrEmpty(result.Value.TargetKey))
			dirTarget = vm.AddDirectory(result.Value.TargetKey);

		foreach (var item in result.Value.Items)
			ExecuteImport(vm, fileService, importService, item, dirTarget, grpTarget);
	}

	private static async Task<(IReadOnlyList<ImportSessionItemViewModel> Items, string TargetKey)?> ShowImportPickerAsync(
		string workingDirectory, string initialTargetKey, SessionTreeViewModel vm, Window? ownerWindow)
	{
		var importService = App.Services.GetRequiredService<IClaudeSessionImportService>();
		var assistService = App.Services.GetRequiredService<IClaudeAssistService>();
		var daemonService = App.Services.GetRequiredService<ITessynDaemonService>();

		var sourceDirectories = vm.BuildSourceDirectories();
		var importTargets = vm.BuildImportTargets();
		var alreadyImportedIds = vm.CollectAllClaudeSessionIds();

		var pickerVm = new ImportPickerViewModel(importService, assistService, daemonService);
		pickerVm.Initialize(sourceDirectories, importTargets, workingDirectory, initialTargetKey, alreadyImportedIds);

		var picker = new ImportPickerWindow { DataContext = pickerVm };

		if (ownerWindow != null)
			await picker.ShowDialog(ownerWindow);
		else
			picker.Show();

		if (picker.Result == null || picker.Result.Count == 0 || pickerVm.SelectedImportTarget == null)
			return null;

		return (picker.Result, pickerVm.SelectedImportTarget.Key);
	}

	internal static void ExecuteImport(
		SessionTreeViewModel vm,
		ISessionFileService fileService,
		IClaudeSessionImportService importService,
		ImportSessionItemViewModel item,
		DirectoryNodeViewModel? dirParent,
		GroupNodeViewModel? grpParent)
	{
		try
		{
			var name = item.Summary.GeneratedTitle
				?? TruncateForSessionName(item.Summary.FirstUserPrompt)
				?? "Imported Session";

			string fileName;

			if (!string.IsNullOrEmpty(item.Summary.JsonlPath))
			{
				// Local JSONL file available — parse and create .txt session file
				var entries = importService.ParseJsonlSession(item.Summary.JsonlPath);
				if (entries.Count == 0)
					return;

				fileName = fileService.CreateSessionFile();
				fileService.WriteSessionFile(fileName, entries);
			}
			else
			{
				// Daemon-sourced session (no local JSONL) — create placeholder file.
				// Content will be loaded from daemon via sessions.get when opened.
				var appSettings = App.Services.GetRequiredService<IAppSettingsService>();
				if (appSettings.Settings.UseTessynDaemon)
					fileName = $"daemon-{System.Guid.NewGuid():N}.pending";
				else
					return; // Cannot import without JSONL and without daemon
			}

			// Determine original project path for cross-project sessions
			string? originalProjectPath = null;
			if (!string.IsNullOrEmpty(item.Summary.JsonlPath))
			{
				// JSONL path is like ~/.claude/projects/<slug>/<sessionId>.jsonl
				// The original project path is NOT directly recoverable from the slug,
				// but we can store the slug-based path so the daemon can find the session.
				var jsonlDir = System.IO.Path.GetDirectoryName(item.Summary.JsonlPath);
				if (jsonlDir != null)
				{
					var slug = System.IO.Path.GetFileName(jsonlDir);
					// Check if this session is from a different project than the import target
					var targetSlug = Constants.ClaudeSessions.BuildProjectSlug(
						dirParent?.Path ?? grpParent?.WorkingDirectory ?? string.Empty);
					if (!string.Equals(slug, targetSlug, StringComparison.Ordinal))
					{
						// Cross-project: reconstruct path from slug (replace leading - with /)
						// Slug format: -Users-aisling-...-projectName → /Users/aisling/.../projectName
						originalProjectPath = slug.StartsWith('-') ? slug.Replace('-', '/') : slug;
					}
				}
			}

			// Add to tree
			if (dirParent != null)
			{
				var node = vm.ImportSession(dirParent, name, fileName, item.SessionId);
				if (originalProjectPath != null)
					node.Model.OriginalProjectPath = originalProjectPath;
			}
			else if (grpParent != null)
			{
				var node = vm.ImportSessionToGroup(grpParent, name, fileName, item.SessionId);
				if (originalProjectPath != null)
					node.Model.OriginalProjectPath = originalProjectPath;
			}
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "ExecuteImport: failed to import session {SessionId}", item.SessionId);
		}
	}

	private static string? TruncateForSessionName(string? prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt))
			return null;

		var firstLine = prompt.Split('\n')[0].Trim();
		if (firstLine.Length > 60)
			return firstLine[..57] + "...";
		return firstLine;
	}
}
