using System;

namespace SquadEditor.App.Ui;

/// <summary>
/// Genie Scout-style best role and percentage rating (0-100%) computed directly from
/// a player's 15 position ratings (+0x150) and 54 attributes (+0x15F, 0..100 scale).
/// </summary>
public static class ScoutRating
{
	private sealed record RoleDef(string Code, int[] PosIndices, (int Attr, int Weight)[] Weights);

	// Attribute indices from player +0x15F (see docs/fm27-notes.md)
	private const int Cro = 0, Dri = 1, Fin = 2, Hea = 3, Lon = 4, Mar = 5, Otb = 6, Pas = 7, Tac = 9, Vis = 10;
	private const int Han = 11, Aer = 12, Cmd = 13, Com = 14, Kic = 15, Thr = 16;
	private const int Ant = 17, Dec = 18, Ooo = 19, Pos = 20, Ref = 21;
	private const int Fir = 22, Tec = 23, Fla = 26, Tea = 28, Wor = 29;
	private const int Acc = 34, Str = 36, Sta = 37, Pac = 38, Jum = 39, Bal = 42, Bra = 43, Agg = 45, Agi = 46, Cmp = 52, Cnt = 53;

	// Position indices from player +0x150: GK=0, SW=1, DL=2, DC=3, DR=4, DM=5, ML=6, MC=7, MR=8, AML=9, AMC=10, AMR=11, ST=12, WBL=13, WBR=14
	private static readonly RoleDef[] Roles =
	{
		new("GK", new[] { 0 }, new[] { (Ref, 5), (Han, 4), (Aer, 4), (Cmd, 3), (Ooo, 4), (Pos, 4), (Cnt, 3), (Agi, 3), (Dec, 3), (Kic, 2), (Com, 2), (Thr, 2) }),
		new("BPD", new[] { 3 }, new[] { (Mar, 5), (Tac, 5), (Pos, 5), (Hea, 4), (Jum, 4), (Str, 4), (Pas, 4), (Cmp, 4), (Dec, 4), (Ant, 3), (Cnt, 3), (Pac, 3) }),
		new("CD", new[] { 3 }, new[] { (Mar, 5), (Tac, 5), (Pos, 5), (Hea, 5), (Jum, 5), (Str, 5), (Bra, 3), (Cnt, 4), (Dec, 4), (Ant, 3), (Pac, 3) }),
		new("WB", new[] { 2, 4, 13, 14 }, new[] { (Pac, 5), (Acc, 5), (Sta, 5), (Cro, 4), (Tac, 4), (Wor, 4), (Otb, 3), (Dri, 3), (Pos, 3), (Mar, 3), (Tea, 3), (Dec, 3) }),
		new("DM", new[] { 5 }, new[] { (Tac, 5), (Pos, 5), (Mar, 4), (Ant, 4), (Cnt, 4), (Dec, 4), (Wor, 4), (Tea, 4), (Sta, 4), (Str, 3), (Pas, 3) }),
		new("DLP", new[] { 5, 7 }, new[] { (Pas, 5), (Vis, 5), (Fir, 5), (Tec, 4), (Dec, 5), (Cmp, 4), (Tea, 4), (Ant, 3), (Pos, 3), (Bal, 3) }),
		new("BBM", new[] { 7 }, new[] { (Sta, 5), (Wor, 5), (Pas, 4), (Tac, 4), (Otb, 4), (Dec, 4), (Tea, 4), (Ant, 3), (Fir, 3), (Pac, 3), (Str, 3), (Fin, 2) }),
		new("MEZ", new[] { 7 }, new[] { (Pas, 5), (Dri, 4), (Fir, 4), (Tec, 4), (Otb, 5), (Dec, 4), (Acc, 4), (Wor, 4), (Vis, 4), (Sta, 3), (Fin, 3) }),
		new("AP", new[] { 7, 10 }, new[] { (Vis, 5), (Pas, 5), (Fir, 5), (Tec, 5), (Dec, 5), (Fla, 4), (Otb, 4), (Cmp, 4), (Agi, 3), (Dri, 3) }),
		new("W", new[] { 6, 8, 9, 11 }, new[] { (Acc, 5), (Pac, 5), (Dri, 5), (Cro, 5), (Agi, 4), (Tec, 4), (Otb, 4), (Fir, 3), (Fla, 3), (Sta, 3) }),
		new("IW", new[] { 9, 11 }, new[] { (Acc, 5), (Pac, 5), (Dri, 5), (Fin, 4), (Otb, 5), (Fir, 4), (Tec, 4), (Pas, 4), (Agi, 4), (Cmp, 3), (Vis, 3) }),
		new("AF", new[] { 12 }, new[] { (Fin, 5), (Otb, 5), (Acc, 5), (Pac, 5), (Cmp, 5), (Fir, 4), (Dri, 4), (Ant, 4), (Tec, 3), (Dec, 3), (Agi, 3) }),
		new("PF", new[] { 12 }, new[] { (Wor, 5), (Agg, 4), (Sta, 5), (Acc, 4), (Pac, 4), (Fin, 4), (Otb, 4), (Ant, 4), (Bra, 4), (Tea, 4), (Str, 4), (Cmp, 3) }),
		new("TF", new[] { 12 }, new[] { (Hea, 5), (Jum, 5), (Str, 5), (Bal, 4), (Bra, 4), (Fin, 4), (Fir, 4), (Otb, 4), (Tea, 4), (Cmp, 3), (Ant, 3) })
	};

	/// <summary>Computes the player's best role code and 0..100 rating from raw bytes [0..14] positions + [15..68] attributes.</summary>
	public static (string Role, double Rating) Evaluate(ReadOnlySpan<byte> raw69)
	{
		if (raw69.Length < 69)
		{
			return ("", 0);
		}
		ReadOnlySpan<byte> pos = raw69[..15];
		ReadOnlySpan<byte> attr = raw69.Slice(15, 54);

		string bestRole = "";
		double bestScore = 0;
		foreach (RoleDef role in Roles)
		{
			int bestPos = 0;
			foreach (int pi in role.PosIndices)
			{
				if (pos[pi] > bestPos) bestPos = pos[pi];
			}
			if (bestPos < 10)
			{
				continue;
			}
			double sum = 0, totalWeight = 0;
			foreach (var (a, w) in role.Weights)
			{
				sum += attr[a] * w;
				totalWeight += w;
			}
			// Attribute score is 0..100; slight penalty if position rating is below natural (18..20).
			double posFactor = bestPos >= 18 ? 1.0 : 0.88 + (bestPos - 10) * 0.015;
			double score = (sum / totalWeight) * posFactor;
			if (score > bestScore)
			{
				bestScore = score;
				bestRole = role.Code;
			}
		}
		return (bestRole, Math.Round(bestScore, 1));
	}
}
