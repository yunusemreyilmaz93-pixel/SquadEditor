using System;
using System.Collections.Generic;

namespace SquadEditor.Core.Memory;

public static class PatternScanner
{
	internal const int ChunkSize = 1048576;

	public static List<ulong> Scan(IMemory memory, IEnumerable<MemoryRegion> regions, Pattern pattern, int maxResults)
	{
		List<ulong> list = new List<ulong>();
		int num = pattern.Length - 1;
		byte[] array = new byte[1048576 + num];
		foreach (MemoryRegion region in regions)
		{
			for (ulong num2 = region.Start; num2 < region.End; num2 += 1048576)
			{
				int num3 = (int)Math.Min((ulong)array.Length, region.End - num2);
				if (num3 < pattern.Length)
				{
					break;
				}
				Span<byte> span = array.AsSpan(0, num3);
				if (memory.TryRead(num2, span))
				{
					int lastStart = Math.Min(1048575, num3 - pattern.Length);
					FindInBuffer(span, pattern, lastStart, num2, list, maxResults);
					if (list.Count >= maxResults)
					{
						return list;
					}
				}
			}
		}
		return list;
	}

	public static IEnumerable<MemoryRegion> Clip(IEnumerable<MemoryRegion> regions, MemoryRegion bounds)
	{
		foreach (MemoryRegion region in regions)
		{
			ulong num = Math.Max(region.Start, bounds.Start);
			ulong num2 = Math.Min(region.End, bounds.End);
			if (num < num2)
			{
				yield return region with
				{
					Start = num,
					Size = num2 - num
				};
			}
		}
	}

	private static void FindInBuffer(ReadOnlySpan<byte> data, Pattern pattern, int lastStart, ulong baseAddress, List<ulong> results, int maxResults)
	{
		int anchorIndex = pattern.AnchorIndex;
		byte anchorByte = pattern.AnchorByte;
		int num = 0;
		while (num <= lastStart)
		{
			int num2 = num + anchorIndex;
			int length = lastStart + anchorIndex - num2 + 1;
			int num3 = data.Slice(num2, length).IndexOf(anchorByte);
			if (num3 < 0)
			{
				break;
			}
			int num4 = num2 + num3 - anchorIndex;
			if (pattern.IsMatchAt(data, num4))
			{
				results.Add(baseAddress + (ulong)num4);
				if (results.Count >= maxResults)
				{
					break;
				}
			}
			num = num4 + 1;
		}
	}
}
