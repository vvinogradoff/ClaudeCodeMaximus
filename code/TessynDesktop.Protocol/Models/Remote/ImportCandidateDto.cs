using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A Claude session that can be imported (<c>import.list</c>).</summary>
/// <param name="ExternalId">The Claude session id.</param>
/// <param name="Title">The cached title; null until one exists (see <c>import.titles</c>).</param>
/// <param name="FirstPrompt">The first user prompt, cut short.</param>
/// <param name="LastModified">The last time the session was used.</param>
/// <param name="EntryCount">The number of messages.</param>
/// <param name="Empty">The session has no message and cannot be imported.</param>
/// <param name="AlreadyImported">The session is already in the tree.</param>
/// <param name="ProjectSlug">The Claude project the session lives under.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ImportCandidateDto(string ExternalId, string? Title, string? FirstPrompt, DateTimeOffset LastModified, int EntryCount, bool Empty, bool AlreadyImported, string ProjectSlug);
