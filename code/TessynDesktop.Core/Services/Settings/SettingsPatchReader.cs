using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads a <c>settings.update</c> patch into its section's patch DTO and holds the value rules the sections share. The reader rejects every member
/// the DTO does not declare (<see cref="System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow"/>), which is what keeps a patch inside
/// its section's whitelist.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SettingsPatchReader
{
	private static readonly JsonSerializerOptions Strict = new(TdcpJsonOptions.Default)
	{
		UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
	};

	/// <summary>Reads <paramref name="patch"/> as a <typeparamref name="TPatch"/>.</summary>
	/// <exception cref="TdcpInvalidParamsException">It is not an object, names a member the patch type does not have, or a value has the wrong type.</exception>
	public static TPatch Read<TPatch>(JsonNode patch)
		where TPatch : class
	{
		if (patch is not JsonObject)
			throw new TdcpInvalidParamsException("'patch' must be an object");

		try
		{
			return patch.Deserialize<TPatch>(Strict) ?? throw new TdcpInvalidParamsException("'patch' must be an object");
		}
		catch (JsonException ex)
		{
			throw new TdcpInvalidParamsException("Invalid patch: " + FirstLine(ex.Message));
		}
	}

	/// <summary>A required, trimmed text of at most <see cref="Constants.SettingsApi.MaxTextLength"/> characters.</summary>
	public static string RequireText(string name, string value)
	{
		var text = value.Trim();
		if (text.Length == 0)
			throw new TdcpInvalidParamsException($"'{name}' must not be empty");

		return LimitLength(name, text);
	}

	/// <summary>A trimmed text that may be empty.</summary>
	public static string OptionalText(string name, string value) => LimitLength(name, value.Trim());

	/// <summary>A rooted (absolute) path, or an empty one when <paramref name="allowEmpty"/>.</summary>
	public static string AbsolutePath(string name, string value, bool allowEmpty)
	{
		var text = allowEmpty ? OptionalText(name, value) : RequireText(name, value);
		if (text.Length == 0)
			return text;

		if (text.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 || !System.IO.Path.IsPathRooted(text))
			throw new TdcpInvalidParamsException($"'{name}' must be an absolute path");

		return text;
	}

	/// <summary>An absolute path to a .wav file (FR.22.4 lets the sound be any file, not only one of the Windows library).</summary>
	public static string WavFile(string name, string value)
	{
		var path = AbsolutePath(name, value, allowEmpty: false);
		if (!path.EndsWith(Constants.SettingsApi.WavExtension, StringComparison.OrdinalIgnoreCase))
			throw new TdcpInvalidParamsException($"'{name}' must be a .wav file");

		return path;
	}

	/// <summary>An absolute http or https URL, or an empty one when <paramref name="allowEmpty"/>.</summary>
	public static string HttpUrl(string name, string value, bool allowEmpty)
	{
		var text = allowEmpty ? OptionalText(name, value) : RequireText(name, value);
		if (text.Length == 0)
			return text;

		if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			throw new TdcpInvalidParamsException($"'{name}' must be an http or https URL");

		return text;
	}

	/// <summary>A whole number within the range.</summary>
	public static T InRange<T>(string name, T value, T min, T max)
		where T : IComparable<T>
	{
		if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
			throw new TdcpInvalidParamsException($"'{name}' must be between {min} and {max}");

		return value;
	}

	/// <summary>A list within <see cref="Constants.SettingsApi.MaxListEntries"/>.</summary>
	public static IReadOnlyList<T> LimitedList<T>(string name, IReadOnlyList<T> value)
	{
		if (value.Count > Constants.SettingsApi.MaxListEntries)
			throw new TdcpInvalidParamsException($"'{name}' holds at most {Constants.SettingsApi.MaxListEntries} entries");

		return value;
	}

	/// <summary>Maps a wire enum onto the model's, rejecting a number that is no member.</summary>
	public static TModel EnumValue<TWire, TModel>(string name, TWire value)
		where TWire : struct, Enum
		where TModel : struct, Enum
	{
		if (!Enum.IsDefined(value))
			throw new TdcpInvalidParamsException($"'{name}' is not a known value");

		return (TModel)Enum.ToObject(typeof(TModel), Convert.ToInt32(value));
	}

	private static string LimitLength(string name, string text) =>
		text.Length > Constants.SettingsApi.MaxTextLength
			? throw new TdcpInvalidParamsException($"'{name}' is longer than {Constants.SettingsApi.MaxTextLength} characters")
			: text;

	private static string FirstLine(string message) => message.Split('\n', 2).First().Trim();
}
