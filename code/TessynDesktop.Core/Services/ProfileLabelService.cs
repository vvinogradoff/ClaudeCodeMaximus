using System;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IProfileLabelService" />
/// <remarks>Created by Claude</remarks>
public sealed class ProfileLabelService : IProfileLabelService
{
	private static readonly ILogger _log = Log.ForContext<ProfileLabelService>();

	private readonly IClaudeProfileService _profileService;
	private readonly IAppSettingsService _appSettings;
	private readonly object _gate = new();

	private string? _label;
	private DateTimeOffset _validUntil = DateTimeOffset.MinValue;
	private Task<string>? _inFlight;

	public ProfileLabelService(IClaudeProfileService profileService, IAppSettingsService appSettings)
	{
		_profileService = profileService;
		_appSettings    = appSettings;
	}

	private static string FallbackLabel => Constants.Remote.Profiles.SystemPrefix + Constants.Remote.Profiles.FallbackName;

	/// <inheritdoc />
	public async Task<string> GetDefaultLabelAsync()
	{
		Task<string> pending;
		lock (_gate)
		{
			if (_label != null && DateTimeOffset.UtcNow < _validUntil)
				return _label;

			pending = StartResolveLocked();
		}

		var finished = await Task.WhenAny(pending, Task.Delay(Constants.Remote.Profiles.LabelResolveTimeout));
		return finished == pending ? await pending : FallbackLabel;
	}

	/// <inheritdoc />
	public string GetDefaultLabelCached()
	{
		lock (_gate)
		{
			if (_label != null && DateTimeOffset.UtcNow < _validUntil)
				return _label;

			StartResolveLocked();
			return _label ?? FallbackLabel;
		}
	}

	/// <summary>Starts a resolution unless one is running. The caller holds <see cref="_gate"/>.</summary>
	private Task<string> StartResolveLocked()
	{
		if (_inFlight == null || _inFlight.IsCompleted)
			_inFlight = ResolveAsync();

		return _inFlight;
	}

	private async Task<string> ResolveAsync()
	{
		string label;
		TimeSpan validFor;
		try
		{
			string claudePath;
			lock (_appSettings.SyncRoot)
				claudePath = _appSettings.Settings.ClaudePath;

			var email = await _profileService.GetAccountEmailAsync(claudePath, null);
			if (string.IsNullOrWhiteSpace(email))
			{
				label    = FallbackLabel;
				validFor = Constants.Remote.Profiles.LabelFailureCacheDuration;
			}
			else
			{
				label    = Constants.Remote.Profiles.SystemPrefix + email;
				validFor = Constants.Remote.Profiles.LabelCacheDuration;
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Resolving the Default profile label failed");
			label    = FallbackLabel;
			validFor = Constants.Remote.Profiles.LabelFailureCacheDuration;
		}

		lock (_gate)
		{
			_label      = label;
			_validUntil = DateTimeOffset.UtcNow + validFor;
		}

		return label;
	}
}
