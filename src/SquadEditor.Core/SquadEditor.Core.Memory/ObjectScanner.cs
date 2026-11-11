using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SquadEditor.Core.Memory;

/// <summary>Finds heap objects by their vtable pointer.</summary>
public static class ObjectScanner
{
	private const int ChunkSize = 16 * 1024 * 1024;

	public static IReadOnlyList<ulong> FindObjects(IMemory memory, ulong vtable, int maxCount)
	{
		IReadOnlyList<ulong> found = FindObjects(memory, new[] { vtable })[vtable];
		if (found.Count > maxCount)
		{
			throw new MemoryAccessException(vtable, 8, $"Found {found.Count} objects, more than the layout's maxCount {maxCount}.");
		}
		return found;
	}

	/// <summary>One pass over memory for several vtables at once.</summary>
	public static Dictionary<ulong, IReadOnlyList<ulong>> FindObjects(IMemory memory, IReadOnlyCollection<ulong> vtables)
	{
		var chunks = new List<(ulong Start, int Length)>();
		foreach (MemoryRegion region in memory.GetRegions().Where((MemoryRegion r) => r.IsWritable))
		{
			for (ulong offset = 0; offset < region.Size; offset += ChunkSize)
			{
				chunks.Add((region.Start + offset, (int)Math.Min(ChunkSize, region.Size - offset)));
			}
		}
		var wanted = new HashSet<ulong>(vtables);
		ulong min = wanted.Min(), max = wanted.Max();
		var found = vtables.Distinct().ToDictionary(v => v, _ => new List<ulong>());
		object gate = new object();
		Parallel.ForEach(chunks, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) }, chunk =>
		{
			byte[] buffer = new byte[chunk.Length];
			if (!memory.TryRead(chunk.Start, buffer))
			{
				return;
			}
			var local = new List<(ulong Vtable, ulong Address)>();
			ReadOnlySpan<ulong> words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buffer);
			for (int i = 0; i < words.Length; i++)
			{
				ulong w = words[i];
				if (w >= min && w <= max && wanted.Contains(w))
				{
					local.Add((w, chunk.Start + (ulong)i * 8));
				}
			}
			if (local.Count > 0)
			{
				lock (gate)
				{
					foreach (var (v, a) in local)
					{
						found[v].Add(a);
					}
				}
			}
		});
		var result = new Dictionary<ulong, IReadOnlyList<ulong>>();
		foreach (var (v, list) in found)
		{
			list.Sort();
			result[v] = list;
		}
		return result;
	}
}
