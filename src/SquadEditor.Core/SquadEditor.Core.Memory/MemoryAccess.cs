using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SquadEditor.Core.Memory;

public static class MemoryAccess
{
	public static T Read<T>(this IMemory memory, ulong address) where T : unmanaged
	{
		Span<byte> span = stackalloc byte[Unsafe.SizeOf<T>()];
		if (!memory.TryRead(address, span))
		{
			throw new MemoryAccessException(address, span.Length);
		}
		return MemoryMarshal.Read<T>(span);
	}

	public static ulong ReadPointer(this IMemory memory, ulong address)
	{
		return memory.Read<ulong>(address);
	}

	public static ulong ReadNonNullPointer(this IMemory memory, ulong address)
	{
		ulong num = memory.ReadPointer(address);
		if (num == 0L)
		{
			throw new MemoryAccessException(address, 8, CoreText.Get("NullPointer", address));
		}
		return num;
	}

	public static byte[] ReadBytes(this IMemory memory, ulong address, int length)
	{
		byte[] array = new byte[length];
		if (!memory.TryRead(address, array))
		{
			throw new MemoryAccessException(address, length);
		}
		return array;
	}

	public static void WriteBytes(this IMemory memory, ulong address, ReadOnlySpan<byte> data)
	{
		if (!memory.TryWrite(address, data))
		{
			throw new MemoryAccessException(address, data.Length);
		}
	}

	public static ulong AddOffset(ulong address, long offset)
	{
		return address + (ulong)offset;
	}
}
