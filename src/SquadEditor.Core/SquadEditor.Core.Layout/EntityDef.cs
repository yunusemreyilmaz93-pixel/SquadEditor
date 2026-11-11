using System.Collections.Generic;

namespace SquadEditor.Core.Layout;

public sealed class EntityDef
{
	public string Name { get; init; } = "";

	public string DisplayField { get; init; } = "";

	public List<string> HeaderFields { get; init; } = new List<string>();

	public List<string> ListFields { get; init; } = new List<string>();

	public List<string> TableFields { get; init; } = new List<string>();

	public List<ProfileAxis> Profile { get; init; } = new List<ProfileAxis>();

	public List<TabDef> Tabs { get; init; } = new List<TabDef>();

	public List<FieldDef> Fields { get; init; } = new List<FieldDef>();
}
