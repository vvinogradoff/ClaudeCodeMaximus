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
}
