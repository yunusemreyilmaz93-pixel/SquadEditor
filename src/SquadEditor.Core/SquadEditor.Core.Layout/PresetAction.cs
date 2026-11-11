namespace SquadEditor.Core.Layout;

public sealed class PresetAction
{
	public string? Field { get; init; }

	public string? Group { get; init; }

	public string Value { get; init; } = "";

	public double? Min { get; init; }

	public double? Max { get; init; }
}
