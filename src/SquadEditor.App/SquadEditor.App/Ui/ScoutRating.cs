using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadEditor.App.Ui;

/// <summary>
/// Genie Scout-style best role, top-3 roles, role attribute highlighting and percentage rating (0-100%)
/// computed directly from a player's 15 position ratings (+0x150) and 54 attributes (+0x15F, 0..100 scale).
/// </summary>
public static class ScoutRating
{
	private sealed record RoleDef(string Code, string Tr, string En, int[] PosIndices, (int Attr, int Weight)[] Weights);

	// Attribute indices from player +0x15F (see docs/fm27-notes.md)
	private const int Cro = 0, Dri = 1, Fin = 2, Hea = 3, Lon = 4, Mar = 5, Otb = 6, Pas = 7, Tac = 9, Vis = 10;
	private const int Han = 11, Aer = 12, Cmd = 13, Com = 14, Kic = 15, Thr = 16;
	private const int Ant = 17, Dec = 18, Ooo = 19, Pos = 20, Ref = 21;
	private const int Fir = 22, Tec = 23, Fla = 26, Tea = 28, Wor = 29;
	private const int Acc = 34, Str = 36, Sta = 37, Pac = 38, Jum = 39, Bal = 42, Bra = 43, Agg = 45, Agi = 46, Cmp = 52, Cnt = 53;

	private static readonly Dictionary<int, string> AttrToField = new()
	{
		[Cro] = "Crossing", [Dri] = "Dribbling", [Fin] = "Finishing", [Hea] = "Heading", [Lon] = "Long Shots",
		[Mar] = "Marking", [Otb] = "Off The Ball", [Pas] = "Passing", [Tac] = "Tackling", [Vis] = "Vision",
		[Han] = "Handling", [Aer] = "Aerial Reach", [Cmd] = "Command Of Area", [Com] = "Communication",
		[Kic] = "Kicking", [Thr] = "Throwing", [Ant] = "Anticipation", [Dec] = "Decisions", [Ooo] = "One On Ones",
		[Pos] = "Positioning", [Ref] = "Reflexes", [Fir] = "First Touch", [Tec] = "Technique", [Fla] = "Flair",
		[Tea] = "Teamwork", [Wor] = "Work Rate", [Acc] = "Acceleration", [Str] = "Strength", [Sta] = "Stamina",
		[Pac] = "Pace", [Jum] = "Jumping Reach", [Bal] = "Balance", [Bra] = "Bravery", [Agg] = "Aggression",
		[Agi] = "Agility", [Cmp] = "Composure", [Cnt] = "Concentration"
	};

	// Position indices from player +0x150: GK=0, SW=1, DL=2, DC=3, DR=4, DM=5, ML=6, MC=7, MR=8, AML=9, AMC=10, AMR=11, ST=12, WBL=13, WBR=14
	private static readonly RoleDef[] Roles =
	{
		new("GK", "Kaleci (GK)", "Goalkeeper (GK)", new[] { 0 }, new[] { (Ref, 5), (Han, 4), (Aer, 4), (Cmd, 3), (Ooo, 4), (Pos, 4), (Cnt, 3), (Agi, 3), (Dec, 3), (Kic, 2), (Com, 2), (Thr, 2) }),
		new("BPD", "Pasör Stoper (BPD)", "Ball-Playing Defender (BPD)", new[] { 3 }, new[] { (Mar, 5), (Tac, 5), (Pos, 5), (Hea, 4), (Jum, 4), (Str, 4), (Pas, 4), (Cmp, 4), (Dec, 4), (Ant, 3), (Cnt, 3), (Pac, 3) }),
		new("CD", "Stoper (CD)", "Central Defender (CD)", new[] { 3 }, new[] { (Mar, 5), (Tac, 5), (Pos, 5), (Hea, 5), (Jum, 5), (Str, 5), (Bra, 3), (Cnt, 4), (Dec, 4), (Ant, 3), (Pac, 3) }),
		new("WB", "Kanat Bek (WB)", "Wing-Back (WB)", new[] { 2, 4, 13, 14 }, new[] { (Pac, 5), (Acc, 5), (Sta, 5), (Cro, 4), (Tac, 4), (Wor, 4), (Otb, 3), (Dri, 3), (Pos, 3), (Mar, 3), (Tea, 3), (Dec, 3) }),
		new("DM", "Defansif Orta Saha (DM)", "Defensive Midfielder (DM)", new[] { 5 }, new[] { (Tac, 5), (Pos, 5), (Mar, 4), (Ant, 4), (Cnt, 4), (Dec, 4), (Wor, 4), (Tea, 4), (Sta, 4), (Str, 3), (Pas, 3) }),
		new("DLP", "Derin Oyun Kurucu (DLP)", "Deep-Lying Playmaker (DLP)", new[] { 5, 7 }, new[] { (Pas, 5), (Vis, 5), (Fir, 5), (Tec, 4), (Dec, 5), (Cmp, 4), (Tea, 4), (Ant, 3), (Pos, 3), (Bal, 3) }),
		new("BBM", "İki Yönlü Orta Saha (BBM)", "Box-to-Box Midfielder (BBM)", new[] { 7 }, new[] { (Sta, 5), (Wor, 5), (Pas, 4), (Tac, 4), (Otb, 4), (Dec, 4), (Tea, 4), (Ant, 3), (Fir, 3), (Pac, 3), (Str, 3), (Fin, 2) }),
		new("MEZ", "Mezzala (MEZ)", "Mezzala (MEZ)", new[] { 7 }, new[] { (Pas, 5), (Dri, 4), (Fir, 4), (Tec, 4), (Otb, 5), (Dec, 4), (Acc, 4), (Wor, 4), (Vis, 4), (Sta, 3), (Fin, 3) }),
		new("AP", "Ofansif Oyun Kurucu (AP)", "Advanced Playmaker (AP)", new[] { 7, 10 }, new[] { (Vis, 5), (Pas, 5), (Fir, 5), (Tec, 5), (Dec, 5), (Fla, 4), (Otb, 4), (Cmp, 4), (Agi, 3), (Dri, 3) }),
		new("W", "Kanat (W)", "Winger (W)", new[] { 6, 8, 9, 11 }, new[] { (Acc, 5), (Pac, 5), (Dri, 5), (Cro, 5), (Agi, 4), (Tec, 4), (Otb, 4), (Fir, 3), (Fla, 3), (Sta, 3) }),
		new("IW", "Ters Ayaklı Kanat (IW)", "Inverted Winger (IW)", new[] { 9, 11 }, new[] { (Acc, 5), (Pac, 5), (Dri, 5), (Fin, 4), (Otb, 5), (Fir, 4), (Tec, 4), (Pas, 4), (Agi, 4), (Cmp, 3), (Vis, 3) }),
		new("AF", "Yaratıcı Forvet (AF)", "Advanced Forward (AF)", new[] { 12 }, new[] { (Fin, 5), (Otb, 5), (Acc, 5), (Pac, 5), (Cmp, 5), (Fir, 4), (Dri, 4), (Ant, 4), (Tec, 3), (Dec, 3), (Agi, 3) }),
		new("PF", "Presçi Forvet (PF)", "Pressing Forward (PF)", new[] { 12 }, new[] { (Wor, 5), (Agg, 4), (Sta, 5), (Acc, 4), (Pac, 4), (Fin, 4), (Otb, 4), (Ant, 4), (Bra, 4), (Tea, 4), (Str, 4), (Cmp, 3) }),
		new("TF", "Hedef Forvet (TF)", "Target Forward (TF)", new[] { 12 }, new[] { (Hea, 5), (Jum, 5), (Str, 5), (Bal, 4), (Bra, 4), (Fin, 4), (Fir, 4), (Otb, 4), (Tea, 4), (Cmp, 3), (Ant, 3) })
	};

	// Match Engine Meta weights (FM-Arena / Evidence-Based FM reality: Pace, Acceleration, Jumping Reach, Agility, Balance, Anticipation, Concentration, Dribbling carry highest real ME impact)
	private static readonly RoleDef[] MetaRoles =
	{
		new("GK", "Kaleci (GK)", "Goalkeeper (GK)", new[] { 0 }, new[] { (Ref, 8), (Agi, 7), (Aer, 6), (Han, 5), (Ooo, 5), (Pos, 4), (Cnt, 4), (Ant, 4), (Cmd, 3), (Dec, 3) }),
		new("BPD", "Pasör Stoper (BPD)", "Ball-Playing Defender (BPD)", new[] { 3 }, new[] { (Pac, 8), (Acc, 8), (Jum, 7), (Ant, 5), (Cnt, 5), (Bal, 4), (Agi, 4), (Pos, 4), (Mar, 4), (Tac, 4), (Pas, 3), (Cmp, 3) }),
		new("CD", "Stoper (CD)", "Central Defender (CD)", new[] { 3 }, new[] { (Pac, 8), (Acc, 8), (Jum, 8), (Ant, 5), (Cnt, 5), (Str, 5), (Bal, 4), (Pos, 4), (Mar, 4), (Tac, 4), (Hea, 3) }),
		new("WB", "Kanat Bek (WB)", "Wing-Back (WB)", new[] { 2, 4, 13, 14 }, new[] { (Pac, 9), (Acc, 9), (Sta, 6), (Agi, 5), (Bal, 5), (Dri, 5), (Ant, 4), (Cnt, 4), (Cro, 4), (Tac, 4), (Wor, 4) }),
		new("DM", "Defansif Orta Saha (DM)", "Defensive Midfielder (DM)", new[] { 5 }, new[] { (Pac, 7), (Acc, 7), (Jum, 5), (Ant, 6), (Cnt, 5), (Bal, 5), (Agi, 5), (Sta, 5), (Pos, 4), (Tac, 4), (Wor, 4) }),
		new("DLP", "Derin Oyun Kurucu (DLP)", "Deep-Lying Playmaker (DLP)", new[] { 5, 7 }, new[] { (Pac, 7), (Acc, 7), (Agi, 6), (Bal, 6), (Ant, 5), (Dri, 5), (Pas, 5), (Vis, 4), (Cnt, 4), (Cmp, 4), (Dec, 4) }),
		new("BBM", "İki Yönlü Orta Saha (BBM)", "Box-to-Box Midfielder (BBM)", new[] { 7 }, new[] { (Pac, 8), (Acc, 8), (Sta, 6), (Agi, 5), (Bal, 5), (Dri, 5), (Ant, 5), (Wor, 5), (Jum, 4), (Cnt, 4), (Otb, 4) }),
		new("MEZ", "Mezzala (MEZ)", "Mezzala (MEZ)", new[] { 7 }, new[] { (Pac, 8), (Acc, 8), (Dri, 6), (Agi, 6), (Bal, 5), (Ant, 5), (Otb, 5), (Sta, 5), (Pas, 4), (Cmp, 4), (Fin, 3) }),
		new("AP", "Ofansif Oyun Kurucu (AP)", "Advanced Playmaker (AP)", new[] { 7, 10 }, new[] { (Pac, 8), (Acc, 8), (Dri, 6), (Agi, 6), (Bal, 6), (Ant, 5), (Pas, 5), (Vis, 4), (Cmp, 4), (Otb, 4), (Cnt, 4) }),
		new("W", "Kanat (W)", "Winger (W)", new[] { 6, 8, 9, 11 }, new[] { (Pac, 9), (Acc, 9), (Dri, 7), (Agi, 6), (Bal, 6), (Ant, 4), (Otb, 4), (Cro, 4), (Sta, 4), (Cnt, 3) }),
		new("IW", "Ters Ayaklı Kanat (IW)", "Inverted Winger (IW)", new[] { 9, 11 }, new[] { (Pac, 9), (Acc, 9), (Dri, 7), (Agi, 6), (Bal, 6), (Ant, 5), (Otb, 5), (Fin, 4), (Cmp, 4), (Sta, 4) }),
		new("AF", "Yaratıcı Forvet (AF)", "Advanced Forward (AF)", new[] { 12 }, new[] { (Pac, 9), (Acc, 9), (Dri, 6), (Agi, 6), (Bal, 6), (Jum, 5), (Ant, 5), (Otb, 5), (Fin, 4), (Cmp, 4), (Cnt, 4) }),
		new("PF", "Presçi Forvet (PF)", "Pressing Forward (PF)", new[] { 12 }, new[] { (Pac, 9), (Acc, 9), (Sta, 6), (Wor, 6), (Bal, 5), (Agi, 5), (Jum, 5), (Ant, 5), (Dri, 5), (Fin, 4), (Cmp, 4) }),
		new("TF", "Hedef Forvet (TF)", "Target Forward (TF)", new[] { 12 }, new[] { (Jum, 9), (Pac, 7), (Acc, 7), (Str, 6), (Bal, 6), (Ant, 5), (Hea, 5), (Otb, 4), (Fin, 4), (Cmp, 4) })
	};

	private static RoleDef[] ActiveRoles =>
		string.Equals(AppSettings.Current.RoleWeightMode, "meta", StringComparison.OrdinalIgnoreCase) ? MetaRoles : Roles;

	public static IReadOnlyList<(string Code, string Label)> AllRoles() =>
		ActiveRoles.Select(r => (r.Code, Build.T(r.Tr, r.En))).ToList();

	/// <summary>Returns 2 for key role attributes, 1 for secondary attributes, 0 otherwise.</summary>
	public static int HighlightLevel(string? roleCode, string fieldName)
	{
		if (string.IsNullOrEmpty(roleCode)) return 0;
		RoleDef? role = ActiveRoles.FirstOrDefault(r => r.Code == roleCode);
		if (role == null) return 0;
		int keyThreshold = string.Equals(AppSettings.Current.RoleWeightMode, "meta", StringComparison.OrdinalIgnoreCase) ? 7 : 5;
		foreach (var (attrIdx, w) in role.Weights)
		{
			if (AttrToField.TryGetValue(attrIdx, out string? name) && name == fieldName)
			{
				return w >= keyThreshold ? 2 : 1;
			}
		}
		return 0;
	}

	/// <summary>Computes the player's top 3 roles and ratings (0..100).</summary>
	public static List<(string Role, double Rating)> EvaluateTop3(ReadOnlySpan<byte> raw69)
	{
		var results = new List<(string Role, double Rating)>();
		if (raw69.Length < 69) return results;
		ReadOnlySpan<byte> pos = raw69[..15];
		ReadOnlySpan<byte> attr = raw69.Slice(15, 54);

		foreach (RoleDef role in ActiveRoles)
		{
			int bestPos = 0;
			foreach (int pi in role.PosIndices)
			{
				if (pos[pi] > bestPos) bestPos = pos[pi];
			}
			if (bestPos < 10) continue;
			double sum = 0, totalWeight = 0;
			foreach (var (a, w) in role.Weights)
			{
				sum += attr[a] * w;
				totalWeight += w;
			}
			double posFactor = bestPos >= 18 ? 1.0 : 0.88 + (bestPos - 10) * 0.015;
			double score = Math.Round((sum / totalWeight) * posFactor, 1);
			results.Add((role.Code, score));
		}
		return results.OrderByDescending(r => r.Rating).Take(3).ToList();
	}

	/// <summary>Computes the player's best role code and 0..100 rating from raw bytes [0..14] positions + [15..68] attributes.</summary>
	public static (string Role, double Rating) Evaluate(ReadOnlySpan<byte> raw69)
	{
		var top = EvaluateTop3(raw69);
		return top.Count > 0 ? top[0] : ("", 0);
	}

	/// <summary>
	/// Projects a player's 54 attributes (0..100 raw scale) to their peak Potential Ability (PA).
	/// Strictly pure / read-only calculation — never mutates memory.
	/// </summary>
	public static byte[] ProjectAttributes(ReadOnlySpan<byte> raw69, int ca, int pa)
	{
		byte[] projected = raw69.ToArray();
		if (projected.Length < 69) return projected;
		int gap = Math.Max(0, pa - ca);
		if (gap == 0) return projected;
		// Each +10 CA headroom adds ~4.2 raw points (~0.84 on 1-20 scale) across trainable attributes
		double baseGain = gap * 0.42;
		for (int i = 0; i < 54; i++)
		{
			int cur = projected[15 + i];
			// Flair(26), Aggression(45), Bravery(43), Determination(51) grow slower in FM
			double factor = i is 26 or 43 or 45 or 51 ? 0.35 : 1.0;
			int next = (int)Math.Round(cur + baseGain * factor);
			projected[15 + i] = (byte)Math.Clamp(next, cur, 100);
		}
		return projected;
	}

	/// <summary>Computes Genie Scout-style Potential Rating (PR %) at peak PA.</summary>
	public static (string Role, double Rating) EvaluatePotential(ReadOnlySpan<byte> raw69, int ca, int pa)
	{
		if (raw69.Length < 69) return ("", 0);
		if (pa <= ca) return Evaluate(raw69);
		byte[] projected = ProjectAttributes(raw69, ca, pa);
		return Evaluate(projected);
	}

	/// <summary>Projects a single 1–20 attribute value to peak PA (read-only preview helper).</summary>
	public static int ProjectAttribute1to20(int current, int ca, int pa, string fieldName)
	{
		int gap = Math.Max(0, pa - ca);
		if (gap <= 0) return current;
		double factor = fieldName is "Flair" or "Aggression" or "Bravery" or "Determination" or "Natural Fitness" ? 0.35 : 1.0;
		int gain = (int)Math.Round((gap * 0.084) * factor);
		return Math.Clamp(current + gain, current, 20);
	}
}
