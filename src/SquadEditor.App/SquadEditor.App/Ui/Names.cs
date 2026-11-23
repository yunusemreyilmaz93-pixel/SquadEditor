using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public enum NameChange
{
	/// <summary>The name stays as it is.</summary>
	None,
	/// <summary>The person is pointed at a name the game has already (no new memory, nothing else changes).</summary>
	Existing,
	/// <summary>The name is written over the old one, which only this person uses, inside the game's own block.</summary>
	InPlace,
	/// <summary>The name is taken away (a common name, a full name).</summary>
	Clear,
	/// <summary>The editor cannot give this name safely.</summary>
	Refused
}

/// <summary>What a rename does to one part of a name.</summary>
public sealed record NamePart(string Label, string From, string To, NameChange Change, string Note, IReadOnlyList<PlannedWrite> Writes);

/// <summary>
/// Renaming people. A person block (player +0x288, staff entity address) holds the full name
/// at +0x40 (an FM string, often null) and pointers to entries of the game's name table at
/// +0x50 first, +0x58 last and +0x60 common name (null for none).
///
/// Name table entries are 24-byte slots of a pool of the game's own (chunks of 10,000; about a
/// million entries): +0x00 the FM string, +0x08 u32 id, +0x0C u16 nation, +0x0E u16 weight.
/// Many people share one entry ("Kerem"), and nations keep lists of entries for new names.
///
/// FM string: block = u32 size asked for (length + 9), u32 (left alone), u32 holders, then the
/// pointer's target: u32 length, UTF-8 text, NUL. Blocks come in steps of 16 here; the editor
/// assumes steps of 8, which always lie inside the game's block.
///
/// A new name is given in the safest way the game allows:
/// 1. an entry with exactly that text exists: the person is pointed at it;
/// 2. else, when only this person uses the entry, its string has one holder and the new text
///    keeps the size of the game's block: the text is written over the old one;
/// 3. else it is refused. No memory is allocated in the game.
/// </summary>
public static class Names
{
	public const long FullName = 0x40, FirstName = 0x50, LastName = 0x58, CommonName = 0x60;
	private const int EntrySize = 0x18;
	private const int Overhead = 17;
	private const int Step = 8;
	public const int LongestName = 64;

	private static NameIndex? _index;
	private static readonly object Gate = new object();

	public static string ReadEntry(IMemory memory, ulong entry)
	{
		return entry == 0 ? "" : ReadString(memory, memory.ReadPointer(entry));
	}

	public static string ReadString(IMemory memory, ulong text)
	{
		if (text == 0)
		{
			return "";
		}
		int length = memory.Read<int>(text);
		return length is > 0 and <= 255 ? Encoding.UTF8.GetString(memory.ReadBytes(text + 4, length)) : "";
	}

	/// <summary>The game's name table, found once per game and indexed by text (a few seconds).</summary>
	public static Task<NameIndex> IndexAsync(GameData data, ulong anyEntry)
	{
		return Task.Run(() =>
		{
			lock (Gate)
			{
				if (_index == null || _index.Memory != data.Memory || !_index.StillValid())
				{
					_index = NameIndex.Create(data.Memory, anyEntry);
				}
				return _index;
			}
		});
	}

	/// <summary>Works out a rename of the person whose person block is at <paramref name="block"/>. Nothing is written.</summary>
	public static async Task<List<NamePart>> PlanAsync(GameData data, ulong block, string first, string last, string common)
	{
		IMemory m = data.Memory;
		ulong firstEntry = m.ReadPointer(block + FirstName);
		NameIndex index = await IndexAsync(data, firstEntry != 0 ? firstEntry : m.ReadPointer(block + LastName));
		return await Task.Run(() =>
		{
			var parts = new List<NamePart>
			{
				PlanEntry(data, index, block, FirstName, Build.T("Ad", "First name"), first.Trim(), allowClear: false),
				PlanEntry(data, index, block, LastName, Build.T("Soyad", "Last name"), last.Trim(), allowClear: false),
				PlanEntry(data, index, block, CommonName, Build.T("Bilinen ad", "Common name"), common.Trim(), allowClear: true)
			};
			bool firstOrLast = parts.Take(2).Any(p => p.Change is NameChange.Existing or NameChange.InPlace);
			if (firstOrLast)
			{
				// The full name keeps its other parts ("Muhammed Kerem Aktürkoğlu"): only the old first and last names are swapped.
				string full = ReadString(m, m.ReadPointer(block + FullName));
				string wanted = $"{first.Trim()} {last.Trim()}";
				string[] words = full.Split(' ');
				bool found = parts.Take(2).All(p => p.Change == NameChange.None || words.Contains(p.From));
				if (full.Length > 0 && found)
				{
					wanted = SwapWord(SwapWord(full, parts[1].From, parts[1].To), parts[0].From, parts[0].To);
				}
				parts.Add(PlanFullName(data, block, wanted));
			}
			return parts;
		});
	}

	/// <summary>Replaces the last whole-word occurrence of <paramref name="from"/> in <paramref name="text"/>.</summary>
	private static string SwapWord(string text, string from, string to)
	{
		if (from.Length == 0 || from == to)
		{
			return text;
		}
		string[] words = text.Split(' ');
		int at = Array.LastIndexOf(words, from);
		if (at < 0)
		{
			return text;
		}
		words[at] = to;
		return string.Join(' ', words);
	}

	private static NamePart PlanEntry(GameData data, NameIndex index, ulong block, long offset, string label, string text, bool allowClear)
	{
		IMemory m = data.Memory;
		ulong field = block + (ulong)offset;
		ulong entry = m.ReadPointer(field);
		string from = ReadEntry(m, entry);
		if (text == from)
		{
			return new NamePart(label, from, text, NameChange.None, "", Array.Empty<PlannedWrite>());
		}
		if (text.Length == 0)
		{
			if (!allowClear)
			{
				return Refused(label, from, text, Build.T("Boş olamaz.", "Cannot be empty."));
			}
			return new NamePart(label, from, text, NameChange.Clear, Build.T("Kaldırılır.", "Removed."),
				new[] { new PlannedWrite(field, GameWrite.U64(entry), GameWrite.U64(0), label, from, "") });
		}
		if (Encoding.UTF8.GetByteCount(text) > LongestName || text.Any(char.IsControl))
		{
			return Refused(label, from, text, Build.T("Geçersiz ya da çok uzun.", "Invalid or too long."));
		}

		ushort nation = entry != 0 ? m.Read<ushort>(entry + 0x0C) : (ushort)0;
		ulong found = index.Find(m, text, nation);
		if (found != 0)
		{
			return new NamePart(label, from, text, NameChange.Existing, Build.T("Oyunda kayıtlı isim kullanılır.", "Uses a name the game already has."),
				new[] { new PlannedWrite(field, GameWrite.U64(entry), GameWrite.U64(found), label, from, text) });
		}
		if (entry == 0)
		{
			return Refused(label, from, text, Build.T("Bu isim oyunda yok.", "The game has no such name."));
		}
		string? why = InPlaceProblem(data, index, block, entry, text);
		if (why != null)
		{
			return Refused(label, from, text, why);
		}
		ulong str = m.ReadPointer(entry);
		return new NamePart(label, from, text, NameChange.InPlace, Build.T("Eski isim yerinde değiştirilir.", "Written over the old name."),
			StringWrites(m, str, from, text, label));
	}

	/// <summary>The full name: written over in place when it fits and only he holds it, otherwise taken away.</summary>
	private static NamePart PlanFullName(GameData data, ulong block, string text)
	{
		IMemory m = data.Memory;
		string label = Build.T("Tam ad", "Full name");
		ulong field = block + FullName;
		ulong str = m.ReadPointer(field);
		if (str == 0)
		{
			return new NamePart(label, "", "", NameChange.None, "", Array.Empty<PlannedWrite>());
		}
		string from = ReadString(m, str);
		if (from == text)
		{
			return new NamePart(label, from, text, NameChange.None, "", Array.Empty<PlannedWrite>());
		}
		if (m.Read<uint>(str - 4) == 1 && SameBlock(Encoding.UTF8.GetByteCount(from), Encoding.UTF8.GetByteCount(text)))
		{
			return new NamePart(label, from, text, NameChange.InPlace, Build.T("Yeni ada göre güncellenir.", "Updated to the new name."),
				StringWrites(m, str, from, text, label));
		}
		return new NamePart(label, from, "", NameChange.Clear, Build.T("Kaldırılır; oyun ad ve soyadı gösterir.", "Removed; the game shows first and last name."),
			new[] { new PlannedWrite(field, GameWrite.U64(str), GameWrite.U64(0), label, from, "") });
	}

	private static string? InPlaceProblem(GameData data, NameIndex index, ulong block, ulong entry, string text)
	{
		IMemory m = data.Memory;
		ulong str = m.ReadPointer(entry);
		string from = ReadString(m, str);
		int oldBytes = Encoding.UTF8.GetByteCount(from), newBytes = Encoding.UTF8.GetByteCount(text);
		if (!SameBlock(oldBytes, newBytes))
		{
			(int min, int max) = Lengths(oldBytes);
			return Build.T($"Oyunda yok; yerinde yazmak için {min}-{max} bayt olmalı (şimdi {newBytes}).",
				$"Not in the game; to write it in place it must take {min}-{max} bytes (now {newBytes}).");
		}
		if (m.Read<uint>(str - 4) != 1)
		{
			return Build.T("Oyunda yok; eski isim başka kayıtlarla ortak.", "Not in the game; the old name is shared with other entries.");
		}
		int others = OtherHolders(data, index, block, entry);
		if (others > 0)
		{
			return Build.T($"Oyunda yok; eski ismi {others} kişi daha kullanıyor, onlar da değişirdi.",
				$"Not in the game; {others} other people use the old name and would change too.");
		}
		return null;
	}

	/// <summary>
	/// Places other than this person that point at the entry and look like a person's name
	/// fields. Long runs of entry pointers (the nations' lists of names) do not count.
	/// </summary>
	private static int OtherHolders(GameData data, NameIndex index, ulong block, ulong entry)
	{
		IMemory m = data.Memory;
		var own = new HashSet<ulong> { block + FirstName, block + LastName, block + CommonName };
		int count = 0;
		foreach (ulong at in ObjectScanner.FindObjects(m, new[] { entry })[entry])
		{
			if (own.Contains(at))
			{
				continue;
			}
			int run = 1;
			for (ulong p = at - 8; run < 4 && index.IsEntry(m.ReadPointer(p)); p -= 8)
			{
				run++;
			}
			for (ulong p = at + 8; run < 4 && index.IsEntry(m.ReadPointer(p)); p += 8)
			{
				run++;
			}
			if (run < 4)
			{
				count++;
			}
		}
		return count;
	}

	private static List<PlannedWrite> StringWrites(IMemory m, ulong str, string from, string to, string label)
	{
		byte[] oldText = Encoding.UTF8.GetBytes(from), newText = Encoding.UTF8.GetBytes(to);
		int span = 4 + Math.Max(oldText.Length, newText.Length) + 1;
		byte[] expected = m.ReadBytes(str, span);
		byte[] bytes = new byte[span];
		BitConverter.GetBytes(newText.Length).CopyTo(bytes, 0);
		newText.CopyTo(bytes, 4);
		return new List<PlannedWrite>
		{
			new PlannedWrite(str - 12, m.ReadBytes(str - 12, 4), GameWrite.U32((uint)(newText.Length + 9)), label, from, to),
			new PlannedWrite(str, expected, bytes, label, from, to)
		};
	}

	private static int BlockSize(int bytes) => (bytes + Overhead + Step - 1) / Step * Step;

	private static bool SameBlock(int oldBytes, int newBytes) => newBytes > 0 && BlockSize(oldBytes) == BlockSize(newBytes);

	private static (int Min, int Max) Lengths(int oldBytes)
	{
		int size = BlockSize(oldBytes);
		return (Math.Max(1, size - Step - Overhead + 1), size - Overhead);
	}

	private static NamePart Refused(string label, string from, string to, string why)
	{
		return new NamePart(label, from, to, NameChange.Refused, why, Array.Empty<PlannedWrite>());
	}
}

/// <summary>The game's name table: its pool's chunks, and the entries by a hash of their text.</summary>
public sealed class NameIndex
{
	private const int EntrySize = 0x18;
	private readonly List<(ulong Start, ulong End)> _chunks;
	private readonly ulong[] _hashes;
	private readonly ulong[] _entries;
	private readonly ulong _pool;
	private readonly byte[] _poolHeader;

	private NameIndex(IMemory memory, ulong pool, byte[] header, List<(ulong, ulong)> chunks, ulong[] hashes, ulong[] entries)
	{
		Memory = memory;
		_pool = pool;
		_poolHeader = header;
		_chunks = chunks;
		_hashes = hashes;
		_entries = entries;
	}

	public IMemory Memory { get; }
	public int Count => _entries.Length;

	/// <summary>Whether the pool still has the chunks it had when indexed (new names go to its free slots).</summary>
	public bool StillValid()
	{
		try
		{
			return Memory.ReadBytes(_pool, 24).AsSpan().SequenceEqual(_poolHeader.AsSpan(0, 24));
		}
		catch (MemoryAccessException)
		{
			return false;
		}
	}

	public bool IsEntry(ulong address)
	{
		foreach (var (start, end) in _chunks)
		{
			if (address >= start && address < end)
			{
				return (address - start) % EntrySize == 0;
			}
		}
		return false;
	}

	/// <summary>An entry whose text is exactly <paramref name="text"/>, of the nation given when there is one; 0 for none.</summary>
	public ulong Find(IMemory memory, string text, ushort nation)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		ulong hash = Hash(bytes);
		int i = Array.BinarySearch(_hashes, hash);
		if (i < 0)
		{
			return 0;
		}
		while (i > 0 && _hashes[i - 1] == hash)
		{
			i--;
		}
		ulong best = 0;
		for (; i < _hashes.Length && _hashes[i] == hash; i++)
		{
			ulong entry = _entries[i];
			try
			{
				ulong str = memory.ReadPointer(entry);
				if (str == 0 || IsEntry(str) || memory.Read<int>(str) != bytes.Length || !memory.ReadBytes(str + 4, bytes.Length).AsSpan().SequenceEqual(bytes))
				{
					continue;
				}
				if (best == 0 || memory.Read<ushort>(entry + 0x0C) == nation)
				{
					best = entry;
				}
			}
			catch (MemoryAccessException)
			{
			}
		}
		return best;
	}

	/// <summary>Finds the pool of 24-byte name entries that holds <paramref name="anyEntry"/> and indexes it.</summary>
	public static NameIndex Create(IMemory memory, ulong anyEntry)
	{
		GamePool pool = GamePool.Find(memory, EntrySize, anyEntry);
		return Index(memory, pool.Address, memory.ReadBytes(pool.Address, 0x38), pool.Chunks.ToList());
	}

	private static NameIndex Index(IMemory memory, ulong pool, byte[] header, List<(ulong Start, ulong End)> chunks)
	{
		var cache = new PageCacheMemory(memory);
		var hashes = new List<ulong>(1 << 20);
		var entries = new List<ulong>(1 << 20);
		bool InPool(ulong a) => chunks.Any(c => a >= c.Start && a < c.End);
		foreach (var (start, end) in chunks)
		{
			byte[] raw;
			try
			{
				raw = memory.ReadBytes(start, (int)(end - start));
			}
			catch (MemoryAccessException)
			{
				continue;
			}
			for (int k = 0; k + EntrySize <= raw.Length; k += EntrySize)
			{
				ulong str = BitConverter.ToUInt64(raw, k);
				if (str == 0 || InPool(str))
				{
					continue;
				}
				try
				{
					int length = cache.Read<int>(str);
					if (length is <= 0 or > 255)
					{
						continue;
					}
					hashes.Add(Hash(cache.ReadBytes(str + 4, length)));
					entries.Add(start + (ulong)k);
				}
				catch (MemoryAccessException)
				{
				}
			}
		}
		ulong[] h = hashes.ToArray(), e = entries.ToArray();
		Array.Sort(h, e);
		return new NameIndex(memory, pool, header, chunks, h, e);
	}

	private static ulong Hash(ReadOnlySpan<byte> bytes)
	{
		ulong hash = 14695981039346656037UL;
		foreach (byte b in bytes)
		{
			hash = (hash ^ b) * 1099511628211UL;
		}
		return hash;
	}
}
