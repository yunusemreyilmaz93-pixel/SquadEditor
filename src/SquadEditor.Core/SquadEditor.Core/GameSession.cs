using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core;

public sealed class GameSession
{
	private readonly Dictionary<string, ulong> _signatureMatches = new Dictionary<string, ulong>();

	private readonly Dictionary<string, ulong> _pinned = new Dictionary<string, ulong>();

	private readonly Dictionary<string, IReadOnlyList<ulong>> _scanCache = new Dictionary<string, IReadOnlyList<ulong>>();

	public IMemory Memory { get; }

	public GameLayout Layout { get; }

	public EditHistory History { get; } = new EditHistory();

	public Freezer Freezer { get; } = new Freezer();

	public GameSession(IMemory memory, GameLayout layout)
	{
		Memory = memory;
		Layout = layout;
		History.Written = Freezer.Follow;
	}

	public ulong ResolveAnchor(string name)
	{
		AnchorDef anchor = Layout.GetAnchor(name);
		if (!_pinned.TryGetValue(name, out var value) && !_signatureMatches.TryGetValue(name, out value))
		{
			value = FindSignature(anchor);
			_signatureMatches[name] = value;
		}
		ulong num = anchor.Resolve switch
		{
			AnchorResolve.RttiVtable => MemoryAccess.AddOffset(value, anchor.Offset), 
			AnchorResolve.Direct => MemoryAccess.AddOffset(value, anchor.Offset), 
			AnchorResolve.RipRelative => MemoryAccess.AddOffset(value + (ulong)anchor.InstructionEnd, Memory.Read<int>(MemoryAccess.AddOffset(value, anchor.Offset))), 
			_ => throw new LayoutException($"Unknown resolve mode {anchor.Resolve}."), 
		};
		foreach (long item in anchor.PointerChain)
		{
			num = MemoryAccess.AddOffset(Memory.ReadNonNullPointer(num), item);
		}
		return num;
	}

	public void ClearCache()
	{
		_signatureMatches.Clear();
		_scanCache.Clear();
	}

	public void PinAnchor(string name, ulong match)
	{
		Layout.GetAnchor(name);
		_pinned[name] = match;
	}

	public IReadOnlyList<ulong> GetCollectionItems(string collectionName)
	{
		CollectionDef collection = Layout.GetCollection(collectionName);
		ulong num = ResolveAnchor(collection.Anchor);
		if (collection.Scan)
		{
			if (!_scanCache.TryGetValue(collectionName, out var cached))
			{
				ScanAll();
				cached = _scanCache[collectionName];
			}
			if (cached.Count > collection.MaxCount)
			{
				throw new LayoutException(CoreText.Get("BadCount", collection.Name, cached.Count, collection.MaxCount));
			}
			return cached;
		}
		int num2 = Memory.Read<int>(MemoryAccess.AddOffset(num, collection.CountOffset));
		if (num2 < 0 || num2 > collection.MaxCount)
		{
			throw new LayoutException(CoreText.Get("BadCount", collection.Name, num2, collection.MaxCount));
		}
		if (num2 == 0)
		{
			return Array.Empty<ulong>();
		}
		long? itemsOffset = collection.ItemsOffset;
		ulong address;
		if (itemsOffset.HasValue)
		{
			long valueOrDefault = itemsOffset.GetValueOrDefault();
			address = Memory.ReadNonNullPointer(MemoryAccess.AddOffset(num, valueOrDefault));
		}
		else
		{
			address = num;
		}
		ulong num3 = MemoryAccess.AddOffset(address, collection.ItemsDataOffset);
		ulong stride = (ulong)collection.Stride;
		List<ulong> list = new List<ulong>(num2);
		if (!collection.ItemIsPointer)
		{
			for (int i = 0; i < num2; i++)
			{
				list.Add(num3 + (ulong)((long)i * (long)stride));
			}
			return list;
		}
		byte[] value = Memory.ReadBytes(num3, checked((int)((ulong)num2 * stride)));
		for (int j = 0; j < num2; j++)
		{
			ulong num4 = BitConverter.ToUInt64(value, (int)((long)j * (long)stride));
			if (num4 != 0L)
			{
				list.Add(num4);
			}
		}
		return list;
	}

	/// <summary>Fills every scan collection with a single pass over memory.</summary>
	private void ScanAll()
	{
		var vtables = Layout.Collections.Where((CollectionDef c) => c.Scan).ToDictionary((CollectionDef c) => c.Name, (CollectionDef c) => ResolveAnchor(c.Anchor));
		Dictionary<ulong, IReadOnlyList<ulong>> found = ObjectScanner.FindObjects(Memory, vtables.Values.Distinct().ToList());
		foreach (var (name, vtable) in vtables)
		{
			_scanCache[name] = found[vtable];
		}
	}

	public string ReadField(ulong entity, FieldDef field)
	{
		if (field.Concat.Count > 0)
		{
			EntityDef owner = Layout.Entities.First((EntityDef e) => e.Fields.Contains(field));
			IEnumerable<string> parts = field.Concat.Select((string n) => ReadField(entity, owner.Fields.First((FieldDef f) => f.Name == n)));
			return string.Join(" ", parts.Where((string p) => p.Length > 0));
		}
		return FieldCodec.Read(Memory, entity, field);
	}

	public void WriteField(ulong entity, FieldDef field, string input, string? entityLabel = null)
	{
		if (!field.IsWritable)
		{
			throw new InvalidOperationException(CoreText.Get("ReadOnly", field.Name));
		}
		byte[] array = FieldCodec.Encode(field, input);
		ulong address = FieldCodec.ResolveAddress(Memory, entity, field.Path);
		if (field.Type == FieldType.DayYear)
		{
			byte[] old = Memory.ReadBytes(address, 2);
			array[1] = (byte)((array[1] & 0x01) | (old[1] & 0xFE));
		}
		string oldValue = FieldCodec.Display(field, Memory.ReadBytes(address, array.Length));
		string newValue = FieldCodec.Display(field, array);
		using (field.Mirrors.Count > 0 && !History.InBatch ? History.BeginBatch(entityLabel ?? field.Name) : null)
		{
			History.Apply(Memory, address, array, new EditInfo(entityLabel ?? $"0x{entity:X}", field.Name, oldValue, newValue));
			foreach (List<long> mirror in field.Mirrors)
			{
				ulong copy = FieldCodec.ResolveAddress(Memory, entity, mirror);
				History.Apply(Memory, copy, array, new EditInfo(entityLabel ?? $"0x{entity:X}", field.Name, oldValue, newValue));
			}
		}
	}

	public int FreezeEntity(ulong entity, string label, EntityDef def)
	{
		List<(ulong, int)> list = new List<(ulong, int)>();
		foreach (FieldDef item in def.Fields.Where((FieldDef f) => f.IsWritable && f.IsNumeric))
		{
			try
			{
				list.Add((FieldCodec.ResolveAddress(Memory, entity, item.Path), FieldCodec.SizeOf(item.Type)));
			}
			catch (MemoryAccessException)
			{
			}
		}
		return Freezer.Freeze(Memory, entity, label, list);
	}

	private ulong FindSignature(AnchorDef anchor)
	{
		if (anchor.Resolve == AnchorResolve.RttiVtable)
		{
			if (!Memory.TryGetModule(Layout.Module, out var rttiModule))
			{
				throw new LayoutException(CoreText.Get("ModuleNotFound", Layout.Module));
			}
			return Rtti.FindVtable(Memory, rttiModule, anchor.TypeName!, anchor.ColOffset) ?? throw new LayoutException(CoreText.Get("AnchorNotFound", anchor.Name));
		}
		Pattern pattern = Pattern.Parse(anchor.Pattern);
		IEnumerable<MemoryRegion> regions;
		if (anchor.Scope == AnchorScope.Module)
		{
			if (!Memory.TryGetModule(Layout.Module, out var module))
			{
				throw new LayoutException(CoreText.Get("ModuleNotFound", Layout.Module));
			}
			regions = PatternScanner.Clip(Memory.GetRegions(), module);
		}
		else
		{
			regions = from r in Memory.GetRegions()
				where r.IsWritable
				select r;
		}
		List<ulong> list = PatternScanner.Scan(Memory, regions, pattern, (!anchor.ExpectUnique) ? 1 : 2);
		if (list.Count == 0)
		{
			throw new LayoutException(CoreText.Get("AnchorNotFound", anchor.Name));
		}
		if (list.Count > 1)
		{
			throw new LayoutException(CoreText.Get("AnchorAmbiguous", anchor.Name));
		}
		return list[0];
	}
}
