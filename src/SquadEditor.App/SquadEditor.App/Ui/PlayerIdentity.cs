using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed record SpokenLanguage(int Index, ulong EntryAddress, ulong LanguagePtr, string Name, int Fluency);

public sealed record PlayerRelation(int Index, ulong EntryAddress, ulong TargetAddress, byte Category, byte Kind, byte Strength, string TargetName)
{
	public bool IsSecondNation => Category == 0x08 && Kind == 0x09;
	public bool IsFavClub => Category == 0x01 && Kind == 0x03;
	public bool IsFavPerson => Category == 0x03 && Kind == 0x01;
	public bool IsDisliked => Kind == 0x02;

	public string Badge => (Category, Kind) switch
	{
		(0x08, 0x09) => Build.T("🌍 İkinci Uyruk", "🌍 Second Nation"),
		(0x01, 0x03) => Build.T("🛡 Favori Kulüp", "🛡 Favoured Club"),
		(0x03, 0x01) => Build.T("⭐ Favori Kişi", "⭐ Favoured Person"),
		(0x01, 0x02) => Build.T("⛔ Sevilmeyen Kulüp", "⛔ Disliked Club"),
		(0x03, 0x02) => Build.T("⛔ Sevilmeyen Kişi", "⛔ Disliked Person"),
		_ => ""
	};
}

/// <summary>
/// Reads and edits a player's Primary Nation (+0x2F0), Second Nationalities & Favourite Clubs/Personnel
/// (+0x300 -> +0x00 std::vector of 16-byte entries), and Spoken Languages (+0x378).
/// All offsets live-verified on FM27 27.0.1 (Ferdi Kadıoğlu & Arda Güler). Every write uses guarded EditHistory (Ctrl+Z undoes).
/// </summary>
public static class PlayerIdentity
{
	public const long PrimaryNationOffset = 0x2F0;
	private const long ExtraDataPtr = 0x300;
	private const long LanguagesVector = 0x378;
	private const int RelEntrySize = 16;

	public static IReadOnlyList<PlayerRelation> ReadRelations(GameData data, ulong player)
	{
		var list = new List<PlayerRelation>();
		IMemory m = data.Memory;
		try
		{
			ulong extra = m.ReadPointer(player + ExtraDataPtr);
			if (extra == 0) return list;
			ulong begin = m.ReadPointer(extra);
			ulong end = m.ReadPointer(extra + 8);
			if (begin == 0 || end <= begin || end - begin > RelEntrySize * 64 || (end - begin) % RelEntrySize != 0)
			{
				return list;
			}
			int count = (int)((end - begin) / RelEntrySize);
			byte[] raw = m.ReadBytes(begin, count * RelEntrySize);
			for (int i = 0; i < count; i++)
			{
				int o = i * RelEntrySize;
				ulong target = BitConverter.ToUInt64(raw, o);
				byte cat = raw[o + 10];
				byte kind = raw[o + 11];
				byte str = raw[o + 12];
				bool interesting = (cat == 0x08 && kind == 0x09) || (cat == 0x01 && (kind == 0x03 || kind == 0x02)) || (cat == 0x03 && (kind == 0x01 || kind == 0x02));
				if (!interesting) continue;
				string name = ResolveRelationTarget(data, target, cat);
				if (name.Length > 0)
				{
					list.Add(new PlayerRelation(i, begin + (ulong)o, target, cat, kind, str, name));
				}
			}
		}
		catch (MemoryAccessException)
		{
		}
		return list;
	}

	public static bool CanAddRelation(IMemory m, ulong player)
	{
		try
		{
			ulong extra = m.ReadPointer(player + ExtraDataPtr);
			if (extra == 0) return false;
			ulong begin = m.ReadPointer(extra);
			ulong end = m.ReadPointer(extra + 8);
			ulong cap = m.ReadPointer(extra + 16);
			return begin != 0 && end >= begin && cap >= end && (cap - end) >= RelEntrySize;
		}
		catch (MemoryAccessException)
		{
			return false;
		}
	}

	public static void AddSecondNation(GameData data, ulong player, ulong nationAddr, string nationName, string entityLabel)
	{
		byte[] entry = new byte[RelEntrySize];
		BitConverter.GetBytes(nationAddr).CopyTo(entry, 0);
		// Verified exact bytes from Ferdi Kadıoğlu (The Netherlands & Kanada): 0xFF00506409080000
		BitConverter.GetBytes(0xFF00506409080000UL).CopyTo(entry, 8);
		AppendRelation(data, player, entry, Build.T($"İkinci uyruk ekle ({nationName})", $"Add second nation ({nationName})"), nationName, entityLabel);
	}

	public static void AddFavClub(GameData data, ulong player, ulong clubAddr, string clubName, byte strength, string entityLabel)
	{
		byte[] entry = new byte[RelEntrySize];
		BitConverter.GetBytes(clubAddr).CopyTo(entry, 0);
		entry[8] = 0x02; entry[9] = 0x00; entry[10] = 0x01; entry[11] = 0x03;
		entry[12] = Math.Clamp(strength, (byte)1, (byte)100);
		entry[13] = 0x00; entry[14] = 0x00; entry[15] = 0xFF;
		AppendRelation(data, player, entry, Build.T($"Favori kulüp ekle ({clubName})", $"Add favoured club ({clubName})"), clubName, entityLabel);
	}

	public static void SetRelationStrength(GameData data, PlayerRelation rel, byte strength, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		byte clamped = Math.Clamp(strength, (byte)1, (byte)100);
		data.Session.History.Apply(data.Memory, rel.EntryAddress + 12, new[] { clamped },
			new EditInfo(entityLabel, $"{rel.Badge}: {rel.TargetName}", rel.Strength.ToString(), clamped.ToString()), guarded: true);
	}

	public static void RemoveRelation(GameData data, ulong player, int index, string targetName, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		IMemory m = data.Memory;
		ulong extra = m.ReadPointer(player + ExtraDataPtr);
		if (extra == 0) return;
		ulong begin = m.ReadPointer(extra);
		ulong end = m.ReadPointer(extra + 8);
		int count = (int)((end - begin) / RelEntrySize);
		if (index < 0 || index >= count) return;
		byte[] raw = m.ReadBytes(begin, count * RelEntrySize);
		using (data.Session.History.BeginBatch(Build.T($"{entityLabel}: İlişki/uyruk sil ({targetName})", $"{entityLabel}: Remove relation ({targetName})")))
		{
			for (int i = index; i < count - 1; i++)
			{
				byte[] next = raw.AsSpan((i + 1) * RelEntrySize, RelEntrySize).ToArray();
				data.Session.History.Apply(m, begin + (ulong)(i * RelEntrySize), next, new EditInfo(entityLabel, Build.T("İlişki", "Relation"), "", ""), guarded: true);
			}
			data.Session.History.Apply(m, extra + 8, BitConverter.GetBytes(end - RelEntrySize),
				new EditInfo(entityLabel, Build.T("İlişki", "Relation"), targetName, "—"), guarded: true);
		}
	}

	private static void AppendRelation(GameData data, ulong player, byte[] entry, string batchTitle, string valueLabel, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		IMemory m = data.Memory;
		ulong extra = m.ReadPointer(player + ExtraDataPtr);
		if (extra == 0) throw new InvalidOperationException(Build.T("Oyuncuda ekstra ilişki bloğu yok.", "Player has no extra relationship block."));
		ulong begin = m.ReadPointer(extra);
		ulong end = m.ReadPointer(extra + 8);
		ulong cap = m.ReadPointer(extra + 16);
		if (begin == 0 || end < begin || cap < end || (cap - end) < RelEntrySize)
		{
			throw new InvalidOperationException(Build.T("Bu oyuncunun ilişki vektöründe boş yer yok (önce mevcut bir kaydı silersen yer açılır).",
				"No spare capacity in this player's relation vector (removing an entry frees a slot)."));
		}
		using (data.Session.History.BeginBatch($"{entityLabel}: {batchTitle}"))
		{
			data.Session.History.Apply(m, end, entry, new EditInfo(entityLabel, batchTitle, "—", valueLabel), guarded: true);
			data.Session.History.Apply(m, extra + 8, BitConverter.GetBytes(end + RelEntrySize), new EditInfo(entityLabel, batchTitle, "", "+1"), guarded: true);
		}
	}

	private static string ResolveRelationTarget(GameData data, ulong target, byte category)
	{
		if (target == 0) return "";
		if (category == 0x08)
		{
			return data.Nations.FirstOrDefault(n => n.Address == target)?.Name ?? "";
		}
		if (category == 0x01 && data.ClubsByAddress.TryGetValue(target, out ClubRow? c))
		{
			return c.Name;
		}
		IMemory m = data.Memory;
		try
		{
			string ReadLenUtf8(ulong pf)
			{
				ulong sp = m.ReadPointer(pf);
				if (sp == 0) return "";
				ulong s = m.ReadPointer(sp);
				if (s == 0) return "";
				int len = m.Read<int>(s);
				return len > 0 && len < 64 ? Encoding.UTF8.GetString(m.ReadBytes(s + 4, len)) : "";
			}
			return (ReadLenUtf8(target + 0x50) + " " + ReadLenUtf8(target + 0x58)).Trim();
		}
		catch (MemoryAccessException)
		{
			return "";
		}
	}

	public static ulong ReadPrimaryNation(IMemory m, ulong player)
	{
		try { return m.ReadPointer(player + PrimaryNationOffset); }
		catch (MemoryAccessException) { return 0; }
	}

	public static void SetPrimaryNation(GameData data, ulong player, ulong nationAddress, string oldNation, string newNation, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		data.Session.History.Apply(data.Memory, player + PrimaryNationOffset, BitConverter.GetBytes(nationAddress),
			new EditInfo(entityLabel, Build.T("Uyruk", "Nation"), oldNation, newNation), guarded: true);
	}

	public static IReadOnlyList<SpokenLanguage> ReadLanguages(IMemory m, ulong player)
	{
		var list = new List<SpokenLanguage>();
		try
		{
			ulong begin = m.ReadPointer(player + LanguagesVector);
			ulong end = m.ReadPointer(player + LanguagesVector + 8);
			if (begin == 0 || end <= begin || end - begin > 8 * 24 || (end - begin) % 8 != 0)
			{
				return list;
			}
			int count = (int)((end - begin) / 8);
			for (int i = 0; i < count; i++)
			{
				ulong entry = m.ReadPointer(begin + (ulong)(i * 8));
				if (entry == 0) continue;
				ulong langPtr = m.ReadPointer(entry);
				int fluency = Math.Clamp(m.Read<int>(entry + 8), 1, 10);
				string name = ReadLanguageName(m, langPtr);
				if (name.Length > 0)
				{
					list.Add(new SpokenLanguage(i, entry, langPtr, name, fluency));
				}
			}
		}
		catch (MemoryAccessException)
		{
		}
		return list;
	}

	public static void SetFluency(GameData data, SpokenLanguage lang, int fluency, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		int clamped = Math.Clamp(fluency, 1, 10);
		data.Session.History.Apply(data.Memory, lang.EntryAddress + 8, BitConverter.GetBytes(clamped),
			new EditInfo(entityLabel, $"{lang.Name}", $"{lang.Fluency}/10", $"{clamped}/10"), guarded: true);
	}

	public static void RemoveLanguage(GameData data, ulong player, int index, string langName, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		IMemory m = data.Memory;
		ulong begin = m.ReadPointer(player + LanguagesVector);
		ulong end = m.ReadPointer(player + LanguagesVector + 8);
		int count = (int)((end - begin) / 8);
		if (index < 0 || index >= count) return;
		using (data.Session.History.BeginBatch(Build.T($"{entityLabel}: Dil sil ({langName})", $"{entityLabel}: Remove language ({langName})")))
		{
			for (int i = index; i < count - 1; i++)
			{
				ulong nextPtr = m.ReadPointer(begin + (ulong)((i + 1) * 8));
				data.Session.History.Apply(m, begin + (ulong)(i * 8), BitConverter.GetBytes(nextPtr),
					new EditInfo(entityLabel, Build.T("Konuştuğu dil", "Language"), "", ""), guarded: true);
			}
			data.Session.History.Apply(m, player + LanguagesVector + 8, BitConverter.GetBytes(end - 8),
				new EditInfo(entityLabel, Build.T("Konuştuğu dil", "Language"), langName, "—"), guarded: true);
		}
	}

	private static string ReadLanguageName(IMemory m, ulong langPtr)
	{
		if (langPtr == 0) return "";
		try
		{
			ulong strPtr = m.ReadPointer(langPtr + 0x18);
			if (strPtr == 0) strPtr = m.ReadPointer(langPtr + 0x20);
			if (strPtr == 0) return "";
			int len = m.Read<int>(strPtr);
			if (len <= 0 || len > 80) return "";
			return Encoding.UTF8.GetString(m.ReadBytes(strPtr + 4, len));
		}
		catch (MemoryAccessException)
		{
			return "";
		}
	}
}
