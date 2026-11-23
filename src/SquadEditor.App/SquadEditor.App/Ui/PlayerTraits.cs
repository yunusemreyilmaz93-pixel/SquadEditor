using System;
using System.Collections.Generic;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>
/// Player preferred moves (traits) bitmask at ACTUAL_PLAYER +0x1EC (u64).
/// Verified on Haaland (0x7812). Every toggle goes through guarded EditHistory so Ctrl+Z undoes it.
/// </summary>
public static class PlayerTraits
{
	private const long TraitsOffset = 0x1EC;

	public static readonly (int Bit, string Tr, string En)[] All =
	{
		(0, "Kısa ve basit paslar oynar", "Plays short simple passes"),
		(1, "Sert şut çeker", "Shoots with power"),
		(2, "Plase vuruş yapar", "Places shots"),
		(3, "Aşırtma vuruş sever", "Likes to lob keeper"),
		(4, "Ofsayt tuzağını bozmaya çalışır", "Likes to try to beat offside trap"),
		(5, "İlk fırsatta şut çeker", "Shoots from distance"),
		(6, "Tek pas oynar", "Plays one-twos"),
		(7, "Kaleciyi çalımlamaya çalışır", "Likes to round keeper"),
		(8, "Öldürücü ara pas dener", "Tries killer balls often"),
		(9, "Oyunun yönünü değiştirir", "Likes to switch ball to other flank"),
		(10, "Kanattan içeri kat eder", "Cuts inside from wing"),
		(11, "Ceza sahasına geç girer", "Arrives late in opponent's area"),
		(12, "Boş alanlara koşu yapar", "Moves into channels"),
		(13, "Topu ayağında tutar", "Dwells on ball"),
		(14, "Sırtı dönük oynar", "Plays with back to goal"),
		(15, "Oyunu yavaşlatır", "Dictates tempo"),
		(16, "Kayarak müdahale yapmaz", "Does not dive into tackles"),
		(17, "Kayarak müdahale yapar", "Dives into tackles"),
		(18, "Topu sık sık çalımlar", "Runs with ball often"),
		(19, "Zayıf ayağını kullanmaktan kaçınır", "Avoids using weaker foot"),
		(20, "Frikikleri sert vurur", "Hits free kicks with power"),
		(21, "Uzun taç atar", "Possesses long flat throw")
	};

	public static ulong ReadMask(IMemory m, ulong player)
	{
		try
		{
			return m.Read<ulong>(player + TraitsOffset);
		}
		catch (MemoryAccessException)
		{
			return 0;
		}
	}

	public static void Toggle(GameData data, ulong player, int bit, string traitName, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		ulong cur = ReadMask(data.Memory, player);
		ulong next = cur ^ (1UL << bit);
		bool added = (next & (1UL << bit)) != 0;
		data.Session.History.Apply(data.Memory, player + TraitsOffset, BitConverter.GetBytes(next),
			new EditInfo(entityLabel, traitName, added ? "—" : "✓", added ? "✓" : "—"), guarded: true);
	}
}
