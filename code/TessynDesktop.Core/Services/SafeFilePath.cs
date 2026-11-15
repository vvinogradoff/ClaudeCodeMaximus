using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TessynDesktop.Services;

/// <summary>A file opened once for a policy decision and the read that follows it; every fact about it comes from the open handle.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class OpenedFile : IDisposable
{
	public OpenedFile(FileStream stream, string finalPath, int linkCount)
	{
		Stream    = stream;
		FinalPath = finalPath;
		LinkCount = linkCount;
	}

	public FileStream Stream { get; }

	/// <summary>The real path of the open file (links, junctions and short names resolved), as a plain <c>X:\...</c> path; empty when it is not one.</summary>
	public string FinalPath { get; }

	/// <summary>Number of hard links to the file; more than one means another name leads to the same content.</summary>
	public int LinkCount { get; }

	public long Length => Stream.Length;

	public DateTimeOffset LastWriteUtc => new(File.GetLastWriteTimeUtc(Stream.SafeFileHandle), TimeSpan.Zero);

	public void Dispose() => Stream.Dispose();
}

/// <summary>
/// The one lexical gate for every path that a Focus reference or a remote file call names, run before any filesystem call: only plain
/// local drive paths (<c>X:\...</c>) pass. UNC, extended (<c>\\?\</c>), device (<c>\\.\</c>) and stream (<c>:</c>) syntax, device names,
/// trailing dots and spaces, and relative paths that leave their base directory are refused, so no path can make the machine touch the
/// network or reach a file by a spelling the deny rules do not know. Windows-only by design (the daemon is).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SafeFilePath
{
	private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
	};

	/// <summary>
	/// The normalised path (drive letter upper-case, single backslashes, <c>.</c> and <c>..</c> resolved), or false with the reason.
	/// A relative <paramref name="raw"/> is resolved under <paramref name="baseDirectory"/> (itself a plain drive path) and must stay inside it.
	/// </summary>
	public static bool TryNormalize(string? raw, string? baseDirectory, out string full, out string reason)
	{
		full   = string.Empty;
		reason = string.Empty;

		var text = raw?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			reason = "the path is empty";
			return false;
		}

		foreach (var c in text)
		{
			if (c < ' ' || c is '<' or '>' or '"' or '|' or '*' or '?')
			{
				reason = text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal)
					? "UNC, extended (\\\\?\\) and device (\\\\.\\) paths are not allowed"
					: "the path contains a character that is not allowed in a file path";
				return false;
			}
		}

		if (text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal))
		{
			reason = "UNC, extended (\\\\?\\) and device (\\\\.\\) paths are not allowed";
			return false;
		}

		var segments = new List<string>();
		int floor;
		char drive;
		string rest;

		if (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':')
		{
			if (text.Length < 3 || text[2] is not ('\\' or '/'))
			{
				reason = "a drive-relative path (C:name) is not allowed - write the full path";
				return false;
			}

			drive = char.ToUpperInvariant(text[0]);
			rest  = text[3..];
			floor = 0;
		}
		else if (text[0] is '\\' or '/')
		{
			reason = "a path without a drive letter is not allowed";
			return false;
		}
		else
		{
			if (string.IsNullOrWhiteSpace(baseDirectory)
			    || !TryNormalize(baseDirectory, null, out var baseFull, out _))
			{
				reason = "a relative path needs a working directory that is a plain local path";
				return false;
			}

			drive = baseFull[0];
			segments.AddRange(baseFull[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries));
			floor = segments.Count;
			rest  = text;
		}

		if (rest.Contains(':'))
		{
			reason = "alternate data streams and device syntax (':') are not allowed";
			return false;
		}

		foreach (var segment in rest.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment == ".")
				continue;

			if (segment == "..")
			{
				if (segments.Count <= floor)
				{
					reason = floor == 0 ? "the path climbs above the drive root" : "the relative path leaves the working directory";
					return false;
				}

				segments.RemoveAt(segments.Count - 1);
				continue;
			}

			if (segment[^1] is '.' or ' ')
			{
				reason = "a name ending in a dot or a space is not allowed";
				return false;
			}

			var stem = segment.Split('.')[0].Trim();
			if (DeviceNames.Contains(stem))
			{
				reason = $"'{stem}' is a reserved device name";
				return false;
			}

			segments.Add(segment);
		}

		full = drive + @":\" + string.Join('\\', segments);
		return true;
	}

	/// <summary>
	/// Opens <paramref name="normalizedPath"/> for reading, once, and reports its final path and hard-link count from the handle. Null
	/// when it does not exist, is not a regular file or cannot be opened. The caller disposes the result and reads from its stream only.
	/// </summary>
	public static OpenedFile? Open(string normalizedPath)
	{
		FileStream stream;
		try
		{
			stream = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
		{
			return null;
		}

		try
		{
			if (!OperatingSystem.IsWindows())
				return new OpenedFile(stream, normalizedPath, 1);

			var handle = stream.SafeFileHandle;
			return new OpenedFile(stream, FinalPathOf(handle), LinkCountOf(handle));
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	/// <summary>True for a plain local drive path as <see cref="TryNormalize"/> would accept it unchanged (the final path of an open file).</summary>
	public static bool IsPlainDrivePath(string path) =>
		TryNormalize(path, null, out var full, out _) && string.Equals(full, path, StringComparison.OrdinalIgnoreCase);

	// ── Win32 ────────────────────────────────────────────────────────────────

	private const int MaxFinalPath = 32_768;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder buffer, uint length, uint flags);

	[StructLayout(LayoutKind.Sequential)]
	private struct ByHandleFileInformation
	{
		public uint FileAttributes;
		public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
		public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
		public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
		public uint VolumeSerialNumber;
		public uint FileSizeHigh;
		public uint FileSizeLow;
		public uint NumberOfLinks;
		public uint FileIndexHigh;
		public uint FileIndexLow;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

	/// <summary>The real path of the handle without the <c>\\?\</c> prefix; empty for anything that is not a plain drive path (a network share, for example).</summary>
	private static string FinalPathOf(SafeFileHandle handle)
	{
		var buffer = new StringBuilder(1024);
		var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
		if (length > buffer.Capacity && length < MaxFinalPath)
		{
			buffer = new StringBuilder((int)length + 1);
			length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
		}

		if (length == 0 || length >= MaxFinalPath)
			return string.Empty;

		var path = buffer.ToString();
		if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
			return string.Empty;

		if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
			path = path[4..];

		return path.Length >= 3 && path[1] == ':' ? char.ToUpperInvariant(path[0]) + path[1..] : string.Empty;
	}

	private static int LinkCountOf(SafeFileHandle handle) =>
		GetFileInformationByHandle(handle, out var info) ? (int)info.NumberOfLinks : int.MaxValue;
}
