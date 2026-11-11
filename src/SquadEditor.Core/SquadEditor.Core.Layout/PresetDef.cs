using System.Collections.Generic;

namespace SquadEditor.Core.Layout;

public sealed class PresetDef
{
	public string Name { get; init; } = "";

	public string Entity { get; init; } = "";

	public string? Description { get; init; }

	public List<PresetAction> Actions { get; init; } = new List<PresetAction>();
}
