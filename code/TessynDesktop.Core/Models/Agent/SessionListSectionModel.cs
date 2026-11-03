namespace TessynDesktop.Models.Agent;

/// <summary>A header-delimited group of rows in a listing page (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionListSectionModel
{
	public required SessionListSectionKind Kind { get; init; }

	/// <summary>Workers sections: the root's directory.</summary>
	public required string DirectoryLabel { get; init; }

	/// <summary>Workers sections: the root's directory.</summary>
	public required string DirectoryPath { get; init; }

	/// <summary>Directory sections: whole directory, before filters.</summary>
	public int TopLevelCount { get; init; }

	/// <summary>Directory sections: whole directory, before filters.</summary>
	public int WorkerCount { get; init; }

	public string? RootShortId { get; init; }

	public string? RootName { get; init; }

	/// <summary>Workers sections: direct workers of the root.</summary>
	public int DirectCount { get; init; }

	/// <summary>Workers sections: all descendants of the root.</summary>
	public int SubtreeCount { get; init; }
}
