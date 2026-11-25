using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed record StaffRoleOption(byte? Code, string LabelTr, string LabelEn, long DeptOffset)
{
	public string Display => Build.T(LabelTr, LabelEn);
}

/// <summary>
/// Moving a member of staff to another club (keeping or changing his job), hiring unemployed staff
/// into a chosen role, or releasing him, using centralized FmOffsets (Windows x64 &amp; macOS ARM64).
/// </summary>
public static class StaffMoves
{
	private static ulong ObjectStart => FmOffsets.StaffObjectStart;
	private static ulong Contract => FmOffsets.PersonContract;
	private const long ContractOwner = 0x08, ContractTeam = 0x10, ContractSenior = 0x18, ContractJob = 0x26;
	private const long TeamClub = 0x30, ClubTeams = 0x18;
	private const int ClubBytes = 0x170, TeamBytes = 0x800;

	private static readonly (long Offset, string Tr, string En)[] Departments =
	{
		(0x60, "sağlık ekibi", "medical team"),
		(0x78, "antrenör ekibi", "coaching staff"),
		(0x90, "gözlemci ekibi", "scouting staff")
	};

	public static readonly IReadOnlyList<StaffRoleOption> RoleOptions = new List<StaffRoleOption>
	{
		new(20, "Asistan Menajer", "Assistant Manager", 0x78),
		new(2, "Antrenör", "Coach", 0x78),
		new(34, "Kaleci Antrenörü", "Goalkeeping Coach", 0x78),
		new(26, "Kondisyon Antrenörü", "Fitness Coach", 0x78),
		new(40, "Duran Top Antrenörü", "Set Piece Coach", 0x78),
		new(64, "Altyapı Sorumlusu", "Head of Youth Development", 0x78),
		new(14, "Gözlemci", "Scout", 0x90),
		new(44, "Baş Gözlemci", "Chief Scout", 0x90),
		new(60, "Performans Analisti", "Performance Analyst", 0x90),
		new(62, "Transfer Analisti", "Recruitment Analyst", 0x90),
		new(12, "Fizyoterapist", "Physio", 0x60),
		new(50, "Baş Fizyoterapist", "Head Physio", 0x60),
		new(28, "Spor Bilimcisi", "Sports Scientist", 0x60),
		new(30, "Spor Bilimi Sorumlusu", "Head of Sports Science", 0x60),
		new(46, "Doktor", "Doctor", 0x60)
	};

	public static bool IsUnemployed(IMemory m, ulong block)
	{
		try { return m.ReadPointer(block + Contract) == 0; } catch { return true; }
	}

	private static (long Offset, string Tr, string En) DepartmentForJob(byte job)
	{
		StaffRoleOption? opt = RoleOptions.FirstOrDefault(r => r.Code == job);
		long off = opt?.DeptOffset ?? 0x78;
		return Departments.First(d => d.Offset == off);
	}

	private static string RoleLabel(byte job)
	{
		StaffRoleOption? opt = RoleOptions.FirstOrDefault(r => r.Code == job);
		return opt?.Display ?? $"#{job}";
	}

	private sealed record StaffList(ulong Header, ulong Begin, ulong End, ulong Capacity, ulong[] Items);

	public static MovePlan Plan(GameData data, ulong block, ulong club, byte? newJob = null)
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
			return HireUnemployed(data, block, obj, club, name, newJob ?? 2);
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
				steps.Add(Build.F("{0} {2} listesinden çıkar.", "Leaves {0}'s {1}.", oldName, dep.En, dep.Tr));
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
			summary = Build.F("{0}, {1} kulübündeki görevinden ayrılır.", "{0} leaves {1}.", name, oldName);
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
			var targetDeps = newJob.HasValue
				? new List<(long Offset, string Tr, string En)> { DepartmentForJob(newJob.Value) }
				: held;
			foreach (var dep in targetDeps)
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
				steps.Add(Build.F("{0} {2} listesine eklenir.", "Joins {0}'s {1}.", newName, dep.En, dep.Tr));
			}
			writes.Add(new PlannedWrite(contract + ContractTeam, GameWrite.U64(team), GameWrite.U64(newSenior), field, oldName, newName));
			writes.Add(new PlannedWrite(contract + ContractSenior, GameWrite.U64(senior), GameWrite.U64(newSenior), field, oldName, newName));
			if (newJob.HasValue && newJob.Value != job)
			{
				writes.Add(new PlannedWrite(contract + ContractJob, new[] { job }, new[] { newJob.Value }, Build.T("Görev", "Job")));
			}
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
			if (newJob.HasValue)
			{
				string roleText = RoleLabel(newJob.Value);
				steps.Add(Build.F("Yeni görevi: {0} (maaş ve bitiş aynı kalır).", "New role: {0} (same wage and end).", roleText));
				summary = Build.F("{0}: {1} → {2} ({3}).", "{0}: {1} → {2} ({3}).", name, oldName, newName, roleText);
			}
			else
			{
				steps.Add(Build.T("Sözleşmesi yeni kulübe geçer (görev, maaş ve bitiş aynı kalır).", "His contract moves to the new club (same job, wage and end)."));
				summary = Build.F("{0}: {1} → {2}.", "{0}: {1} → {2}.", name, oldName, newName);
			}
		}
		return new MovePlan(summary, steps, writes.Where(w => !w.Expected.AsSpan().SequenceEqual(w.Bytes)).ToList(), club, newSenior);
	}

	private static Exception Refuse(string tr, string en) => new InvalidOperationException(Build.T(tr, en));

	private static MovePlan HireUnemployed(GameData data, ulong block, ulong obj, ulong club, string name, byte chosenJob)
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
		var chosen = DepartmentForJob(chosenJob);
		StaffList targetList = ReadList(m, club + (ulong)chosen.Offset);
		if (targetList.Items.Contains(obj))
		{
			throw Refuse("Yeni kulübün listesinde zaten var.", "Already in the new club's list.");
		}
		if (targetList.Begin == 0 || (targetList.End - targetList.Begin) / 8 >= Transfers.Room(m, targetList.Begin, targetList.Capacity))
		{
			throw Refuse($"{newName} {chosen.Tr} listesi dolu ve oyunun belleğinde yer yok. Önce oradan birini görevden al, sonra tekrar dene.",
				$"{newName}'s {chosen.En} list is full and the game's memory has no room. Release someone there first, then try again.");
		}

		ulong model = 0;
		var searchOrder = new[] { chosen }.Concat(Departments.Where(d => d.Offset != chosen.Offset));
		foreach (var dep in searchOrder)
		{
			StaffList list = dep.Offset == chosen.Offset ? targetList : ReadList(m, club + (ulong)dep.Offset);
			foreach (ulong memberObj in list.Items)
			{
				try
				{
					ulong c = m.ReadPointer(memberObj + ObjectStart + Contract);
					if (c != 0 && IsFullContract(m, c) && m.ReadPointer(c + ContractSenior) == newSenior)
					{
						model = c;
						break;
					}
				}
				catch (MemoryAccessException)
				{
				}
			}
			if (model != 0) break;
		}
		if (model == 0)
		{
			throw Refuse($"{newName} personel listelerinde örnek alınacak tam sözleşme bulunamadı.",
				$"No model full contract was found in {newName}'s staff lists.");
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
		bytes[ContractJob] = chosenJob;
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
		string roleText = RoleLabel(chosenJob);
		var steps = new List<string>
		{
			Build.F("Oyunun sözleşme havuzundan yeni sözleşme alınır ({0} boş yer).", "A new contract is taken from the game's pool ({0} free).", pool.Free),
			Build.F("{0} {5} listesine {2} olarak eklenir (maaş {3}, bitiş 30.06.{4}).", "Joins {0}'s {1} as {2} ({3}, until 30.06.{4}).", newName, chosen.En, roleText, Money.Format(wage, true), endYear, chosen.Tr)
		};
		return new MovePlan(Build.F("{0}, {1} kulübüne {2} olarak katılır.", "{0} joins {1} as {2}.", name, newName, roleText), steps, writes, club, newSenior);
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
