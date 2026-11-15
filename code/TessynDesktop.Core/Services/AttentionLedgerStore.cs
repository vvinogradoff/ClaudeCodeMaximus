using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads and writes Attention Ledger sidecar files, one JSON per NodeId (FR.23.3), through
/// <see cref="JsonSidecarFile"/> (atomic write, corrupt file moved aside). Not thread-safe by
/// itself; <see cref="AttentionService"/> serialises access.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionLedgerStore
{
	private readonly JsonSidecarFile _file = new();
	private readonly string _rootDir;

	public AttentionLedgerStore() : this(GetDefaultRootDir())
	{
	}

	/// <summary>Constructor for testing: allows injecting a custom root directory.</summary>
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
		var ledger = _file.Read<AttentionLedgerModel>(GetPath(nodeId)) ?? new AttentionLedgerModel();
		ledger.NodeId = nodeId;
		return ledger;
	}

	/// <summary>Writes the ledger atomically: temp file in the same folder, then rename over the target.</summary>
	public void Save(AttentionLedgerModel ledger) => _file.Write(GetPath(ledger.NodeId), ledger);

	/// <summary>The NodeIds that have a ledger file.</summary>
	public IReadOnlyList<string> EnumerateNodeIds()
	{
		if (!Directory.Exists(_rootDir))
			return [];

		return Directory.EnumerateFiles(_rootDir, "*" + Constants.Attention.LedgerFileExtension)
			.Select(Path.GetFileNameWithoutExtension)
			.Where(n => !string.IsNullOrEmpty(n))
			.Select(n => n!)
			.ToList();
	}

	private string GetPath(string nodeId) =>
		Path.Combine(_rootDir, Path.GetFileName(nodeId) + Constants.Attention.LedgerFileExtension);
}
