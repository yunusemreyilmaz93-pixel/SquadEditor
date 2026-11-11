using System;

namespace SquadEditor.Core.Memory;

public sealed class MemoryAccessException(ulong address, int length, string? reason = null) : Exception(reason ?? CoreText.Get("ReadFailed", address, length))
{
	public ulong Address { get; } = address;
}
