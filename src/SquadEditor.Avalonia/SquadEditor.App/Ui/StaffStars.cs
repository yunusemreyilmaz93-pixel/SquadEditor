using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SquadEditor.App.Ui;

/// <summary>
/// Football Manager / Genie Scout coaching star calculator (0.5★ – 5.0★) across all 9 training categories.
/// Uses the classic FM weighted formula: primary coaching attribute + secondary attribute + DDM (Det + Mot + Man).
/// </summary>
public static class StaffStars
{
	public sealed record StarCategory(string Tr, string En, double Stars)
	{
		public string Label => Build.T(Tr, En);
		public string StarsText => $"{Stars:0.0}★";
	}

	private static double ToStars(int weightedScore, int maxScore)
	{
		if (weightedScore <= 0) return 0.5;
		double raw = (double)weightedScore / maxScore * 5.0;
		double halfSteps = Math.Round(raw * 2.0, MidpointRounding.AwayFromZero) / 2.0;
		return Math.Clamp(halfSteps, 0.5, 5.0);
	}

	public static IReadOnlyList<StarCategory> Evaluate(StaffRow s)
	{
		int ddm = s.Determination + s.Motivating + s.ManManagement;
		return new[]
		{
			new StarCategory("Hücum (Taktik)", "Attacking (Tactical)", ToStars(6 * s.Attacking + 3 * s.Tactical + 2 * ddm, 300)),
			new StarCategory("Hücum (Teknik)", "Attacking (Technical)", ToStars(6 * s.Attacking + 3 * s.Technical + 2 * ddm, 300)),
			new StarCategory("Savunma (Taktik)", "Defending (Tactical)", ToStars(6 * s.Defending + 3 * s.Tactical + 2 * ddm, 300)),
			new StarCategory("Savunma (Teknik)", "Defending (Technical)", ToStars(6 * s.Defending + 3 * s.Technical + 2 * ddm, 300)),
			new StarCategory("Topa Sahip Olma (Taktik)", "Possession (Tactical)", ToStars(6 * s.Tactical + 3 * s.Technical + 2 * ddm, 300)),
			new StarCategory("Topa Sahip Olma (Teknik)", "Possession (Technical)", ToStars(6 * s.Technical + 3 * s.Tactical + 2 * ddm, 300)),
			new StarCategory("Kondisyon", "Fitness", ToStars(9 * s.Fitness + 2 * ddm, 300)),
			new StarCategory("Kaleci (Şut Karşılama)", "GK (Shot Stopping)", ToStars(6 * s.Goalkeeping + 3 * s.Tactical + 2 * ddm, 300)),
			new StarCategory("Kaleci (Top Dağıtım)", "GK (Handling)", ToStars(6 * s.Goalkeeping + 3 * s.Technical + 2 * ddm, 300)),
		};
	}

	public static (double BestStars, string BestShort) Best(StaffRow s)
	{
		var all = Evaluate(s);
		StarCategory top = all.OrderByDescending(c => c.Stars).First();
		return (top.Stars, $"{top.Stars:0.0}★");
	}

	public static Border BuildCard(StaffRow s)
	{
		var stack = new StackPanel { Margin = new Thickness(12, 4, 12, 10) };
		foreach (StarCategory cat in Evaluate(s).OrderByDescending(c => c.Stars))
		{
			var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
			int full = (int)Math.Floor(cat.Stars);
			bool half = (cat.Stars - full) >= 0.5;
			string starGlyphs = new string('★', full) + (half ? "½" : "") + new string('☆', Math.Max(0, 5 - full - (half ? 1 : 0)));
			var starTxt = Build.Text($"{starGlyphs}  {cat.Stars:0.0}", 12,
				cat.Stars >= 4.5 ? Build.Res("Positive") : cat.Stars >= 3.5 ? Build.Res("Accent") : Build.Res("Muted"),
				cat.Stars >= 4.0 ? FontWeight.Bold : FontWeight.Normal);
			DockPanel.SetDock(starTxt, Dock.Right);
			dp.Children.Add(starTxt);
			dp.Children.Add(Build.Text(cat.Label, 12.5, Build.Res("Text")));
			stack.Children.Add(dp);
		}
		return Build.Card(Build.T("Antrenörlük Yıldızları (FM 5★ Hesaplayıcı)", "Coaching Stars (FM 5★ Calculator)"), stack);
	}
}
