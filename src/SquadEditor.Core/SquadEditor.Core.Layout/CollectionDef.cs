namespace SquadEditor.Core.Layout;

public sealed class CollectionDef
{
	public string Name { get; init; } = "";

	public string Entity { get; init; } = "";

	public string Anchor { get; init; } = "";

	public long CountOffset { get; init; }

	public long? ItemsOffset { get; init; }

	public long ItemsDataOffset { get; init; }

	public long Stride { get; init; } = 8L;

	public bool ItemIsPointer { get; init; } = true;

	public int MaxCount { get; init; } = 500000;

	/// <summary>Items are found by scanning writable memory for objects whose first 8 bytes equal the anchor address (a vtable).</summary>
	public bool Scan { get; init; }
}
