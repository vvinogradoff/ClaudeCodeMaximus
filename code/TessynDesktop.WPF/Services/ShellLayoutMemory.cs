using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Reads and writes the docked shell's saved layout (section <c>shellLayout</c>, FR.6.18, FR.6.22) and works out what the shell starts from: the saved layout, or the default
/// one with the old tree width and collapse taken over once. Not persistent (a test window without a test data root): nothing is read or written. Holds no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutMemory
{
	private readonly IClientSettingsStore _settings;
	private readonly IClientStateStore _state;

	public ShellLayoutMemory(IClientSettingsStore settings, IClientStateStore state, bool persistent)
	{
		_settings = settings;
		_state = state;
		Persistent = persistent;
	}

	/// <summary>False for a test window without a test data root: the default layout, never saved.</summary>
	public bool Persistent { get; }

	public ShellLayoutStart ReadStart()
	{
		if (!Persistent)
			return Default(Constants.ShellLayout.ReasonNotPersistent, migrate: false);

		var saved = _settings.Get<DockedShellLayoutSettings>();
		if (string.IsNullOrWhiteSpace(saved.Layout))
			return Default(Constants.ShellLayout.ReasonNothingSaved, migrate: true);

		if (saved.Version != Constants.DockedShell.LayoutVersion)
			return Default(string.Format(Constants.ShellLayout.ReasonVersionFormat, saved.Version, Constants.DockedShell.LayoutVersion), migrate: true);

		return new ShellLayoutStart(saved.Layout, Constants.DockedShell.DefaultLeftPaneWidth, false, null);
	}

	/// <summary>The default layout after a saved one could not be used; the old tree settings are taken over as for a first start.</summary>
	public ShellLayoutStart DefaultFor(string reason) => Default(reason, Persistent);

	public void Save(string xml)
	{
		if (!Persistent)
			return;

		_settings.Update<DockedShellLayoutSettings>(s =>
		{
			s.Version = Constants.DockedShell.LayoutVersion;
			s.Layout = xml;
		});
	}

	private ShellLayoutStart Default(string reason, bool migrate)
	{
		if (!migrate)
			return new ShellLayoutStart(null, Constants.DockedShell.DefaultLeftPaneWidth, false, reason);

		var width = Math.Max(Constants.ShellLayout.MinLeftPaneWidth, JsonClientStateStore.ClampTreeWidth(_state.State.TreeWidth));
		var collapsed = _settings.Get<ShellLayoutSettings>().TreeCollapsed;
		return new ShellLayoutStart(null, width, collapsed, reason);
	}
}
