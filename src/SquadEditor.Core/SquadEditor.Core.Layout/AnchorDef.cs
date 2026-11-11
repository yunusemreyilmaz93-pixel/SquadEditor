using System.Collections.Generic;

namespace SquadEditor.Core.Layout;

public sealed class AnchorDef
{
	public string Name { get; init; } = "";

	public string Pattern { get; init; } = "";

	public AnchorScope Scope { get; init; }

	public AnchorResolve Resolve { get; init; }

	public long Offset { get; init; }

	public int InstructionEnd { get; init; }

	public List<long> PointerChain { get; init; } = new List<long>();

	public bool ExpectUnique { get; init; } = true;

	/// <summary>MSVC RTTI type name (e.g. ".?AVACTUAL_PLAYER@db@@") for <see cref="AnchorResolve.RttiVtable"/>.</summary>
	public string? TypeName { get; init; }

	/// <summary>Which vtable of the class: its offset inside the object (0 = primary). Objects are then found at that offset.</summary>
	public uint ColOffset { get; init; }
}
