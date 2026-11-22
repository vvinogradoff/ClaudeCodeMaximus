using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Records what was put on the clipboard instead of touching the system clipboard.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeClipboardWriter : IClipboardWriter
{
	public List<string> Written { get; } = [];

	public bool Busy { get; set; }

	public bool TrySetText(string text)
	{
		if (Busy)
			return false;

		Written.Add(text);
		return true;
	}
}
