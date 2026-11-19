using System.Windows;

namespace TessynDesktop.WPF.Services;

/// <summary>Reads real file system paths (<see cref="DataFormats.FileDrop"/>) from a drag-and-drop or clipboard data object; any other format yields nothing.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FileDropReader
{
	/// <summary>The dropped or pasted paths in order, or an empty list when the data is not a file list.</summary>
	public IReadOnlyList<string> Read(IDataObject? data)
	{
		if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
			return [];

		try
		{
			return data.GetData(DataFormats.FileDrop) is string[] paths
				? [.. paths.Where(p => !string.IsNullOrWhiteSpace(p))]
				: [];
		}
		catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
		{
			return [];   // the source went away mid-drag; leave the drop to the default behaviour
		}
	}

	/// <summary>
	/// True when <paramref name="clipboard"/> holds a file list and no text, as Explorer's Copy of files leaves it: Paste is then the composer's
	/// (presence only; the list is read when Paste runs). A clipboard with text as well (some apps put a file's name beside the list) pastes as text.
	/// </summary>
	public bool HasPastedFiles(IDataObject? clipboard)
	{
		if (clipboard == null)
			return false;

		try
		{
			return clipboard.GetDataPresent(DataFormats.FileDrop) && !HasText(clipboard);
		}
		catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
		{
			return false;   // the program that copied went away; nothing to paste
		}
	}

	/// <summary>The paths of a file list on the clipboard that carries no text (see <see cref="HasPastedFiles"/>), or an empty list.</summary>
	public IReadOnlyList<string> ReadPastedFiles(IDataObject? clipboard) => HasPastedFiles(clipboard) ? Read(clipboard) : [];

	private bool HasText(IDataObject data) =>
		data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);
}
