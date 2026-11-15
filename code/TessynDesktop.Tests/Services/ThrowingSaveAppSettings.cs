using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Forwards to a real settings service, but <see cref="Save"/> throws an <see cref="IOException"/> while <see cref="FailSaves"/> is set.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ThrowingSaveAppSettings(IAppSettingsService inner) : IAppSettingsService
{
	public bool FailSaves { get; set; }
	public AppSettingsModel Settings => inner.Settings;
	public object SyncRoot => inner.SyncRoot;
	public void Load() => inner.Load();

	public void Save()
	{
		if (FailSaves)
			throw new IOException("disk full");
		inner.Save();
	}
}
