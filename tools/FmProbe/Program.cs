// FmProbe: read-only memory explorer for mapping Football Manager layouts.
using System.Globalization;
using System.Text;
using SquadEditor.Core.Memory;
using SquadEditor.Core.Layout;

if (args.Length == 0)
{
    Console.WriteLine("""
        FmProbe <command> [args]   (read-only)
          regions                       summary of memory regions
          str <text> [max]              find UTF-8 and UTF-16 text in writable memory
          ptr <hexAddr> [range] [max]   find pointers to [addr-range, addr]
          dump <hexAddr> [len]          hex dump
          bytes <hex pattern> [max]     scan writable memory ("?" wildcards)
        """);
    return;
}

if (args[0] == "check")
{
    var l = SquadEditor.Core.Layout.GameLayout.Load(args[1]);
    Console.WriteLine($"OK: {l.Game} {l.GameVersion}, {l.Entities.Sum(e => e.Fields.Count)} fields, {l.Presets.Count} presets");
    return;
}

using var mem = ProcessMemory.Attach("fm");
var writable = mem.GetRegions().Where(r => r.IsWritable).ToList();
string cmd = args[0];

switch (cmd)
{
    case "regions":
    {
        ulong total = 0;
        foreach (var r in writable) total += r.Size;
        Console.WriteLine($"pid {mem.ProcessId}, writable regions: {writable.Count}, {total / (1024 * 1024)} MB");
        foreach (var r in writable.OrderByDescending(r => r.Size).Take(20))
            Console.WriteLine($"  {r.Start:X12} {r.Size / 1024,10} KB");
        break;
    }
    case "str":
    {
        int max = args.Length > 2 ? int.Parse(args[2]) : 50;
        Find(Encoding.UTF8.GetBytes(args[1]), "utf8", max);
        Find(Encoding.Unicode.GetBytes(args[1]), "utf16", max);
        break;
    }
    case "bytes":
    {
        int max = args.Length > 2 ? int.Parse(args[2]) : 50;
        var pattern = Pattern.Parse(args[1]);
        foreach (var hit in PatternScanner.Scan(mem, writable, pattern, max))
            Console.WriteLine($"{hit:X12}");
        break;
    }
    case "ptr":
    {
        ulong target = Hex(args[1]);
        ulong range = args.Length > 2 ? Hex(args[2]) : 0;
        int max = args.Length > 3 ? int.Parse(args[3]) : 50;
        int found = 0;
        foreach (var (addr, buf) in Chunks())
        {
            for (int i = 0; i + 8 <= buf.Length; i += 8)
            {
                ulong v = BitConverter.ToUInt64(buf, i);
                if (v <= target && target - v <= range)
                {
                    Console.WriteLine($"{addr + (ulong)i:X12} -> {v:X12} (+0x{target - v:X})");
                    if (++found >= max) return;
                }
            }
        }
        break;
    }
    case "dump":
    {
        ulong a = Hex(args[1]);
        int len = args.Length > 2 ? int.Parse(args[2]) : 256;
        var buf = new byte[len];
        if (!mem.TryRead(a, buf)) { Console.WriteLine("unreadable"); return; }
        for (int i = 0; i < len; i += 16)
        {
            var row = buf.AsSpan(i, Math.Min(16, len - i));
            var hex = string.Join(" ", row.ToArray().Select(b => b.ToString("X2")));
            var txt = new string(row.ToArray().Select(b => b is >= 32 and < 127 ? (char)b : '.').ToArray());
            Console.WriteLine($"{a + (ulong)i:X12} +{i:X4}  {hex,-47}  {txt}");
        }
        break;
    }
    case "objs":
    {
        // objs <vtableHex> [nameFilter] : objects with this vtable, person-style name at +0x40
        ulong vt = Hex(args[1]);
        string? filter = args.Length > 2 ? args[2] : null;
        int count = 0;
        foreach (var (addr, buf) in Chunks())
        {
            for (int i = 0; i + 8 <= buf.Length - 8; i += 8)
            {
                if (BitConverter.ToUInt64(buf, i) != vt) continue;
                ulong obj = addr + (ulong)i;
                count++;
                if (filter == null && count > 30) continue;
                string name = ReadLenString(obj + 0x40);
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                var h = new byte[0x90];
                mem.TryRead(obj, h);
                uint uid = BitConverter.ToUInt32(h, 0x0C);
                int doy = BitConverter.ToUInt16(h, 0x88), year = BitConverter.ToUInt16(h, 0x8A);
                Console.WriteLine($"{obj:X12} uid {uid,10} dob {doy}/{year} {name}");
            }
        }
        Console.WriteLine($"total {count}");
        break;
    }
    case "fuzzy":
    {
        // fuzzy <scale> <tol> v1 v2 ... : consecutive bytes with |b - v*scale| <= tol
        int scale = int.Parse(args[1]), tol = int.Parse(args[2]);
        int[] want = args.Skip(3).Select(int.Parse).ToArray();
        int found = 0;
        foreach (var (addr, buf) in Chunks(want.Length))
        {
            for (int i = 0; i + want.Length <= buf.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < want.Length && ok; k++)
                    ok = Math.Abs(buf[i + k] - want[k] * scale) <= tol;
                if (!ok) continue;
                Console.WriteLine($"{addr + (ulong)i:X12}");
                if (++found >= 100) return;
            }
        }
        break;
    }
    case "run":
    {
        // run <addrInArray> <vtableHex> : extent of a contiguous array of pointers to objects with this vtable
        ulong at = Hex(args[1]);
        bool any = args[2] == "any";
        ulong vt = any ? 0 : Hex(args[2]);
        mem.TryGetModule("game_plugin.dll", out var gp);
        bool IsObj(ulong slot)
        {
            var b = new byte[8];
            if (!mem.TryRead(slot, b)) return false;
            ulong p = BitConverter.ToUInt64(b, 0);
            if (p == 0 || !mem.TryRead(p, b)) return false;
            ulong v = BitConverter.ToUInt64(b, 0);
            return any ? v >= gp.Start && v < gp.End : v == vt;
        }
        ulong lo = at, hi = at;
        while (IsObj(lo - 8)) lo -= 8;
        while (IsObj(hi + 8)) hi += 8;
        Console.WriteLine($"{lo:X12}..{hi:X12} count {(hi - lo) / 8 + 1}");
        break;
    }
    case "bigruns":
    {
        // bigruns <vtableHex> [minRun] : arrays (8-byte stride) holding many pointers to objects with this vtable
        ulong vt = Hex(args[1]);
        int minRun = args.Length > 2 ? int.Parse(args[2]) : 1000;
        var objs = new HashSet<ulong>();
        foreach (var (addr, buf) in Chunks())
            for (int i = 0; i + 8 <= buf.Length - 8; i += 8)
                if (BitConverter.ToUInt64(buf, i) == vt) objs.Add(addr + (ulong)i);
        Console.WriteLine($"objects: {objs.Count}");
        foreach (var (addr, buf) in Chunks(0))
        {
            int run = 0, gaps = 0; int start = 0;
            for (int i = 0; i + 8 <= buf.Length; i += 8)
            {
                ulong v = BitConverter.ToUInt64(buf, i);
                if (objs.Contains(v)) { if (run == 0) start = i; run++; gaps = 0; }
                else if (run > 0 && v != 0 && ++gaps <= 0) { }
                else if (run > 0 && (v != 0 || ++gaps > 64))
                {
                    if (run >= minRun) Console.WriteLine($"{addr + (ulong)start:X12} run {run} to {addr + (ulong)i:X12}");
                    run = 0; gaps = 0;
                }
            }
            if (run >= minRun) Console.WriteLine($"{addr + (ulong)start:X12} run {run} (chunk end)");
        }
        break;
    }
    case "roots":
    {
        // roots <vtableHex> [minHits] : static pointers in game_plugin data that reach arrays of these objects (depth <= 3)
        ulong vt = Hex(args[1]);
        int minHits = args.Length > 2 ? int.Parse(args[2]) : 48;
        mem.TryGetModule("game_plugin.dll", out var gp);
        var objs = new HashSet<ulong>();
        foreach (var (addr, buf) in Chunks())
            for (int i = 0; i + 8 <= buf.Length - 8; i += 8)
                if (BitConverter.ToUInt64(buf, i) == vt) objs.Add(addr + (ulong)i);
        Console.WriteLine($"objects: {objs.Count}");
        var heapRegions = writable.Where(r => r.Start < gp.Start || r.Start >= gp.End).ToList();
        bool IsHeap(ulong v) => v > 0x10000 && (v < gp.Start || v >= gp.End) && v < 0x7FF000000000;
        ulong[] Q(ulong a, int n) { var b = new byte[n * 8]; if (!mem.TryRead(a, b)) return Array.Empty<ulong>(); var r = new ulong[n]; for (int i = 0; i < n; i++) r[i] = BitConverter.ToUInt64(b, i * 8); return r; }
        int Hits(ulong a) { var q = Q(a, 64); int h = 0; foreach (var v in q) if (objs.Contains(v)) h++; return h; }
        var seen = new HashSet<ulong>();
        int reported = 0;
        void Walk(ulong obj, string path, int depth)
        {
            if (depth > 3 || !seen.Add(obj)) return;
            var q = Q(obj, 64);
            for (int i = 0; i < q.Length; i++)
            {
                ulong v = q[i];
                if (!IsHeap(v)) continue;
                int h = Hits(v);
                if (h >= minHits)
                {
                    if (reported++ < 200) Console.WriteLine($"{path}+{i * 8:X} -> {v:X12} hits {h}");
                    continue;
                }
                Walk(v, $"{path}+{i * 8:X}->", depth + 1);
            }
        }
        foreach (var r in writable.Where(r => r.Start >= gp.Start && r.Start < gp.End))
        {
            var buf = new byte[r.Size];
            if (!mem.TryRead(r.Start, buf)) continue;
            for (int i = 0; i + 8 <= buf.Length; i += 8)
            {
                ulong v = BitConverter.ToUInt64(buf, i);
                if (!IsHeap(v)) continue;
                Walk(v, $"gp+{r.Start + (ulong)i - gp.Start:X}->", 1);
            }
        }
        Console.WriteLine($"done, {reported} hits");
        break;
    }
    case "read":
    {
        // read <layout> <collection> <nameFilter> <field...> : any entity through GameSession (read-only)
        var layout = SquadEditor.Core.Layout.GameLayout.Load(args[1]);
        var session = new SquadEditor.Core.GameSession(mem, layout);
        var col = layout.GetCollection(args[2]);
        var def = layout.GetEntity(col.Entity);
        foreach (var a in session.GetCollectionItems(args[2]))
        {
            string name;
            try { name = session.ReadField(a, def.Fields.First(f => f.Name == def.DisplayField)); } catch { continue; }
            if (!name.Equals(args[3], StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{a:X12} {name}");
            foreach (var f in args.Skip(4))
            {
                string v;
                try { v = session.ReadField(a, def.Fields.First(x => x.Name == f)); } catch (Exception e) { v = "!" + e.GetType().Name; }
                Console.WriteLine($"   {f} = {v}");
            }
        }
        break;
    }
    case "injuries":
    {
        // injuries <layout> [max] : players with a non-empty injury or absence list (read-only)
        var layout = SquadEditor.Core.Layout.GameLayout.Load(args[1]);
        var session = new SquadEditor.Core.GameSession(mem, layout);
        var def = layout.GetEntity("Player");
        int max = args.Length > 2 ? int.Parse(args[2]) : 20, shown = 0;
        foreach (var a in session.GetCollectionItems("Players"))
        {
            ulong rec = mem.Read<ulong>(a + 0xF8);
            if (rec == 0) continue;
            ulong ib = mem.Read<ulong>(rec), ie = mem.Read<ulong>(rec + 8), ab = mem.Read<ulong>(rec + 0x18), ae = mem.Read<ulong>(rec + 0x20);
            bool bans = false;
            for (ulong e = ab; e < ae && e - ab < 32 * 50; e += 32) if (mem.Read<ushort>(e + 0x16) == 0xFFFF) bans = true;
            if (ie == ib && !bans) continue;
            string name = session.ReadField(a, def.Fields.First(f => f.Name == "Name"));
            Console.WriteLine($"{a:X12} {name}: injuries {ie - ib} bytes at {ib:X12}, absences {(ae - ab) / 32}{(bans ? " (ban)" : "")} at {ab:X12}");
            if (++shown >= max) break;
        }
        break;
    }
    case "players":
    {
        // players <layout> [nameFilter] [field...] : read-only end-to-end test through GameSession
        var layout = SquadEditor.Core.Layout.GameLayout.Load(args[1]);
        var session = new SquadEditor.Core.GameSession(mem, layout);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var items = session.GetCollectionItems("Players");
        Console.WriteLine($"players: {items.Count} in {sw.ElapsedMilliseconds} ms");
        var def = layout.GetEntity("Player");
        string? filter = args.Length > 2 ? args[2] : null;
        string[] show = args.Length > 3 ? args.Skip(3).ToArray() : new[] { "CA", "PA", "Birth Year", "Finishing", "Pace" };
        int n = 0;
        foreach (var a in items)
        {
            string name;
            try { name = session.ReadField(a, def.Fields.First(f => f.Name == "Name")); } catch (Exception e) { name = "<" + e.Message + ">"; }
            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var vals = show.Select(f => { try { return f + "=" + session.ReadField(a, def.Fields.First(x => x.Name == f)); } catch (Exception e) { return f + "=!" + e.GetType().Name; } });
            Console.WriteLine($"{a:X12} {name} | {string.Join(" ", vals)}");
            if (++n >= 25) break;
        }
        break;
    }
    case "rtti":
    {
        // rtti NAME... : vtable (via RTTI) and live object count per db class
        mem.TryGetModule("game_plugin.dll", out var gp);
        foreach (var arg in args.Skip(1))
        {
            string n = arg.Split(':')[0];
            uint off = arg.Contains(':') ? Convert.ToUInt32(arg.Split(':')[1], 16) : 0;
            var vt = Rtti.FindVtable(mem, gp, ".?AV" + n + "@db@@", off);
            if (vt == null) { Console.WriteLine($"{n,-32} no vtable"); continue; }
            var objs = ObjectScanner.FindObjects(mem, vt.Value, 5_000_000);
            Console.WriteLine($"{n,-32} vt gp+{vt.Value - gp.Start:X} count {objs.Count} first {(objs.Count > 0 ? objs[0].ToString("X12") : "-")}");
        }
        break;
    }
    case "vthist":
    {
        // vthist [top] : histogram of game_plugin vtables at the start of heap objects, with RTTI names
        int top = args.Length > 1 ? int.Parse(args[1]) : 80;
        mem.TryGetModule("game_plugin.dll", out var gp);
        var hist = new Dictionary<ulong, int>();
        foreach (var (addr, buf) in Chunks(0))
        {
            var w = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buf.AsSpan(0, buf.Length & ~7));
            foreach (ulong v in w)
                if (v >= gp.Start && v < gp.End) { hist.TryGetValue(v, out int c); hist[v] = c + 1; }
        }
        int shown = 0;
        foreach (var (vt, count) in hist.OrderByDescending(kv => kv.Value))
        {
            string? name = RttiName(vt, gp.Start);
            if (name == null) continue;
            Console.WriteLine($"{count,9}  gp+{vt - gp.Start:X}  {name}");
            if (++shown >= top) break;
        }
        break;
    }
    case "fields":
    {
        // fields <hexAddr> [len] [depth] : pointer fields that lead to RTTI-typed objects
        ulong a = Hex(args[1]);
        int len = args.Length > 2 ? int.Parse(args[2]) : 0x400;
        int depth = args.Length > 3 ? int.Parse(args[3]) : 1;
        mem.TryGetModule("game_plugin.dll", out var gp);
        void Show(ulong obj, int n, int d, string indent)
        {
            var buf = new byte[n];
            if (!mem.TryRead(obj, buf)) return;
            for (int i = 0; i + 8 <= n; i += 8)
            {
                ulong v = BitConverter.ToUInt64(buf, i);
                if (v < 0x10000 || v >= 0x7FF000000000) continue;
                var vb = new byte[8];
                if (!mem.TryRead(v, vb)) continue;
                ulong vt = BitConverter.ToUInt64(vb, 0);
                if (vt < gp.Start || vt >= gp.End) continue;
                string? name = RttiName(vt, gp.Start);
                if (name == null) continue;
                Console.WriteLine($"{indent}+0x{i:X3} -> {v:X12} {name}");
                if (d > 1) Show(v, 0x200, d - 1, indent + "    ");
            }
        }
        Show(a, len, depth, "");
        break;
    }
    case "strs":
    {
        // strs <hexAddr> [len] : fields that point (directly or through one pointer) to length-prefixed UTF-8 text
        ulong a = Hex(args[1]);
        int len = args.Length > 2 ? int.Parse(args[2]) : 0x200;
        var buf = new byte[len];
        mem.TryRead(a, buf);
        string? Text(ulong p)
        {
            var h = new byte[4];
            if (p < 0x10000 || !mem.TryRead(p, h)) return null;
            int l = BitConverter.ToInt32(h, 0);
            if (l < 2 || l > 120) return null;
            var t = new byte[l];
            if (!mem.TryRead(p + 4, t)) return null;
            string s = Encoding.UTF8.GetString(t);
            return s.All(c => !char.IsControl(c)) && s.Any(char.IsLetter) ? s : null;
        }
        for (int i = 0; i + 8 <= len; i += 8)
        {
            ulong v = BitConverter.ToUInt64(buf, i);
            string? d = Text(v);
            if (d != null) { Console.WriteLine($"+0x{i:X3} direct  \"{d}\""); continue; }
            var pb = new byte[8];
            if (v > 0x10000 && v < 0x7FF000000000 && mem.TryRead(v, pb))
            {
                string? ind = Text(BitConverter.ToUInt64(pb, 0));
                if (ind != null) Console.WriteLine($"+0x{i:X3} ->[0]   \"{ind}\"");
            }
        }
        break;
    }
    case "people":
    {
        // people <class[:colOffset]> <name filter> : objects of a db class, names read from the person part (+0x50 / +0x58)
        mem.TryGetModule("game_plugin.dll", out var gp);
        string n = args[1].Split(':')[0];
        uint off = args[1].Contains(':') ? Convert.ToUInt32(args[1].Split(':')[1], 16) : 0;
        ulong vt = Rtti.FindVtable(mem, gp, ".?AV" + n + "@db@@", off) ?? 0;
        string f = args[2];
        string Entry(ulong at) { try { return ReadLenString(mem.Read<ulong>(at)); } catch { return ""; } }
        foreach (ulong o in ObjectScanner.FindObjects(mem, vt, 5_000_000))
        {
            string name = Entry(o + 0x50) + " " + Entry(o + 0x58);
            if (!name.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{o:X12} uid {mem.Read<uint>(o + 0x0C)} {name}");
        }
        break;
    }
    case "squad":
    {
        // squad <playerHex> : the player's team, contract, club teams and their player lists
        ulong pl = Hex(args[1]);
        ulong team = mem.Read<ulong>(pl + 0x130), con = mem.Read<ulong>(pl + 0x330);
        ulong club = team == 0 ? 0 : mem.Read<ulong>(team + 0x30);
        Console.WriteLine($"player {pl:X12} team {team:X12} club {club:X12} {ReadLenString(club + 0xC8)} contract {con:X12} id {mem.Read<uint>(pl + 0x290)} uid {mem.Read<uint>(pl + 0x294)}");
        if (con != 0)
            Console.WriteLine($"  contract owner {mem.Read<ulong>(con + 8):X12} (person {pl + 0x288:X12}) team {mem.Read<ulong>(con + 0x10):X12} senior {mem.Read<ulong>(con + 0x18):X12} job {mem.Read<byte>(con + 0x26)} status {mem.Read<byte>(con + 0x54)} no {mem.Read<byte>(con + 0x5D)} list {mem.Read<byte>(con + 0x57)} kind {mem.Read<byte>(con + 0xC3)}");
        if (club == 0) break;
        ulong teams = mem.Read<ulong>(club + 0x18);
        for (int i = 0; i < 12; i++)
        {
            ulong t = mem.Read<ulong>(teams + (ulong)i * 8);
            if (t == 0 || mem.Read<ulong>(t + 0x30) != club) break;
            ulong b = mem.Read<ulong>(t + 0x38), e = mem.Read<ulong>(t + 0x40), c = mem.Read<ulong>(t + 0x48);
            int idx = -1; for (ulong a = b; a < e; a += 8) if (mem.Read<ulong>(a) == pl) idx = (int)((a - b) / 8);
            var tail = new byte[64]; mem.TryRead(c, tail);
            Console.WriteLine($"  team[{i}] {t:X12} kind {mem.Read<byte>(t + 0x28)} players {(e - b) / 8} cap {(c - b) / 8} idx {idx} capt {mem.Read<ulong>(t + 0x88):X} vice {mem.Read<ulong>(t + 0x90):X} after-cap {Convert.ToHexString(tail, 0, 32)}");
        }
        break;
    }
    case "listroom":
    {
        // listroom <layout> : room in the senior teams' player lists (capacity, then allocator block slack)
        var layout = SquadEditor.Core.Layout.GameLayout.Load(args[1]);
        var session = new SquadEditor.Core.GameSession(mem, layout);
        int[] sizes = { 144, 160, 192, 224, 256, 288, 320, 384, 448, 512 };
        var hist = new SortedDictionary<int,int>(); var seen = new HashSet<ulong>(); int cap = 0, slack = 0, slackDirty = 0, none = 0, outside = 0;
        foreach (var pl in session.GetCollectionItems("Players"))
        {
            ulong team = mem.Read<ulong>(pl + 0x130); if (team == 0) continue;
            ulong club = mem.Read<ulong>(team + 0x30); if (club == 0) continue;
            ulong st = mem.Read<ulong>(mem.Read<ulong>(club + 0x18)); if (!seen.Add(st)) continue;
            ulong b = mem.Read<ulong>(st + 0x38), e = mem.Read<ulong>(st + 0x40), c = mem.Read<ulong>(st + 0x48);
            if (c > e) { cap++; continue; }
            ulong bytes = c - b;
            if (bytes <= (ulong)sizes[0] || bytes > (ulong)sizes[^1]) { outside++; hist[(int)bytes / 8] = hist.GetValueOrDefault((int)bytes / 8) + 1; continue; }
            ulong block = (ulong)sizes.First(x => (ulong)x >= bytes);
            if (block == bytes) { none++; continue; }
            var t = new byte[block - bytes]; mem.TryRead(c, t);
            if (t.All(x => x == 0)) slack++; else slackDirty++;
        }
        Console.WriteLine($"senior teams {seen.Count}: below capacity {cap}, block slack {slack}, slack not zero {slackDirty}, exact block {none}, outside classes {outside}: {string.Join(" ", hist.Select(k => k.Key + ":" + k.Value))}");
        break;
    }
    case "selections":
    {
        // selections : simatch::PREPARED_TACTICS_CREATOR_ARRAY objects, their list and 26 person-id slots
        mem.TryGetModule("game_plugin.dll", out var gp);
        ulong? vt = Rtti.FindVtable(mem, gp, ".?AVPREPARED_TACTICS_CREATOR_ARRAY@simatch@@");
        Console.WriteLine($"vtable {(vt is null ? "none" : $"gp+{vt.Value - gp.Start:X}")}");
        if (vt is null) break;
        foreach (ulong o in ObjectScanner.FindObjects(mem, vt.Value, 10000))
        {
            ulong b = mem.Read<ulong>(o + 8), e = mem.Read<ulong>(o + 16), c = mem.Read<ulong>(o + 24);
            var ids = Enumerable.Range(0, 26).Select(i => mem.Read<uint>(o + 0x28 + (ulong)i * 4)).ToArray();
            Console.WriteLine($"{o:X12} list {b:X}/{(e - b) / 8}/{(c - b) / 8} flags {mem.Read<uint>(o + 0x20):X8} ids {string.Join(",", ids.Select(x => x == 0xFFFFFFFF ? "-" : x.ToString()))}");
        }
        break;
    }
    case "nametable":
    {
        // nametable <entryHex> : walk the name table around an entry (0x18 stride, consecutive ids)
        ulong e = Hex(args[1]);
        bool Ok(ulong a, out uint id) { id = 0; try { ulong sp = mem.Read<ulong>(a); id = mem.Read<uint>(a + 8); int l = mem.Read<int>(sp); return l > 0 && l < 200; } catch { return false; } }
        Ok(e, out uint id0);
        ulong lo = e, hi = e; uint idl = id0, idh = id0;
        while (Ok(lo - 0x18, out uint i) && i == idl - 1) { lo -= 0x18; idl = i; }
        while (Ok(hi + 0x18, out uint i) && i == idh + 1) { hi += 0x18; idh = i; }
        Console.WriteLine($"{lo:X12}..{hi:X12} ids {idl}..{idh} count {(hi - lo) / 0x18 + 1}");
        foreach (ulong a in new[] { lo, lo + 0x18, e, hi - 0x18, hi })
        {
            var b = new byte[0x18]; mem.TryRead(a, b);
            Console.WriteLine($"  {a:X12} {ReadLenString(a)} | {Convert.ToHexString(b, 8, 16)}");
        }
        break;
    }
    case "faz1":
    {
        mem.TryGetModule("game_plugin.dll", out var gp);
        ulong? nVt = Rtti.FindVtable(mem, gp, ".?AVNATION@db@@");
        ulong? cVt = Rtti.FindVtable(mem, gp, ".?AVCOMP@db@@");
        ulong? clVt = Rtti.FindVtable(mem, gp, ".?AVCLUB@db@@");
        var vts = new[] { nVt!.Value, cVt!.Value, clVt!.Value };
        var found = ObjectScanner.FindObjects(mem, vts);

        Console.WriteLine("=== NATIONS (Türkiye, Brazil, England, San Marino) ===");
        var sampleNations = new List<(string Name, byte[] Raw)>();
        foreach (ulong a in found[nVt.Value])
        {
            string n = ReadLenString(a + 0x18);
            string code = ReadLenString(a + 0x28);
            if (code is "TUR" or "BRA" or "ENG" or "SMR")
            {
                var raw = new byte[0x200]; mem.TryRead(a, raw);
                sampleNations.Add(($"{code}({n})", raw));
                Console.WriteLine($"  {a:X12} {code} {n} uid={BitConverter.ToUInt32(raw, 0x0C)}");
            }
        }
        if (sampleNations.Count >= 3)
        {
            Console.WriteLine("--- Nation u16 diffs (reputation ~ 1000..9500) ---");
            for (int o = 0x10; o <= 0x1FC; o += 2)
            {
                var vals = sampleNations.Select(x => BitConverter.ToUInt16(x.Raw, o)).ToArray();
                if (vals.Distinct().Count() >= 3 && vals.All(v => v >= 500 && v <= 10000))
                    Console.WriteLine($"  +0x{o:X3} u16: " + string.Join("  ", sampleNations.Select((x, i) => $"{x.Name.Split('(')[0]}={vals[i]}")));
            }
            Console.WriteLine("--- Nation u8 diffs (youth 1..200 / dev 1..5 / imp 1..5 / econ 1..20) ---");
            for (int o = 0x38; o <= 0x1FF; o++)
            {
                var vals = sampleNations.Select(x => x.Raw[o]).ToArray();
                if (vals.Distinct().Count() >= 2 && vals.All(v => v >= 1 && v <= 200))
                {
                    // Print if BRA > SMR or ENG > SMR (heuristic for youth/econ/dev/imp)
                    int iBra = sampleNations.FindIndex(x => x.Name.StartsWith("BRA"));
                    int iEng = sampleNations.FindIndex(x => x.Name.StartsWith("ENG"));
                    int iSmr = sampleNations.FindIndex(x => x.Name.StartsWith("SMR"));
                    if (iBra >= 0 && iEng >= 0 && iSmr >= 0 && (vals[iBra] > vals[iSmr] || vals[iEng] > vals[iSmr]))
                        Console.WriteLine($"  +0x{o:X3} u8:  " + string.Join("  ", sampleNations.Select((x, i) => $"{x.Name.Split('(')[0]}={vals[i]}")));
                }
            }
        }

        Console.WriteLine("\n=== COMPS (Süper Lig, Premier League, Champions League) ===");
        var sampleComps = new List<(string Name, byte[] Raw)>();
        foreach (ulong a in found[cVt.Value])
        {
            string n = ReadLenString(a + 0x40);
            if (n.Contains("Süper Lig") || n == "English Premier Division" || n == "Premier League" || n.Contains("Champions League") || n.Contains("Şampiyonlar Ligi"))
            {
                var raw = new byte[0x1B0]; mem.TryRead(a, raw);
                if (sampleComps.Count < 8)
                {
                    sampleComps.Add((n, raw));
                    Console.WriteLine($"  {a:X12} uid={BitConverter.ToUInt32(raw, 0x0C)} {n} | short={ReadLenString(a + 0x48)}");
                }
            }
        }
        if (sampleComps.Count >= 2)
        {
            for (int o = 0x10; o <= 0x1AC; o += 2)
            {
                var vals = sampleComps.Select(x => BitConverter.ToUInt16(x.Raw, o)).ToArray();
                if (vals.Distinct().Count() >= 2 && vals.All(v => v >= 100 && v <= 10000))
                    Console.WriteLine($"  +0x{o:X3} u16: " + string.Join("  ", sampleComps.Take(4).Select((x, i) => $"{x.Name[..Math.Min(12, x.Name.Length)]}={vals[i]}")));
            }
        }

        Console.WriteLine("\n=== FENERBAHÇE CLUB_FINANCE_BASE ===");
        foreach (ulong a in found[clVt.Value])
        {
            if (ReadLenString(a + 0xC8) == "Fenerbahçe")
            {
                ulong fin = mem.Read<ulong>(a + 0x150);
                Console.WriteLine($"  Club={a:X12} Finance={fin:X12}");
                var fb = new byte[0x900]; mem.TryRead(fin, fb);
                for (int o = 0; o < 0x900; o += 4)
                {
                    int iv = BitConverter.ToInt32(fb, o);
                    float fv = BitConverter.ToSingle(fb, o);
                    if ((o >= 0x7B0 && o <= 0x8D0) && (iv != 0))
                    {
                        Console.WriteLine($"  +0x{o:X3}: i32={iv,12}  f32={fv:0.##}  u8=[{fb[o]},{fb[o+1]},{fb[o+2]},{fb[o+3]}]");
                    }
                }
                break;
            }
        }
        break;
    }
    default:
        Console.WriteLine("unknown command");
        break;
}

void Find(byte[] needle, string label, int max)
{
    int found = 0;
    foreach (var (addr, buf) in Chunks(needle.Length))
    {
        int start = 0;
        while (true)
        {
            int i = buf.AsSpan(start).IndexOf(needle);
            if (i < 0) break;
            Console.WriteLine($"{label} {addr + (ulong)(start + i):X12}");
            if (++found >= max) return;
            start += i + 1;
        }
    }
}

IEnumerable<(ulong Addr, byte[] Buf)> Chunks(int overlap = 8)
{
    const int Size = 32 * 1024 * 1024;
    foreach (var r in writable)
    {
        for (ulong off = 0; off < r.Size; off += Size)
        {
            int len = (int)Math.Min((ulong)Size + (ulong)overlap, r.Size - off);
            var buf = new byte[len];
            if (mem.TryRead(r.Start + off, buf)) yield return (r.Start + off, buf);
        }
    }
}

string ReadLenString(ulong ptrAddr)
{
    var p = new byte[8];
    if (!mem.TryRead(ptrAddr, p)) return "";
    ulong sp = BitConverter.ToUInt64(p, 0);
    if (sp == 0) return "";
    var l = new byte[4];
    if (!mem.TryRead(sp, l)) return "";
    int len = BitConverter.ToInt32(l, 0);
    if (len <= 0 || len > 200) return "";
    var t = new byte[len];
    return mem.TryRead(sp + 4, t) ? Encoding.UTF8.GetString(t) : "";
}

string? RttiName(ulong vt, ulong moduleBase)
{
    try
    {
        ulong col = mem.Read<ulong>(vt - 8);
        if (col < moduleBase || mem.Read<uint>(col) != 1) return null;
        uint off = mem.Read<uint>(col + 4);
        ulong td = moduleBase + mem.Read<uint>(col + 12);
        var b = new byte[96];
        if (!mem.TryRead(td + 16, b)) return null;
        string n = Encoding.ASCII.GetString(b);
        int z = n.IndexOf(' ');
        n = z >= 0 ? n[..z] : n;
        if (!n.StartsWith(".?AV")) return null;
        return off == 0 ? n : $"{n} (+0x{off:X})";
    }
    catch { return null; }
}

static ulong Hex(string s) => ulong.Parse(s.Replace("0x", ""), NumberStyles.HexNumber);
