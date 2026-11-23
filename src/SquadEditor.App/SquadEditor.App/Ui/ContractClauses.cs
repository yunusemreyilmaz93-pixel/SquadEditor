using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed record ContractClause(int Index, ulong Address, int AmountGbp, short Param, byte TypeId)
{
	public bool IsMoney => AmountGbp >= 0;

	public string Label => TypeId switch
	{
		0x00 => Build.T("Serbest Kalma Bedeli", "Minimum Fee Release Clause"),
		0x01 => Build.T("Serbest Kalma Bedeli (Yurtiçi)", "Minimum Fee Release Clause (Domestic)"),
		0x02 => Build.T("Küme Düşme Serbest Kalma", "Relegation Release Clause"),
		0x03 => Build.T("Yıllık Maaş Artışı (%)", "Yearly Wage Rise (%)"),
		0x04 => Build.T("Küme Düşme Maaş Kesintisi (%)", "Relegation Wage Drop (%)"),
		0x05 => Build.T("Üst Lige Çıkma Maaş Artışı (%)", "Promotion Wage Rise (%)"),
		0x0D => Build.T("Yabancı Kulüplere Serbest Kalma", "Release Clause (Foreign Clubs)"),
		0x0E => Build.T("Şampiyonlar Ligi Serbest Kalma", "Release Clause (Champions League)"),
		0x20 => Build.T("Maç Başı Bonus", "Appearance Fee"),
		0x21 => Build.T("Gol Bonusu", "Goal Bonus"),
		0x22 => Build.T("Gol Yememe Bonusu", "Clean Sheet Bonus"),
		0x23 => Build.T("Kullanılmayan Yedek Bonusu", "Unused Sub Fee"),
		0x26 => Build.T("Özel Performans / Yılın Takımı Bonusu", "Performance / Milestone Bonus"),
		_ => Build.T($"Sözleşme Maddesi #{TypeId}", $"Contract Clause #{TypeId}")
	};

	public string ValueDisplay => IsMoney
		? Money.Format(AmountGbp, false)
		: Param >= 0 ? $"%{Param}" : "—";
}

/// <summary>
/// Reads, edits, and removes existing clauses/bonuses in FULL_CONTRACT +0x68 (std::vector of 8-byte entries:
/// +0x00 i32 amount GBP (-1 if none), +0x04 i16 param/percent, +0x06 u8 type, +0x07 u8 flags).
/// </summary>
public static class ContractClauses
{
	private const long PlayerContract = 0x330;
	private const long ClausesVector = 0x68;
	private const int EntrySize = 8;

	public static IReadOnlyList<ContractClause> Read(IMemory m, ulong player)
	{
		var list = new List<ContractClause>();
		try
		{
			ulong contract = m.ReadPointer(player + PlayerContract);
			if (contract == 0) return list;
			ulong begin = m.ReadPointer(contract + ClausesVector);
			ulong end = m.ReadPointer(contract + ClausesVector + 8);
			if (begin == 0 || end <= begin || end - begin > EntrySize * 32 || (end - begin) % EntrySize != 0)
			{
				return list;
			}
			int count = (int)((end - begin) / EntrySize);
			byte[] raw = m.ReadBytes(begin, count * EntrySize);
			for (int i = 0; i < count; i++)
			{
				int o = i * EntrySize;
				int amt = BitConverter.ToInt32(raw, o);
				short param = BitConverter.ToInt16(raw, o + 4);
				byte type = raw[o + 6];
				list.Add(new ContractClause(i, begin + (ulong)o, amt, param, type));
			}
		}
		catch (MemoryAccessException)
		{
		}
		return list;
	}

	public static void UpdateValue(GameData data, ContractClause clause, string input, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		string cleaned = input.Trim().Replace(" ", "").Replace("£", "").Replace("€", "").Replace("$", "").Replace("₺", "").Replace("%", "").Replace(",", ".");
		if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double val) || val < 0)
		{
			throw new FormatException(Build.T("Geçerli bir sayı gir.", "Enter a valid number."));
		}
		if (clause.IsMoney)
		{
			int gbp = (int)Math.Clamp(Math.Round(Money.FromDisplay(val, false)), 0, 2_000_000_000);
			data.Session.History.Apply(data.Memory, clause.Address, BitConverter.GetBytes(gbp),
				new EditInfo(entityLabel, clause.Label, clause.ValueDisplay, Money.Format(gbp, false)), guarded: true);
		}
		else
		{
			short p = (short)Math.Clamp(Math.Round(val), 0, 100);
			data.Session.History.Apply(data.Memory, clause.Address + 4, BitConverter.GetBytes(p),
				new EditInfo(entityLabel, clause.Label, clause.ValueDisplay, $"%{p}"), guarded: true);
		}
	}

	public static void Remove(GameData data, ulong player, int index, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		IMemory m = data.Memory;
		ulong contract = m.ReadPointer(player + PlayerContract);
		if (contract == 0) return;
		ulong begin = m.ReadPointer(contract + ClausesVector);
		ulong end = m.ReadPointer(contract + ClausesVector + 8);
		int count = (int)((end - begin) / EntrySize);
		if (index < 0 || index >= count) return;
		byte[] raw = m.ReadBytes(begin, count * EntrySize);
		using (data.Session.History.BeginBatch(Build.T($"{entityLabel}: Sözleşme maddesi sil", $"{entityLabel}: Remove clause")))
		{
			for (int i = index; i < count - 1; i++)
			{
				byte[] next = raw.AsSpan((i + 1) * EntrySize, EntrySize).ToArray();
				data.Session.History.Apply(m, begin + (ulong)(i * EntrySize), next, new EditInfo(entityLabel, Build.T("Sözleşme maddesi", "Clause"), "", ""), guarded: true);
			}
			data.Session.History.Apply(m, contract + ClausesVector + 8, BitConverter.GetBytes(end - EntrySize),
				new EditInfo(entityLabel, Build.T("Sözleşme maddesi", "Clause"), $"{count}", $"{count - 1}"), guarded: true);
		}
	}
}
