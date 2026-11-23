using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>
/// Moving a member of staff to another club, keeping his job, or releasing him.
///
/// A club keeps its staff in three department lists (begin, end, capacity) of object pointers:
/// +0x60 medical, +0x78 coaching, +0x90 scouting (a fitness coach may be in two). A member of
/// staff's object starts 0x100 before his person block; his contract (block +0xA8) points back
/// at the block (+0x08) and at the team he works for and the senior team (+0x10, +0x18). Some
/// staff have a 40-byte BASIC_CONTRACT without dates.
///
/// A move takes him out of every department list of his club that holds him and adds him to the
/// same lists of the new club, where their memory has room, and points his contract at the new
/// club's senior team (signed and joined today for a full contract). Only staff the club and its
/// teams do not point at directly move: not a manager (team +0x80), a director of football (club
/// +0x148), the board, or the human manager. A release takes him out of the lists and leaves him
/// without a contract, as the game's unemployed staff are.
/// </summary>
public static class StaffMoves
{
	private const long ObjectStart = 0x100, Contract = 0xA8;
	private const long ContractOwner = 0x08, ContractTeam = 0x10, ContractSenior = 0x18, ContractJob = 0x26;
	private const long TeamClub = 0x30, ClubTeams = 0x18;
	private const int ClubBytes = 0x170, TeamBytes = 0x800;
	private static readonly (long Offset, string Tr, string En)[] Departments =
	{
		(0x60, "sağlık ekibi", "medical team"), (0x78, "antrenör ekibi", "coaching staff"), (0x90, "gözlemci ekibi", "scouting staff")
	};

	private sealed record StaffList(ulong Header, ulong Begin, ulong End, ulong Capacity, ulong[] Items);

	public static MovePlan Plan(GameData data, ulong block, ulong club)
	{
		IMemory m = data.Memory;
		ulong obj = block - ObjectStart;
		string name = data.Read(block, data.StaffDef, "Name");
		ulong contract = m.ReadPointer(block + Contract);
		if (contract == 0)
		{
			if (club == 0)
			{
				throw Refuse("Bu kişi zaten kulüpsüz.", "This person is already unemployed.");
			}
			return HireUnemployed(data, block, obj, club, name);
		}
		byte job = m.Read<byte>(contract + ContractJob);
		ulong team = m.ReadPointer(contract + ContractTeam), senior = m.ReadPointer(contract + ContractSenior);
		if (m.ReadPointer(contract + ContractOwner) != block || team == 0 || senior == 0)
		{
			throw Refuse("Sözleşme beklenen yapıda değil.", "The contract is not as expected.");
		}
		if (job == 1 || job % 2 == 1)
		{
			throw Refuse("Oyuncu-antrenörler taşınmıyor.", "Player-coaches are not moved.");
		}
		ulong oldClub = m.ReadPointer(senior + TeamClub);
		if (oldClub == 0 || m.ReadPointer(team + TeamClub) != oldClub)
		{
			throw Refuse("Sözleşme beklenen yapıda değil.", "The contract is not as expected.");
		}
		if (club == oldClub)
		{
			throw Refuse("Zaten bu kulüpte.", "Already at this club.");
		}
		bool full = IsFullContract(m, contract);
		string oldName = ClubName(data, oldClub);
		string field = Build.T("Kulüp", "Club");
		var writes = new List<PlannedWrite>();
		var steps = new List<string>();
		var held = new List<(long Offset, string Tr, string En)>();
		foreach (var dep in Departments)
		{
			StaffList list = ReadList(m, oldClub + (ulong)dep.Offset);
			int count = list.Items.Count(x => x == obj);
			if (count > 1)
			{
				throw Refuse("Personel bir listede birden fazla görünüyor.", "The member of staff appears in a list more than once.");
			}
			if (count == 1)
			{
				held.Add(dep);
				Removal(list, Array.IndexOf(list.Items, obj), writes, field);
				steps.Add(Build.T($"{oldName} {dep.Tr} listesinden çıkar.", $"Leaves {oldName}'s {dep.En}."));
			}
		}
		if (held.Count == 0)
		{
			throw Refuse("Kulübün personel listelerinde değil (yönetim, teknik direktör ya da futbol direktörü taşınmaz).",
				"Not in the club's staff lists (the board, managers and directors of football are not moved).");
		}
		CheckUnheld(m, oldClub, obj, block);

		string summary;
		ulong newSenior = 0;
		if (club == 0)
		{
			writes.Add(new PlannedWrite(block + Contract, GameWrite.U64(contract), GameWrite.U64(0), Build.T("Sözleşme", "Contract"), oldName, ""));
			steps.Add(Build.T("Sözleşmesi kaldırılır: kulüpsüz kalır.", "His contract is removed: he has no club."));
			summary = Build.T($"{name}, {oldName} kulübündeki görevinden ayrılır.", $"{name} leaves {oldName}.");
		}
		else
		{
			List<ulong> teams = Teams(m, club);
			if (teams.Count == 0)
			{
				throw Refuse("Kulübün A takımı bulunamadı.", "The club's senior team was not found.");
			}
			newSenior = teams[0];
			string newName = ClubName(data, club);
			foreach (var dep in held)
			{
				StaffList list = ReadList(m, club + (ulong)dep.Offset);
				if (list.Items.Contains(obj))
				{
					throw Refuse("Yeni kulübün listesinde zaten var.", "Already in the new club's list.");
				}
				if (list.Begin == 0 || (list.End - list.Begin) / 8 >= Transfers.Room(m, list.Begin, list.Capacity))
				{
					throw Refuse($"{newName} {dep.Tr} listesi dolu ve oyunun belleğinde yer yok. Önce oradan birini görevden al, sonra tekrar dene.",
						$"{newName}'s {dep.En} list is full and the game's memory has no room. Release someone there first, then try again.");
				}
				writes.Add(new PlannedWrite(list.End, m.ReadBytes(list.End, 8), GameWrite.U64(obj), field));
				writes.Add(new PlannedWrite(list.Header + 8, GameWrite.U64(list.End).Concat(GameWrite.U64(list.Capacity)).ToArray(),
					GameWrite.U64(list.End + 8).Concat(GameWrite.U64(Math.Max(list.Capacity, list.End + 8))).ToArray(), field));
				steps.Add(Build.T($"{newName} {dep.Tr} listesine eklenir.", $"Joins {newName}'s {dep.En}."));
			}
			writes.Add(new PlannedWrite(contract + ContractTeam, GameWrite.U64(team), GameWrite.U64(newSenior), field, oldName, newName));
			writes.Add(new PlannedWrite(contract + ContractSenior, GameWrite.U64(senior), GameWrite.U64(newSenior), field, oldName, newName));
			if (full)
			{
				foreach (long offset in new long[] { 0x44, 0x4C })
				{
					byte[] date = m.ReadBytes(contract + (ulong)offset, 4);
					if (BitConverter.ToUInt16(date, 2) > 1900)
					{
						var today = new byte[4];
						BitConverter.GetBytes((ushort)((BitConverter.ToUInt16(date, 0) & ~0x1FF) | data.GameDate.DayOfYear)).CopyTo(today, 0);
						BitConverter.GetBytes((ushort)data.GameDate.Year).CopyTo(today, 2);
						writes.Add(new PlannedWrite(contract + (ulong)offset, date, today, Build.T("Sözleşme tarihi", "Contract date")));
					}
				}
			}
			steps.Add(Build.T("Sözleşmesi yeni kulübe geçer (görev, maaş ve bitiş aynı kalır).", "His contract moves to the new club (same job, wage and end)."));
			summary = Build.T($"{name}: {oldName} → {newName}.", $"{name}: {oldName} → {newName}.");
		}
		return new MovePlan(summary, steps, writes.Where(w => !w.Expected.AsSpan().SequenceEqual(w.Bytes)).ToList(), club, newSenior);
	}

	private static Exception Refuse(string tr, string en) => new InvalidOperationException(Build.T(tr, en));

	private static MovePlan HireUnemployed(GameData data, ulong block, ulong obj, ulong club, string name)
	{
		const int ContractSize = 0xC8;
		IMemory m = data.Memory;
		List<ulong> teams = Teams(m, club);
		if (teams.Count == 0)
		{
			throw Refuse("Kulübün A takımı bulunamadı.", "The club's senior team was not found.");
		}
		ulong newSenior = teams[0];
		string newName = ClubName(data, club);
		// Pick a department with room and a model FULL_CONTRACT (coaching first, then scouting, then medical).
		var order = new[] { Departments[1], Departments[2], Departments[0] };
		(long Offset, string Tr, string En) chosen = default;
		StaffList? targetList = null;
		ulong model = 0;
		foreach (var dep in order)
		{
			StaffList list = ReadList(m, club + (ulong)dep.Offset);
			if (list.Items.Contains(obj))
			{
				throw Refuse("Yeni kulübün listesinde zaten var.", "Already in the new club's list.");
			}
			if (list.Begin == 0 || (list.End - list.Begin) / 8 >= Transfers.Room(m, list.Begin, list.Capacity))
			{
				continue;
			}
			ulong foundModel = 0;
			foreach (ulong memberObj in list.Items)
			{
				try
				{
					ulong c = m.ReadPointer(memberObj + ObjectStart + Contract);
					if (c != 0 && IsFullContract(m, c) && m.ReadPointer(c + ContractSenior) == newSenior)
					{
						foundModel = c;
						break;
					}
				}
				catch (MemoryAccessException)
				{
				}
			}
			if (foundModel != 0)
			{
				chosen = dep;
				targetList = list;
				model = foundModel;
				break;
			}
		}
		if (targetList == null || model == 0)
		{
			throw Refuse($"{newName} personel listelerinde boş yer ya da örnek alınacak tam sözleşme bulunamadı.",
				$"No room or model full contract was found in {newName}'s staff lists.");
		}
		GamePool pool = GamePool.Find(m, ContractSize, model);
		var writes = new List<PlannedWrite>();
		(ulong slot, PlannedWrite header) = pool.Take(m);
		writes.Add(header);

		byte[] bytes = m.ReadBytes(model, ContractSize);
		void Put(long offset, byte[] value) => value.CopyTo(bytes, (int)offset);
		DateTime today = data.GameDate;
		int endYear = today.Month <= 6 ? today.Year + 1 : today.Year + 2;
		byte[] MakeDate(long offset, DateTime day)
		{
			ushort flags = (ushort)(BitConverter.ToUInt16(bytes, (int)offset) & ~0x1FF);
			var d = new byte[4];
			BitConverter.GetBytes((ushort)(flags | day.DayOfYear)).CopyTo(d, 0);
			BitConverter.GetBytes((ushort)day.Year).CopyTo(d, 2);
			return d;
		}
		Put(ContractOwner, GameWrite.U64(block));
		Put(ContractTeam, GameWrite.U64(newSenior));
		Put(ContractSenior, GameWrite.U64(newSenior));
		Put(0x44, MakeDate(0x44, today));
		Put(0x4C, MakeDate(0x4C, today));
		Put(0x48, MakeDate(0x48, new DateTime(endYear, 6, 30)));
		Put(0x38, new byte[4]);
		foreach (long offset in new long[] { 0x28, 0x30, 0x98, 0xA0 })
		{
			Put(offset, new byte[8]);
		}
		Put(0x68, new byte[24]);
		Put(0x80, new byte[24]);
		writes.Add(new PlannedWrite(slot, m.ReadBytes(slot, ContractSize), bytes, Build.T("Sözleşme", "Contract")));

		string field = Build.T("Kulüp", "Club");
		writes.Add(new PlannedWrite(targetList.End, m.ReadBytes(targetList.End, 8), GameWrite.U64(obj), field));
		writes.Add(new PlannedWrite(targetList.Header + 8, GameWrite.U64(targetList.End).Concat(GameWrite.U64(targetList.Capacity)).ToArray(),
			GameWrite.U64(targetList.End + 8).Concat(GameWrite.U64(Math.Max(targetList.Capacity, targetList.End + 8))).ToArray(), field));
		writes.Add(new PlannedWrite(block + Contract, GameWrite.U64(0), GameWrite.U64(slot), Build.T("Sözleşme", "Contract"), "", newName));

		int wage = BitConverter.ToInt32(bytes, 0x20);
		var steps = new List<string>
		{
			Build.T($"Oyunun sözleşme havuzundan yeni sözleşme alınır ({pool.Free} boş yer).", $"A new contract is taken from the game's pool ({pool.Free} free)."),
			Build.T($"{newName} {chosen.Tr} listesine eklenir (maaş {Money.Format(wage, true)}, bitiş 30.06.{endYear}).",
				$"Joins {newName}'s {chosen.En} ({Money.Format(wage, true)}, until 30.06.{endYear}).")
		};
		return new MovePlan(Build.T($"{name}, {newName} ekibine katılır.", $"{name} joins {newName}."), steps, writes, club, newSenior);
	}

	private static string ClubName(GameData data, ulong club)
	{
		return data.ClubsByAddress.TryGetValue(club, out ClubRow? row) ? row.Name : data.Read(club, data.ClubDef, "Name");
	}

	private static bool IsFullContract(IMemory m, ulong contract)
	{
		if (!m.TryGetModule("game_plugin.dll", out MemoryRegion module))
		{
			return false;
		}
		if (_fullContractVtable.Memory != m)
		{
			_fullContractVtable = (m, Rtti.FindVtable(m, module, ".?AVFULL_CONTRACT@db@@"));
		}
		return _fullContractVtable.Vtable != null && m.ReadPointer(contract) == _fullContractVtable.Vtable;
	}

	private static (IMemory? Memory, ulong? Vtable) _fullContractVtable;

	/// <summary>The club and its teams must not point at him other than through the department lists.</summary>
	private static void CheckUnheld(IMemory m, ulong club, ulong obj, ulong block)
	{
		var places = new List<(ulong, int)> { (club, ClubBytes) };
		places.AddRange(Teams(m, club).Select(t => (t, TeamBytes)));
		foreach (var (address, length) in places)
		{
			byte[] raw;
			try
			{
				raw = m.ReadBytes(address, length);
			}
			catch (MemoryAccessException)
			{
				continue;
			}
			for (int i = 0; i + 8 <= raw.Length; i += 8)
			{
				ulong w = BitConverter.ToUInt64(raw, i);
				if (w == obj || w == block)
				{
					throw Refuse("Kulüp onu özel bir görevde tutuyor (teknik direktör, futbol direktörü gibi); taşınmaz.",
						"The club holds him in a post of its own (manager, director of football); he is not moved.");
				}
			}
		}
	}

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

	private static StaffList ReadList(IMemory m, ulong header)
	{
		ulong begin = m.ReadPointer(header), end = m.ReadPointer(header + 8), cap = m.ReadPointer(header + 16);
		if (begin == 0 && end == 0 && cap == 0)
		{
			return new StaffList(header, 0, 0, 0, Array.Empty<ulong>());
		}
		if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || end - begin > 8 * 1000)
		{
			throw Refuse("Personel listesi okunamadı.", "A staff list could not be read.");
		}
		byte[] raw = m.ReadBytes(begin, (int)(end - begin));
		return new StaffList(header, begin, end, cap, Enumerable.Range(0, raw.Length / 8).Select(i => BitConverter.ToUInt64(raw, i * 8)).ToArray());
	}

	private static void Removal(StaffList list, int index, List<PlannedWrite> writes, string field)
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
}
