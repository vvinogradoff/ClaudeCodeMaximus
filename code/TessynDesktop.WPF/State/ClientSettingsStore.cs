using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.State;

/// <summary><see cref="IClientSettingsStore"/> over the <c>Sections</c> of the client state file.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientSettingsStore : IClientSettingsStore
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly IClientStateStore _state;
	private readonly ILogger<ClientSettingsStore> _logger;

	public ClientSettingsStore(IClientStateStore state, ILogger<ClientSettingsStore> logger)
	{
		_state = state;
		_logger = logger;
	}

	public event EventHandler<ClientSettingsChangedEventArgs>? Changed;

	public T Get<T>() where T : class, new()
	{
		var section = Describe<T>();
		if (!_state.State.Sections.TryGetValue(section.Name, out var stored))
			return new T();

		if (stored.Version > section.Version)
		{
			_logger.LogWarning("Settings section {Section} is version {Stored}, newer than {Known}; using defaults", section.Name, stored.Version, section.Version);
			return new T();
		}

		try
		{
			return stored.Data.Deserialize<T>(Json) ?? new T();
		}
		catch (JsonException ex)
		{
			_logger.LogWarning(ex, "Settings section {Section} is unreadable; using defaults", section.Name);
			return new T();
		}
	}

	public void Update<T>(Action<T> change) where T : class, new()
	{
		var section = Describe<T>();
		var value = Get<T>();
		change(value);
		var model = new ClientSectionModel { Version = section.Version, Data = JsonSerializer.SerializeToElement(value, Json) };
		_state.Update(state => state.Sections[section.Name] = model);
		Changed?.Invoke(this, new ClientSettingsChangedEventArgs(section.Name));
	}

	private static ClientSettingsSectionAttribute Describe<T>() =>
		typeof(T).GetCustomAttribute<ClientSettingsSectionAttribute>()
		?? throw new InvalidOperationException($"{typeof(T).Name} has no {nameof(ClientSettingsSectionAttribute)}");
}
