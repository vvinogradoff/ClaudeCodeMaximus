namespace TessynDesktop.WPF.Services;

/// <summary>
/// A package's entry into the shell: registers its slot views, commands and menu entries once at startup, before the main window is
/// created. Register it with one line in <c>ServiceRegistration</c>: <c>services.AddSingleton&lt;IShellModule, MyModule&gt;()</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IShellModule
{
	void Register(IShellSlots slots, IShellCommands commands);
}
