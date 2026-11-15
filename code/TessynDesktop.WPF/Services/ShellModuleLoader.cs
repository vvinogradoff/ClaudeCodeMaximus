using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>Runs every registered <see cref="IShellModule"/> once; a failing module is logged and skipped so it cannot keep the window from opening.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellModuleLoader
{
	private readonly IEnumerable<IShellModule> _modules;
	private readonly IShellSlots _slots;
	private readonly IShellCommands _commands;
	private readonly ILogger<ShellModuleLoader> _logger;

	public ShellModuleLoader(IEnumerable<IShellModule> modules, IShellSlots slots, IShellCommands commands, ILogger<ShellModuleLoader> logger)
	{
		_modules = modules;
		_slots = slots;
		_commands = commands;
		_logger = logger;
	}

	public void Load()
	{
		foreach (var module in _modules)
		{
			try
			{
				module.Register(_slots, _commands);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Shell module {Module} failed to register", module.GetType().Name);
			}
		}
	}
}
