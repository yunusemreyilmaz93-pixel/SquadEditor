using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>What a player's loan says: where he is, until when, and the option to buy.</summary>
public sealed record LoanInfo(ulong Contract, ulong FromClub, ulong ToClub, DateTime? Until, int OptionFee);

public static partial class Transfers
{
	private const long LoanUntil = 0x48, LoanOptionFee = 0xC0;

	/// <summary>The writes that add <paramref name="item"/> to the end of <paramref name="list"/>, where its memory has room.</summary>
	private static void Append(IMemory m, PlayerList list, ulong item, string owner, List<PlannedWrite> writes, string field)
	{
		ulong room = Room(m, list);
		if ((ulong)list.Items.Length >= room)
		{
			throw Refuse($"{owner} listesi dolu ({list.Items.Length}) ve oyunun belleğinde yer yok. Önce oradan birini çıkar (serbest bırak), sonra tekrar dene.",
				$"{owner}'s list is full ({list.Items.Length}) and the game's memory has no room. Take someone out of it first (release), then try again.");
		}
		writes.Add(new PlannedWrite(list.End, m.ReadBytes(list.End, 8), GameWrite.U64(item), field));
		ulong newEnd = list.End + 8;
		writes.Add(new PlannedWrite(list.Header + 8, Concat(list.End, list.Capacity), Concat(newEnd, Math.Max(list.Capacity, newEnd)), field));
	}

	/// <summary>The player's loan contract (player +0x338 record, +0x00), 0 when he is not on loan.</summary>
	public static ulong LoanContract(IMemory m, ulong player)
	{
		try
		{
			ulong record = m.ReadPointer(player + LoanRecord);
			if (record == 0)
			{
				return 0;
			}
			ulong loan = m.ReadPointer(record);
			return loan != 0 && m.ReadPointer(loan + ContractOwner) == player + PersonBlock ? loan : 0;
		}
		catch (MemoryAccessException)
		{
			return 0;
		}
	}

	public static LoanInfo? Loan(IMemory m, ulong player)
	{
		ulong loan = LoanContract(m, player);
		if (loan == 0)
		{
			return null;
		}
		ulong own = m.ReadPointer(player + PlayerContract);
		ulong from = own == 0 ? 0 : m.ReadPointer(m.ReadPointer(own + ContractTeam) + TeamClub);
		ulong to = m.ReadPointer(m.ReadPointer(loan + ContractTeam) + TeamClub);
		return new LoanInfo(loan, from, to, ReadDate(m.ReadBytes(loan + LoanUntil, 4)), m.Read<int>(loan + LoanOptionFee));
	}

	/// <summary>
	/// Recalling a player from loan the way FMLibreEditor found safe: the loan's last day becomes
	/// today and its option to buy is taken away; the game ends the loan itself the next day.
	/// </summary>
	public static MovePlan PlanRecall(GameData data, ulong player)
	{
		IMemory m = data.Memory;
		LoanInfo loan = Loan(m, player) ?? throw Refuse("Oyuncu kiralık değil.", "The player is not on loan.");
		string name = data.Read(player, data.PlayerDef, "Name");
		byte[] until = m.ReadBytes(loan.Contract + LoanUntil, 4);
		var writes = new List<PlannedWrite>
		{
			new PlannedWrite(loan.Contract + LoanUntil, until, Date(until, 0, data.GameDate), Build.T("Kiralık bitişi", "Loan ends")),
			new PlannedWrite(loan.Contract + LoanOptionFee, m.ReadBytes(loan.Contract + LoanOptionFee, 4), BitConverter.GetBytes(-1), Build.T("Satın alma opsiyonu", "Option to buy"))
		};
		var steps = new List<string>
		{
			Build.T($"Kiralık {data.GameDate:dd.MM.yyyy} günü biter; oyun onu bir gün sonra kulübüne döndürür.", $"The loan ends on {data.GameDate:dd.MM.yyyy}; the game sends him back the next day."),
			Build.T("Satın alma opsiyonu kaldırılır (yoksa kiralayan kulüp onu satın alabilir).", "The option to buy is removed (otherwise the loan club could buy him).")
		};
		return new MovePlan(Build.T($"{name}, {ClubName(data, loan.FromClub)} kulübüne geri çağrılır.", $"{name} is recalled to {ClubName(data, loan.FromClub)}."),
			steps, writes.Where(w => !w.Expected.AsSpan().SequenceEqual(w.Bytes)).ToList(), loan.FromClub, 0);
	}

	/// <summary>
	/// Making a loan permanent: he stays where he plays; his own contract moves to that club's
	/// senior team with the squad status and number of his loan contract, his old club's lists
	/// and captaincies lose him, and the loan is let go as the game lets one go (the record's
	/// loan slot emptied, or the record pointer when it holds nothing else).
	/// </summary>
	public static MovePlan PlanPermanent(GameData data, ulong player)
	{
		IMemory m = data.Memory;
		LoanInfo loan = Loan(m, player) ?? throw Refuse("Oyuncu kiralık değil.", "The player is not on loan.");
		string name = data.Read(player, data.PlayerDef, "Name");
		ulong contract = m.ReadPointer(player + PlayerContract);
		if (contract == 0 || m.ReadPointer(contract + ContractOwner) != player + PersonBlock || m.Read<byte>(contract + ContractJob) != 1)
		{
			throw Refuse("Sözleşme beklenen yapıda değil.", "The contract is not as expected.");
		}
		ulong employer = m.ReadPointer(contract + ContractTeam);
		if (m.ReadPointer(contract + ContractSenior) != employer)
		{
			throw Refuse("Oyuncu-antrenörler taşınmıyor.", "Player-coaches are not moved.");
		}
		ulong team = m.ReadPointer(player + PlayerTeam);
		if (team == 0 || m.ReadPointer(team + TeamClub) != loan.ToClub || loan.FromClub == 0 || loan.FromClub == loan.ToClub)
		{
			throw Refuse("Oyuncu kiralık olduğu kulüpte görünmüyor.", "The player does not play for the club he is on loan to.");
		}
		List<ulong> stayTeams = Teams(m, loan.ToClub);
		if (stayTeams.Count == 0)
		{
			throw Refuse("Kulübün A takımı bulunamadı.", "The club's senior team was not found.");
		}
		ulong senior = stayTeams[0];
		if (!ReadList(m, team).Items.Contains(player))
		{
			throw Refuse("Oyuncu oynadığı takımın listesinde bulunamadı.", "The player is not in the list of the team he plays for.");
		}
		string oldName = ClubName(data, loan.FromClub), newName = ClubName(data, loan.ToClub);
		string field = Build.T("Kulüp", "Club");
		var writes = new List<PlannedWrite>();
		var steps = new List<string>();
		ulong block = player + PersonBlock;
		List<ulong> oldTeams = Teams(m, loan.FromClub);
		foreach (ulong t in oldTeams)
		{
			PlayerList list = ReadList(m, t);
			int at = Array.IndexOf(list.Items, player);
			if (at >= 0)
			{
				Removal(list, at, writes, field);
				steps.Add(Build.T($"{oldName} {TeamLabel(data, t)} listesinden çıkar.", $"Leaves {oldName} {TeamLabel(data, t)}."));
			}
			foreach (long post in new[] { TeamCaptain, TeamVice })
			{
				if (m.ReadPointer(t + (ulong)post) == block)
				{
					writes.Add(new PlannedWrite(t + (ulong)post, GameWrite.U64(block), GameWrite.U64(0), Build.T("Kaptanlık", "Captaincy")));
				}
			}
		}
		SelectionWrites(m, player, oldTeams, writes);

		byte[] own = m.ReadBytes(contract, ContractSize);
		writes.Add(new PlannedWrite(contract + ContractTeam, GameWrite.U64(employer), GameWrite.U64(senior), field, oldName, newName));
		writes.Add(new PlannedWrite(contract + ContractSenior, GameWrite.U64(employer), GameWrite.U64(senior), field, oldName, newName));
		writes.Add(new PlannedWrite(contract + 0x44, own[0x44..0x48], Date(own, 0x44, data.GameDate), Build.T("İmza tarihi", "Signed")));
		writes.Add(new PlannedWrite(contract + ContractJoined, own[0x4C..0x50], Date(own, ContractJoined, data.GameDate), Build.T("Katılma tarihi", "Joined")));
		writes.Add(new PlannedWrite(contract + 0x54, new[] { own[0x54] }, new[] { m.Read<byte>(loan.Contract + 0x54) }, Build.T("Kadro durumu", "Squad status")));
		writes.Add(new PlannedWrite(contract + ContractNumber, new[] { own[ContractNumber] }, new[] { m.Read<byte>(loan.Contract + ContractNumber) }, Build.T("Forma numarası", "Squad number")));
		writes.Add(new PlannedWrite(contract + ContractListing, new[] { own[ContractListing] }, new[] { (byte)(own[ContractListing] & ~3) }, Build.T("Transfer listesi", "Transfer list")));

		ulong record = m.ReadPointer(player + LoanRecord);
		bool alone = new long[] { 0x08, 0x10, 0x18 }.All(o => m.ReadPointer(record + (ulong)o) == 0);
		if (alone)
		{
			writes.Add(new PlannedWrite(player + LoanRecord, GameWrite.U64(record), GameWrite.U64(0), Build.T("Kiralık", "Loan")));
		}
		else
		{
			writes.Add(new PlannedWrite(record, GameWrite.U64(loan.Contract), GameWrite.U64(0), Build.T("Kiralık", "Loan")));
		}
		steps.Add(Build.T("Sözleşmesi yeni kulübe geçer (maaş ve bitiş aynı), kiralıktaki forma numarası ve kadro durumu kalır.",
			"His contract moves to the new club (same wage and end), keeping his loan's squad number and status."));
		steps.Add(Build.T("Kiralık sözleşmesi kapanır.", "The loan contract is let go."));
		return new MovePlan(Build.T($"{name}: {oldName} → {newName} (kiralık kalıcı olur).", $"{name}: {oldName} → {newName} (loan made permanent)."),
			steps, writes.Where(w => !w.Expected.AsSpan().SequenceEqual(w.Bytes)).ToList(), loan.ToClub, team);
	}

	/// <summary>
	/// Signing a free agent: a slot of the game's own pool of contracts is taken as the game takes
	/// one, and filled with a copy of the contract of the club's senior player nearest him in
	/// ability (a goalkeeper's for a goalkeeper, no youth status or youth contract for one of 21 or
	/// more), made his: owner, teams, signed and joined today, ending 30 June after next season,
	/// no squad number, no loyalty bonus, off the lists, and without the other's clauses, bonuses,
	/// unhappiness record or other records of its own (+0x28, +0x30, +0x68, +0x80, +0x98, +0xA0).
	/// </summary>
	private static MovePlan Sign(GameData data, ulong player, ulong club, string name)
	{
		IMemory m = data.Memory;
		List<ulong> teams = Teams(m, club);
		if (teams.Count == 0)
		{
			throw Refuse("Kulübün A takımı bulunamadı.", "The club's senior team was not found.");
		}
		ulong senior = teams[0];
		string clubName = ClubName(data, club);
		string field = Build.T("Kulüp", "Club");
		PlayerList target = ReadList(m, senior);
		if (target.Items.Contains(player))
		{
			throw Refuse("Oyuncu zaten o takımın listesinde.", "The player is in that team's list already.");
		}
		if (m.ReadPointer(player + LoanRecord) is var rec && rec != 0 && m.ReadPointer(rec) != 0)
		{
			throw Refuse("Oyuncunun kiralık sözleşmesi var.", "The player has a loan contract.");
		}
		bool goalkeeper = m.Read<byte>(player + 0x150) >= 15;
		int ability = m.Read<ushort>(player + 0x264);
		int age = data.Players.FirstOrDefault(p => p.Address == player)?.Age ?? 25;
		bool young = age > 0 && age < 21;
		byte[] keeperStatus = { 15, 16, 17, 20, 21 }, seniorStatus = { 1, 2, 3, 4, 5, 7, 9 }, youthStatus = { 10, 11, 13 };
		ulong model = 0, anyModel = 0;
		int best = int.MaxValue, bestAny = int.MaxValue;
		foreach (ulong other in target.Items)
		{
			try
			{
				ulong c = m.ReadPointer(other + PlayerContract);
				if (c == 0 || m.ReadPointer(c + ContractOwner) != other + PersonBlock || m.Read<byte>(c + ContractJob) != 1
					|| m.ReadPointer(c + ContractTeam) != senior || m.ReadPointer(c + ContractSenior) != senior)
				{
					continue;
				}
				byte status = m.Read<byte>(c + 0x54), kind = m.Read<byte>(c + 0xC3);
				int distance = Math.Abs(m.Read<ushort>(other + 0x264) - ability);
				bool fits = ((goalkeeper ? keeperStatus : seniorStatus).Contains(status) || young && youthStatus.Contains(status)) && (young || kind != 3);
				if (fits && distance < best)
				{
					(model, best) = (c, distance);
				}
				if (distance < bestAny)
				{
					(anyModel, bestAny) = (c, distance);
				}
			}
			catch (MemoryAccessException)
			{
			}
		}
		byte? newStatus = null;
		if (model == 0)
		{
			model = anyModel;
			newStatus = goalkeeper ? (byte)20 : (byte)4;
		}
		if (model == 0)
		{
			throw Refuse("Kulübün A takımında örnek alınacak sözleşme yok.", "The club's senior team has no contract to model a new one on.");
		}
		GamePool pool = GamePool.Find(m, ContractSize, model);
		var writes = new List<PlannedWrite>();
		(ulong slot, PlannedWrite header) = pool.Take(m);
		writes.Add(header);

		byte[] bytes = m.ReadBytes(model, ContractSize);
		void Put(long offset, byte[] value) => value.CopyTo(bytes, (int)offset);
		DateTime today = data.GameDate;
		int endYear = today.Month <= 6 ? today.Year + 1 : today.Year + 2;
		Put(ContractOwner, GameWrite.U64(player + PersonBlock));
		Put(ContractTeam, GameWrite.U64(senior));
		Put(ContractSenior, GameWrite.U64(senior));
		bytes[ContractJob] = 1;
		Put(0x44, Date(bytes, 0x44, today));
		Put(ContractJoined, Date(bytes, ContractJoined, today));
		Put(0x48, Date(bytes, 0x48, new DateTime(endYear, 6, 30)));
		Put(0x38, new byte[4]);
		if (newStatus is { } st)
		{
			bytes[0x54] = st;
		}
		bytes[ContractNumber] = 0xFF;
		bytes[ContractListing] &= unchecked((byte)~3);
		foreach (long offset in new long[] { 0x28, 0x30, 0x98, 0xA0 })
		{
			Put(offset, new byte[8]);
		}
		Put(0x68, new byte[24]);
		Put(0x80, new byte[24]);
		writes.Add(new PlannedWrite(slot, m.ReadBytes(slot, ContractSize), bytes, Build.T("Sözleşme", "Contract")));

		Append(m, target, player, clubName, writes, field);
		writes.Add(new PlannedWrite(player + PlayerContract, GameWrite.U64(0), GameWrite.U64(slot), Build.T("Sözleşme", "Contract")));
		writes.Add(new PlannedWrite(player + PlayerTeam, GameWrite.U64(0), GameWrite.U64(senior), field, "", clubName));
		int wage = BitConverter.ToInt32(bytes, 0x20);
		var steps = new List<string>
		{
			Build.T($"Oyunun sözleşme havuzundan yeni bir sözleşme alınır ({pool.Free} boş yer).", $"A new contract is taken from the game's contract pool ({pool.Free} free)."),
			Build.T($"Sözleşme: {Money.Format(wage, true)}, bitiş 30.06.{endYear}, forma numarası yok (sonra düzenlenebilir).",
				$"Contract: {Money.Format(wage, true)}, until 30.06.{endYear}, no squad number (editable afterwards)."),
			Build.T($"{clubName} A takım listesine eklenir ({target.Items.Length} → {target.Items.Length + 1}).", $"Joins {clubName}'s senior team ({target.Items.Length} → {target.Items.Length + 1}).")
		};
		return new MovePlan(Build.T($"{name}, {clubName} ile sözleşme imzalar.", $"{name} signs for {clubName}."), steps, writes, club, senior);
	}

	/// <summary>A date (u16 day of year with the flags of the bytes it replaces, u16 year).</summary>
	private static byte[] Date(byte[] record, long offset, DateTime day)
	{
		ushort flags = (ushort)(BitConverter.ToUInt16(record, (int)offset) & ~0x1FF);
		var date = new byte[4];
		BitConverter.GetBytes((ushort)(flags | day.DayOfYear)).CopyTo(date, 0);
		BitConverter.GetBytes((ushort)day.Year).CopyTo(date, 2);
		return date;
	}

	private static DateTime? ReadDate(byte[] raw)
	{
		int day = BitConverter.ToUInt16(raw, 0) & 0x1FF, year = BitConverter.ToUInt16(raw, 2);
		return year > 1900 && year < 3000 && day >= 1 ? new DateTime(year, 1, 1).AddDays(day - 1) : null;
	}
}
