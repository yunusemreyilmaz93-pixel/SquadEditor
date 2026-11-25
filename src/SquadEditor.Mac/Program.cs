using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Photino.NET;
using SquadEditor.Core;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.Mac;

internal static class Program
{
	private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
	private static readonly object LockObj = new();

	private static IMemory? _mem;
	private static GameSession? _session;
	private static GameLayout? _layout;
	private static List<PlayerRow> _players = new();
	private static List<ClubRow> _clubs = new();
	private static List<StaffRow> _staff = new();
	private static List<CompRow> _comps = new();
	private static string _status = "Connecting…";
	private static string _weightMode = "classic";
	private static bool _readOnly;
	private static Timer? _freezeTimer;

	[STAThread]
	public static int Main(string[] args)
	{
		int port = 47270;
		_layout = LoadEmbeddedLayout();
		TryAttachAndIndex();

		_freezeTimer = new Timer(_ =>
		{
			lock (LockObj)
			{
				try { _session?.Freezer.Tick(_session.Memory); } catch { }
			}
		}, null, 1500, 1500);

		using var listener = new HttpListener();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		listener.Start();

		_ = Task.Run(async () =>
		{
			while (listener.IsListening)
			{
				try
				{
					HttpListenerContext ctx = await listener.GetContextAsync();
					_ = Task.Run(() => HandleRequest(ctx));
				}
				catch { break; }
			}
		});

		string url = $"http://127.0.0.1:{port}/";
		var window = new PhotinoWindow()
			.SetTitle("SquadEditor v1.6.0 — FM27")
			.SetUseOsDefaultSize(false)
			.SetSize(1340, 860)
			.Center()
			.SetDevToolsEnabled(false)
			.Load(url);

		window.WaitForClose();
		listener.Stop();
		return 0;
	}

	private static GameLayout LoadEmbeddedLayout()
	{
		using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fm27-mac.json")!;
		string tmp = Path.Combine(Path.GetTempPath(), "squadeditor-fm27-mac.json");
		using (FileStream fs = File.Create(tmp)) s.CopyTo(fs);
		return GameLayout.Load(tmp);
	}

	private static byte[] ReadEmbeddedBytes(string name)
	{
		using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
		using var ms = new MemoryStream();
		s.CopyTo(ms);
		return ms.ToArray();
	}

	private static void TryAttachAndIndex()
	{
		lock (LockObj)
		{
			try
			{
				(_mem as IDisposable)?.Dispose();
				_mem = null;
				_session = null;

				if (OperatingSystem.IsMacOS()) _mem = MacProcessMemory.Attach(_layout!.ProcessName);
				else if (OperatingSystem.IsWindows()) _mem = ProcessMemory.Attach(_layout!.ProcessName);
				else { _status = "Unsupported OS"; return; }

				_session = new GameSession(_mem, _layout!);
				IndexAll();
				_status = $"FM27 Connected · {_players.Count:N0} players · {_clubs.Count:N0} clubs";
			}
			catch (Exception ex)
			{
				_status = ex.Message;
			}
		}
	}

	private static string SafeRead(GameSession s, ulong a, FieldDef? f)
	{
		if (f == null) return "";
		try { return s.ReadField(a, f); } catch { return ""; }
	}
	private static int SafeInt(GameSession s, ulong a, FieldDef? f) => int.TryParse(SafeRead(s, a, f), out int v) ? v : 0;

	private static void IndexAll()
	{
		var s = _session!;
		var l = _layout!;

		var pDef = l.GetEntity("Player");
		FieldDef? PF(string n) => pDef.Fields.FirstOrDefault(f => f.Name == n);
		var pName = PF("Name"); var pClub = PF("Club"); var pNation = PF("Nation");
		var pCa = PF("CA"); var pPa = PF("PA"); var pWage = PF("Wage"); var pVal = PF("Value");
		var pDob = PF("Date of Birth"); var pExp = PF("Contract Expires"); var pUid = PF("Unique ID");

		// On macOS ARM64, positions start at 0x150 - 0x8 = 0x148 (15 bytes) and 54 attributes at 0x15F - 0x8 = 0x157
		ulong posOff = OperatingSystem.IsMacOS() ? 0x148UL : 0x150UL;
		Span<byte> raw69 = stackalloc byte[69];
		string[] posNames = { "GK", "SW", "DL", "DC", "DR", "DM", "ML", "MC", "MR", "AML", "AMC", "AMR", "ST", "WBL", "WBR" };

		var plist = new List<PlayerRow>();
		foreach (ulong addr in s.GetCollectionItems("Players"))
		{
			string name = SafeRead(s, addr, pName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			int ca = SafeInt(s, addr, pCa);
			int pa = SafeInt(s, addr, pPa);
			string dob = SafeRead(s, addr, pDob);
			int age = CalcAge(dob);
			string exp = SafeRead(s, addr, pExp);

			string posText = "";
			string bestRole = "";
			double rolePct = 0, prPct = 0;
			if (s.Memory.TryRead(addr + posOff, raw69))
			{
				var nat = new List<string>();
				for (int i = 0; i < 15; i++) if (raw69[i] >= 15) nat.Add(posNames[i]);
				if (nat.Count == 0)
				{
					int bi = 0;
					for (int i = 1; i < 15; i++) if (raw69[i] > raw69[bi]) bi = i;
					nat.Add(posNames[bi]);
				}
				posText = string.Join(", ", nat.Take(3));
				(bestRole, rolePct, prPct) = MacScoutMath.Evaluate(raw69, ca, pa, _weightMode == "meta");
			}

			plist.Add(new PlayerRow(
				addr.ToString("X"), name, posText, age, SafeRead(s, addr, pClub), SafeRead(s, addr, pNation),
				ca, pa, Math.Max(0, pa - ca), bestRole, rolePct, prPct,
				SafeInt(s, addr, pWage), SafeInt(s, addr, pVal), dob, exp, SafeRead(s, addr, pUid)));
		}
		_players = plist.OrderByDescending(x => x.Ca).ThenByDescending(x => x.Pa).ToList();

		var cDef = l.GetEntity("Club");
		FieldDef? CF(string n) => cDef.Fields.FirstOrDefault(f => f.Name == n);
		var cName = CF("Name"); var cRep = CF("Reputation"); var cBal = CF("Balance");
		var cTb = CF("Transfer Budget"); var cWb = CF("Wage Budget"); var cTr = CF("Training Facilities"); var cYf = CF("Youth Facilities");
		var clist = new List<ClubRow>();
		foreach (ulong addr in s.GetCollectionItems("Clubs"))
		{
			string name = SafeRead(s, addr, cName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			clist.Add(new ClubRow(addr.ToString("X"), name, SafeInt(s, addr, cRep), SafeInt(s, addr, cBal), SafeInt(s, addr, cTb), SafeInt(s, addr, cWb), SafeInt(s, addr, cTr), SafeInt(s, addr, cYf)));
		}
		_clubs = clist.OrderByDescending(x => x.Rep).ToList();

		var sDef = l.GetEntity("Staff");
		FieldDef? SF(string n) => sDef.Fields.FirstOrDefault(f => f.Name == n);
		var sName = SF("Name"); var sClub = SF("Club"); var sNation = SF("Nation");
		var sCa = SF("CA"); var sPa = SF("PA");
		var sDet = SF("Determination"); var sLod = SF("Level of Discipline"); var sMot = SF("Motivating");
		var sAtt = SF("Attacking"); var sDefA = SF("Defending"); var sFit = SF("Fitness");
		var sTac = SF("Tactical"); var sTec = SF("Technical"); var sGkS = SF("Goalkeepers");
		var slist = new List<StaffRow>();
		foreach (ulong addr in s.GetCollectionItems("Staff"))
		{
			string name = SafeRead(s, addr, sName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			int ddm = SafeInt(s, addr, sDet) + SafeInt(s, addr, sLod) + SafeInt(s, addr, sMot);
			double stars = MacScoutMath.BestCoachStars(
				ddm, SafeInt(s, addr, sAtt), SafeInt(s, addr, sDefA), SafeInt(s, addr, sFit),
				SafeInt(s, addr, sTac), SafeInt(s, addr, sTec), SafeInt(s, addr, sGkS));
			slist.Add(new StaffRow(addr.ToString("X"), name, SafeRead(s, addr, sClub), SafeRead(s, addr, sNation), SafeInt(s, addr, sCa), SafeInt(s, addr, sPa), stars));
		}
		_staff = slist.OrderByDescending(x => x.Stars).ThenByDescending(x => x.Ca).ToList();

		var coDef = l.GetEntity("Competition");
		var coName = coDef.Fields.FirstOrDefault(f => f.Name == "Name");
		var coRep = coDef.Fields.FirstOrDefault(f => f.Name == "Reputation");
		var colist = new List<CompRow>();
		foreach (ulong addr in s.GetCollectionItems("Competitions"))
		{
			string name = SafeRead(s, addr, coName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			colist.Add(new CompRow(addr.ToString("X"), name, SafeInt(s, addr, coRep)));
		}
		_comps = colist.OrderByDescending(x => x.Rep).ToList();
	}

	private static int CalcAge(string dob)
	{
		if (DateTime.TryParseExact(dob, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
		{
			int a = 2026 - dt.Year;
			return Math.Clamp(a, 14, 55);
		}
		return 0;
	}

	private static void HandleRequest(HttpListenerContext ctx)
	{
		try
		{
			string path = ctx.Request.Url?.AbsolutePath ?? "/";
			string method = ctx.Request.HttpMethod;

			if (path == "/" && method == "GET") { WriteBytes(ctx, ReadEmbeddedBytes("ui.html"), "text/html; charset=utf-8"); return; }
			if (path == "/logo.png" && method == "GET") { WriteBytes(ctx, ReadEmbeddedBytes("logo.png"), "image/png"); return; }

			if (path == "/api/status")
			{
				lock (LockObj)
				{
					WriteJson(ctx, new
					{
						connected = _session != null,
						status = _status,
						players = _players.Count,
						clubs = _clubs.Count,
						staff = _staff.Count,
						comps = _comps.Count,
						undoCount = _session?.History.Batches.Count ?? 0,
						frozenCount = _session?.Freezer.Entities.Count ?? 0,
						weightMode = _weightMode,
						readOnly = _readOnly
					});
				}
				return;
			}
			if (path == "/api/settings" && method == "POST")
			{
				using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
				var body = JsonSerializer.Deserialize<SettingsReq>(sr.ReadToEnd(), JsonOpts)!;
				bool reindex = false;
				lock (LockObj)
				{
					if (!string.IsNullOrEmpty(body.WeightMode) && body.WeightMode != _weightMode)
					{
						_weightMode = body.WeightMode == "meta" ? "meta" : "classic";
						reindex = true;
					}
					if (body.ReadOnly.HasValue) _readOnly = body.ReadOnly.Value;
					if (reindex && _session != null) IndexAll();
				}
				WriteJson(ctx, new { ok = true, weightMode = _weightMode, readOnly = _readOnly });
				return;
			}
			if (path == "/api/reconnect" && method == "POST")
			{
				TryAttachAndIndex();
				WriteJson(ctx, new { connected = _session != null, status = _status });
				return;
			}
			if (path == "/api/players")
			{
				var qs = ctx.Request.QueryString;
				string q = (qs["q"] ?? "").Trim();
				string pos = (qs["pos"] ?? "").Trim();
				int minCa = int.TryParse(qs["minCa"], out int a) ? a : 0;
				int minPa = int.TryParse(qs["minPa"], out int b) ? b : 0;
				int maxAge = int.TryParse(qs["maxAge"], out int c) ? c : 0;
				int maxValM = int.TryParse(qs["maxValM"], out int d) ? d : 0;
				bool freeOnly = qs["free"] == "1";
				string sort = qs["sort"] ?? "ca";

				lock (LockObj)
				{
					IEnumerable<PlayerRow> res = _players;
					if (q.Length > 0)
						res = res.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Club.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Nation.Contains(q, StringComparison.OrdinalIgnoreCase));
					if (pos.Length > 0) res = res.Where(p => p.Pos.Contains(pos, StringComparison.OrdinalIgnoreCase));
					if (minCa > 0) res = res.Where(p => p.Ca >= minCa);
					if (minPa > 0) res = res.Where(p => p.Pa >= minPa);
					if (maxAge > 0) res = res.Where(p => p.Age > 0 && p.Age <= maxAge);
					if (maxValM > 0) res = res.Where(p => p.Value <= maxValM * 1_000_000);
					if (freeOnly) res = res.Where(p => string.IsNullOrWhiteSpace(p.Club));

					res = sort switch
					{
						"pa" => res.OrderByDescending(p => p.Pa).ThenByDescending(p => p.Ca),
						"pr" => res.OrderByDescending(p => p.PrPct).ThenByDescending(p => p.RolePct),
						"role" => res.OrderByDescending(p => p.RolePct),
						"growth" => res.OrderByDescending(p => p.Growth),
						"age" => res.OrderBy(p => p.Age == 0 ? 99 : p.Age).ThenByDescending(p => p.Pa),
						_ => res.OrderByDescending(p => p.Ca).ThenByDescending(p => p.Pa)
					};
					WriteJson(ctx, res.Take(200).ToList());
				}
				return;
			}
			if (path == "/api/clubs")
			{
				string q = (ctx.Request.QueryString["q"] ?? "").Trim();
				lock (LockObj)
				{
					IEnumerable<ClubRow> res = _clubs;
					if (q.Length > 0) res = res.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
					WriteJson(ctx, res.Take(150).ToList());
				}
				return;
			}
			if (path == "/api/staff")
			{
				var qs = ctx.Request.QueryString;
				string q = (qs["q"] ?? "").Trim();
				double minStars = double.TryParse(qs["minStars"], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ? ms : 0;
				lock (LockObj)
				{
					IEnumerable<StaffRow> res = _staff;
					if (q.Length > 0) res = res.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Club.Contains(q, StringComparison.OrdinalIgnoreCase));
					if (minStars > 0) res = res.Where(s => s.Stars >= minStars);
					WriteJson(ctx, res.Take(150).ToList());
				}
				return;
			}
			if (path == "/api/comps")
			{
				string q = (ctx.Request.QueryString["q"] ?? "").Trim();
				lock (LockObj)
				{
					IEnumerable<CompRow> res = _comps;
					if (q.Length > 0) res = res.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
					WriteJson(ctx, res.Take(150).ToList());
				}
				return;
			}
			if (path == "/api/entity" && method == "GET")
			{
				string type = ctx.Request.QueryString["type"] ?? "Player";
				ulong addr = Convert.ToUInt64(ctx.Request.QueryString["addr"] ?? "0", 16);
				lock (LockObj)
				{
					if (_session == null) { WriteJson(ctx, new { error = "Not connected" }, 400); return; }
					var def = _layout!.GetEntity(type);
					var fields = new List<object>();
					foreach (var f in def.Fields.Where(x => x.GroupKey != "Internal"))
					{
						string val = "";
						try { val = _session.ReadField(addr, f); } catch { continue; }
						fields.Add(new { name = f.Name, group = f.GroupKey, value = val, writable = f.IsWritable && !_readOnly, min = f.Min, max = f.Max });
					}
					object? extra = null;
					if (type == "Player")
					{
						var row = _players.FirstOrDefault(p => p.Addr == addr.ToString("X"));
						extra = row;
					}
					else if (type == "Club")
					{
						var row = _clubs.FirstOrDefault(c => c.Addr == addr.ToString("X"));
						var squad = row != null ? _players.Where(p => string.Equals(p.Club, row.Name, StringComparison.OrdinalIgnoreCase)).Take(45).ToList() : new List<PlayerRow>();
						extra = new { club = row, squad };
					}
					WriteJson(ctx, new { type, addr = addr.ToString("X"), frozen = _session.Freezer.IsFrozen(addr), fields, extra });
				}
				return;
			}
			if (path == "/api/write" && method == "POST")
			{
				if (_readOnly) { WriteJson(ctx, new { error = "Read-Only mode is enabled" }, 403); return; }
				using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
				var body = JsonSerializer.Deserialize<WriteReq>(sr.ReadToEnd(), JsonOpts)!;
				ulong addr = Convert.ToUInt64(body.Addr, 16);
				lock (LockObj)
				{
					if (_session == null) { WriteJson(ctx, new { error = "Not connected" }, 400); return; }
					var def = _layout!.GetEntity(body.Type);
					var field = def.Fields.First(f => f.Name == body.Field);
					_session.WriteField(addr, field, body.Value, body.Label);
					WriteJson(ctx, new { ok = true, undoCount = _session.History.Batches.Count });
				}
				return;
			}
			if (path == "/api/quick" && method == "POST")
			{
				if (_readOnly) { WriteJson(ctx, new { error = "Read-Only mode is enabled" }, 403); return; }
				using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
				var body = JsonSerializer.Deserialize<QuickReq>(sr.ReadToEnd(), JsonOpts)!;
				ulong addr = Convert.ToUInt64(body.Addr, 16);
				lock (LockObj)
				{
					var sess = _session;
					if (sess == null) { WriteJson(ctx, new { error = "Not connected" }, 400); return; }
					var def = _layout!.GetEntity(body.Type);
					void Set(string fn, string v)
					{
						var fd = def.Fields.FirstOrDefault(f => f.Name == fn);
						if (fd != null && fd.IsWritable) { try { sess.WriteField(addr, fd, v, body.Label); } catch { } }
					}
					void AddInt(string fn, int delta)
					{
						var fd = def.Fields.FirstOrDefault(f => f.Name == fn);
						if (fd != null && fd.IsWritable && int.TryParse(SafeRead(sess, addr, fd), out int cur))
						{
							try { sess.WriteField(addr, fd, (cur + delta).ToString(), body.Label); } catch { }
						}
					}

					if (body.Action == "toggle_freeze")
					{
						if (_session.Freezer.IsFrozen(addr)) _session.Freezer.Unfreeze(addr);
						else _session.FreezeEntity(addr, body.Label ?? addr.ToString("X"), def);
					}
					else
					{
						using (_session.History.BeginBatch($"{body.Label}: {body.Action}"))
						{
							if (body.Action == "max_fitness") { Set("Condition", "100"); Set("Match Sharpness", "100"); Set("Morale", "20"); Set("Happiness", "100"); }
							else if (body.Action == "bal_50") AddInt("Balance", 50_000_000);
							else if (body.Action == "tb_50") AddInt("Transfer Budget", 50_000_000);
							else if (body.Action == "rich_club") { Set("Balance", "500000000"); Set("Transfer Budget", "200000000"); Set("Wage Budget", "10000000"); }
							else if (body.Action == "max_facilities")
							{
								foreach (string fn in new[] { "Training Facilities", "Youth Facilities", "Youth Coaching", "Youth Recruitment", "Corporate Facilities" }) Set(fn, "20");
							}
							else if (body.Action == "world_no1") { Set("Reputation", "200"); Set("Reputation (10000)", "10000"); }
						}
					}
					WriteJson(ctx, new { ok = true, frozen = _session.Freezer.IsFrozen(addr), undoCount = _session.History.Batches.Count });
				}
				return;
			}
			if (path == "/api/undo" && method == "POST")
			{
				lock (LockObj)
				{
					if (_session != null && _session.History.Batches.Count > 0) _session.History.UndoLast(_session.Memory);
					WriteJson(ctx, new { ok = true, undoCount = _session?.History.Batches.Count ?? 0 });
				}
				return;
			}

			ctx.Response.StatusCode = 404;
			ctx.Response.Close();
		}
		catch (Exception ex)
		{
			try { WriteJson(ctx, new { error = ex.Message }, 500); } catch { }
		}
	}

	private static void WriteBytes(HttpListenerContext ctx, byte[] bytes, string mime, int code = 200)
	{
		ctx.Response.StatusCode = code;
		ctx.Response.ContentType = mime;
		ctx.Response.ContentLength64 = bytes.Length;
		ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
		ctx.Response.Close();
	}

	private static void WriteJson(HttpListenerContext ctx, object obj, int code = 200)
		=> WriteBytes(ctx, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, JsonOpts)), "application/json; charset=utf-8", code);

	private sealed record PlayerRow(string Addr, string Name, string Pos, int Age, string Club, string Nation, int Ca, int Pa, int Growth, string BestRole, double RolePct, double PrPct, int Wage, int Value, string Dob, string ContractEnd, string Uid);
	private sealed record ClubRow(string Addr, string Name, int Rep, int Balance, int TransferBudget, int WageBudget, int TrainingFac, int YouthFac);
	private sealed record StaffRow(string Addr, string Name, string Club, string Nation, int Ca, int Pa, double Stars);
	private sealed record CompRow(string Addr, string Name, int Rep);
	private sealed record WriteReq(string Type, string Addr, string Field, string Value, string? Label);
	private sealed record QuickReq(string Type, string Addr, string Action, string? Label);
	private sealed record SettingsReq(string? WeightMode, bool? ReadOnly);
}

internal static class MacScoutMath
{
	private const int Cro = 0, Dri = 1, Fin = 2, Hea = 3, Mar = 5, Otb = 6, Pas = 7, Tac = 9, Vis = 10;
	private const int Han = 11, Aer = 12, Cmd = 13, Ooo = 19, Pos = 20, Ref = 21;
	private const int Ant = 17, Dec = 18, Fir = 22, Tec = 23, Fla = 26, Tea = 28, Wor = 29;
	private const int Acc = 34, Str = 36, Sta = 37, Pac = 38, Jum = 39, Bal = 42, Agi = 46, Cmp = 52, Cnt = 53;

	private static readonly (string Code, int[] Pos, (int A, int W)[] Classic, (int A, int W)[] Meta)[] Roles =
	{
		("GK", new[]{0}, new[]{(Ref,5),(Han,4),(Aer,4),(Ooo,4),(Pos,4),(Cnt,3),(Agi,3),(Cmd,3)}, new[]{(Ref,8),(Agi,7),(Aer,6),(Han,5),(Ooo,5),(Pos,4),(Cnt,4),(Ant,4)}),
		("CD", new[]{3}, new[]{(Mar,5),(Tac,5),(Pos,5),(Hea,5),(Jum,5),(Str,5),(Cnt,4),(Dec,4),(Pac,3)}, new[]{(Pac,8),(Acc,8),(Jum,8),(Ant,5),(Cnt,5),(Str,5),(Bal,4),(Pos,4),(Mar,4),(Tac,4)}),
		("WB", new[]{2,4,13,14}, new[]{(Pac,5),(Acc,5),(Sta,5),(Cro,4),(Tac,4),(Wor,4),(Otb,3),(Dri,3),(Pos,3)}, new[]{(Pac,9),(Acc,9),(Sta,6),(Agi,5),(Bal,5),(Dri,5),(Ant,4),(Cro,4),(Tac,4)}),
		("DM", new[]{5}, new[]{(Tac,5),(Pos,5),(Mar,4),(Ant,4),(Cnt,4),(Dec,4),(Wor,4),(Tea,4),(Sta,4)}, new[]{(Pac,7),(Acc,7),(Jum,5),(Ant,6),(Cnt,5),(Bal,5),(Agi,5),(Sta,5),(Tac,4)}),
		("DLP", new[]{5,7}, new[]{(Pas,5),(Vis,5),(Fir,5),(Tec,4),(Dec,5),(Cmp,4),(Tea,4),(Ant,3)}, new[]{(Pac,7),(Acc,7),(Agi,6),(Bal,6),(Ant,5),(Dri,5),(Pas,5),(Vis,4),(Cmp,4)}),
		("BBM", new[]{7}, new[]{(Sta,5),(Wor,5),(Pas,4),(Tac,4),(Otb,4),(Dec,4),(Tea,4),(Pac,3),(Str,3)}, new[]{(Pac,8),(Acc,8),(Sta,6),(Agi,5),(Bal,5),(Dri,5),(Ant,5),(Wor,5),(Jum,4)}),
		("AP", new[]{7,10}, new[]{(Vis,5),(Pas,5),(Fir,5),(Tec,5),(Dec,5),(Fla,4),(Otb,4),(Cmp,4)}, new[]{(Pac,8),(Acc,8),(Dri,6),(Agi,6),(Bal,6),(Ant,5),(Pas,5),(Vis,4),(Cmp,4)}),
		("IW", new[]{6,8,9,11}, new[]{(Acc,5),(Pac,5),(Dri,5),(Fin,4),(Otb,5),(Fir,4),(Tec,4),(Pas,4),(Agi,4)}, new[]{(Pac,9),(Acc,9),(Dri,7),(Agi,6),(Bal,6),(Ant,5),(Otb,5),(Fin,4),(Cmp,4)}),
		("AF", new[]{12}, new[]{(Fin,5),(Otb,5),(Acc,5),(Pac,5),(Cmp,5),(Fir,4),(Dri,4),(Ant,4)}, new[]{(Pac,9),(Acc,9),(Dri,6),(Agi,6),(Bal,6),(Jum,5),(Ant,5),(Otb,5),(Fin,4),(Cmp,4)})
	};

	public static (string Role, double Pct, double PrPct) Evaluate(ReadOnlySpan<byte> raw69, int ca, int pa, bool meta)
	{
		ReadOnlySpan<byte> pos = raw69[..15];
		ReadOnlySpan<byte> attr = raw69.Slice(15, 54);
		double gain = Math.Max(0, pa - ca) * 0.42;

		string bestCode = "";
		double bestCur = 0, bestPr = 0;

		foreach (var r in Roles)
		{
			int bp = 0;
			foreach (int pi in r.Pos) if (pos[pi] > bp) bp = pos[pi];
			if (bp < 10) continue;
			double pf = bp >= 18 ? 1.0 : 0.88 + (bp - 10) * 0.015;
			var weights = meta ? r.Meta : r.Classic;
			double sCur = 0, sPr = 0, tw = 0;
			foreach (var (a, w) in weights)
			{
				sCur += attr[a] * w;
				sPr += Math.Min(100.0, attr[a] + gain) * w;
				tw += w;
			}
			double cur = Math.Round((sCur / tw) * pf, 1);
			double pr = Math.Round((sPr / tw) * pf, 1);
			if (cur > bestCur) { bestCur = cur; bestPr = pr; bestCode = r.Code; }
		}
		return (bestCode, bestCur, bestPr);
	}

	public static double BestCoachStars(int ddm, int att, int def, int fit, int tac, int tec, int gk)
	{
		double Calc(int main, int sec)
		{
			double pts = main * 6 + sec * 3 + ddm * 2;
			if (pts >= 270) return 5.0;
			if (pts >= 240) return 4.5;
			if (pts >= 210) return 4.0;
			if (pts >= 180) return 3.5;
			if (pts >= 150) return 3.0;
			if (pts >= 120) return 2.5;
			if (pts >= 90) return 2.0;
			return 1.5;
		}
		return new[]
		{
			Calc(fit, 0), Calc(gk, tac), Calc(gk, tec),
			Calc(def, tac), Calc(def, tec), Calc(att, tac), Calc(att, tec),
			Calc(tac, tec), Calc(tec, att)
		}.Max();
	}
}
