namespace SquadEditor.Core.Memory;

public readonly record struct MemoryRegion(ulong Start, ulong Size, bool IsWritable)
{
	public ulong End => Start + Size;
}
