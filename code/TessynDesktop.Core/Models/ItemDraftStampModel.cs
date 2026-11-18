namespace TessynDesktop.Models;

/// <summary>A Focus item's reply draft file: the item id and the file's (length, last write time) stamp. Read from the directory listing, never from the content.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ItemDraftStampModel(string ItemId, SessionFileStampModel Stamp);
