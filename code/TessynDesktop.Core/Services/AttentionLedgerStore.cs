using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Reads and writes Attention Ledger sidecar files, one JSON per NodeId (FR.23.3). Writes are atomic
/// (temp file then rename); an unreadable file is moved aside, never overwritten. Not thread-safe by
/// itself — <see cref="AttentionService"/> serialises access.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionLedgerStore
{
	private static readonly ILogger _log = Log.ForContext<AttentionLedgerStore>();

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	private readonly string _rootDir;

	public AttentionLedgerStore() : this(GetDefaultRootDir())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom root directory.</summary>
	public AttentionLedgerStore(string rootDir)
	{
		_rootDir = rootDir;
	}

	private static string GetDefaultRootDir()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.Attention.FolderName);
	}

	/// <summary>
	/// Loads the ledger of <paramref name="nodeId"/>. A missing file is an empty ledger (nothing is created);
	/// an unreadable one is moved aside to <c>&lt;NodeId&gt;.json.bad-&lt;yyyyMMddHHmmss&gt;</c> and an empty ledger returned.
	/// </summary>
	public AttentionLedgerModel Load(string nodeId)
	{
		var path = GetPath(nodeId);
		if (!File.Exists(path))
			return NewLedger(nodeId);

		try
		{
			var ledger = JsonSerializer.Deserialize<AttentionLedgerModel>(File.ReadAllText(path), SerializerOptions);
			if (ledger == null)
				throw new JsonException("Ledger file is empty or null.");

			ledger.NodeId = nodeId;
			return ledger;
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			var badPath = MoveAside(path);
			_log.Warning(ex, "AttentionLedgerStore: unreadable ledger for node {NodeId}, moved to {BadPath}", nodeId, badPath);
			return NewLedger(nodeId);
		}
	}

	/// <summary>Writes the ledger atomically: temp file in the same folder, then rename over the target.</summary>
	public void Save(AttentionLedgerModel ledger)
	{
		Directory.CreateDirectory(_rootDir);

		var path    = GetPath(ledger.NodeId);
		var tmpPath = path + Constants.Attention.LedgerTempExtension;

		File.WriteAllText(tmpPath, JsonSerializer.Serialize(ledger, SerializerOptions));
		File.Move(tmpPath, path, overwrite: true);
	}

	private static AttentionLedgerModel NewLedger(string nodeId) => new() { NodeId = nodeId };

	private string GetPath(string nodeId) =>
		Path.Combine(_rootDir, Path.GetFileName(nodeId) + Constants.Attention.LedgerFileExtension);

	private static string MoveAside(string path)
	{
		var stamp   = DateTime.Now.ToString(Constants.Attention.CorruptTimestampFormat);
		var badPath = path + Constants.Attention.CorruptFileInfix + stamp;
		for (var n = 1; File.Exists(badPath); n++)
			badPath = path + Constants.Attention.CorruptFileInfix + stamp + "-" + n;

		File.Move(path, badPath);
		return badPath;
	}
}
