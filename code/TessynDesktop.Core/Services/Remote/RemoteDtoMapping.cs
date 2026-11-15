using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Maps Core models to the wire DTOs of <c>TessynDesktop.Protocol</c> (the DTO assembly cannot see Core models).</summary>
/// <remarks>Created by Claude</remarks>
public static class RemoteDtoMapping
{
	public static AttentionItemDto ToDto(this AttentionItemModel model)
	{
		return new AttentionItemDto
		{
			Id             = model.Id,
			Number         = model.Number,
			Kind           = model.Kind,
			Title          = model.Title,
			Body           = string.IsNullOrEmpty(model.Body) ? null : model.Body,
			Options        = [.. model.Options],
			Recommendation = model.Recommendation,
			Status         = model.Status,
			Answer         = model.Answer,
			ResolutionNote = model.ResolutionNote,
			CreatedUtc     = model.CreatedUtc,
			UpdatedUtc     = model.UpdatedUtc,
			ClosedUtc      = model.ClosedUtc,
			Refs           = model.Refs.Count == 0 ? null : [.. model.Refs.Select(r => r.ToDto())],
		};
	}

	/// <summary>The wire form of a reference: no absolute path.</summary>
	public static AttentionRefDto ToDto(this AttentionRefModel model) => new()
	{
		Kind     = model.Kind,
		Label    = model.Label,
		NodeId   = model.NodeId,
		FileId   = model.FileId,
		EntryId  = model.EntryId,
		Offset   = model.Offset,
		Length   = model.Length,
		RefId    = model.RefId,
		Name     = model.Name,
		Path     = model.RelativePath,
		Size     = model.Size,
		Mime     = model.Mime,
		MtimeUtc = model.MtimeUtc,
		Url      = model.Url,
	};

	public static AttentionSummaryDto ToDto(this AttentionSummaryModel model)
	{
		return new AttentionSummaryDto
		{
			OpenCount      = model.OpenCount,
			TopKind        = model.TopKind,
			AllOpenCount   = model.AllOpenCount,
			AllOpenTopKind = model.AllOpenTopKind,
		};
	}

	public static SessionTagDto ToDto(this SessionTagModel model)
	{
		return new SessionTagDto
		{
			Kind     = model.Kind,
			Label    = model.Label,
			Url      = model.Url,
			Source   = model.Source,
			AddedUtc = model.AddedUtc,
		};
	}

	/// <summary>Maps <paramref name="model"/>; the content is cut to <paramref name="maxContentChars"/> characters when longer.</summary>
	public static SessionEntryDto ToDto(this SessionEntryModel model, int index, int maxContentChars)
	{
		var cut = model.Content.Length > maxContentChars;
		return new SessionEntryDto
		{
			Index       = index,
			Timestamp   = model.Timestamp,
			Role        = model.Role.ToLowerInvariant(),
			ProfileName = model.ProfileName,
			ModelId     = model.ModelId,
			Effort      = model.Effort,
			Source      = model.Source,
			Content     = cut ? model.Content[..maxContentChars] : model.Content,
			Truncated   = cut ? true : null,
			FullLength  = cut ? model.Content.Length : null,
		};
	}
}
