using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SquadEditor.Core.Memory;

/// <summary>
/// Finds C++ vtables in macOS / Clang Mach-O binaries using Itanium C++ ABI RTTI.
/// Automatically translates MSVC type names (e.g. ".?AVACTUAL_PLAYER@db@@") into Itanium names ("N2db13ACTUAL_PLAYERE").
/// </summary>
public static class ItaniumRtti
{
	private const ulong PacMask = 0x0000_FFFF_FFFF_FFFFUL;

	public static string ToItaniumName(string typeName)
	{
		// Already Itanium?
		if (!typeName.StartsWith(".?AV", StringComparison.Ordinal) && !typeName.StartsWith(".?AU", StringComparison.Ordinal))
		{
			return typeName;
		}
		// Format: .?AVClass@Ns1@Ns2@@ -> parts in reverse: Ns2, Ns1, Class -> N<len>Ns2<len>Ns1<len>ClassE
		string body = typeName[4..];
		if (body.EndsWith("@@", StringComparison.Ordinal)) body = body[..^2];
		string[] parts = body.Split('@', StringSplitOptions.RemoveEmptyEntries);
		Array.Reverse(parts);
		if (parts.Length == 1)
		{
			return $"{parts[0].Length}{parts[0]}";
		}
		var sb = new StringBuilder("N");
		foreach (string p in parts)
		{
			sb.Append(p.Length).Append(p);
		}
		sb.Append('E');
		return sb.ToString();
	}

	public static List<(ulong Vtable, long OffsetToTop)> FindAllVtables(IMemory memory, MemoryRegion module, string typeName)
	{
		string itanium = ToItaniumName(typeName);
		List<MemoryRegion> regions = PatternScanner.Clip(memory.GetRegions(), module).ToList();
		byte[] nameBytes = Encoding.ASCII.GetBytes(itanium + "\0");
		var found = new List<(ulong, long)>();

		foreach (ulong nameAddr in PatternScanner.Scan(memory, regions, Pattern.Parse(Hex(nameBytes)), 16))
		{
			foreach (ulong namePtrAddr in FindPointerRefs(memory, regions, nameAddr))
			{
				ulong typeInfoAddr = namePtrAddr - 8;
				foreach (ulong tiRef in FindPointerRefs(memory, regions, typeInfoAddr))
				{
					long offsetToTop = unchecked((long)memory.Read<ulong>(tiRef - 8));
					found.Add((tiRef + 8, offsetToTop));
				}
			}
		}
		return found;
	}

	public static ulong? FindVtable(IMemory memory, MemoryRegion module, string typeName, uint colOffset = 0)
	{
		long expected = -(long)colOffset;
		var all = FindAllVtables(memory, module, typeName);
		foreach (var (vt, off) in all)
		{
			if (off == expected) return vt;
		}
		return null;
	}

	private static List<ulong> FindPointerRefs(IMemory memory, List<MemoryRegion> regions, ulong target)
	{
		ulong cleanTarget = target & PacMask;
		var hits = new List<ulong>();
		const int chunkSize = 4 * 1024 * 1024;
		byte[] buf = new byte[chunkSize];

		foreach (MemoryRegion r in regions)
		{
			for (ulong off = 0; off < r.Size; off += chunkSize)
			{
				int len = (int)Math.Min(chunkSize, r.Size - off);
				Span<byte> slice = buf.AsSpan(0, len);
				if (!memory.TryRead(r.Start + off, slice)) continue;
				ReadOnlySpan<ulong> words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(slice);
				for (int i = 0; i < words.Length; i++)
				{
					if ((words[i] & PacMask) == cleanTarget)
					{
						hits.Add(r.Start + off + (ulong)i * 8);
					}
				}
			}
		}
		return hits;
	}

	private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
