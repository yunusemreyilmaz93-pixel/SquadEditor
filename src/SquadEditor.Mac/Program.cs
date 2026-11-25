using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	private static List<PlayerSummary> _players = new();
	private static List<ClubSummary> _clubs = new();
	private static List<StaffSummary> _staff = new();
	private static List<CompSummary> _comps = new();
	private static string _status = "Not connected";
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
			.SetTitle("SquadEditor — FM27")
			.SetUseOsDefaultSize(false)
			.SetSize(1280, 820)
			.Center()
			.SetDevToolsEnabled(false)
			.Load(url);

		window.WaitForClose();
		listener.Stop();
		return 0;
	}

	private static GameLayout LoadEmbeddedLayout()
	{
		using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fm27-mac.json")
			?? throw new InvalidOperationException("Embedded fm27-mac.json missing.");
		string tmp = Path.Combine(Path.GetTempPath(), "squadeditor-fm27-mac.json");
		using (FileStream fs = File.Create(tmp)) s.CopyTo(fs);
		return GameLayout.Load(tmp);
	}

	private static string ReadEmbeddedHtml()
	{
		using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui.html")!;
		using var sr = new StreamReader(s, Encoding.UTF8);
		return sr.ReadToEnd();
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

				if (OperatingSystem.IsMacOS())
				{
					_mem = MacProcessMemory.Attach(_layout!.ProcessName);
				}
				else if (OperatingSystem.IsWindows())
				{
					_mem = ProcessMemory.Attach(_layout!.ProcessName);
				}
				else
				{
					_status = "Unsupported OS";
					return;
				}

				_session = new GameSession(_mem, _layout!);
				IndexAll();
				_status = $"Connected · {_players.Count:N0} players · {_clubs.Count:N0} clubs · {_staff.Count:N0} staff";
				Console.WriteLine(_status);
			}
			catch (Exception ex)
			{
				_status = ex.Message;
				Console.WriteLine("Connect error: " + ex.Message);
			}
		}
	}

	private static void IndexAll()
	{
		var session = _session!;
		var layout = _layout!;

		var pDef = layout.GetEntity("Player");
		var pName = pDef.Fields.First(f => f.Name == "Name");
		var pClub = pDef.Fields.First(f => f.Name == "Club");
		var pNation = pDef.Fields.First(f => f.Name == "Nation");
		var pCa = pDef.Fields.First(f => f.Name == "CA");
		var pPa = pDef.Fields.First(f => f.Name == "PA");
		var pWage = pDef.Fields.First(f => f.Name == "Wage");
		var pVal = pDef.Fields.First(f => f.Name == "Value");
		var pDob = pDef.Fields.First(f => f.Name == "Date of Birth");
		var pUid = pDef.Fields.First(f => f.Name == "Unique ID");

		string SafeRead(ulong a, FieldDef f) { try { return session.ReadField(a, f); } catch { return ""; } }
		int SafeInt(ulong a, FieldDef f) => int.TryParse(SafeRead(a, f), out int v) ? v : 0;

		var plist = new List<PlayerSummary>();
		foreach (ulong addr in session.GetCollectionItems("Players"))
		{
			string name = SafeRead(addr, pName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			int ca = SafeInt(addr, pCa);
			int pa = SafeInt(addr, pPa);
			plist.Add(new PlayerSummary(
				addr.ToString("X"), name, SafeRead(addr, pClub), SafeRead(addr, pNation),
				ca, pa, SafeInt(addr, pWage), SafeInt(addr, pVal), SafeRead(addr, pDob), SafeRead(addr, pUid)));
		}
		_players = plist.OrderByDescending(x => x.Ca).ThenByDescending(x => x.Pa).ToList();

		var cDef = layout.GetEntity("Club");
		var cName = cDef.Fields.First(f => f.Name == "Name");
		var cRep = cDef.Fields.First(f => f.Name == "Reputation");
		var cBal = cDef.Fields.First(f => f.Name == "Balance");
		var cTb = cDef.Fields.First(f => f.Name == "Transfer Budget");
		var cWb = cDef.Fields.First(f => f.Name == "Wage Budget");
		var clist = new List<ClubSummary>();
		foreach (ulong addr in session.GetCollectionItems("Clubs"))
		{
			string name = SafeRead(addr, cName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			clist.Add(new ClubSummary(addr.ToString("X"), name, SafeInt(addr, cRep), SafeInt(addr, cBal), SafeInt(addr, cTb), SafeInt(addr, cWb)));
		}
		_clubs = clist.OrderByDescending(x => x.Rep).ToList();

		var sDef = layout.GetEntity("Staff");
		var sName = sDef.Fields.First(f => f.Name == "Name");
		var sClub = sDef.Fields.First(f => f.Name == "Club");
		var sNation = sDef.Fields.First(f => f.Name == "Nation");
		var sCa = sDef.Fields.First(f => f.Name == "CA");
		var sPa = sDef.Fields.First(f => f.Name == "PA");
		var slist = new List<StaffSummary>();
		foreach (ulong addr in session.GetCollectionItems("Staff"))
		{
			string name = SafeRead(addr, sName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			slist.Add(new StaffSummary(addr.ToString("X"), name, SafeRead(addr, sClub), SafeRead(addr, sNation), SafeInt(addr, sCa), SafeInt(addr, sPa)));
		}
		_staff = slist.OrderByDescending(x => x.Ca).ToList();

		var coDef = layout.GetEntity("Competition");
		var coName = coDef.Fields.First(f => f.Name == "Name");
		var coRep = coDef.Fields.First(f => f.Name == "Reputation");
		var colist = new List<CompSummary>();
		foreach (ulong addr in session.GetCollectionItems("Competitions"))
		{
			string name = SafeRead(addr, coName);
			if (string.IsNullOrWhiteSpace(name)) continue;
			colist.Add(new CompSummary(addr.ToString("X"), name, SafeInt(addr, coRep)));
		}
		_comps = colist.OrderByDescending(x => x.Rep).ToList();
	}

	private static void HandleRequest(HttpListenerContext ctx)
	{
		try
		{
			string path = ctx.Request.Url?.AbsolutePath ?? "/";
			string method = ctx.Request.HttpMethod;

			if (path == "/" && method == "GET")
			{
				WriteText(ctx, ReadEmbeddedHtml(), "text/html; charset=utf-8");
				return;
			}
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
						frozenCount = _session?.Freezer.Entities.Count ?? 0
					});
				}
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
				int minCa = int.TryParse(qs["minCa"], out int a) ? a : 0;
				int minPa = int.TryParse(qs["minPa"], out int b) ? b : 0;
				bool freeOnly = qs["free"] == "1";
				lock (LockObj)
				{
					IEnumerable<PlayerSummary> res = _players;
					if (q.Length > 0)
						res = res.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Club.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Nation.Contains(q, StringComparison.OrdinalIgnoreCase));
					if (minCa > 0) res = res.Where(p => p.Ca >= minCa);
					if (minPa > 0) res = res.Where(p => p.Pa >= minPa);
					if (freeOnly) res = res.Where(p => string.IsNullOrWhiteSpace(p.Club));
					WriteJson(ctx, res.Take(200).ToList());
				}
				return;
			}
			if (path == "/api/clubs")
			{
				string q = (ctx.Request.QueryString["q"] ?? "").Trim();
				lock (LockObj)
				{
					IEnumerable<ClubSummary> res = _clubs;
					if (q.Length > 0) res = res.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
					WriteJson(ctx, res.Take(150).ToList());
				}
				return;
			}
			if (path == "/api/staff")
			{
				string q = (ctx.Request.QueryString["q"] ?? "").Trim();
				lock (LockObj)
				{
					IEnumerable<StaffSummary> res = _staff;
					if (q.Length > 0) res = res.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Club.Contains(q, StringComparison.OrdinalIgnoreCase));
					WriteJson(ctx, res.Take(150).ToList());
				}
				return;
			}
			if (path == "/api/comps")
			{
				string q = (ctx.Request.QueryString["q"] ?? "").Trim();
				lock (LockObj)
				{
					IEnumerable<CompSummary> res = _comps;
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
						fields.Add(new { name = f.Name, group = f.GroupKey, value = val, writable = f.IsWritable, min = f.Min, max = f.Max });
					}
					WriteJson(ctx, new { type, addr = addr.ToString("X"), frozen = _session.Freezer.IsFrozen(addr), fields });
				}
				return;
			}
			if (path == "/api/write" && method == "POST")
			{
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
				using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
				var body = JsonSerializer.Deserialize<QuickReq>(sr.ReadToEnd(), JsonOpts)!;
				ulong addr = Convert.ToUInt64(body.Addr, 16);
				lock (LockObj)
				{
					if (_session == null) { WriteJson(ctx, new { error = "Not connected" }, 400); return; }
					var def = _layout!.GetEntity(body.Type);
					void Set(string fn, string v)
					{
						var fd = def.Fields.FirstOrDefault(f => f.Name == fn);
						if (fd != null && fd.IsWritable) { try { _session.WriteField(addr, fd, v, body.Label); } catch { } }
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
							if (body.Action == "max_fitness")
							{
								Set("Condition", "100"); Set("Match Sharpness", "100"); Set("Morale", "20"); Set("Happiness", "100");
							}
							else if (body.Action == "rich_club")
							{
								Set("Balance", "500000000"); Set("Transfer Budget", "200000000"); Set("Wage Budget", "10000000");
							}
							else if (body.Action == "max_facilities")
							{
								foreach (string fn in new[] { "Training Facilities", "Youth Facilities", "Youth Coaching", "Youth Recruitment", "Corporate Facilities" })
									Set(fn, "20");
							}
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

	private static void WriteText(HttpListenerContext ctx, string content, string mime, int code = 200)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(content);
		ctx.Response.StatusCode = code;
		ctx.Response.ContentType = mime;
		ctx.Response.ContentLength64 = bytes.Length;
		ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
		ctx.Response.Close();
	}

	private static void WriteJson(HttpListenerContext ctx, object obj, int code = 200)
		=> WriteText(ctx, JsonSerializer.Serialize(obj, JsonOpts), "application/json; charset=utf-8", code);

	private sealed record PlayerSummary(string Addr, string Name, string Club, string Nation, int Ca, int Pa, int Wage, int Value, string Dob, string Uid);
	private sealed record ClubSummary(string Addr, string Name, int Rep, int Balance, int TransferBudget, int WageBudget);
	private sealed record StaffSummary(string Addr, string Name, string Club, string Nation, int Ca, int Pa);
	private sealed record CompSummary(string Addr, string Name, int Rep);
	private sealed record WriteReq(string Type, string Addr, string Field, string Value, string? Label);
	private sealed record QuickReq(string Type, string Addr, string Action, string? Label);
}
