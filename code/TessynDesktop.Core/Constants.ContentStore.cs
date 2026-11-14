using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The daemon-owned session content store: per-session sidecar entry index and byte-range reads (P1.E, TR.TDCP.PAGE.*).</summary>
	public static class ContentStore
	{
		/// <summary>Folder under the data root that holds one sidecar index per session file.</summary>
		public const string FolderName = "entry-index";

		/// <summary>Appended to the session file name to name its sidecar (<c>&lt;session&gt;.txt.idx</c>).</summary>
		public const string IndexFileExtension = ".idx";

		/// <summary>Suffix of the temporary file a sidecar is written to before it replaces the old one.</summary>
		public const string TempFileExtension = ".tmp";

		/// <summary>First four bytes of a sidecar: "TXIX" read as a little-endian integer.</summary>
		public const uint Magic = 0x58495854;

		/// <summary>Bumped when the sidecar layout changes; a sidecar of another version is rebuilt.</summary>
		public const int FormatVersion = 1;

		/// <summary>Most session indexes kept in memory; the least recently used is persisted and dropped beyond it.</summary>
		public const int MaxIndexedFiles = 64;

		/// <summary>Read buffer of the header scanner.</summary>
		public const int ScanBufferBytes = 64 * 1024;

		/// <summary>Bytes of a line that starts with "[" the scanner decodes to test it against the header rule; longer lines are body text.</summary>
		public const int HeaderPrefixMaxBytes = 4096;

		/// <summary>Length of the UTF-8 byte order mark a session file starts with.</summary>
		public const int Utf8BomLength = 3;

		/// <summary>Bytes sampled at each fingerprint position (file start, middle entry, last entry).</summary>
		public const int FingerprintSampleBytes = 64;

		/// <summary>Bytes before the last entry's header and after it that the fingerprint covers.</summary>
		public const int FingerprintLastEntryLeadBytes = 64;
		public const int FingerprintLastEntryTailBytes = 128;

		/// <summary>A tail update rewrites its sidecar at most this often; a rebuild always writes at once.</summary>
		public static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(5);

		/// <summary>Times a read re-validates and retries after it finds the index disagreeing with the file.</summary>
		public const int MaxReadRetries = 2;

		/// <summary>Default byte budget of one page of entries.</summary>
		public const int DefaultPageMaxBytes = 256 * 1024;

		/// <summary>Default cap of one entry within a page, header line included; a longer entry is cut and fetched on by range.</summary>
		public const int DefaultEntryMaxBytes = 64 * 1024;

		/// <summary>Default cap of one range read.</summary>
		public const int DefaultRangeMaxBytes = 64 * 1024;
	}
}
