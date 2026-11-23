using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>A move or release worked out from the game as it is now, ready to be written.</summary>
public sealed record MovePlan(string Summary, IReadOnlyList<string> Steps, IReadOnlyList<PlannedWrite> Writes, ulong NewClub, ulong NewTeam);

/// <summary>
/// Moving a player to another club's senior team, or releasing him.
///
/// A player points at the team he plays for (+0x130) and at his contract (+0x330); the
/// contract (FULL_CONTRACT) points back at his person block (+0x08) and at the team that
/// employs him (+0x10, senior team +0x18). Each team keeps its players as a list
/// (begin, end, capacity) at +0x38, its captain and vice-captain (person blocks) at +0x88 and
/// +0x90. A club's teams are a list at +0x18, the senior team first.
///
/// A move takes the player out of every list of his club's teams that holds him (the later
/// players move up one), empties the captaincies he holds there, adds him to the new club's
/// senior team, and points his team and his contract's teams at it, with no squad number,
/// off the transfer and loan lists and joined today. A release leaves him with no team and no
/// contract, as the game's own free agents are; the contract is left as it was.
///
/// The player only goes where the game's own memory has room: below the list's capacity, or
/// in the unused rest of the allocator's block (sizes measured on FM27's squad lists). No
/// memory is allocated in the game; a full list is refused.
///
/// Leaving a club a human manager runs, the player is also taken out of the match engine's
/// prepared selections (simatch::PREPARED_TACTICS_CREATOR_ARRAY: +0x08 list, +0x28 26 u32
/// person ids, 0xFFFFFFFF empty) that are mostly that club's players, as the game does when
/// a player leaves.
///
/// Only simple cases move: a player under a plain contract with the club he plays for (no
/// loans, no player-coaches).
/// </summary>
public static partial class Transfers
{
	private const long PlayerTeam = 0x130, PlayerContract = 0x330, PersonBlock = 0x288, PersonId = 0x290;
	private const long ContractOwner = 0x08, ContractTeam = 0x10, ContractSenior = 0x18, ContractJob = 0x26;
	private const long ContractJoined = 0x4C, ContractListing = 0x57, ContractNumber = 0x5D;
	private const long LoanRecord = 0x338;
	private const int ContractSize = 0xC8;
	private const long TeamClub = 0x30, TeamPlayers = 0x38, TeamCaptain = 0x88, TeamVice = 0x90;
	private const long ClubTeams = 0x18, ClubWomen = 0xB9;
	private const uint Empty = 0xFFFFFFFF;
	private const int Slots = 26;
	private static readonly ulong[] BlockSizes = { 144, 160, 192, 224, 256, 288, 320, 384, 448, 512 };

	private sealed record PlayerList(ulong Team, ulong Header, ulong Begin, ulong End, ulong Capacity, ulong[] Items);

	/// <summary>Works out moving the player to <paramref name="club"/>'s senior team (with optional GBP transfer fee), or releasing him when club is 0. Nothing is written.</summary>
	public static MovePlan Plan(GameData data, ulong player, ulong club, long feeGbp = 0)
	{
		IMemory m = data.Memory;
		string name = data.Read(player, data.PlayerDef, "Name");
		ulong team = m.ReadPointer(player + PlayerTeam);
		ulong contract = m.ReadPointer(player + PlayerContract);
		if (team == 0 && contract == 0)
		{
			if (club == 0)
			{
				throw Refuse("Oyuncu zaten serbest.", "The player is a free agent already.");
			}
			return Sign(data, player, club, name);
		}
		if (team == 0 || contract == 0)
		{
			throw Refuse("Oyuncunun takımı ya da sözleşmesi eksik.", "The player has a team or a contract missing.");
		}
		if (LoanContract(m, player) != 0)
		{
			throw Refuse("Oyuncu kiralık; kiralık işlemleri için Kiralık kartını kullan.", "The player is on loan; use the Loan card.");
		}
		ulong block = player + PersonBlock;
		if (m.ReadPointer(contract + ContractOwner) != block || m.Read<byte>(contract + ContractJob) != 1)
		{
			throw Refuse("Sözleşme beklenen yapıda değil.", "The contract is not as expected.");
		}
		ulong employer = m.ReadPointer(contract + ContractTeam);
		if (m.ReadPointer(contract + ContractSenior) != employer)
		{
			throw Refuse("Oyuncu-antrenörler taşınmıyor.", "Player-coaches are not moved.");
		}
		ulong oldClub = m.ReadPointer(team + TeamClub);
		if (oldClub == 0 || m.ReadPointer(employer + TeamClub) != oldClub)
		{
			throw Refuse("Oyuncu kiralık; kiralık oyuncular taşınmıyor.", "The player is on loan; loans are not moved.");
		}
		if (club == oldClub)
		{
			throw Refuse("Oyuncu zaten bu kulüpte.", "The player is at this club already.");
		}

		var writes = new List<PlannedWrite>();
		var steps = new List<string>();
		string oldName = ClubName(data, oldClub);
		string field = Build.T("Kulüp", "Club");

		// Out of every list of his club's teams that holds him; captaincies there emptied.
		List<ulong> oldTeams = Teams(m, oldClub);
		bool listed = false;
		foreach (ulong t in oldTeams)
		{
			PlayerList list = ReadList(m, t);
			int at = Array.IndexOf(list.Items, player);
			if (at >= 0)
			{
				if (list.Items.Count(x => x == player) != 1)
				{
					throw Refuse("Oyuncu takım listesinde birden fazla görünüyor.", "The player appears in a team list more than once.");
				}
				listed |= t == team;
				Removal(list, at, writes, field);
				steps.Add(Build.F("{0} {1} listesinden çıkar ({2} → {3}).", "Leaves {0} {1} ({2} → {3}).", oldName, TeamLabel(data, t), list.Items.Length, list.Items.Length - 1));
			}
			foreach (long post in new[] { TeamCaptain, TeamVice })
			{
				if (m.ReadPointer(t + (ulong)post) == block)
				{
					writes.Add(new PlannedWrite(t + (ulong)post, GameWrite.U64(block), GameWrite.U64(0), Build.T("Kaptanlık", "Captaincy")));
					steps.Add(post == TeamCaptain ? Build.T("Kaptanlığı boşalır.", "His captaincy is left empty.") : Build.T("Kaptan yardımcılığı boşalır.", "His vice-captaincy is left empty."));
				}
			}
		}
		if (!listed)
		{
			throw Refuse("Oyuncu kendi takımının listesinde bulunamadı.", "The player is not in his team's list.");
		}

		int emptied = SelectionWrites(m, player, oldTeams, writes);
		if (emptied > 0)
		{
			steps.Add(Build.F("Maç kadrosu seçimlerinden çıkar ({0} kopya).", "Taken out of the match selections ({0} copies).", emptied));
		}

		ulong senior = 0;
		string summary;
		if (club == 0)
		{
			writes.Add(new PlannedWrite(player + PlayerTeam, GameWrite.U64(team), GameWrite.U64(0), field, oldName, ""));
			writes.Add(new PlannedWrite(player + PlayerContract, GameWrite.U64(contract), GameWrite.U64(0), Build.T("Sözleşme", "Contract")));
			steps.Add(Build.T("Takımı ve sözleşmesi kaldırılır: serbest oyuncu olur.", "His team and contract are removed: he becomes a free agent."));
			summary = Build.F("{0}, {1} kulübünden serbest bırakılır.", "{0} is released by {1}.", name, oldName);
		}
		else
		{
			if (m.Read<byte>(club + ClubWomen) != m.Read<byte>(oldClub + ClubWomen))
			{
				throw Refuse("Erkek ve kadın kulüpleri arasında taşıma yapılmaz.", "Players do not move between men's and women's clubs.");
			}
			List<ulong> newTeams = Teams(m, club);
			if (newTeams.Count == 0)
			{
				throw Refuse("Kulübün A takımı bulunamadı.", "The club's senior team was not found.");
			}
			senior = newTeams[0];
			string newName = ClubName(data, club);
			PlayerList target = ReadList(m, senior);
			if (target.Items.Contains(player))
			{
				throw Refuse("Oyuncu zaten o takımın listesinde.", "The player is in that team's list already.");
			}
			Append(data, m, target, player, newName, writes, steps, field);
			steps.Add(Build.F("{0} A takımına katılır.", "Joins {0}'s senior team.", newName));

			writes.Add(new PlannedWrite(player + PlayerTeam, GameWrite.U64(team), GameWrite.U64(senior), field, oldName, newName));
			writes.Add(new PlannedWrite(contract + ContractTeam, GameWrite.U64(employer), GameWrite.U64(senior), field, oldName, newName));
			writes.Add(new PlannedWrite(contract + ContractSenior, GameWrite.U64(employer), GameWrite.U64(senior), field, oldName, newName));
			byte number = m.Read<byte>(contract + ContractNumber), listing = m.Read<byte>(contract + ContractListing);
			writes.Add(new PlannedWrite(contract + ContractNumber, new[] { number }, new byte[] { 0xFF }, Build.T("Forma numarası", "Squad number")));
			writes.Add(new PlannedWrite(contract + ContractListing, new[] { listing }, new[] { (byte)(listing & ~3) }, Build.T("Transfer listesi", "Transfer list")));
			byte[] joined = m.ReadBytes(contract + ContractJoined, 4);
			ushort day = BitConverter.ToUInt16(joined, 0);
			DateTime today = data.GameDate;
			if (BitConverter.ToUInt16(joined, 2) > 1900)
			{
				var date = new byte[4];
				BitConverter.GetBytes((ushort)((day & ~0x1FF) | today.DayOfYear)).CopyTo(date, 0);
				BitConverter.GetBytes((ushort)today.Year).CopyTo(date, 2);
				writes.Add(new PlannedWrite(contract + ContractJoined, joined, date, Build.T("Katılma tarihi", "Joined")));
			}
			steps.Add(Build.T("Sözleşmesi yeni kulübe geçer (maaş ve bitiş aynı kalır), forma numarası boşalır, katılma tarihi bugün olur.",
				"His contract moves to the new club (same wage and end date), no squad number, joined today."));
			if (feeGbp > 0)
			{
				AddTransferFeeWrites(m, oldClub, club, oldName, newName, feeGbp, writes, steps);
				summary = Build.F("{0}: {1} → {2} ({3} bonservis).", "{0}: {1} → {2} ({3} fee).", name, oldName, newName, Money.Format(feeGbp, false));
			}
			else
			{
				summary = Build.F("{0}: {1} → {2} (bonservissiz).", "{0}: {1} → {2} (free transfer).", name, oldName, newName);
			}
		}
		return new MovePlan(summary, steps, writes.Where(w => !w.Expected.AsSpan().SequenceEqual(w.Bytes)).ToList(), club, senior);
	}

	private static void AddTransferFeeWrites(IMemory m, ulong sellerClub, ulong buyerClub, string sellerName, string buyerName, long feeGbp, List<PlannedWrite> writes, List<string> steps)
	{
		const long ClubFinance = 0x150, FinBalance = 0x0C, FinTransfer = 0x7C4, FinRevPct = 0x7D0;
		long[] transferMirrors = { 0x7C4, 0x7C8, 0x7D4 };
		ulong buyerFin = m.ReadPointer(buyerClub + ClubFinance);
		ulong sellerFin = m.ReadPointer(sellerClub + ClubFinance);
		if (buyerFin != 0)
		{
			int bal = m.Read<int>(buyerFin + FinBalance);
			int newBal = (int)Math.Clamp((long)bal - feeGbp, -2_000_000_000, 2_000_000_000);
			writes.Add(new PlannedWrite(buyerFin + FinBalance, BitConverter.GetBytes(bal), BitConverter.GetBytes(newBal), Build.T("Alıcı Bakiye", "Buyer Balance")));
			int bud = m.Read<int>(buyerFin + FinTransfer);
			int newBud = (int)Math.Max(0, (long)bud - feeGbp);
			foreach (long off in transferMirrors)
			{
				int cur = m.Read<int>(buyerFin + (ulong)off);
				writes.Add(new PlannedWrite(buyerFin + (ulong)off, BitConverter.GetBytes(cur), BitConverter.GetBytes(newBud), Build.T("Alıcı Transfer Bütçesi", "Buyer Transfer Budget")));
			}
		}
		if (sellerFin != 0)
		{
			int bal = m.Read<int>(sellerFin + FinBalance);
			int newBal = (int)Math.Clamp((long)bal + feeGbp, -2_000_000_000, 2_000_000_000);
			writes.Add(new PlannedWrite(sellerFin + FinBalance, BitConverter.GetBytes(bal), BitConverter.GetBytes(newBal), Build.T("Satıcı Bakiye", "Seller Balance")));
			int revPct = Math.Clamp(m.Read<int>(sellerFin + FinRevPct), 0, 100);
			long budAdd = feeGbp * revPct / 100;
			if (budAdd > 0)
			{
				int bud = m.Read<int>(sellerFin + FinTransfer);
				int newBud = (int)Math.Clamp((long)bud + budAdd, 0, 2_000_000_000);
				foreach (long off in transferMirrors)
				{
					int cur = m.Read<int>(sellerFin + (ulong)off);
					writes.Add(new PlannedWrite(sellerFin + (ulong)off, BitConverter.GetBytes(cur), BitConverter.GetBytes(newBud), Build.T("Satıcı Transfer Bütçesi", "Seller Transfer Budget")));
				}
			}
		}
		steps.Add(Build.F("Bonservis: {1} kasasından {0} düşülür, {2} kasasına eklenir.", "Transfer fee: {0} deducted from {1} and paid to {2}.", Money.Format(feeGbp, false), buyerName, sellerName));
	}

	private static Exception Refuse(string tr, string en) => new InvalidOperationException(Build.T(tr, en));

	private static string ClubName(GameData data, ulong club)
	{
		return data.ClubsByAddress.TryGetValue(club, out ClubRow? row) ? row.Name : data.Read(club, data.ClubDef, "Name");
	}

	private static string TeamLabel(GameData data, ulong team)
	{
		return data.TeamNames.TryGetValue(team, out string? label) ? label : Build.T("takım", "team");
	}

	/// <summary>The club's teams, the senior team first; only teams that point back at the club.</summary>
	private static List<ulong> Teams(IMemory m, ulong club)
	{
		ulong begin = m.ReadPointer(club + ClubTeams), end = m.ReadPointer(club + ClubTeams + 8);
		var teams = new List<ulong>();
		if (begin == 0 || end < begin || end - begin > 8 * 32)
		{
			return teams;
		}
		for (ulong at = begin; at < end; at += 8)
		{
			ulong t = m.ReadPointer(at);
			if (t != 0 && m.ReadPointer(t + TeamClub) == club)
			{
				teams.Add(t);
			}
		}
		return teams;
	}

	private static PlayerList ReadList(IMemory m, ulong team)
	{
		ulong header = team + TeamPlayers;
		ulong begin = m.ReadPointer(header), end = m.ReadPointer(header + 8), cap = m.ReadPointer(header + 16);
		if (begin == 0 && end == 0 && cap == 0)
		{
			return new PlayerList(team, header, 0, 0, 0, Array.Empty<ulong>());
		}
		if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || end - begin > 8 * 400)
		{
			throw Refuse("Takım listesi okunamadı.", "A team list could not be read.");
		}
		byte[] raw = m.ReadBytes(begin, (int)(end - begin));
		ulong[] items = Enumerable.Range(0, raw.Length / 8).Select(i => BitConverter.ToUInt64(raw, i * 8)).ToArray();
		return new PlayerList(team, header, begin, end, cap, items);
	}

	/// <summary>
	/// How many pointers the list's memory holds: its capacity, or the whole allocator block
	/// when the capacity falls inside one of the measured block sizes and the rest is unused (zero).
	/// </summary>
	private static ulong Room(IMemory m, PlayerList list) => Room(m, list.Begin, list.Capacity);

	/// <summary>The same for any list of pointers (begin, capacity).</summary>
	internal static ulong Room(IMemory m, ulong begin, ulong capacityEnd)
	{
		if (begin == 0)
		{
			return 0;
		}
		ulong bytes = capacityEnd - begin;
		ulong capacity = bytes / 8;
		if (bytes <= BlockSizes[0] || bytes > BlockSizes[^1])
		{
			return capacity;
		}
		ulong block = BlockSizes.First(b => b >= bytes);
		if (block == bytes)
		{
			return capacity;
		}
		byte[] spare = m.ReadBytes(capacityEnd, (int)(block - bytes));
		return spare.All(b => b == 0) ? block / 8 : capacity;
	}

	/// <summary>Out of the list: the players after him move up one, the list ends one earlier, the freed slot is cleared.</summary>
	private static void Removal(PlayerList list, int index, List<PlannedWrite> writes, string field)
	{
		int last = list.Items.Length - 1;
		byte[] Bytes(IEnumerable<ulong> items) => items.SelectMany(BitConverter.GetBytes).ToArray();
		if (index < last)
		{
			writes.Add(new PlannedWrite(list.Begin + (ulong)index * 8, Bytes(list.Items[index..last]), Bytes(list.Items[(index + 1)..]), field));
		}
		writes.Add(new PlannedWrite(list.Header + 8, GameWrite.U64(list.End), GameWrite.U64(list.End - 8), field));
		writes.Add(new PlannedWrite(list.Begin + (ulong)last * 8, GameWrite.U64(list.Items[last]), GameWrite.U64(0), field));
	}

	private static byte[] Concat(ulong a, ulong b) => GameWrite.U64(a).Concat(GameWrite.U64(b)).ToArray();

	private static readonly object _tacticsLock = new object();
	private static (IMemory? Memory, ulong Vtable, List<ulong> Objects, DateTime ScannedAt) _tacticsCache;

	/// <summary>Pre-scans match selection objects in the background so the first club click in MoveWindow is instant.</summary>
	public static void Warmup(IMemory m)
	{
		try
		{
			if (!m.TryGetModule("game_plugin.dll", out MemoryRegion module)) return;
			lock (_tacticsLock)
			{
				if (_tacticsCache.Memory == m && _tacticsCache.Vtable != 0 && (DateTime.UtcNow - _tacticsCache.ScannedAt).TotalSeconds < 60)
				{
					return;
				}
				ulong? foundVtable = (_tacticsCache.Memory == m && _tacticsCache.Vtable != 0)
					? _tacticsCache.Vtable
					: Rtti.FindVtable(m, module, ".?AVPREPARED_TACTICS_CREATOR_ARRAY@simatch@@");
				if (foundVtable == null) return;
				List<ulong> objects = ObjectScanner.FindObjects(m, foundVtable.Value, 100000).ToList();
				_tacticsCache = (m, foundVtable.Value, objects, DateTime.UtcNow);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// Empties the player's slots in every prepared selection that holds him and whose filled
	/// slots are mostly players of his club's teams. Returns how many copies change.
	/// </summary>
	private static int SelectionWrites(IMemory m, ulong player, List<ulong> clubTeams, List<PlannedWrite> writes)
	{
		uint id = m.Read<uint>(player + PersonId);
		if (id == 0 || id == Empty || !m.TryGetModule("game_plugin.dll", out MemoryRegion module))
		{
			return 0;
		}
		ulong vtable;
		List<ulong> objects;
		lock (_tacticsLock)
		{
			if (_tacticsCache.Memory == m && _tacticsCache.Vtable != 0 && (DateTime.UtcNow - _tacticsCache.ScannedAt).TotalSeconds < 60)
			{
				vtable = _tacticsCache.Vtable;
				objects = _tacticsCache.Objects;
			}
			else
			{
				ulong? foundVtable = (_tacticsCache.Memory == m && _tacticsCache.Vtable != 0)
					? _tacticsCache.Vtable
					: Rtti.FindVtable(m, module, ".?AVPREPARED_TACTICS_CREATOR_ARRAY@simatch@@");
				if (foundVtable == null)
				{
					throw Refuse("Oyunun maç kadrosu seçimleri bu sürümde bulunamadı; taşıma yapılmadı.", "The game's match selections were not found in this version; nothing was moved.");
				}
				vtable = foundVtable.Value;
				objects = ObjectScanner.FindObjects(m, vtable, 100000).ToList();
				_tacticsCache = (m, vtable, objects, DateTime.UtcNow);
			}
		}
		var clubIds = new HashSet<uint>();
		foreach (ulong t in clubTeams)
		{
			foreach (ulong p in ReadList(m, t).Items)
			{
				try
				{
					clubIds.Add(m.Read<uint>(p + PersonId));
				}
				catch (MemoryAccessException)
				{
				}
			}
		}
		int changed = 0;
		foreach (ulong o in objects)
		{
			try
			{
				if (m.ReadPointer(o) != vtable)
				{
					continue;
				}
				ulong begin = m.ReadPointer(o + 8), end = m.ReadPointer(o + 16), cap = m.ReadPointer(o + 24);
				if (begin == 0 || end <= begin || cap < end)
				{
					continue;
				}
				byte[] raw = m.ReadBytes(o + 0x28, Slots * 4);
				uint[] ids = Enumerable.Range(0, Slots).Select(i => BitConverter.ToUInt32(raw, i * 4)).ToArray();
				if (!ids.Contains(id))
				{
					continue;
				}
				var filled = ids.Where(x => x != Empty).ToList();
				if (filled.Count(clubIds.Contains) * 2 <= filled.Count)
				{
					continue;
				}
				byte[] bytes = ids.SelectMany(x => BitConverter.GetBytes(x == id ? Empty : x)).ToArray();
				writes.Add(new PlannedWrite(o + 0x28, raw, bytes, Build.T("Maç kadrosu", "Selection")));
				changed++;
			}
			catch (MemoryAccessException)
			{
			}
		}
		return changed;
	}
}
