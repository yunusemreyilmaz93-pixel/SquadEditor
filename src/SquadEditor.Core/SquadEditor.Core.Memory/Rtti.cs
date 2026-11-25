using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SquadEditor.Core.Memory;

/// <summary>Finds a class's primary vtable through MSVC x64 RTTI, so layouts survive ASLR and patches.</summary>
public static class Rtti
{
	public static ulong? FindVtable(IMemory memory, MemoryRegion module, string typeName, uint colOffset = 0)
	{
		if (OperatingSystem.IsMacOS())
		{
			return ItaniumRtti.FindVtable(memory, module, typeName, colOffset);
		}
		List<MemoryRegion> regions = PatternScanner.Clip(memory.GetRegions(), module).ToList();
		byte[] name = Encoding.ASCII.GetBytes(typeName + "\0");
		foreach (ulong hit in PatternScanner.Scan(memory, regions, Pattern.Parse(Hex(name)), 16))
		{
			// TypeDescriptor: pVFTable (8), spare (8), name.
			uint tdRva = (uint)(hit - 16 - module.Start);
			// CompleteObjectLocator: signature 1, offset of this vtable in the object (0 = primary), cdOffset, pTypeDescriptor (RVA).
			Pattern col = Pattern.Parse("01 00 00 00 " + Hex(BitConverter.GetBytes(colOffset)) + " ?? ?? ?? ?? " + Hex(BitConverter.GetBytes(tdRva)));
			foreach (ulong colAddress in PatternScanner.Scan(memory, regions, col, 64))
			{
				uint self = memory.Read<uint>(colAddress + 20);
				if (self != (uint)(colAddress - module.Start))
				{
					continue;
				}
				List<ulong> meta = PatternScanner.Scan(memory, regions, Pattern.Parse(Hex(BitConverter.GetBytes(colAddress))), 1);
				if (meta.Count > 0)
				{
					return meta[0] + 8;
				}
			}
		}
		return null;
	}

	private static string Hex(byte[] bytes)
	{
		return string.Join(" ", bytes.Select((byte b) => b.ToString("X2")));
	}
}
