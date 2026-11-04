using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Atomic JSON sidecar file access shared by the Attention Ledger store (FR.23.3) and the Session Tag
/// store (FR.24.5): a write goes to a temp file and is renamed over the target; an unreadable file is
/// moved aside to <c>&lt;file&gt;.bad-&lt;yyyyMMddHHmmss&gt;</c> with a warning, never overwritten.
/// Not thread-safe by itself; the owning service serialises access.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonSidecarFile
{
	private static readonly ILogger _log = Log.ForContext<JsonSidecarFile>();

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	/// <summary>Reads <paramref name="path"/>. Returns null when the file is missing (nothing is created) or was unreadable (moved aside).</summary>
	public T? Read<T>(string path) where T : class
	{
		if (!File.Exists(path))
			return null;

		try
		{
			return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions)
			       ?? throw new JsonException("Sidecar file is empty or null.");
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			var badPath = MoveAside(path);
			_log.Warning(ex, "JsonSidecarFile: unreadable file {Path}, moved to {BadPath}", path, badPath);
			return null;
		}
	}

	/// <summary>Writes atomically: temp file in the same folder, then rename over the target. Creates the folder.</summary>
	public void Write<T>(string path, T value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		var tmpPath = path + Constants.Sidecar.TempExtension;
		File.WriteAllText(tmpPath, JsonSerializer.Serialize(value, SerializerOptions));
		File.Move(tmpPath, path, overwrite: true);
	}

	private static string MoveAside(string path)
	{
		var stamp   = DateTime.Now.ToString(Constants.Sidecar.CorruptTimestampFormat);
		var badPath = path + Constants.Sidecar.CorruptFileInfix + stamp;
		for (var n = 1; File.Exists(badPath); n++)
			badPath = path + Constants.Sidecar.CorruptFileInfix + stamp + "-" + n;

		File.Move(path, badPath);
		return badPath;
	}
}
