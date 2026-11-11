using System.Collections.Generic;

namespace SquadEditor.Core.Layout;

public sealed class TabDef
{
	public string Name { get; init; } = "";

	public List<string> Groups { get; init; } = new List<string>();
}
