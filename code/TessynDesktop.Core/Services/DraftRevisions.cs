using System;
using System.Security.Cryptography;
using System.Text;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The revision of a draft as the wire shows it (<c>draft.get</c>, <c>draft.set</c>, <c>draft.changed</c>): a short hash of the text, so two
/// readers of the same text agree on it and a write of an identical text is no conflict. No draft is <see cref="RemoteProtocol.Composer.EmptyDraftRevision"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DraftRevisions
{
	private const int HexLength = 16;

	/// <summary>The revision of <paramref name="text"/>; null or empty means there is no draft.</summary>
	public string Of(string? text)
	{
		if (string.IsNullOrEmpty(text))
			return RemoteProtocol.Composer.EmptyDraftRevision;

		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
		return Convert.ToHexString(hash)[..HexLength].ToLowerInvariant();
	}
}
