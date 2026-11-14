using System;
using System.IO;
using System.Text;

namespace TessynDesktop.Services;

/// <summary>
/// The sidecar layout (little-endian): header (magic, format version, file id, next ordinal, record count, indexed length,
/// last-write ticks, content version, fingerprint, last profile / model / effort as optional strings), then one 28-byte record per
/// entry (ordinal, offset, length, timestamp ticks, role, flags, two reserved bytes), then an 8-byte hash of everything before it.
/// Anything that does not read back cleanly is reported as null and the index is rebuilt from the session file.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class EntryIndexFileCodec
{
	private const ulong OffsetBasis = 14695981039346656037UL;
	private const ulong Prime = 1099511628211UL;
	private const int ReservedBytes = 2;

	public byte[] Write(EntryIndexSnapshot snapshot)
	{
		using var memory = new MemoryStream();
		using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
		{
			writer.Write(Constants.ContentStore.Magic);
			writer.Write(Constants.ContentStore.FormatVersion);
			writer.Write(snapshot.FileId.ToByteArray());
			writer.Write(snapshot.NextOrdinal);
			writer.Write(snapshot.Count);
			writer.Write(snapshot.IndexedLength);
			writer.Write(snapshot.LastWriteTicks);
			writer.Write(snapshot.ContentVersion);
			writer.Write(snapshot.Fingerprint);
			WriteOptional(writer, snapshot.LastProfileName);
			WriteOptional(writer, snapshot.LastModelId);
			WriteOptional(writer, snapshot.LastEffort);

			for (var i = 0; i < snapshot.Count; i++)
			{
				var record = snapshot.Records[i];
				writer.Write(record.Ordinal);
				writer.Write(record.Offset);
				writer.Write(record.Length);
				writer.Write(record.TimestampTicks);
				writer.Write((byte)record.Role);
				writer.Write((byte)record.Flags);
				writer.Write(new byte[ReservedBytes]);
			}
		}

		var payload = memory.ToArray();
		var result  = new byte[payload.Length + sizeof(ulong)];
		payload.CopyTo(result, 0);
		BitConverter.TryWriteBytes(result.AsSpan(payload.Length), Hash(payload, payload.Length));
		return result;
	}

	/// <summary>The snapshot stored in <paramref name="bytes"/>, or null when they are not a valid index of this format version.</summary>
	public EntryIndexSnapshot? TryRead(byte[] bytes)
	{
		try
		{
			if (bytes.Length < sizeof(ulong))
				return null;

			var payloadLength = bytes.Length - sizeof(ulong);
			if (BitConverter.ToUInt64(bytes, payloadLength) != Hash(bytes, payloadLength))
				return null;

			using var reader = new BinaryReader(new MemoryStream(bytes, 0, payloadLength), Encoding.UTF8);
			if (reader.ReadUInt32() != Constants.ContentStore.Magic || reader.ReadInt32() != Constants.ContentStore.FormatVersion)
				return null;

			var fileId         = new Guid(reader.ReadBytes(16));
			var nextOrdinal    = reader.ReadInt32();
			var count          = reader.ReadInt32();
			var indexedLength  = reader.ReadInt64();
			var lastWriteTicks = reader.ReadInt64();
			var contentVersion = reader.ReadInt64();
			var fingerprint    = reader.ReadUInt64();
			var profile        = ReadOptional(reader);
			var model          = ReadOptional(reader);
			var effort         = ReadOptional(reader);

			if (count < 0 || nextOrdinal != count || indexedLength < 0)
				return null;

			var records = new EntryIndexRecord[count];
			var previousEnd = 0L;
			for (var i = 0; i < count; i++)
			{
				var ordinal   = reader.ReadInt32();
				var offset    = reader.ReadInt64();
				var length    = reader.ReadInt32();
				var timestamp = reader.ReadInt64();
				var role      = (EntryRoleCode)reader.ReadByte();
				var flags     = (EntryIndexFlags)reader.ReadByte();
				reader.ReadBytes(ReservedBytes);

				// Ordinals are dense and records tile the file in order, or the sidecar is not one we wrote.
				if (ordinal != i || offset < previousEnd || length < 0 || offset + length > indexedLength)
					return null;

				previousEnd = offset + length;
				records[i]  = new EntryIndexRecord(ordinal, offset, length, timestamp, role, flags);
			}

			return new EntryIndexSnapshot
			{
				FileId          = fileId,
				Records         = records,
				Count           = count,
				IndexedLength   = indexedLength,
				LastWriteTicks  = lastWriteTicks,
				ContentVersion  = contentVersion,
				Fingerprint     = fingerprint,
				LastProfileName = profile,
				LastModelId     = model,
				LastEffort      = effort,
			};
		}
		catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentException or OverflowException)
		{
			return null;
		}
	}

	private static void WriteOptional(BinaryWriter writer, string? value)
	{
		writer.Write(value != null);
		if (value != null)
			writer.Write(value);
	}

	private static string? ReadOptional(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

	private static ulong Hash(byte[] bytes, int length)
	{
		var hash = OffsetBasis;
		for (var i = 0; i < length; i++)
			hash = (hash ^ bytes[i]) * Prime;

		return hash;
	}
}
