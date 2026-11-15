namespace TessynDesktop.WPF.Services;

/// <summary>
/// A package's entry into the shell: registers its slot views, commands and menu entries once at startup, before the main window is
/// created; the registrations commit only if <see cref="Register"/> completes, so a module that throws registers nothing. Register it with one line in <c>ServiceRegistration</c>: <c>services.AddSingleton&lt;IShellModule, MyModule&gt;()</c>.
/// </summary>
/// <remarks>
/// A slot view's <c>Create</c> runs once per host when the host loads; it is not recreated when the user switches sessions, so keep per-session
/// state in the host's DataContext (e.g. <c>SessionPaneViewModel</c>) and let the view follow it. Create views as transient and not
/// <c>IDisposable</c> (the root provider would hold them). A failing or null-returning <c>Create</c> is logged and skipped. A module added after startup never shows.
/// Created by Claude</remarks>
public interface IShellModule
{
	void Register(IShellSlots slots, IShellCommands commands);
}
