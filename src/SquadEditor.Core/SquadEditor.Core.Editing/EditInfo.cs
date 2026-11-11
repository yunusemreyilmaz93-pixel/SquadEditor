namespace SquadEditor.Core.Editing;

public sealed record EditInfo(string Entity, string Field, string OldValue, string NewValue);
