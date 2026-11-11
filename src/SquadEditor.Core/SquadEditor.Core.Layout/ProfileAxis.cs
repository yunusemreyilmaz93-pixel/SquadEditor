using System.Collections.Generic;

namespace SquadEditor.Core.Layout;

public sealed class ProfileAxis
{
	public string Name { get; init; } = "";

	public List<string> Fields { get; init; } = new List<string>();
}
