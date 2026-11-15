using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// File drag-and-drop and paste into the prompt input (FR.31.2). In path mode the dropped paths go into the text; in file mode every file is
/// copied into the session's attachments through <c>attachment.import</c> (one call per file, sequential, cancellable) and
/// <c>Attached file: &lt;path&gt;</c> goes into the text. Problems are shown as the non-blocking <see cref="Notice"/>, never as a dialog,
/// and nothing is inserted for a file that failed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private readonly ComposerTextInserter _inserter = new();
	private IClientStateStore? _stateStore;
	private CancellationTokenSource? _importCts;

	/// <summary>File mode (global client state): dropped files are copied into the session's attachments.</summary>
	[ObservableProperty]
	private bool _attachAsFiles;

	/// <summary>True while dropped files are being copied.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(CancelImportCommand))]
	private bool _isImporting;

	/// <summary>"Attaching 2 of 5: name" while <see cref="IsImporting"/>.</summary>
	[ObservableProperty]
	private string _importStatus = string.Empty;

	/// <summary>Files finished so far and the total, for a progress bar.</summary>
	[ObservableProperty]
	private int _importDone;

	[ObservableProperty]
	private int _importTotal;

	/// <summary>Raised after the text changed through a drop or paste; the view puts the caret at this index and keeps the focus in the box.</summary>
	public event EventHandler<int>? InputCaretRequested;

	private void InitializeComposer(IClientStateStore? stateStore)
	{
		_stateStore = stateStore;
		AttachAsFiles = stateStore?.State.AttachAsFiles ?? false;
	}

	partial void OnAttachAsFilesChanged(bool value)
	{
		if (_stateStore != null && _stateStore.State.AttachAsFiles != value)
			_stateStore.Update(s => s.AttachAsFiles = value);
	}

	[RelayCommand(CanExecute = nameof(IsImporting))]
	private void CancelImport() => _importCts?.Cancel();

	/// <summary>
	/// Handles files dropped or pasted into the input. <paramref name="selectionStart"/> and <paramref name="selectionLength"/> are the caret and
	/// selection of the box at that moment. Completes when the text was updated (file mode: when every file was copied or refused).
	/// </summary>
	public async Task HandleFilesAsync(IReadOnlyList<string> paths, int selectionStart, int selectionLength)
	{
		if (paths.Count == 0)
			return;

		if (!AttachAsFiles)
		{
			Notice = null;
			ApplyInsertion(NodeId, selectionStart, selectionLength, string.Join(Environment.NewLine, paths));
			_logger.LogInformation("Composer: {Count} file(s) handled in path mode", paths.Count);
			return;
		}

		var id = NodeId;
		if (id == null || !HasSession)
		{
			Notice = Constants.Texts.AttachNeedsSession;
			return;
		}

		if (IsImporting)
		{
			Notice = Constants.Texts.AttachBusy;
			return;
		}

		await ImportFilesAsync(id, paths, selectionStart, selectionLength);
		_logger.LogInformation("Composer: {Count} file(s) handled in file mode for {NodeId}", paths.Count, id);
	}

	private async Task ImportFilesAsync(string nodeId, IReadOnlyList<string> paths, int selectionStart, int selectionLength)
	{
		var snapshot = InputText;
		var cts = new CancellationTokenSource();
		_importCts = cts;
		IsImporting = true;
		ImportTotal = paths.Count;
		ImportDone = 0;
		Notice = null;

		var lines = new List<string>();
		var failures = new List<string>();
		var cancelled = false;
		try
		{
			for (var i = 0; i < paths.Count; i++)
			{
				if (cts.IsCancellationRequested)
				{
					cancelled = true;
					break;
				}

				var source = paths[i];
				var name = DisplayName(source);
				ImportStatus = $"Attaching {i + 1} of {paths.Count}: {name}";
				try
				{
					if (Directory.Exists(source))
					{
						failures.Add($"{name}: {Constants.Texts.AttachFolderRefused}");
					}
					else
					{
						var attachment = await _client.AttachmentImportAsync(nodeId, source, cts.Token);
						lines.Add(Constants.Texts.AttachedFilePrefix + attachment.Path);
					}
				}
				catch (OperationCanceledException) when (cts.IsCancellationRequested)
				{
					cancelled = true;
					break;
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "attachment.import failed for {Path} in {NodeId}", source, nodeId);
					failures.Add($"{name}: {ex.Message}");
				}

				ImportDone = i + 1;
			}
		}
		finally
		{
			IsImporting = false;
			ImportStatus = string.Empty;
			_importCts = null;
			cts.Dispose();
		}

		if (lines.Count > 0)
		{
			// The selection only still means something when the user did not type in the meantime; otherwise insert without removing anything.
			var untouched = NodeId != nodeId || InputText == snapshot;
			ApplyInsertion(nodeId, selectionStart, untouched ? selectionLength : 0, string.Join(Environment.NewLine, lines));
		}

		Notice = BuildImportNotice(failures, cancelled, lines.Count);
	}

	/// <summary>Inserts into the current text when <paramref name="nodeId"/> is the open session, otherwise into that session's saved draft.</summary>
	private void ApplyInsertion(string? nodeId, int start, int length, string insertion)
	{
		if (nodeId == null || nodeId == NodeId)
		{
			var result = _inserter.Insert(InputText, start, length, insertion);
			InputText = result.Text;
			InputCaretRequested?.Invoke(this, result.Caret);
		}
		else if (_drafts.TryGetValue(nodeId, out var draft))
		{
			_drafts[nodeId] = _inserter.Insert(draft, start, length, insertion).Text;
		}
	}

	private static string? BuildImportNotice(List<string> failures, bool cancelled, int attached)
	{
		if (failures.Count == 0 && !cancelled)
			return null;

		var lines = new List<string>();
		if (failures.Count > 0)
		{
			lines.Add(Constants.Texts.AttachFailedHeading);
			lines.AddRange(failures.Take(Constants.Limits.MaxListedAttachFailures));
			if (failures.Count > Constants.Limits.MaxListedAttachFailures)
				lines.Add($"and {failures.Count - Constants.Limits.MaxListedAttachFailures} more");
		}

		if (cancelled)
			lines.Add(attached > 0 ? $"{Constants.Texts.AttachCancelled} {attached} file(s) were already attached." : Constants.Texts.AttachCancelled);

		return string.Join(Environment.NewLine, lines);
	}

	private static string DisplayName(string path)
	{
		var trimmed = path.TrimEnd('\\', '/');
		var name = Path.GetFileName(trimmed);
		return name.Length > 0 ? name : trimmed;
	}
}
