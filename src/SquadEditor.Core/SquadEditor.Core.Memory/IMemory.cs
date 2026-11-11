using System;
using System.Collections.Generic;

namespace SquadEditor.Core.Memory;

public interface IMemory
{
	bool TryRead(ulong address, Span<byte> buffer);

	bool TryWrite(ulong address, ReadOnlySpan<byte> data);

	IReadOnlyList<MemoryRegion> GetRegions();

	bool TryGetModule(string name, out MemoryRegion module);
}
