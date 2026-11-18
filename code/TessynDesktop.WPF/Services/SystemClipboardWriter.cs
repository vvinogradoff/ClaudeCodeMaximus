using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IClipboardWriter"/> over the Windows clipboard; another process may hold it open for a moment, so the write is retried by WPF and a refusal is only logged.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SystemClipboardWriter : IClipboardWriter
{
	private readonly ILogger<SystemClipboardWriter> _logger;

	public SystemClipboardWriter(ILogger<SystemClipboardWriter> logger)
	{
		_logger = logger;
	}

	public bool TrySetText(string text)
	{
		try
		{
			Clipboard.SetDataObject(text, copy: true);
			return true;
		}
		catch (ExternalException ex)
		{
			_logger.LogWarning(ex, "The clipboard could not be written");
			return false;
		}
	}
}
