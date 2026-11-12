using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Auth;

/// <summary>Server-side "auth epoch": a random value persisted to a file; bumping it invalidates every issued login cookie.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AuthEpochStore
{
	private readonly object _lock = new();
	private readonly string? _path;
	private string _epoch;

	public AuthEpochStore(IOptions<TessynWebOptions> options)
	{
		_path = ResolvePath(options.Value);
		_epoch = Load() ?? NewEpoch();
		Persist();
	}

	public string Current
	{
		get
		{
			lock (_lock)
				return _epoch;
		}
	}

	/// <summary>Replaces the epoch so all existing auth cookies stop validating.</summary>
	public void Bump()
	{
		lock (_lock)
		{
			_epoch = NewEpoch();
			Persist();
		}
	}

	private static string? ResolvePath(TessynWebOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.Auth.EpochPath))
			return options.Auth.EpochPath;

		if (string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
			return null;

		var parent = Directory.GetParent(options.DataProtectionKeysPath.TrimEnd('/', '\\'))?.FullName;
		return parent is null ? null : Path.Combine(parent, Constants.Auth.EpochFileName);
	}

	private string? Load()
	{
		if (_path is null || !File.Exists(_path))
			return null;

		var text = File.ReadAllText(_path).Trim();
		return text.Length == 0 ? null : text;
	}

	private void Persist()
	{
		if (_path is null)
			return;

		var directory = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		var temp = _path + ".tmp";
		File.WriteAllText(temp, _epoch);
		File.Move(temp, _path, overwrite: true);
	}

	private static string NewEpoch()
	{
		return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Constants.Auth.EpochBytes));
	}
}
