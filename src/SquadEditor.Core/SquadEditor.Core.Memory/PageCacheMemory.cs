using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SquadEditor.Core.Memory;

/// <summary>
/// Read-through cache of 4 KB pages over another memory. Bulk reads (building lists of
/// tens of thousands of records) touch the same pages many times; this turns hundreds of
/// thousands of small ReadProcessMemory calls into a few thousand page reads.
/// Use it for a short snapshot only: cached pages do not follow later changes. Writes go straight through.
/// </summary>
public sealed class PageCacheMemory : IMemory
{
	private const int PageSize = 4096;

	private readonly IMemory _inner;

	private readonly ConcurrentDictionary<ulong, byte[]?> _pages = new ConcurrentDictionary<ulong, byte[]?>();

	public PageCacheMemory(IMemory inner)
	{
		_inner = inner;
	}

	public bool TryRead(ulong address, Span<byte> buffer)
	{
		ulong first = address / PageSize;
		ulong last = (address + (ulong)buffer.Length - 1) / PageSize;
		if (buffer.Length == 0 || last - first > 4)
		{
			return _inner.TryRead(address, buffer);
		}
		int written = 0;
		for (ulong page = first; page <= last; page++)
		{
			byte[]? data = _pages.GetOrAdd(page, p =>
			{
				byte[] bytes = new byte[PageSize];
				return _inner.TryRead(p * PageSize, bytes) ? bytes : null;
			});
			if (data == null)
			{
				return _inner.TryRead(address, buffer);
			}
			int offset = page == first ? (int)(address % PageSize) : 0;
			int count = Math.Min(PageSize - offset, buffer.Length - written);
			data.AsSpan(offset, count).CopyTo(buffer.Slice(written));
			written += count;
		}
		return true;
	}

	public bool TryWrite(ulong address, ReadOnlySpan<byte> data)
	{
		_pages.Clear();
		return _inner.TryWrite(address, data);
	}

	public IReadOnlyList<MemoryRegion> GetRegions() => _inner.GetRegions();

	public bool TryGetModule(string name, out MemoryRegion module) => _inner.TryGetModule(name, out module);
}
