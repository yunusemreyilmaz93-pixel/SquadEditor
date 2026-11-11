using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed record Injury(string Name, DateTime? Started, int ShortestDays, int LongestDays)
{
	public DateTime? BackBy => Started?.AddDays(LongestDays);
}

public sealed record Ban(DateTime? Started, int Matches);

/// <summary>
/// A player's injuries and bans. ACTUAL_PLAYER +0xF8 points at a record holding two lists
/// (begin, end, capacity): +0x00 pointers to current injuries, +0x18 32-byte absences (a ban has
/// 0xFFFF at +0x16 and the matches left at +0x1C). Healing empties the injury list and lifting
/// takes the bans out of the absences, as the game does when they are over. Every write goes
/// through the edit history, so Ctrl+Z puts them back.
/// </summary>
public static class Medical
{
	private const long RecordOffset = 0xF8;
	private const int AbsenceSize = 32;

	public static IReadOnlyList<Injury> Injuries(IMemory memory, ulong player)
	{
		var list = new List<Injury>();
		try
		{
			ulong record = memory.ReadPointer(player + RecordOffset);
			if (record == 0)
			{
				return list;
			}
			ulong begin = memory.ReadPointer(record), end = memory.ReadPointer(record + 8);
			for (ulong at = begin; at < end && at - begin < 8 * 32; at += 8)
			{
				ulong injury = memory.ReadPointer(at);
				if (injury == 0)
				{
					continue;
				}
				ulong kind = memory.ReadPointer(injury + 0x08);
				string name = kind == 0 ? "" : Text(memory, memory.ReadPointer(kind + 0x18));
				list.Add(new Injury(name, Date(memory, injury + 0x20), memory.Read<ushort>(injury + 0x2A), memory.Read<ushort>(injury + 0x28)));
			}
		}
		catch (MemoryAccessException)
		{
		}
		return list;
	}

	public static IReadOnlyList<Ban> Bans(IMemory memory, ulong player)
	{
		var list = new List<Ban>();
		foreach (ulong entry in Absences(memory, player, out _))
		{
			if (memory.Read<ushort>(entry + 0x16) == 0xFFFF)
			{
				list.Add(new Ban(Date(memory, entry + 0x10), memory.Read<byte>(entry + 0x1C)));
			}
		}
		return list;
	}

	/// <summary>Empties the list of current injuries. Returns false when there was none.</summary>
	public static bool Heal(GameData data, ulong player, string label)
	{
		IMemory memory = data.Memory;
		ulong record = memory.ReadPointer(player + RecordOffset);
		if (record == 0)
		{
			return false;
		}
		ulong begin = memory.ReadPointer(record), end = memory.ReadPointer(record + 8);
		if (end <= begin)
		{
			return false;
		}
		Write(data, record + 8, BitConverter.GetBytes(begin), label, Build.T("Sakatlık", "Injury"),
			Build.T($"{(end - begin) / 8} sakatlık", $"{(end - begin) / 8} injuries"), Build.T("iyileşti", "healed"));
		return true;
	}

	/// <summary>Takes the bans out of the absences; other absences move to the front.</summary>
	public static bool LiftBans(GameData data, ulong player, string label)
	{
		IMemory memory = data.Memory;
		List<ulong> entries = Absences(memory, player, out ulong record);
		var keep = entries.Where(e => memory.Read<ushort>(e + 0x16) != 0xFFFF).Select(e => memory.ReadBytes(e, AbsenceSize)).ToList();
		if (record == 0 || keep.Count == entries.Count)
		{
			return false;
		}
		ulong begin = memory.ReadPointer(record + 0x18);
		using (data.Session.History.InBatch ? null : data.Session.History.BeginBatch(label))
		{
			for (int i = 0; i < keep.Count; i++)
			{
				Write(data, begin + (ulong)(i * AbsenceSize), keep[i], label, Build.T("Ceza", "Ban"), "", "");
			}
			Write(data, record + 0x20, BitConverter.GetBytes(begin + (ulong)(keep.Count * AbsenceSize)), label, Build.T("Ceza", "Ban"),
				Build.T($"{entries.Count - keep.Count} ceza", $"{entries.Count - keep.Count} bans"), Build.T("kaldırıldı", "lifted"));
		}
		return true;
	}

	/// <summary>Short status for lists: "Sakat 35 gün", "Cezalı 2 maç".</summary>
	public static string Status(IMemory memory, ulong player, DateTime today)
	{
		var injuries = Injuries(memory, player);
		if (injuries.Count > 0)
		{
			int days = injuries.Max(i => i.BackBy is { } back ? (int)Math.Ceiling((back - today).TotalDays) : i.LongestDays);
			return days > 0 ? Build.T($"Sakat · {days} gün", $"Injured · {days} d") : Build.T("Sakat", "Injured");
		}
		var bans = Bans(memory, player);
		return bans.Count > 0 ? Build.T($"Cezalı · {bans.Sum(b => b.Matches)} maç", $"Banned · {bans.Sum(b => b.Matches)} m") : "";
	}

	private static List<ulong> Absences(IMemory memory, ulong player, out ulong record)
	{
		var list = new List<ulong>();
		record = 0;
		try
		{
			record = memory.ReadPointer(player + RecordOffset);
			if (record == 0)
			{
				return list;
			}
			ulong begin = memory.ReadPointer(record + 0x18), end = memory.ReadPointer(record + 0x20);
			for (ulong at = begin; at + AbsenceSize <= end && at - begin < AbsenceSize * 64; at += AbsenceSize)
			{
				list.Add(at);
			}
		}
		catch (MemoryAccessException)
		{
		}
		return list;
	}

	private static void Write(GameData data, ulong address, byte[] bytes, string label, string field, string oldText, string newText)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		data.Session.History.Apply(data.Memory, address, bytes, new EditInfo(label, field, oldText, newText));
	}

	private static DateTime? Date(IMemory memory, ulong address)
	{
		int day = memory.Read<ushort>(address) & 0x1FF, year = memory.Read<ushort>(address + 2);
		return year > 1900 && year < 3000 && day >= 1 ? new DateTime(year, 1, 1).AddDays(day - 1) : null;
	}

	private static string Text(IMemory memory, ulong pointer)
	{
		if (pointer == 0)
		{
			return "";
		}
		int length = memory.Read<int>(pointer);
		return length is > 0 and < 256 ? Encoding.UTF8.GetString(memory.ReadBytes(pointer + 4, length)) : "";
	}
}
