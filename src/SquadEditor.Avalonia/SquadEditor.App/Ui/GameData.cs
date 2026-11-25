using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SquadEditor.Core;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed class PlayerRow
{
	public ulong Address { get; init; }
	public string Name { get; set; } = "";
	public uint Uid { get; init; }
	public ulong ClubAddress { get; set; }
	public uint ClubUid { get; set; }
	public string Club { get; set; } = "";
	public string Nation { get; init; } = "";
	public string Positions { get; init; } = "";
	public int PositionOrder { get; init; }
	public string DateOfBirth { get; init; } = "";
	public int BirthYear { get; init; }
	public int CA { get; set; }
	public int PA { get; set; }
	public int Growth => Math.Max(0, PA - CA);
	public string BestRole { get; init; } = "";
	public double BestRating { get; init; }
	public double PotentialRating { get; init; }
	public string RoleDisplay => BestRole.Length > 0 ? $"{BestRole} {BestRating:0.0}%" : "";
	public string PotentialDisplay => PotentialRating > 0 ? $"{PotentialRating:0.0}%" : "";
	public int LeftFoot { get; init; }
	public int RightFoot { get; init; }
	public bool BothFeet => LeftFoot >= 15 && RightFoot >= 15;
	public long AskingPrice { get; init; }
	public int Consistency { get; init; }
	public int ImportantMatches { get; init; }
	public int InjuryProneness { get; init; }
	public int Determination { get; init; }
	public int Professionalism { get; init; }
	public int Pace { get; init; }
	public int Finishing { get; init; }
	public int Passing { get; init; }
	public long Wage { get; init; }
	public long Value { get; init; }
	public int Morale { get; set; }
	public int Age { get; set; }
	public DateTime? Born { get; init; }
	public ulong TeamAddress { get; set; }
	public string Team { get; set; } = "";
	public string Status { get; set; } = "";
	public string ContractEnd { get; set; } = "";
	public DateTime? ContractDate { get; set; }
	public bool IsShortlisted { get; set; }
	public bool IsRegen => Uid >= 2_002_000_000u && Uid < 3_000_000_000u;
	public string ShortlistStar => IsShortlisted ? "★" : "☆";
	public string Search { get; set; } = "";
}

public sealed class StaffRow
{
	public ulong Address { get; init; }
	public bool IsHuman { get; init; }
	public string Name { get; set; } = "";
	public uint Uid { get; init; }
	public ulong ClubAddress { get; set; }
	public uint ClubUid { get; set; }
	public string Club { get; set; } = "";
	public string Job { get; set; } = "";
	public byte JobCode { get; set; }
	public bool IsManagerProfile { get; init; }
	public string Nation { get; init; } = "";
	public string DateOfBirth { get; init; } = "";
	public DateTime? Born { get; init; }
	public int Age { get; set; }
	public int CA { get; set; }
	public int PA { get; set; }
	public int Attacking { get; init; }
	public int Defending { get; init; }
	public int Fitness { get; init; }
	public int Goalkeeping { get; init; }
	public int Tactical { get; init; }
	public int Technical { get; init; }
	public int SetPieces { get; init; }
	public int WorkingWithYoungsters { get; init; }
	public int Motivating { get; init; }
	public int ManManagement { get; init; }
	public int JudgingAbility { get; init; }
	public int JudgingPotential { get; init; }
	public int TacticalKnowledge { get; init; }
	public int Physiotherapy { get; init; }
	public int SportsScience { get; init; }
	public double BestStars => StaffStars.Best(this).BestStars;
	public string BestStarsText => StaffStars.Best(this).BestShort;
	public int Growth => Math.Max(0, PA - CA);
	public string BestRole { get; init; } = "";
	public double BestRating { get; init; }
	public double PotentialRating { get; init; }
	public string RoleDisplay => BestRole.Length > 0 ? $"{BestRole} {BestRating:0.0}%" : "";
	public string PotentialDisplay => PotentialRating > 0 ? $"{PotentialRating:0.0}%" : "";
	public int LeftFoot { get; init; }
	public int RightFoot { get; init; }
	public bool BothFeet => LeftFoot >= 15 && RightFoot >= 15;
	public long AskingPrice { get; init; }
	public int Consistency { get; init; }
	public int ImportantMatches { get; init; }
	public int InjuryProneness { get; init; }
	public int Determination { get; init; }
	public int Professionalism { get; init; }
	public int Pace { get; init; }
	public int Finishing { get; init; }
	public int Passing { get; init; }
	public long Wage { get; init; }
	public string ContractEnd { get; set; } = "";
	public string Search { get; set; } = "";
}

public sealed class ClubRow
{
	public ulong Address { get; init; }
	public string Name { get; set; } = "";
	public string FullName { get; init; } = "";
	public string Nation { get; init; } = "";
	public uint Uid { get; init; }
	public int SquadSize { get; set; }
	public double AverageCA { get; set; }
	public int BestCA { get; set; }
	public string League { get; set; } = "";
	public string Search { get; init; } = "";
}

public sealed class NationRow
{
	public ulong Address { get; init; }
	public uint Uid { get; init; }
	public string Name { get; init; } = "";
	public string Code { get; init; } = "";
	public int YouthRating { get; set; }
	public string Development { get; init; } = "";
	public string Importance { get; init; } = "";
	public int EconomicFactor { get; set; }
	public string Search { get; init; } = "";
}

public sealed class CompRow
{
	public ulong Address { get; init; }
	public uint Uid { get; init; }
	public string Name { get; init; } = "";
	public string ShortName { get; init; } = "";
	public string Code { get; init; } = "";
	public int Reputation { get; set; }
	public int Reputation10000 { get; set; }
	public string Search { get; init; } = "";
}

/// <summary>A connected game: the layout session plus fast in-memory lists for search and squads (Windows x64 &amp; macOS ARM64).</summary>
public sealed class GameData : IDisposable
{
	public static readonly string[] PositionCodes = { "GK", "SW", "DL", "DC", "DR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST", "WBL", "WBR" };

	private readonly IDisposable _disposableMemory;
	private readonly int _pid;

	private GameData(IMemory memory, IDisposable disposableMemory, int pid, GameSession session)
	{
		Memory = memory;
		_disposableMemory = disposableMemory;
		_pid = pid;
		Session = session;
		PlayerDef = session.Layout.GetEntity("Player");
		ClubDef = session.Layout.GetEntity("Club");
		StaffDef = session.Layout.GetEntity("Staff");
		HumanDef = BuildHumanDef(StaffDef);
		NationDef = session.Layout.Entities.FirstOrDefault(e => e.Name == "Nation");
		CompDef = session.Layout.Entities.FirstOrDefault(e => e.Name == "Competition");
	}

	/// <summary>HUMAN_NON_PLAYER has colOffset +0x340 larger than ACTUAL_NON_PLAYER on both Win (0x440 vs 0x100) and Mac (0x438 vs 0xF8), so NON_PLAYER fields before PersonBlock shift by -0x340.</summary>
	private static EntityDef BuildHumanDef(EntityDef staff)
	{
		const long shift = -0x340;
		return new EntityDef
		{
			Name = "Human",
			DisplayField = staff.DisplayField,
			HeaderFields = staff.HeaderFields,
			ListFields = staff.ListFields,
			TableFields = staff.TableFields,
			Fields = staff.Fields.Select(f =>
			{
				if (f.Path.Count == 0 || f.Path[0] >= 0) return f;
				var path = new List<long>(f.Path);
				path[0] += shift;
				var mirrors = f.Mirrors.Select(m =>
				{
					var copy = new List<long>(m);
					if (copy.Count > 0 && copy[0] < 0) copy[0] += shift;
					return copy;
				}).ToList();
				return new FieldDef
				{
					Name = f.Name,
					Path = path,
					Mirrors = mirrors,
					Type = f.Type,
					Min = f.Min,
					Max = f.Max,
					ReadOnly = f.ReadOnly,
					Group = f.Group,
					Format = f.Format,
					Options = f.Options,
					MaxLength = f.MaxLength,
					Scale = f.Scale,
					Concat = f.Concat,
					Currency = f.Currency
				};
			}).ToList()
		};
	}

	public IMemory Memory { get; }
	public GameSession Session { get; }
	public GameLayout Layout => Session.Layout;
	public EntityDef PlayerDef { get; }
	public EntityDef ClubDef { get; }
	public EntityDef StaffDef { get; }
	public EntityDef HumanDef { get; }
	public EntityDef? NationDef { get; }
	public EntityDef? CompDef { get; }
	public IReadOnlyList<StaffRow> Staff { get; private set; } = Array.Empty<StaffRow>();
	public ILookup<ulong, StaffRow> StaffByClub { get; private set; } = Array.Empty<StaffRow>().ToLookup(s => s.ClubAddress);
	public IReadOnlyList<NationRow> Nations { get; private set; } = Array.Empty<NationRow>();
	public IReadOnlyList<CompRow> Competitions { get; private set; } = Array.Empty<CompRow>();
	public DateTime GameDate { get; private set; } = DateTime.Today;
	public string SaveKey { get; private set; } = "default";
	public IReadOnlyDictionary<ulong, string> TeamNames { get; private set; } = new Dictionary<ulong, string>();

	public bool HasExited
	{
		get
		{
			if (OperatingSystem.IsWindows() && Memory is ProcessMemory pm) return pm.HasExited;
			try
			{
				using var proc = Process.GetProcessById(_pid);
				return proc.HasExited;
			}
			catch
			{
				return true;
			}
		}
	}

	public int AgeOn(DateTime? born)
	{
		if (born == null) return 0;
		int age = GameDate.Year - born.Value.Year;
		if (GameDate < born.Value.AddYears(age)) age--;
		return age;
	}

	public static DateTime? ParseDate(string text)
		=> DateTime.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : null;

	public IReadOnlyList<PlayerRow> Players { get; private set; } = Array.Empty<PlayerRow>();
	public IReadOnlyList<ClubRow> Clubs { get; private set; } = Array.Empty<ClubRow>();
	public IReadOnlyDictionary<ulong, ClubRow> ClubsByAddress { get; private set; } = new Dictionary<ulong, ClubRow>();
	public ILookup<ulong, PlayerRow> Squads { get; private set; } = Array.Empty<PlayerRow>().ToLookup(p => p.ClubAddress);

	public static Process? FindGameProcess(string processName)
	{
		string target = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
		if (!OperatingSystem.IsMacOS())
		{
			return Process.GetProcessesByName(target).FirstOrDefault();
		}
		Process[] all = Process.GetProcesses();
		Process? match = all.FirstOrDefault(p =>
			string.Equals(p.ProcessName, target, StringComparison.OrdinalIgnoreCase) ||
			p.ProcessName.Contains("Football Manager", StringComparison.OrdinalIgnoreCase));
		foreach (Process p in all)
		{
			if (p != match) p.Dispose();
		}
		return match;
	}

	public static GameData Open(GameLayout layout)
	{
		if (OperatingSystem.IsMacOS())
		{
			MacProcessMemory macMem = MacProcessMemory.Attach(layout.ProcessName);
			return new GameData(macMem, macMem, macMem.ProcessId, new GameSession(macMem, layout));
		}
		if (OperatingSystem.IsWindows())
		{
			ProcessMemory winMem = ProcessMemory.Attach(layout.ProcessName);
			return new GameData(winMem, winMem, winMem.ProcessId, new GameSession(winMem, layout));
		}
		throw new PlatformNotSupportedException("Unsupported operating system.");
	}

	public FieldDef Field(EntityDef entity, string name) => entity.Fields.First(f => f.Name == name);

	private GameSession _reader = null!;

	public string Read(ulong entity, EntityDef def, string field)
	{
		try
		{
			return (_reader ?? Session).ReadField(entity, Field(def, field));
		}
		catch (Exception)
		{
			return "";
		}
	}

	private int ReadInt(ulong entity, EntityDef def, string field)
		=> int.TryParse(Read(entity, def, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

	public string ReadPositions(ulong player)
	{
		var ratings = PositionCodes.Select(code => (code, value: ReadInt(player, PlayerDef, code))).ToList();
		int best = ratings.Max(r => r.value);
		if (best <= 1) return "";
		return string.Join(", ", ratings.Where(r => r.value >= 18 || r.value == best).Select(r => r.code));
	}

	public Task LoadAsync(IProgress<string>? progress = null)
	{
		return Task.Run(() =>
		{
			_reader = new GameSession(new PageCacheMemory(Memory), Layout);
			progress?.Report("clubs");
			IReadOnlyList<ulong> clubAddresses = Session.GetCollectionItems("Clubs");
			var clubs = clubAddresses.Select(a =>
			{
				string name = Read(a, ClubDef, "Name");
				return new ClubRow
				{
					Address = a,
					Name = name,
					FullName = Read(a, ClubDef, "Full Name"),
					Nation = Read(a, ClubDef, "Nation"),
					Uid = (uint)ReadInt(a, ClubDef, "Unique ID"),
					Search = Normalize(name + " " + Read(a, ClubDef, "Full Name"))
				};
			}).Where(c => c.Name.Length > 0).ToList();
			var clubMap = clubs.GroupBy(c => c.Address).ToDictionary(g => g.Key, g => g.First());

			progress?.Report("players");
			ulong raw69Off = FmOffsets.PlayerRaw69;
			IReadOnlyList<ulong> playerAddresses = Session.GetCollectionItems("Players");
			var players = new PlayerRow[playerAddresses.Count];
			Parallel.For(0, playerAddresses.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
			{
				ulong a = playerAddresses[i];
				string name = Read(a, PlayerDef, "Name");
				ulong club = ulong.TryParse(Read(a, PlayerDef, "Club Ref"), out ulong c) ? c : 0;
				string dob = Read(a, PlayerDef, "Date of Birth");
				string nation = Read(a, PlayerDef, "Nation");
				string clubName = clubMap.TryGetValue(club, out ClubRow? row) ? row.Name : "";
				string contractEnd = Read(a, PlayerDef, "Contract Expires");
				byte[] raw69 = new byte[69];
				bool hasRaw = _reader.Memory.TryRead(a + raw69Off, raw69);
				(string bestRole, double bestRating) = hasRaw ? ScoutRating.Evaluate(raw69) : ("", 0);
				int posOrder = 99;
				if (hasRaw)
				{
					int[] footballIdx = { 0, 1, 2, 3, 4, 13, 14, 5, 6, 7, 8, 9, 10, 11, 12 };
					int bestPosVal = 0;
					for (int k = 0; k < footballIdx.Length; k++)
					{
						int v = raw69[footballIdx[k]];
						if (v > bestPosVal)
						{
							bestPosVal = v;
							posOrder = k;
						}
					}
				}
				static int Scaled(byte b) => Math.Clamp((int)Math.Round(b / 5.0), 1, 20);
				int ca = ReadInt(a, PlayerDef, "CA");
				int pa = ReadInt(a, PlayerDef, "PA");
				double potentialRating = hasRaw ? ScoutRating.EvaluatePotential(raw69, ca, pa).Rating : 0;
				players[i] = new PlayerRow
				{
					Address = a,
					Name = name,
					Uid = (uint)ReadInt(a, PlayerDef, "Unique ID"),
					ClubAddress = club,
					Club = clubName,
					ClubUid = row?.Uid ?? 0,
					Nation = nation,
					Positions = ReadPositions(a),
					PositionOrder = posOrder,
					DateOfBirth = dob,
					Born = ParseDate(dob),
					Value = ReadInt(a, PlayerDef, "Value"),
					AskingPrice = ReadInt(a, PlayerDef, "Asking Price"),
					Morale = ReadInt(a, PlayerDef, "Morale"),
					TeamAddress = ulong.TryParse(Read(a, PlayerDef, "Team Ref"), out ulong t) ? t : 0,
					BirthYear = ReadInt(a, PlayerDef, "Birth Year"),
					CA = ca,
					PA = pa,
					BestRole = bestRole,
					BestRating = bestRating,
					PotentialRating = potentialRating,
					LeftFoot = ReadInt(a, PlayerDef, "Left Foot"),
					RightFoot = ReadInt(a, PlayerDef, "Right Foot"),
					Consistency = Scaled(raw69[15 + 44]),
					ImportantMatches = Scaled(raw69[15 + 47]),
					InjuryProneness = Scaled(raw69[15 + 48]),
					Determination = Scaled(raw69[15 + 51]),
					Pace = Scaled(raw69[15 + 38]),
					Finishing = Scaled(raw69[15 + 2]),
					Passing = Scaled(raw69[15 + 7]),
					Professionalism = ReadInt(a, PlayerDef, "Professionalism"),
					Wage = ReadInt(a, PlayerDef, "Wage"),
					ContractEnd = contractEnd,
					ContractDate = ParseDate(contractEnd),
					Search = Normalize($"{name} {clubName} {nation}")
				};
			});
			var playerList = players.Where(p => p.Name.Length > 0).ToList();
			DateTime? today = playerList.Take(4000).Select(p => ParseDate(Read(p.Address, PlayerDef, "Last Update"))).Where(d => d != null).Max();
			if (today != null)
			{
				GameDate = today.Value;
			}
			foreach (PlayerRow p in playerList)
			{
				p.Age = AgeOn(p.Born);
				p.Status = Medical.Status(_reader.Memory, p.Address, GameDate);
			}

			progress?.Report("staff");
			var staffAddressesList = new List<ulong>();
			var humanSet = new HashSet<ulong>();
			try
			{
				staffAddressesList.AddRange(Session.GetCollectionItems("Staff"));
			}
			catch (Exception)
			{
			}
			try
			{
				var humans = Session.GetCollectionItems("Humans");
				foreach (ulong h in humans) humanSet.Add(h);
				staffAddressesList.AddRange(humans);
				uint humanUid = humans.Select(h => (uint)ReadInt(h, HumanDef, "Unique ID")).FirstOrDefault(id => id > 0);
				if (humanUid > 0)
				{
					SaveKey = "save:" + humanUid;
				}
			}
			catch (Exception)
			{
			}
			IReadOnlyList<ulong> staffAddresses = staffAddressesList.Distinct().ToList();
			var staff = new StaffRow[staffAddresses.Count];
			Parallel.For(0, staffAddresses.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
			{
				ulong a = staffAddresses[i];
				bool isHuman = humanSet.Contains(a);
				EntityDef def = isHuman ? HumanDef : StaffDef;
				string name = Read(a, def, "Name");
				ulong club = ulong.TryParse(Read(a, def, "Club Ref"), out ulong c) ? c : 0;
				string dob = Read(a, def, "Date of Birth");
				string jobKey = Read(a, def, "Job");
				byte jobCode = byte.TryParse(jobKey, out byte jc) ? jc : (byte)0;
				string job = Field(def, "Job").Options.TryGetValue(jobKey, out string? j) ? Layout.Text(Loc.Instance.Language, j) : (jobKey.Length > 0 && jobKey != "0" ? "#" + jobKey : "");
				bool isMgrFlag = false;
				if (!isHuman)
				{
					try { isMgrFlag = Memory.Read<byte>(a - 0xAC) == 12; } catch { }
				}
				string clubName = clubMap.TryGetValue(club, out ClubRow? row) ? row.Name : "";
				string nation = Read(a, def, "Nation");
				staff[i] = new StaffRow
				{
					Address = a,
					IsHuman = isHuman,
					Name = name,
					Uid = (uint)ReadInt(a, def, "Unique ID"),
					ClubAddress = club,
					Club = clubName,
					ClubUid = row?.Uid ?? 0,
					Job = job,
					JobCode = jobCode,
					IsManagerProfile = isMgrFlag || jobCode is 16 or 18 or 66 or 68,
					Nation = nation,
					DateOfBirth = dob,
					Born = ParseDate(dob),
					CA = ReadInt(a, def, "CA"),
					PA = ReadInt(a, def, "PA"),
					Attacking = ReadInt(a, def, "Attacking"),
					Defending = ReadInt(a, def, "Defending"),
					Fitness = ReadInt(a, def, "Fitness"),
					Goalkeeping = ReadInt(a, def, "Goalkeeping"),
					Tactical = ReadInt(a, def, "Tactical"),
					Technical = ReadInt(a, def, "Technical"),
					SetPieces = ReadInt(a, def, "Set Pieces"),
					WorkingWithYoungsters = ReadInt(a, def, "Working with Youngsters"),
					Determination = ReadInt(a, def, "Determination"),
					Motivating = ReadInt(a, def, "Motivating"),
					ManManagement = ReadInt(a, def, "Man Management"),
					JudgingAbility = ReadInt(a, def, "Judging Player Ability"),
					JudgingPotential = ReadInt(a, def, "Judging Player Potential"),
					TacticalKnowledge = ReadInt(a, def, "Tactical Knowledge"),
					Physiotherapy = ReadInt(a, def, "Physiotherapy"),
					SportsScience = ReadInt(a, def, "Sports Science"),
					Wage = ReadInt(a, def, "Wage"),
					ContractEnd = Read(a, def, "Contract Expires"),
					Search = Normalize($"{name} {clubName} {nation} {job}")
				};
			});
			var staffList = staff.Where(s => s.Name.Length > 0).ToList();
			foreach (StaffRow s in staffList)
			{
				s.Age = AgeOn(s.Born);
			}
			var squads = playerList.Where(p => p.ClubAddress != 0).ToLookup(p => p.ClubAddress);
			var teamNames = new Dictionary<ulong, string>();
			foreach (var byClub in squads)
			{
				var teams = byClub.Where(p => p.TeamAddress != 0).GroupBy(p => p.TeamAddress)
					.OrderByDescending(g => g.Average(p => p.CA)).ToList();
				for (int i = 0; i < teams.Count; i++)
				{
					string league = Read(teams[i].OrderByDescending(p => p.CA).First().Address, PlayerDef, "League");
					teamNames[teams[i].Key] = i == 0 ? Build.T("A Takım", "First team") : (league.Length > 0 ? league : Build.T("Rezerv", "Reserves"));
				}
			}
			foreach (PlayerRow p in playerList)
			{
				if (p.TeamAddress != 0 && p.Wage == 0 && p.ContractEnd.Length == 0 && p.Value == 0)
				{
					p.Team = Build.T("Altyapı / Gri Oyuncular", "Youth / Virtual");
				}
				else
				{
					p.Team = teamNames.TryGetValue(p.TeamAddress, out string? tn) ? tn : "";
				}
			}
			TeamNames = teamNames;
			foreach (ClubRow club in clubs)
			{
				var squad = squads[club.Address].ToList();
				club.SquadSize = squad.Count;
				if (squad.Count > 0)
				{
					club.AverageCA = squad.Average(p => p.CA);
					club.BestCA = squad.Max(p => p.CA);
					club.League = Read(squad.OrderByDescending(p => p.CA).First().Address, PlayerDef, "League");
				}
			}
			var nationList = new List<NationRow>();
			if (NationDef != null)
			{
				try
				{
					FieldDef devF = Field(NationDef, "State of Development");
					FieldDef impF = Field(NationDef, "Game Importance");
					foreach (ulong a in Session.GetCollectionItems("Nations"))
					{
						string name = Read(a, NationDef, "Name");
						if (name.Length == 0) continue;
						string code = Read(a, NationDef, "Code");
						string devK = Read(a, NationDef, "State of Development");
						string impK = Read(a, NationDef, "Game Importance");
						nationList.Add(new NationRow
						{
							Address = a,
							Uid = (uint)ReadInt(a, NationDef, "Unique ID"),
							Name = name,
							Code = code,
							YouthRating = ReadInt(a, NationDef, "Youth Rating"),
							Development = devF.Options.TryGetValue(devK, out string? dv) ? Layout.Text(Loc.Instance.Language, dv) : devK,
							Importance = impF.Options.TryGetValue(impK, out string? iv) ? Layout.Text(Loc.Instance.Language, iv) : impK,
							EconomicFactor = ReadInt(a, NationDef, "Economic Factor"),
							Search = Normalize($"{name} {code}")
						});
					}
				}
				catch (Exception) { }
			}
			var compList = new List<CompRow>();
			if (CompDef != null)
			{
				try
				{
					foreach (ulong a in Session.GetCollectionItems("Competitions"))
					{
						string name = Read(a, CompDef, "Name");
						if (name.Length == 0) continue;
						string shortName = Read(a, CompDef, "Short Name");
						string code = Read(a, CompDef, "Code");
						compList.Add(new CompRow
						{
							Address = a,
							Uid = (uint)ReadInt(a, CompDef, "Unique ID"),
							Name = name,
							ShortName = shortName,
							Code = code,
							Reputation = ReadInt(a, CompDef, "Reputation"),
							Reputation10000 = ReadInt(a, CompDef, "Reputation (10000)"),
							Search = Normalize($"{name} {shortName} {code}")
						});
					}
				}
				catch (Exception) { }
			}
			_reader = Session;
			Nations = nationList;
			Competitions = compList;
			Staff = staffList;
			StaffByClub = staffList.Where(s => s.ClubAddress != 0).ToLookup(s => s.ClubAddress);
			Players = playerList;
			Clubs = clubs;
			ClubsByAddress = clubMap;
			Squads = squads;
		});
	}

	public void PlayerMoved(ulong player, ulong club, ulong team)
	{
		PlayerRow? row = Players.FirstOrDefault(p => p.Address == player);
		if (row == null) return;
		row.ClubAddress = club;
		row.TeamAddress = team;
		row.Club = ClubsByAddress.TryGetValue(club, out ClubRow? c) ? c.Name : "";
		row.ClubUid = c?.Uid ?? 0;
		row.Team = team != 0 ? Build.T("A Takım", "First team") : "";
		if (club == 0)
		{
			row.ContractEnd = "";
			row.ContractDate = null;
		}
		else
		{
			row.ContractEnd = Read(player, PlayerDef, "Contract Expires");
			row.ContractDate = ParseDate(row.ContractEnd);
		}
		row.Search = Normalize($"{row.Name} {row.Club} {row.Nation}");
		Squads = Players.Where(p => p.ClubAddress != 0).ToLookup(p => p.ClubAddress);
	}

	public void StaffMoved(ulong staffAddress, ulong club)
	{
		StaffRow? row = Staff.FirstOrDefault(s => s.Address == staffAddress);
		if (row == null) return;
		row.ClubAddress = club;
		row.Club = ClubsByAddress.TryGetValue(club, out ClubRow? c) ? c.Name : "";
		row.ClubUid = c?.Uid ?? 0;
		string jobKey = club == 0 ? "" : Read(staffAddress, StaffDef, "Job");
		row.JobCode = byte.TryParse(jobKey, out byte jc) ? jc : (byte)0;
		row.Job = club == 0 ? "" : (Field(StaffDef, "Job").Options.TryGetValue(jobKey, out string? j) ? Layout.Text(Loc.Instance.Language, j) : (jobKey.Length > 0 && jobKey != "0" ? "#" + jobKey : ""));
		row.ContractEnd = club == 0 ? "" : Read(staffAddress, StaffDef, "Contract Expires");
		row.Search = Normalize($"{row.Name} {row.Club} {row.Nation} {row.Job}");
		StaffByClub = Staff.Where(s => s.ClubAddress != 0).ToLookup(s => s.ClubAddress);
	}

	public void Renamed(ulong address, string name)
	{
		if (Players.FirstOrDefault(p => p.Address == address) is { } row)
		{
			row.Name = name;
			row.Search = Normalize($"{row.Name} {row.Club} {row.Nation}");
		}
		else if (Staff.FirstOrDefault(s => s.Address == address) is { } staff)
		{
			staff.Name = name;
			staff.Search = Normalize($"{staff.Name} {staff.Club} {staff.Nation} {staff.Job}");
		}
	}

	public static string Normalize(string text)
	{
		string d = text.ToLowerInvariant().Replace('ı', 'i').Normalize(System.Text.NormalizationForm.FormD);
		var sb = new System.Text.StringBuilder(d.Length);
		foreach (char ch in d)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(ch switch { 'ø' => 'o', 'ß' => 's', 'đ' => 'd', 'ł' => 'l', _ => ch });
			}
		}
		return sb.ToString();
	}

	public void Dispose() => _disposableMemory.Dispose();
}
