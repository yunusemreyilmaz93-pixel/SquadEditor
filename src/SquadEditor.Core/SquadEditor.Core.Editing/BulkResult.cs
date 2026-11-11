namespace SquadEditor.Core.Editing;

public sealed record BulkResult(int Written, int Failed, string? FirstError);
