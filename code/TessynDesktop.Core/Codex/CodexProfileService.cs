using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Codex;

/// <inheritdoc cref="ICodexProfileService" />
/// <remarks>
/// Stored profiles live in <c>AppSettingsModel.CodexProfiles</c> and in their folders under <c>%AppData%\TessynDesktop\codex-profiles\</c>; their
/// hosts start with <c>-c cli_auth_credentials_store="file"</c> (the pool's host does that for every home it is given). Codex Default is listed
/// always, and recorded in the settings once its account was read. <c>account/updated</c> with <c>authMode: null</c> on any host sets that
/// profile's auth-error state (FR.33.13); the <c>workspaceRoutingReady</c> update that comes at every turn start is not an auth change.
/// Created by Claude.
/// </remarks>
public sealed class CodexProfileService : ICodexProfileService
{
	private static readonly ILogger _log = Log.ForContext<CodexProfileService>();

	private readonly CodexHostPool _pool;
	private readonly IAppSettingsService _appSettings;
	private readonly string _storedRoot;
	private readonly string _defaultHome;
	private readonly CodexProfileNaming _naming = new();
	private readonly ConcurrentDictionary<string, bool> _authErrors = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, bool> _signedIn = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, PendingSignIn> _signIns = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, CodexSignInOutcomeModel> _outcomes = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<CodexAppServerHost, byte> _watched = new();

	// FR.33.5: new stored ids are allocated under this lock and reserved until their sign-in ends, so two sign-ins never share an id.
	private readonly object _idLock = new();
	private readonly HashSet<string> _reservedIds = new(StringComparer.OrdinalIgnoreCase);

	/// <param name="storedRoot">Where the stored profiles' folders are; the data root's <c>codex-profiles</c> when null.</param>
	/// <param name="defaultHome">Codex Default's home; <c>%USERPROFILE%\.codex</c> when null.</param>
	public CodexProfileService(CodexHostPool pool, IAppSettingsService appSettings, string? storedRoot = null, string? defaultHome = null)
	{
		_pool        = pool;
		_appSettings = appSettings;
		_storedRoot  = storedRoot ?? Path.Combine(Constants.DataRoot, Constants.Codex.Profiles.StoredFolderName);
		_defaultHome = defaultHome ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.Codex.Launch.DefaultHomeFolderName);

		_pool.HostCreated += Watch;
		foreach (var host in _pool.Hosts)
			Watch(host);
	}

	/// <inheritdoc />
	public event Action<string, bool>? AuthErrorChanged;

	/// <inheritdoc />
	public event Action<string>? SignedIn;

	/// <inheritdoc />
	public IReadOnlyList<CodexProfileInfoModel> List()
	{
		CodexProfileModel[] stored;
		lock (_appSettings.SyncRoot)
			stored = [.. _appSettings.Settings.CodexProfiles];

		var defaultRecord = stored.FirstOrDefault(p => IsDefault(p.ProfileId));
		var result = new List<CodexProfileInfoModel>
		{
			Info(Constants.Codex.Profiles.DefaultProfileId, defaultRecord, _defaultHome, isDefault: true),
		};
		result.AddRange(stored
			.Where(p => !IsDefault(p.ProfileId))
			.OrderBy(p => StoredNumber(p.ProfileId))
			.Select(p => Info(p.ProfileId, p, StoredHome(p.ProfileId), isDefault: false)));
		return result;
	}

	/// <inheritdoc />
	public CodexProfileRef? Find(string profileId)
	{
		if (IsDefault(profileId))
			return new CodexProfileRef(Constants.Codex.Profiles.DefaultProfileId, CodexHome: null);

		var stored = Stored(profileId);
		return stored is null ? null : new CodexProfileRef(stored.ProfileId, StoredHome(stored.ProfileId));
	}

	/// <inheritdoc />
	public string? DisplayNameOf(string profileId)
	{
		if (IsDefault(profileId))
		{
			var record = Stored(Constants.Codex.Profiles.DefaultProfileId);
			return _naming.DisplayName(string.IsNullOrEmpty(record?.Email) ? Constants.Codex.Profiles.DefaultUnknownEmail : record.Email);
		}

		var stored = Stored(profileId);
		return stored is null ? null : _naming.DisplayName(stored.Email);
	}

	/// <inheritdoc />
	public string HomeOf(CodexProfileRef profile) => profile.CodexHome ?? _defaultHome;

	/// <inheritdoc />
	public async Task<CodexGetAccountResult> ReadAccountAsync(string profileId, CancellationToken cancellationToken = default)
	{
		var profile = Find(profileId) ?? throw new InvalidOperationException(string.Format(Constants.Codex.Profiles.UnknownProfileFormat, profileId));
		var account = await _pool.GetHost(profile).ReadAccountAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

		_signedIn[profile.ProfileId] = account.IsSignedIn;
		if (account.IsSignedIn)
		{
			Record(profile.ProfileId, account.Account!);
			SetAuthError(profile.ProfileId, false);
		}
		else if (account.RequiresOpenaiAuth)
			SetAuthError(profile.ProfileId, true);

		return account;
	}

	/// <inheritdoc />
	public async Task<CodexProfileRef?> ResolveCatalogProfileAsync(CancellationToken cancellationToken = default)
	{
		string? configured;
		lock (_appSettings.SyncRoot)
			configured = _appSettings.Settings.CodexCatalogProfileId;

		var candidates = new List<string>();
		if (!string.IsNullOrEmpty(configured))
			candidates.Add(configured);
		candidates.Add(Constants.Codex.Profiles.DefaultProfileId);
		candidates.AddRange(List().Where(p => !p.IsDefault).Select(p => p.ProfileId));

		foreach (var id in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (Find(id) is not { } profile)
				continue;

			try
			{
				if ((await ReadAccountAsync(id, cancellationToken).ConfigureAwait(false)).IsSignedIn)
					return profile;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_log.Warning(ex, "Could not read the ChatGPT account of Codex profile {Profile}", id);
			}
		}

		return null;
	}

	/// <inheritdoc />
	public async Task<CodexSignInStartModel> BeginSignInAsync(bool deviceCode, string? profileId = null, CancellationToken cancellationToken = default)
	{
		CodexProfileRef profile;
		if (!string.IsNullOrEmpty(profileId))
			profile = Find(profileId) ?? throw new InvalidOperationException(string.Format(Constants.Codex.Profiles.UnknownProfileFormat, profileId));
		else
		{
			var id = ReserveStoredId();
			profile = new CodexProfileRef(id, StoredHome(id));
		}

		CodexAppServerHost host;
		CodexLoginStartResult started;
		try
		{
			// Codex requires an existing CODEX_HOME (FR.33.5).
			if (profile.CodexHome is not null)
				Directory.CreateDirectory(profile.CodexHome);

			host    = _pool.GetHost(profile);
			started = await host.LoginStartAsync(deviceCode, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			ReleaseReservation(profile.ProfileId);
			throw;
		}

		var loginId = started.LoginId ?? Guid.NewGuid().ToString();

		var pending = new PendingSignIn(profile, host);
		_signIns[loginId] = pending;
		_log.Information("ChatGPT sign-in {LoginId} started for Codex profile {Profile} ({Kind})", loginId, profile.ProfileId, deviceCode ? "device code" : "browser");

		return new CodexSignInStartModel(loginId, profile.ProfileId, deviceCode, started.AuthUrl, started.VerificationUrl, started.UserCode);
	}

	/// <inheritdoc />
	public Task<CodexSignInOutcomeModel> WaitForSignInAsync(string loginId, CancellationToken cancellationToken = default)
	{
		if (_signIns.TryGetValue(loginId, out var pending))
			return pending.Outcome.Task.WaitAsync(cancellationToken);

		return _outcomes.TryGetValue(loginId, out var outcome)
			? Task.FromResult(outcome)
			: Task.FromException<CodexSignInOutcomeModel>(new InvalidOperationException(Constants.Codex.Profiles.UnknownLogin));
	}

	/// <inheritdoc />
	public async Task CancelSignInAsync(string loginId, CancellationToken cancellationToken = default)
	{
		if (!_signIns.TryGetValue(loginId, out var pending))
			throw new InvalidOperationException(Constants.Codex.Profiles.UnknownLogin);

		try
		{
			await pending.Host.CancelLoginAsync(loginId, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_log.Warning(ex, "account/login/cancel for sign-in {LoginId} failed", loginId);
		}

		Finish(loginId, new CodexSignInOutcomeModel(false, pending.Profile.ProfileId, null, null, Constants.Codex.Profiles.SignInCancelled));
	}

	/// <inheritdoc />
	public async Task SignOutAsync(string profileId, CancellationToken cancellationToken = default)
	{
		var profile = Find(profileId) ?? throw new InvalidOperationException(string.Format(Constants.Codex.Profiles.UnknownProfileFormat, profileId));
		await _pool.GetHost(profile).LogoutAsync(cancellationToken).ConfigureAwait(false);
		_signedIn[profile.ProfileId] = false;
		_log.Information("Codex profile {Profile} signed out", profile.ProfileId);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
	{
		if (IsDefault(profileId))
			throw new InvalidOperationException(Constants.Codex.Profiles.DefaultCannotBeDeleted);

		var profile = Find(profileId) ?? throw new InvalidOperationException(string.Format(Constants.Codex.Profiles.UnknownProfileFormat, profileId));
		await _pool.StopHostAsync(profile.ProfileId).ConfigureAwait(false);

		var home = profile.CodexHome!;
		if (Directory.Exists(home))
		{
			var removed = home + Constants.Auth.RemovedDirectorySuffix;
			for (var n = 2; Directory.Exists(removed); n++)
				removed = home + Constants.Auth.RemovedDirectorySuffix + n;
			Directory.Move(home, removed);
			_log.Information("Codex profile {Profile} deleted: its folder is now {Folder}", profile.ProfileId, removed);
		}

		lock (_appSettings.SyncRoot)
		{
			_appSettings.Settings.CodexProfiles.RemoveAll(p => string.Equals(p.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
			if (string.Equals(_appSettings.Settings.CodexCatalogProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase))
				_appSettings.Settings.CodexCatalogProfileId = null;
			_appSettings.Save();
		}

		_authErrors.TryRemove(profile.ProfileId, out _);
		_signedIn.TryRemove(profile.ProfileId, out _);
	}

	/// <inheritdoc />
	public bool HasAuthError(string profileId) => _authErrors.TryGetValue(profileId, out var value) && value;

	/// <inheritdoc />
	public void SetAuthError(string profileId, bool hasAuthError)
	{
		var previous = HasAuthError(profileId);
		_authErrors[profileId] = hasAuthError;
		if (previous == hasAuthError)
			return;

		if (hasAuthError)
			_log.Warning("Codex profile {Profile} needs to sign in again", profileId);
		AuthErrorChanged?.Invoke(profileId, hasAuthError);
	}

	private void Watch(CodexAppServerHost host)
	{
		if (_watched.TryAdd(host, 0))
			host.NotificationReceived += notification => OnNotification(host, notification);
	}

	private void OnNotification(CodexAppServerHost host, CodexNotification notification)
	{
		try
		{
			if (notification.Method == Constants.Codex.Methods.AccountLoginCompleted)
				OnLoginCompleted(notification.Params.Deserialize<CodexLoginCompletedNotification>(Constants.Codex.SerializerOptions)!);
			else if (notification.Method == Constants.Codex.Methods.AccountUpdated)
				OnAccountUpdated(host, notification.Params.Deserialize<CodexAccountUpdatedNotification>(Constants.Codex.SerializerOptions)!);
		}
		catch (JsonException ex)
		{
			_log.Warning(ex, "Codex notification {Method} of profile {Profile} could not be read", notification.Method, host.ProfileId);
		}
	}

	private void OnAccountUpdated(CodexAppServerHost host, CodexAccountUpdatedNotification update)
	{
		if (string.Equals(update.Reason, Constants.Codex.WorkspaceRoutingReady, StringComparison.Ordinal))
			return;

		if (update.IsSignedOut)
		{
			_signedIn[host.ProfileId] = false;
			SetAuthError(host.ProfileId, true);
		}
	}

	private void OnLoginCompleted(CodexLoginCompletedNotification completed)
	{
		var loginId = completed.LoginId;
		if (loginId is null || !_signIns.TryGetValue(loginId, out var pending))
			return;

		if (!completed.Success)
		{
			Finish(loginId, new CodexSignInOutcomeModel(false, pending.Profile.ProfileId, null, null,
				string.Format(Constants.Codex.Profiles.SignInFailedFormat, completed.Error ?? "unknown error")));
			return;
		}

		_ = CompleteSignInAsync(loginId, pending);
	}

	private async Task CompleteSignInAsync(string loginId, PendingSignIn pending)
	{
		try
		{
			var account = await pending.Host.ReadAccountAsync(refreshToken: false).ConfigureAwait(false);
			if (!account.IsSignedIn)
			{
				Finish(loginId, new CodexSignInOutcomeModel(false, pending.Profile.ProfileId, null, null,
					string.Format(Constants.Codex.Profiles.SignInFailedFormat, "the account could not be read")));
				return;
			}

			Record(pending.Profile.ProfileId, account.Account!);
			_signedIn[pending.Profile.ProfileId] = true;
			SetAuthError(pending.Profile.ProfileId, false);
			_log.Information("Codex profile {Profile} signed in ({Plan})", pending.Profile.ProfileId, account.Account!.PlanType);
			Finish(loginId, new CodexSignInOutcomeModel(true, pending.Profile.ProfileId, account.Account.Email, account.Account.PlanType, null));
			SignedIn?.Invoke(pending.Profile.ProfileId);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Completing ChatGPT sign-in {LoginId} failed", loginId);
			Finish(loginId, new CodexSignInOutcomeModel(false, pending.Profile.ProfileId, null, null, string.Format(Constants.Codex.Profiles.SignInFailedFormat, ex.Message)));
		}
	}

	private void Finish(string loginId, CodexSignInOutcomeModel outcome)
	{
		_outcomes[loginId] = outcome;
		if (_signIns.TryRemove(loginId, out var pending))
		{
			ReleaseReservation(pending.Profile.ProfileId);
			pending.Outcome.TrySetResult(outcome);
		}
	}

	private void Record(string profileId, CodexAccount account)
	{
		lock (_appSettings.SyncRoot)
		{
			var list   = _appSettings.Settings.CodexProfiles;
			var record = list.FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
			var email  = account.Email ?? string.Empty;
			if (record is not null && record.Email == email && record.PlanType == account.PlanType)
				return;

			if (record is null)
			{
				list.Add(record = new CodexProfileModel { ProfileId = IsDefault(profileId) ? Constants.Codex.Profiles.DefaultProfileId : profileId });
				_appSettings.Settings.CodexLastProfileNumber = Math.Max(_appSettings.Settings.CodexLastProfileNumber, StoredNumber(record.ProfileId));
			}
			record.Email    = email;
			record.PlanType = account.PlanType;
			_appSettings.Save();
		}
	}

	private CodexProfileInfoModel Info(string profileId, CodexProfileModel? record, string home, bool isDefault) =>
		new(profileId, DisplayNameOf(profileId) ?? profileId, record?.Email, record?.PlanType, home, isDefault,
			_signedIn.TryGetValue(profileId, out var signedIn) ? signedIn : record is not null, HasAuthError(profileId));

	private CodexProfileModel? Stored(string profileId)
	{
		lock (_appSettings.SyncRoot)
			return _appSettings.Settings.CodexProfiles.FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>The next free <c>codex_N</c>: above every stored id. A folder left by a failed sign-in is reused.</summary>
	/// <summary>
	/// The next free <c>codex_N</c>, reserved for the caller: above every stored id, every reserved one, every deleted one (a <c>codex_N_rem…</c>
	/// folder) and the highest number ever added (<c>CodexLastProfileNumber</c>), so a deleted profile's id is never given to another account and
	/// its pinned sessions keep failing with the missing-pin error (FR.12.27). A folder left by a failed sign-in, never added, is reused.
	/// </summary>
	private string ReserveStoredId()
	{
		lock (_idLock)
		{
			int max;
			lock (_appSettings.SyncRoot)
				max = Math.Max(_appSettings.Settings.CodexLastProfileNumber,
					_appSettings.Settings.CodexProfiles.Select(p => StoredNumber(p.ProfileId)).DefaultIfEmpty(0).Max());

			max = Math.Max(max, _reservedIds.Select(StoredNumber).DefaultIfEmpty(0).Max());
			max = Math.Max(max, RemovedNumbers().DefaultIfEmpty(0).Max());

			var id = Constants.Codex.Profiles.StoredIdPrefix + (max + 1);
			_reservedIds.Add(id);
			return id;
		}
	}

	private void ReleaseReservation(string profileId)
	{
		lock (_idLock)
			_reservedIds.Remove(profileId);
	}

	/// <summary>The numbers of the deleted profiles' folders (<c>codex_N_rem</c>, <c>codex_N_rem2</c> …).</summary>
	private IEnumerable<int> RemovedNumbers()
	{
		if (!Directory.Exists(_storedRoot))
			yield break;

		foreach (var folder in Directory.EnumerateDirectories(_storedRoot, Constants.Codex.Profiles.StoredIdPrefix + "*" + Constants.Auth.RemovedDirectorySuffix + "*"))
		{
			var name = Path.GetFileName(folder);
			var cut  = name.IndexOf(Constants.Auth.RemovedDirectorySuffix, StringComparison.OrdinalIgnoreCase);
			if (cut > 0 && StoredNumber(name[..cut]) is > 0 and var n)
				yield return n;
		}
	}

	private int StoredNumber(string profileId) =>
		profileId.StartsWith(Constants.Codex.Profiles.StoredIdPrefix, StringComparison.OrdinalIgnoreCase)
		&& int.TryParse(profileId.AsSpan(Constants.Codex.Profiles.StoredIdPrefix.Length), out var n) ? n : 0;

	private string StoredHome(string profileId) => Path.Combine(_storedRoot, profileId);

	private bool IsDefault(string? profileId) => string.Equals(profileId, Constants.Codex.Profiles.DefaultProfileId, StringComparison.OrdinalIgnoreCase);

	/// <summary>A sign-in waiting for <c>account/login/completed</c>.</summary>
	private sealed record PendingSignIn(CodexProfileRef Profile, CodexAppServerHost Host)
	{
		public TaskCompletionSource<CodexSignInOutcomeModel> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
