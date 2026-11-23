using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>
/// One of the game's object pools: statics of game_plugin.dll that hand out slots of one size
/// from chunks (layout from FMLibreEditor's FM27 notes, checked on our save):
/// +0x00 (begin, end, capacity) of chunk pointers, +0x18 first free slot (a free slot's first
/// 8 bytes point at the next, the last's at null), +0x20 u32 free, +0x24 u32 used, +0x28 u32
/// slot size, +0x2C u32 slots of the first chunk, +0x30 u32 slots of each later chunk.
/// Name table entries (24 bytes) and contracts (FULL_CONTRACT, 200 bytes) live in such pools.
/// </summary>
public sealed class GamePool
{
	private GamePool(ulong address, uint slotSize, List<(ulong Start, ulong End)> chunks, ulong firstFree, uint free, uint used)
	{
		Address = address;
		SlotSize = slotSize;
		Chunks = chunks;
		FirstFree = firstFree;
		Free = free;
		Used = used;
	}

	public ulong Address { get; }
	public uint SlotSize { get; }
	public IReadOnlyList<(ulong Start, ulong End)> Chunks { get; }
	public ulong FirstFree { get; }
	public uint Free { get; }
	public uint Used { get; }

	public bool IsSlot(ulong address) => Chunks.Any(c => address >= c.Start && address < c.End && (address - c.Start) % SlotSize == 0);

	public bool Contains(ulong address) => Chunks.Any(c => address >= c.Start && address < c.End);

	/// <summary>The pool of <paramref name="slotSize"/>-byte slots one of whose chunks holds <paramref name="member"/>.</summary>
	public static GamePool Find(IMemory memory, uint slotSize, ulong member)
	{
		if (member == 0 || !memory.TryGetModule("game_plugin.dll", out MemoryRegion module))
		{
			throw new InvalidOperationException(Build.T("Oyunun nesne havuzu bulunamadı.", "The game's object pool was not found."));
		}
		foreach (MemoryRegion region in PatternScanner.Clip(memory.GetRegions(), module).Where(r => r.IsWritable))
		{
			byte[] buffer = new byte[region.Size];
			if (!memory.TryRead(region.Start, buffer))
			{
				continue;
			}
			for (int i = 0; i + 0x38 <= buffer.Length; i += 8)
			{
				if (BitConverter.ToUInt32(buffer, i + 0x28) == slotSize && Read(memory, region.Start + (ulong)i, buffer.AsSpan(i, 0x38)) is { } pool && pool.IsSlot(member))
				{
					return pool;
				}
			}
		}
		throw new InvalidOperationException(Build.T("Oyunun nesne havuzu bulunamadı.", "The game's object pool was not found."));
	}

	/// <summary>The pool at <paramref name="address"/> as it is now, checked; null when it does not read as one.</summary>
	public static GamePool? ReadAt(IMemory memory, ulong address)
	{
		try
		{
			return Read(memory, address, memory.ReadBytes(address, 0x38));
		}
		catch (MemoryAccessException)
		{
			return null;
		}
	}

	private static GamePool? Read(IMemory memory, ulong at, ReadOnlySpan<byte> h)
	{
		ulong begin = BitConverter.ToUInt64(h[0..]), end = BitConverter.ToUInt64(h[8..]), cap = BitConverter.ToUInt64(h[16..]);
		if (begin == 0 || end <= begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 > 4096)
		{
			return null;
		}
		ulong firstFree = BitConverter.ToUInt64(h[0x18..]);
		uint free = BitConverter.ToUInt32(h[0x20..]), used = BitConverter.ToUInt32(h[0x24..]);
		uint size = BitConverter.ToUInt32(h[0x28..]), first = BitConverter.ToUInt32(h[0x2C..]), later = BitConverter.ToUInt32(h[0x30..]);
		int count = (int)((end - begin) / 8);
		if (size < 8 || size > 1 << 20 || first == 0 || later == 0 || (ulong)free + used != first + (ulong)later * (ulong)(count - 1) || (free == 0) != (firstFree == 0))
		{
			return null;
		}
		byte[] raw;
		try
		{
			raw = memory.ReadBytes(begin, count * 8);
		}
		catch (MemoryAccessException)
		{
			return null;
		}
		var chunks = Enumerable.Range(0, count).Select(k =>
		{
			ulong s = BitConverter.ToUInt64(raw, k * 8);
			return (Start: s, End: s + (ulong)(k == 0 ? first : later) * size);
		}).ToList();
		if (chunks.Any(c => c.Start == 0))
		{
			return null;
		}
		var pool = new GamePool(at, size, chunks, firstFree, free, used);
		return firstFree == 0 || pool.IsSlot(firstFree) ? pool : null;
	}

	/// <summary>
	/// The writes that take the first free slot as the game does: the pool's first free slot
	/// becomes the next one, one fewer free and one more used (one 16-byte write).
	/// </summary>
	public (ulong Slot, PlannedWrite Header) Take(IMemory memory)
	{
		if (Free == 0 || FirstFree == 0)
		{
			throw new InvalidOperationException(Build.T(
				"Oyunun sözleşme havuzunda boş yer yok. Oyunda bir gün ilerletip (oyun yeni sözleşme yapınca yer açılır) tekrar dene.",
				"The game's contract pool has no free slot. Play on a day (the game makes room when it makes a contract) and try again."));
		}
		ulong next = memory.ReadPointer(FirstFree);
		bool chained = Free == 1 ? next == 0 : next != FirstFree && IsSlot(next);
		if (!chained)
		{
			throw new InvalidOperationException(Build.T("Oyunun nesne havuzu beklenen yapıda değil.", "The game's object pool is not as expected."));
		}
		byte[] before = memory.ReadBytes(Address + 0x18, 16);
		byte[] after = (byte[])before.Clone();
		BitConverter.GetBytes(next).CopyTo(after, 0);
		BitConverter.GetBytes(Free - 1).CopyTo(after, 8);
		BitConverter.GetBytes(Used + 1).CopyTo(after, 12);
		return (FirstFree, new PlannedWrite(Address + 0x18, before, after, Build.T("Sözleşme havuzu", "Contract pool")));
	}
}
