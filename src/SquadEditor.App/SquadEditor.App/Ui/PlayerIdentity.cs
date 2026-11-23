using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed record SpokenLanguage(int Index, ulong EntryAddress, ulong LanguagePtr, string Name, int Fluency);

/// <summary>
/// Reads and edits a player's Primary Nation (player +0x2F0 -> db::NATION*) and Spoken Languages
/// (player +0x378 = PersonBlock +0xF0 std::vector of pointers to { db::LANGUAGE* +0x00, i32 fluency 1..10 +0x08 }).
/// Verified live on FM27 27.0.1. Every write goes through guarded EditHistory (Ctrl+Z undoes).
/// </summary>
public static class PlayerIdentity
{
	public const long PrimaryNationOffset = 0x2F0;
	private const long LanguagesVector = 0x378;

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
