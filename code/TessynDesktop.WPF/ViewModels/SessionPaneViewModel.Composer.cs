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
/// and nothing is inserted for a file that failed. Paths with control or invisible characters are refused in both modes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private readonly ComposerTextInserter _inserter = new();
	private readonly PromptPathGuard _pathGuard = new();
	private IClientStateStore? _stateStore;
	private CancellationTokenSource? _importCts;
	private bool _importAbandoned;
	private IReadOnlyList<string> _testDropPaths = [];

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

	/// <summary>Whether a path is a folder; runs off the UI thread (a UNC or offline drive can block for seconds). Replaceable for tests.</summary>
	public Func<string, bool> DirectoryProbe { get; init; } = Directory.Exists;

	/// <summary>
	/// The <c>--test-drop</c> seam (<see cref="Infrastructure.StartupOptions.TestDropPaths"/>): paths the view hands to the input once the first
	/// session is shown. Empty in normal use.
	/// </summary>
	public IReadOnlyList<string> TestDropPaths
	{
		get => _testDropPaths;
		init => _testDropPaths = value;
	}

	/// <summary>Raised after the text changed through a drop or paste; the view puts the caret at this index (and takes focus only when it already is in the pane).</summary>
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

	/// <summary>The user left the session a copy belongs to: stop it, and show nothing about it afterwards.</summary>
	private void AbandonImport()
	{
		if (_importCts == null)
			return;

		_importAbandoned = true;
		_importCts.Cancel();
	}

	/// <summary>Returns the pending <c>--test-drop</c> paths once (and logs that the seam is used); empty afterwards.</summary>
	public IReadOnlyList<string> TakeTestDropPaths()
	{
		var paths = _testDropPaths;
		_testDropPaths = [];
		if (paths.Count > 0)
			_logger.LogInformation("Test seam: handing {Count} --test-drop path(s) to the prompt input", paths.Count);

		return paths;
	}

	/// <summary>
	/// Handles files dropped or pasted into the input. <paramref name="selectionStart"/> and <paramref name="selectionLength"/> are the caret and
	/// selection of the box at that moment. Completes when the text was updated (file mode: when every file was copied or refused).
	/// </summary>
	public async Task HandleFilesAsync(IReadOnlyList<string> paths, int selectionStart, int selectionLength)
	{
		if (paths.Count == 0)
			return;

		var failures = new List<string>();
		var accepted = new List<string>();
		foreach (var path in paths)
		{
			if (_pathGuard.IsSafe(path))
				accepted.Add(path);
			else
				failures.Add($"{_pathGuard.DisplayName(path)}: {Constants.Texts.HiddenCharactersRefused}");
		}

		if (!AttachAsFiles)
		{
			if (accepted.Count > 0)
				ApplyInsertion(NodeId, selectionStart, selectionLength, string.Join(Environment.NewLine, accepted));

			Notice = BuildNotice(Constants.Texts.InsertFailedHeading, failures, cancelled: false, attached: 0);
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

		await ImportFilesAsync(id, accepted, failures, selectionStart, selectionLength);
		_logger.LogInformation("Composer: {Count} file(s) handled in file mode for {NodeId}", paths.Count, id);
	}

	private async Task ImportFilesAsync(string nodeId, IReadOnlyList<string> paths, List<string> failures, int selectionStart, int selectionLength)
	{
		var snapshot = InputText;
		var cts = new CancellationTokenSource();
		_importCts = cts;
		_importAbandoned = false;
		IsImporting = true;
		ImportTotal = paths.Count;
		ImportDone = 0;
		Notice = null;

		var lines = new List<string>();
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
				var name = _pathGuard.DisplayName(source);
				ImportStatus = $"Attaching {i + 1} of {paths.Count}: {name}";
				try
				{
					if (await Task.Run(() => DirectoryProbe(source), cts.Token))
					{
						failures.Add($"{name}: {Constants.Texts.AttachFolderRefused}");
					}
					else
					{
						var attachment = await _client.AttachmentImportAsync(nodeId, source, cts.Token);
						if (_pathGuard.IsSafe(attachment.Path))
							lines.Add(Constants.Texts.AttachedFilePrefix + attachment.Path);
						else
							failures.Add($"{name}: {Constants.Texts.StoredPathRefused}");
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
			var joined = string.Join(Environment.NewLine, lines);
			var current = NodeId == nodeId ? InputText : _drafts.GetValueOrDefault(nodeId, string.Empty);
			if (current == snapshot)
				ApplyInsertion(nodeId, selectionStart, selectionLength, joined);
			else
				AppendOnNewLine(nodeId, current, joined);   // the user typed meanwhile: the old offset means nothing, so never cut into their text
		}

		// The notice belongs to the session it was about; another session that is visible now must not show it.
		if (NodeId == nodeId && !_importAbandoned)
			Notice = BuildNotice(Constants.Texts.AttachFailedHeading, failures, cancelled, lines.Count);
	}

	/// <summary>Inserts into the open session's text, or into the saved draft of <paramref name="nodeId"/> when another session is open.</summary>
	private void ApplyInsertion(string? nodeId, int start, int length, string insertion)
	{
		if (nodeId == null || nodeId == NodeId)
		{
			var result = _inserter.Insert(InputText, start, length, insertion);
			InputText = result.Text;
			InputCaretRequested?.Invoke(this, result.Caret);
		}
		else
		{
			_drafts[nodeId] = _inserter.Insert(_drafts.GetValueOrDefault(nodeId, string.Empty), start, length, insertion).Text;
		}
	}

	/// <summary>Adds the lines at the end of <paramref name="current"/>, on a line of their own.</summary>
	private void AppendOnNewLine(string nodeId, string current, string insertion)
	{
		var prefix = current.Length == 0 || current.EndsWith('\n') ? string.Empty : Environment.NewLine;
		ApplyInsertion(nodeId, current.Length, 0, prefix + insertion);
	}

	private static string? BuildNotice(string heading, List<string> failures, bool cancelled, int attached)
	{
		if (failures.Count == 0 && !cancelled)
			return null;

		var lines = new List<string>();
		if (failures.Count > 0)
		{
			lines.Add(heading);
			lines.AddRange(failures.Take(Constants.Limits.MaxListedAttachFailures));
			if (failures.Count > Constants.Limits.MaxListedAttachFailures)
				lines.Add($"and {failures.Count - Constants.Limits.MaxListedAttachFailures} more");
		}

		if (cancelled)
			lines.Add(attached > 0 ? $"{Constants.Texts.AttachCancelled} {attached} file(s) were already attached." : Constants.Texts.AttachCancelled);

		return string.Join(Environment.NewLine, lines);
	}
}
