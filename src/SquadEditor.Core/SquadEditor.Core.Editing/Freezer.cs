using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Editing;

public sealed class Freezer
{
	private sealed record Frozen(ulong Entity, string Label, byte[] Bytes);

	private readonly Dictionary<ulong, Frozen> _values = new Dictionary<ulong, Frozen>();

	public IReadOnlyList<FrozenEntity> Entities => (from v in _values.Values
		group v by v.Entity into g
		select new FrozenEntity(g.Key, g.First().Label, g.Count())).OrderBy((FrozenEntity e) => e.Label, StringComparer.CurrentCulture).ToList();

	public event EventHandler? Changed;

	public bool IsFrozen(ulong entity)
	{
		return _values.Values.Any((Frozen v) => v.Entity == entity);
	}

	public bool IsAddressFrozen(ulong address)
	{
		return _values.ContainsKey(address);
	}

	public int Freeze(IMemory memory, ulong entity, string label, IEnumerable<(ulong Address, int Size)> fields)
	{
		int num = 0;
		foreach (var field in fields)
		{
			ulong item = field.Address;
			byte[] array = new byte[field.Size];
			if (memory.TryRead(item, array))
			{
				_values[item] = new Frozen(entity, label, array);
				num++;
			}
		}
		Changed?.Invoke(this, EventArgs.Empty);
		return num;
	}

	public void Unfreeze(ulong entity)
	{
		foreach (ulong item in (from kv in _values
			where kv.Value.Entity == entity
			select kv.Key).ToList())
		{
			_values.Remove(item);
		}
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Clear()
	{
		_values.Clear();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public int Tick(IMemory memory)
	{
		int num = 0;
		foreach (var (address, frozen2) in _values)
		{
			byte[] array = new byte[frozen2.Bytes.Length];
			if (memory.TryRead(address, array) && !((ReadOnlySpan<byte>)array.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)frozen2.Bytes) && memory.TryWrite(address, frozen2.Bytes))
			{
				num++;
			}
		}
		return num;
	}

	internal void Follow(ulong address, byte[] bytes)
	{
		if (_values.TryGetValue(address, out Frozen? value) && value.Bytes.Length == bytes.Length)
		{
			_values[address] = value with
			{
				Bytes = bytes.ToArray()
			};
		}
	}
}
