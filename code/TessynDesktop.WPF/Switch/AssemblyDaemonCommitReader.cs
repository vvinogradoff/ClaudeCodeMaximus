using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// Reads <c>AssemblyInformationalVersionAttribute</c> (<c>1.0.0+&lt;commit&gt;</c>) from the assembly's metadata without loading it: the running daemon's file stays
/// untouched and nothing enters this process.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AssemblyDaemonCommitReader : IDaemonCommitReader
{
	private const ushort AttributeBlobProlog = 1;

	public string? ReadCommit(string folder)
	{
		try
		{
			var path = Path.Combine(folder, Constants.Switch.HostAssemblyName);
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var pe = new PEReader(stream);
			if (!pe.HasMetadata)
				return null;

			var reader = pe.GetMetadataReader();
			foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
			{
				var attribute = reader.GetCustomAttribute(handle);
				if (!IsInformationalVersion(reader, attribute.Constructor))
					continue;

				var blob = reader.GetBlobReader(attribute.Value);
				if (blob.ReadUInt16() != AttributeBlobProlog)
					return null;

				return CommitOf(blob.ReadSerializedString());
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
		{
		}

		return null;
	}

	/// <summary>The hex commit after the <c>+</c> of a version such as <c>1.0.0+13fd7d07...</c>; null when there is none.</summary>
	public static string? CommitOf(string? version)
	{
		if (string.IsNullOrWhiteSpace(version))
			return null;

		var at = version.IndexOf(Constants.Switch.CommitSeparator);
		if (at < 0)
			return null;

		var commit = version[(at + 1)..].Trim();
		return commit.Length >= Constants.Switch.MinimumCommitLength && commit.All(Uri.IsHexDigit) ? commit.ToLowerInvariant() : null;
	}

	private static bool IsInformationalVersion(MetadataReader reader, EntityHandle constructor)
	{
		if (constructor.Kind != HandleKind.MemberReference)
			return false;

		var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
		if (parent.Kind != HandleKind.TypeReference)
			return false;

		var type = reader.GetTypeReference((TypeReferenceHandle)parent);
		return reader.GetString(type.Name) == Constants.Switch.InformationalVersionAttributeName
			&& reader.GetString(type.Namespace) == Constants.Switch.ReflectionNamespace;
	}
}
