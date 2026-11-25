using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace SquadEditor.App.Ui;

/// <summary>VS Code / Spotlight-style command palette for instant navigation and actions.</summary>
public sealed class CommandPaletteWindow : Window
{
	private sealed record CmdItem(string Icon, string Title, string Subtitle, string Search, Action Run);

	public CommandPaletteWindow(GameData data, INavigator nav, Action backup, Action reload, Action openSettings, Action openHistory, Action showFrozen, Action undo, Action<string> applyTheme)
	{
		Title = Build.T("Komut Paleti", "Command Palette");
		Width = 560;
		Height = 440;
		SystemDecorations = SystemDecorations.None;
		Background = Brushes.Transparent;
		TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		FontFamily = Build.Font("UiFont");

		var actions = new List<CmdItem>
		{
			new("💾", Build.T("Kayıt Yedeği Al", "Back up latest FM save"), Build.T("Aksiyon · .fm dosyasını yedekle", "Action · back up .fm save"), "yedek backup save", backup),
			new("↻", Build.T("Kaydı Yeniden Oku (F5)", "Reload save (F5)"), Build.T("Aksiyon · belleği tazele", "Action · refresh memory"), "yenile reload f5", reload),
			new("📋", Build.T("Değişiklik Geçmişini Aç", "Open Change History"), Build.T("Aksiyon · yapılan tüm düzenlemeler", "Action · view all edits"), "gecmis history undo", openHistory),
			new("❄", Build.T("Dondurulan Kayıtları Göster", "Show Frozen Records"), Build.T("Aksiyon · dondurulan oyuncular/personel", "Action · frozen list"), "dondurulan frozen", showFrozen),
			new("↶", Build.T($"Son Değişikliği Geri Al ({PlatformPaths.UndoHint})", $"Undo Last Change ({PlatformPaths.UndoHint})"), Build.T("Aksiyon · geri al", "Action · undo"), "geri al undo", undo),
			new("⚙", Build.T("Ayarları Aç", "Open Settings"), Build.T("Aksiyon · tema, para birimi, dil", "Action · preferences"), "ayarlar settings", openSettings),
			new("🎨", Build.T("Tema: FM Gece Laciverti", "Theme: FM Night Navy"), Build.T("Tema · anında geçiş", "Theme · instant switch"), "tema theme dark lacivert", () => applyTheme("dark")),
			new("🎨", Build.T("Tema: OLED Saf Siyah", "Theme: OLED Pure Black"), Build.T("Tema · anında geçiş", "Theme · instant switch"), "tema theme oled siyah black", () => applyTheme("oled")),
			new("🎨", Build.T("Tema: WTCS Kömür & Altın", "Theme: WTCS Charcoal & Gold"), Build.T("Tema · anında geçiş", "Theme · instant switch"), "tema theme wtcs komur altin charcoal gold", () => applyTheme("charcoal")),
			new("🎨", Build.T("Tema: Açık Editorial", "Theme: Light Editorial"), Build.T("Tema · anında geçiş", "Theme · instant switch"), "tema theme acik light", () => applyTheme("light"))
		};

		var box = new TextBox
		{
			Height = 38,
			FontSize = 14,
			Padding = new Thickness(12, 6, 12, 6),
			Margin = new Thickness(10, 10, 10, 8)
		};
		ToolTip.SetTip(box, Build.T("Oyuncu, kulüp, ülke, lig veya komut yaz… (Enter: çalıştır, Esc: kapat)", "Type player, club, nation, league or command… (Enter: run, Esc: close)"));
		var list = new ListBox
		{
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			Margin = new Thickness(6, 0, 6, 8)
		};

		List<CmdItem> current = new();
		void Refresh()
		{
			string q = GameData.Normalize((box.Text ?? "").Trim());
			var items = new List<CmdItem>();
			if (q.Length == 0)
			{
				items.AddRange(actions);
			}
			else
			{
				items.AddRange(actions.Where(a => GameData.Normalize(a.Title + " " + a.Search).Contains(q)).Take(5));
				foreach (NationRow n in data.Nations.Where(x => x.Search.Contains(q)).OrderByDescending(x => x.YouthRating).Take(3))
				{
					NationRow nation = n;
					items.Add(new("🌍", n.Name, Build.F("Ülke · Gençlik {0}", "Nation · Youth {0}", n.YouthRating), n.Search, () => nav.OpenNation(nation.Address)));
				}
				foreach (CompRow c in data.Competitions.Where(x => x.Search.Contains(q)).OrderByDescending(x => x.Reputation).Take(3))
				{
					CompRow comp = c;
					items.Add(new("🏆", c.Name, Build.F("Lig · İtibar {0}", "League · Rep {0}", c.Reputation), c.Search, () => nav.OpenComp(comp.Address)));
				}
				foreach (ClubRow c in data.Clubs.Where(x => x.SquadSize > 0 && x.Search.Contains(q)).OrderByDescending(x => x.AverageCA).Take(5))
				{
					ClubRow club = c;
					items.Add(new("🛡", c.Name, $"{c.League} · {c.Nation}", c.Search, () => nav.OpenClub(club.Address)));
				}
				foreach (PlayerRow p in data.Players.Where(x => x.Search.Contains(q)).OrderByDescending(x => x.CA).Take(8))
				{
					PlayerRow player = p;
					items.Add(new("⚽", p.Name, $"{p.Positions} · CA {p.CA}/{p.PA} · {p.Club}", p.Search, () => nav.OpenPlayer(player.Address)));
				}
			}
			current = items;
			list.ItemsSource = items.Select(it =>
			{
				var dp = new DockPanel { Margin = new Thickness(4, 3, 4, 3) };
				var ic = Build.Text(it.Icon, 14, Build.Res("Accent"));
				ic.Width = 26;
				DockPanel.SetDock(ic, Dock.Left);
				dp.Children.Add(ic);
				var sub = Build.Text(it.Subtitle, 11.5, Build.Res("Muted"));
				DockPanel.SetDock(sub, Dock.Right);
				dp.Children.Add(sub);
				dp.Children.Add(Build.Text(it.Title, 13, Build.Res("Text"), FontWeight.SemiBold));
				return dp;
			}).ToList();
			if (current.Count > 0) list.SelectedIndex = 0;
		}

		void ExecuteSelected()
		{
			if (list.SelectedIndex >= 0 && list.SelectedIndex < current.Count)
			{
				var act = current[list.SelectedIndex].Run;
				Close();
				act();
			}
		}

		box.TextChanged += (_, _) => Refresh();
		box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
		{
			if (e.Key == Key.Escape) { Close(); e.Handled = true; }
			else if (e.Key == Key.Enter) { ExecuteSelected(); e.Handled = true; }
			else if (e.Key == Key.Down && list.SelectedIndex < current.Count - 1) { list.SelectedIndex++; if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
			else if (e.Key == Key.Up && list.SelectedIndex > 0) { list.SelectedIndex--; if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
		}, RoutingStrategies.Tunnel);
		list.DoubleTapped += (_, _) => ExecuteSelected();
		Deactivated += (_, _) => { if (IsVisible) Close(); };

		var dock = new DockPanel();
		DockPanel.SetDock(box, Dock.Top);
		dock.Children.Add(box);
		dock.Children.Add(list);
		Content = new Border
		{
			Background = Build.Res("Panel"),
			BorderBrush = Build.Res("Accent"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(10),
			BoxShadow = BoxShadows.Parse("0 6 24 0 #8C000000"),
			Margin = new Thickness(12),
			Child = dock
		};
		Loaded += (_, _) => { Refresh(); box.Focus(); };
	}
}

/// <summary>Builds an automatic Best XI pitch &amp; Squad Depth / Gap Analysis with 10 formations + Custom Role Tactic Builder.</summary>
public static class ClubBestXi
{
	private sealed record Slot(string Label, string Role, string[] Codes, double X, double Y);

	private sealed class CustomTacticDto
	{
		public string Name { get; set; } = "";
		public List<CustomSlotDto> Slots { get; set; } = new();
	}

	private sealed class CustomSlotDto
	{
		public string Pos { get; set; } = "MC";
		public string Role { get; set; } = "BBM";
	}

	private static readonly Dictionary<string, (double X, double Y, string[] Codes)> PosCoords = new()
	{
		["GK"] = (0.50, 0.91, new[] { "GK" }),
		["DL"] = (0.15, 0.73, new[] { "DL", "WBL" }),
		["DC"] = (0.50, 0.76, new[] { "DC" }),
		["DR"] = (0.85, 0.73, new[] { "DR", "WBR" }),
		["WBL"] = (0.14, 0.57, new[] { "WBL", "DL", "ML" }),
		["DM"] = (0.50, 0.57, new[] { "DM", "MC" }),
		["WBR"] = (0.86, 0.57, new[] { "WBR", "DR", "MR" }),
		["ML"] = (0.16, 0.43, new[] { "ML", "AML" }),
		["MC"] = (0.50, 0.44, new[] { "MC", "DM", "AMC" }),
		["MR"] = (0.84, 0.43, new[] { "MR", "AMR" }),
		["AML"] = (0.18, 0.27, new[] { "AML", "ML" }),
		["AMC"] = (0.50, 0.29, new[] { "AMC", "MC" }),
		["AMR"] = (0.82, 0.27, new[] { "AMR", "MR" }),
		["ST"] = (0.50, 0.12, new[] { "ST" })
	};

	private static readonly Dictionary<string, Slot[]> BuiltInFormations = new()
	{
		["4-2-3-1"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DL", "WB", new[] { "DL", "WBL" }, 0.15, 0.73),
			new Slot("DC", "BPD", new[] { "DC" }, 0.38, 0.76),
			new Slot("DC", "CD", new[] { "DC" }, 0.62, 0.76),
			new Slot("DR", "WB", new[] { "DR", "WBR" }, 0.85, 0.73),
			new Slot("DM", "DM", new[] { "DM", "MC" }, 0.36, 0.56),
			new Slot("DM", "DLP", new[] { "DM", "MC" }, 0.64, 0.56),
			new Slot("AML", "IW", new[] { "AML", "ML" }, 0.17, 0.32),
			new Slot("AMC", "AP", new[] { "AMC", "MC" }, 0.50, 0.32),
			new Slot("AMR", "IW", new[] { "AMR", "MR" }, 0.83, 0.32),
			new Slot("ST", "AF", new[] { "ST" }, 0.50, 0.12)
		},
		["4-3-3 DM"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DL", "WB", new[] { "DL", "WBL" }, 0.15, 0.73),
			new Slot("DC", "BPD", new[] { "DC" }, 0.38, 0.76),
			new Slot("DC", "CD", new[] { "DC" }, 0.62, 0.76),
			new Slot("DR", "WB", new[] { "DR", "WBR" }, 0.85, 0.73),
			new Slot("DM", "DM", new[] { "DM", "MC" }, 0.50, 0.57),
			new Slot("MC", "BBM", new[] { "MC", "AMC" }, 0.28, 0.43),
			new Slot("MC", "MEZ", new[] { "MC", "AMC" }, 0.72, 0.43),
			new Slot("AML", "IW", new[] { "AML", "ML" }, 0.17, 0.21),
			new Slot("AMR", "IW", new[] { "AMR", "MR" }, 0.83, 0.21),
			new Slot("ST", "AF", new[] { "ST" }, 0.50, 0.12)
		},
		["4-4-2"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DL", "WB", new[] { "DL", "WBL" }, 0.15, 0.74),
			new Slot("DC", "BPD", new[] { "DC" }, 0.38, 0.76),
			new Slot("DC", "CD", new[] { "DC" }, 0.62, 0.76),
			new Slot("DR", "WB", new[] { "DR", "WBR" }, 0.85, 0.74),
			new Slot("ML", "W", new[] { "ML", "AML" }, 0.16, 0.46),
			new Slot("MC", "DLP", new[] { "MC", "DM" }, 0.38, 0.49),
			new Slot("MC", "BBM", new[] { "MC", "DM" }, 0.62, 0.49),
			new Slot("MR", "W", new[] { "MR", "AMR" }, 0.84, 0.46),
			new Slot("ST", "AF", new[] { "ST" }, 0.37, 0.15),
			new Slot("ST", "PF", new[] { "ST" }, 0.63, 0.15)
		},
		["3-5-2"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DC", "CD", new[] { "DC" }, 0.23, 0.75),
			new Slot("DC", "BPD", new[] { "DC" }, 0.50, 0.77),
			new Slot("DC", "CD", new[] { "DC" }, 0.77, 0.75),
			new Slot("WBL", "WB", new[] { "WBL", "DL", "ML" }, 0.14, 0.50),
			new Slot("DM", "DM", new[] { "DM", "MC" }, 0.36, 0.55),
			new Slot("DM", "DLP", new[] { "DM", "MC" }, 0.64, 0.55),
			new Slot("WBR", "WB", new[] { "WBR", "DR", "MR" }, 0.86, 0.50),
			new Slot("AMC", "AP", new[] { "AMC", "MC" }, 0.50, 0.33),
			new Slot("ST", "AF", new[] { "ST" }, 0.37, 0.14),
			new Slot("ST", "TF", new[] { "ST" }, 0.63, 0.14)
		},
		["3-4-2-1"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DC", "CD", new[] { "DC" }, 0.23, 0.75),
			new Slot("DC", "BPD", new[] { "DC" }, 0.50, 0.77),
			new Slot("DC", "CD", new[] { "DC" }, 0.77, 0.75),
			new Slot("WBL", "WB", new[] { "WBL", "DL", "ML" }, 0.14, 0.52),
			new Slot("MC", "DLP", new[] { "MC", "DM" }, 0.38, 0.52),
			new Slot("MC", "BBM", new[] { "MC", "DM" }, 0.62, 0.52),
			new Slot("WBR", "WB", new[] { "WBR", "DR", "MR" }, 0.86, 0.52),
			new Slot("AMC", "AP", new[] { "AMC", "AML" }, 0.33, 0.31),
			new Slot("AMC", "IW", new[] { "AMC", "AMR" }, 0.67, 0.31),
			new Slot("ST", "AF", new[] { "ST" }, 0.50, 0.12)
		},
		["4-1-4-1"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DL", "WB", new[] { "DL", "WBL" }, 0.15, 0.74),
			new Slot("DC", "BPD", new[] { "DC" }, 0.38, 0.76),
			new Slot("DC", "CD", new[] { "DC" }, 0.62, 0.76),
			new Slot("DR", "WB", new[] { "DR", "WBR" }, 0.85, 0.74),
			new Slot("DM", "DM", new[] { "DM", "MC" }, 0.50, 0.59),
			new Slot("ML", "W", new[] { "ML", "AML" }, 0.15, 0.39),
			new Slot("MC", "MEZ", new[] { "MC", "AMC" }, 0.37, 0.42),
			new Slot("MC", "BBM", new[] { "MC", "AMC" }, 0.63, 0.42),
			new Slot("MR", "W", new[] { "MR", "AMR" }, 0.85, 0.39),
			new Slot("ST", "PF", new[] { "ST" }, 0.50, 0.13)
		},
		["4-2-2-2"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DL", "WB", new[] { "DL", "WBL" }, 0.15, 0.74),
			new Slot("DC", "BPD", new[] { "DC" }, 0.38, 0.76),
			new Slot("DC", "CD", new[] { "DC" }, 0.62, 0.76),
			new Slot("DR", "WB", new[] { "DR", "WBR" }, 0.85, 0.74),
			new Slot("DM", "DM", new[] { "DM", "MC" }, 0.36, 0.56),
			new Slot("DM", "DLP", new[] { "DM", "MC" }, 0.64, 0.56),
			new Slot("AML", "IW", new[] { "AML", "AMC", "ML" }, 0.25, 0.34),
			new Slot("AMR", "IW", new[] { "AMR", "AMC", "MR" }, 0.75, 0.34),
			new Slot("ST", "AF", new[] { "ST" }, 0.36, 0.14),
			new Slot("ST", "PF", new[] { "ST" }, 0.64, 0.14)
		},
		["4-3-1-2"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("DL", "WB", new[] { "DL", "WBL" }, 0.15, 0.74),
			new Slot("DC", "BPD", new[] { "DC" }, 0.38, 0.76),
			new Slot("DC", "CD", new[] { "DC" }, 0.62, 0.76),
			new Slot("DR", "WB", new[] { "DR", "WBR" }, 0.85, 0.74),
			new Slot("MC", "BBM", new[] { "MC", "DM" }, 0.25, 0.51),
			new Slot("MC", "DLP", new[] { "MC", "DM" }, 0.50, 0.55),
			new Slot("MC", "MEZ", new[] { "MC", "DM" }, 0.75, 0.51),
			new Slot("AMC", "AP", new[] { "AMC", "MC" }, 0.50, 0.33),
			new Slot("ST", "AF", new[] { "ST" }, 0.36, 0.14),
			new Slot("ST", "PF", new[] { "ST" }, 0.64, 0.14)
		},
		["5-3-2"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("WBL", "WB", new[] { "WBL", "DL" }, 0.13, 0.64),
			new Slot("DC", "CD", new[] { "DC" }, 0.28, 0.76),
			new Slot("DC", "BPD", new[] { "DC" }, 0.50, 0.77),
			new Slot("DC", "CD", new[] { "DC" }, 0.72, 0.76),
			new Slot("WBR", "WB", new[] { "WBR", "DR" }, 0.87, 0.64),
			new Slot("MC", "BBM", new[] { "MC", "DM" }, 0.28, 0.45),
			new Slot("MC", "DLP", new[] { "MC", "DM" }, 0.50, 0.49),
			new Slot("MC", "MEZ", new[] { "MC", "DM" }, 0.72, 0.45),
			new Slot("ST", "AF", new[] { "ST" }, 0.36, 0.15),
			new Slot("ST", "TF", new[] { "ST" }, 0.64, 0.15)
		},
		["5-2-3"] = new[]
		{
			new Slot("GK", "GK", new[] { "GK" }, 0.50, 0.91),
			new Slot("WBL", "WB", new[] { "WBL", "DL" }, 0.13, 0.63),
			new Slot("DC", "CD", new[] { "DC" }, 0.28, 0.76),
			new Slot("DC", "BPD", new[] { "DC" }, 0.50, 0.77),
			new Slot("DC", "CD", new[] { "DC" }, 0.72, 0.76),
			new Slot("WBR", "WB", new[] { "WBR", "DR" }, 0.87, 0.63),
			new Slot("MC", "DM", new[] { "MC", "DM" }, 0.37, 0.48),
			new Slot("MC", "DLP", new[] { "MC", "DM" }, 0.63, 0.48),
			new Slot("AML", "IW", new[] { "AML", "ML" }, 0.18, 0.23),
			new Slot("AMR", "IW", new[] { "AMR", "MR" }, 0.82, 0.23),
			new Slot("ST", "AF", new[] { "ST" }, 0.50, 0.12)
		}
	};

	private static string CustomTacticsPath => Path.Combine(PlatformPaths.LocalAppDataDir, "custom_tactics.json");

	private static List<CustomTacticDto> LoadCustomTactics()
	{
		try
		{
			if (!File.Exists(CustomTacticsPath)) return new();
			return JsonSerializer.Deserialize<List<CustomTacticDto>>(File.ReadAllText(CustomTacticsPath)) ?? new();
		}
		catch { return new(); }
	}

	private static void SaveCustomTactics(List<CustomTacticDto> list)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(CustomTacticsPath)!);
			File.WriteAllText(CustomTacticsPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch { }
	}

	private static Slot[] LayoutSlots(List<CustomSlotDto> dtos)
	{
		var grouped = dtos.Select((s, i) => (s, i)).GroupBy(x => x.s.Pos).ToDictionary(g => g.Key, g => g.ToList());
		var result = new Slot[dtos.Count];
		foreach (var (pos, list) in grouped)
		{
			var (baseX, baseY, codes) = PosCoords.TryGetValue(pos, out var c) ? c : (0.50, 0.50, new[] { pos });
			for (int k = 0; k < list.Count; k++)
			{
				double x = list.Count == 1 ? baseX
					: list.Count == 2 ? (k == 0 ? 0.36 : 0.64)
					: (0.24 + k * (0.52 / Math.Max(1, list.Count - 1)));
				result[list[k].i] = new Slot(pos, list[k].s.Role, codes, x, baseY);
			}
		}
		return result;
	}

	public static Control BuildView(GameData data, ClubRow club, INavigator nav)
	{
		var squad = data.Squads[club.Address].OrderByDescending(p => p.CA).ToList();
		var host = new ContentControl();
		var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
		string currentForm = "4-2-3-1";

		Dictionary<string, Slot[]> AllFormations()
		{
			var map = new Dictionary<string, Slot[]>(BuiltInFormations);
			foreach (var ct in LoadCustomTactics())
			{
				if (ct.Name.Length > 0 && ct.Slots.Count == 11)
					map[ct.Name] = LayoutSlots(ct.Slots);
			}
			return map;
		}

		var formPicker = new ComboBox { Width = 180, Height = 28, MinHeight = 28, FontSize = 12.5, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
		var quickPills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

		void Render(string formName)
		{
			var allForms = AllFormations();
			if (!allForms.TryGetValue(formName, out Slot[]? slots))
			{
				formName = "4-2-3-1";
				slots = BuiltInFormations[formName];
			}
			currentForm = formName;
			var used = new HashSet<ulong>();
			var starters = new List<(Slot Slot, PlayerRow? Player, PlayerRow? Backup)>();

			bool Fits(PlayerRow p, string[] codes) =>
				p.Positions.Split(' ', '/', ',').Any(tok => codes.Contains(tok));

			double RoleFitScore(PlayerRow p, Slot s) =>
				p.CA + (p.BestRole == s.Role ? 8.0 : 0.0) + p.BestRating * 0.15;

			var picks = new PlayerRow?[slots.Length];
			for (int i = 0; i < slots.Length; i++)
			{
				Slot sl = slots[i];
				PlayerRow? p = squad.Where(x => !used.Contains(x.Address) && Fits(x, sl.Codes))
					.OrderByDescending(x => RoleFitScore(x, sl)).FirstOrDefault()
					?? squad.FirstOrDefault(x => !used.Contains(x.Address));
				picks[i] = p;
				if (p != null) used.Add(p.Address);
			}
			for (int i = 0; i < slots.Length; i++)
			{
				Slot sl = slots[i];
				PlayerRow? bk = squad.Where(x => !used.Contains(x.Address) && Fits(x, sl.Codes))
					.OrderByDescending(x => RoleFitScore(x, sl)).FirstOrDefault();
				if (bk != null) used.Add(bk.Address);
				starters.Add((sl, picks[i], bk));
			}

			double avgCa = starters.Where(s => s.Player != null).Select(s => s.Player!.CA).DefaultIfEmpty(100).Average();

			double w = 440, h = 560;
			var canvas = PlayerVisuals.CreatePitchCanvas(w, h);

			foreach (var (slot, p, _) in starters)
			{
				static string PitchShortName(string full)
				{
					var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length == 0) return "—";
					if (parts.Length == 1) return parts[0];
					if (parts.Length >= 3) return parts[0];
					return parts[^1];
				}
				string shortName = p == null ? "—" : PitchShortName(p.Name);
				bool weak = p == null || p.CA < avgCa - 14;
				var badge = new Border
				{
					Width = 108,
					Padding = new Thickness(6, 4, 6, 4),
					CornerRadius = new CornerRadius(7),
					Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x10, 0x17, 0x22)),
					BorderBrush = weak ? Build.Res("Danger") : Build.Res("Positive"),
					BorderThickness = new Thickness(1.3),
					Cursor = p != null ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow)
				};
				var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
				var topLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
				topLine.Children.Add(Build.Text($"{slot.Label}·{slot.Role}", 9.5, Build.Res("Muted"), FontWeight.SemiBold));
				if (p != null)
				{
					var caTxt = Build.Text(" " + p.CA, 10.5, Colors.Attribute((int)Math.Round(p.CA / 10.0)), FontWeight.Bold);
					topLine.Children.Add(caTxt);
				}
				sp.Children.Add(topLine);
				var nm = Build.Text(shortName, 11.5, Brushes.White, FontWeight.SemiBold);
				nm.HorizontalAlignment = HorizontalAlignment.Center;
				sp.Children.Add(nm);
				badge.Child = sp;
				if (p != null)
				{
					PlayerRow target = p;
					badge.PointerReleased += (_, e) =>
					{
						if (e.InitialPressMouseButton == MouseButton.Left) nav.OpenPlayer(target.Address);
					};
				}
				Canvas.SetLeft(badge, Math.Clamp(slot.X * w - 54, 4, w - 112));
				Canvas.SetTop(badge, Math.Clamp(slot.Y * h - 22, 4, h - 46));
				canvas.Children.Add(badge);
			}

			long clubBudget = long.TryParse(data.Read(club.Address, data.ClubDef, "Transfer Budget"), out long cbVal) ? cbVal : 0;
			var depthStack = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
			var metaTxt = Build.Text(Build.F("İlk 11 Ort. CA: {0:0.0}  ·  Kırmızı rozetler zayıf veya yedeksiz mevkiler", "Best XI Avg CA: {0:0.0}  ·  Red badges mark weak or uncovered slots", avgCa), 11.5, Build.Res("Muted"));
			metaTxt.Margin = new Thickness(4, 0, 4, 8);
			depthStack.Children.Add(metaTxt);

			foreach (var (slot, p, bk) in starters)
			{
				Slot curSlot = slot;
				PlayerRow? curStarter = p;
				bool weak = p == null || p.CA < avgCa - 14 || bk == null;

				var g = new Grid { Height = 36 };
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

				var posPill = new Border
				{
					Background = weak ? new SolidColorBrush(Color.FromArgb(0x28, 0xF8, 0x51, 0x49)) : Build.Res("AccentSoft"),
					BorderBrush = weak ? Build.Res("Danger") : Build.Res("Accent"),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(5),
					Padding = new Thickness(6, 2, 6, 2),
					HorizontalAlignment = HorizontalAlignment.Left,
					VerticalAlignment = VerticalAlignment.Center,
					Child = Build.Text($"{slot.Label} · {slot.Role}", 10.5, weak ? Build.Res("Danger") : Build.Res("Accent"), FontWeight.Bold)
				};
				g.Children.Add(posPill);

				var starterBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
				var pBtn = new Button
				{
					Content = p != null ? p.Name : Build.T("Eksik!", "Missing!"),
					Theme = Build.StyleOf("LinkButton"),
					FontSize = 12,
					FontWeight = FontWeight.SemiBold,
					HorizontalAlignment = HorizontalAlignment.Left
				};
				if (p != null) { PlayerRow t = p; pBtn.Click += (_, _) => nav.OpenPlayer(t.Address); }
				starterBox.Children.Add(pBtn);
				if (p != null)
				{
					var caPill = Build.Text($" {p.CA}", 11.5, Colors.Attribute((int)Math.Round(p.CA / 10.0)), FontWeight.Bold);
					caPill.VerticalAlignment = VerticalAlignment.Center;
					starterBox.Children.Add(caPill);
				}
				Grid.SetColumn(starterBox, 1);
				g.Children.Add(starterBox);

				var bkTxt = Build.Text(bk != null ? $"{bk.Name} ({bk.CA})" : Build.T("Yedek yok", "No backup"), 11.5, bk == null ? Build.Res("Danger") : Build.Res("Muted"));
				bkTxt.VerticalAlignment = VerticalAlignment.Center;
				Grid.SetColumn(bkTxt, 2);
				g.Children.Add(bkTxt);

				var findBtn = new Button
				{
					Content = Icons.Glyph(IconKind.Search, 13),
					Theme = weak ? Build.StyleOf("AccentButton") : Build.StyleOf("GhostButton"),
					Padding = new Thickness(7, 4, 7, 4),
					Margin = new Thickness(6, 0, 0, 0),
					VerticalAlignment = VerticalAlignment.Center
				};
				ToolTip.SetTip(findBtn, Build.F("{0} ({1}) için akıllı transfer hedeflerini öner", "Suggest smart transfer targets for {0} ({1})", curSlot.Label, curSlot.Role));
				findBtn.Click += (_, _) => OpenTransferFinder(TopLevel.GetTopLevel(findBtn) as Window, data, club, curSlot, curStarter, clubBudget, nav);
				Grid.SetColumn(findBtn, 3);
				g.Children.Add(findBtn);

				var rowCard = new Border
				{
					Background = Brushes.Transparent,
					BorderBrush = Build.Res("StrokeSoft"),
					BorderThickness = new Thickness(0, 0, 0, 1),
					CornerRadius = new CornerRadius(5),
					Padding = new Thickness(6, 2, 6, 2),
					Margin = new Thickness(0, 1, 0, 1),
					Child = g
				};
				rowCard.PointerEntered += (_, _) => rowCard.Background = Build.Res("CardHover");
				rowCard.PointerExited += (_, _) => rowCard.Background = Brushes.Transparent;
				depthStack.Children.Add(rowCard);
			}

			var split = new Grid { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
			split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
			var pitchCard = Build.Card(Build.F("En İyi 11 ({0})", "Best XI ({0})", formName), new Border { Padding = new Thickness(12, 4, 12, 12), Child = canvas });
			var depthCard = Build.Card(Build.T("Kadro Derinliği & Eksik Analizi", "Squad Depth & Gap Analysis"), depthStack);
			Grid.SetColumn(pitchCard, 0);
			Grid.SetColumn(depthCard, 1);
			split.Children.Add(pitchCard);
			split.Children.Add(depthCard);
			host.Content = Build.Scroll(split);
		}

		void ReloadFormPicker(string selectName)
		{
			var keys = AllFormations().Keys.ToList();
			formPicker.ItemsSource = keys;
			formPicker.SelectedItem = keys.Contains(selectName) ? selectName : keys[0];
		}

		formPicker.SelectionChanged += (_, _) =>
		{
			if (formPicker.SelectedItem is string fn && fn != currentForm)
				Render(fn);
		};

		var customBtn = new Button
		{
			Content = Icons.Label(IconKind.Sliders, Build.T("Özel Taktik & Roller", "Custom Tactic & Roles"), 13.5),
			Padding = new Thickness(10, 4, 10, 4),
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center
		};
		ToolTip.SetTip(customBtn, Build.T("Kendi dizilişini ve 11 oyuncunun FM rollerini seçip kaydet", "Build and save your own formation with custom FM player roles"));
		customBtn.Click += async (_, _) =>
		{
			var all = AllFormations();
			Slot[] seed = all.TryGetValue(currentForm, out var s) ? s : BuiltInFormations["4-2-3-1"];
			Window? owner = TopLevel.GetTopLevel(customBtn) as Window
				?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
			string? saved = await OpenCustomTacticBuilderAsync(owner, seed, currentForm);
			if (saved != null)
			{
				ReloadFormPicker(saved);
				Render(saved);
			}
		};

		quickPills.Children.Add(Build.Text(Build.T("Diziliş:", "Formation:"), 12.5, Build.Res("Muted")));
		quickPills.Children.Add(formPicker);
		foreach (string quick in new[] { "4-2-3-1", "4-3-3 DM", "4-4-2", "3-5-2", "3-4-2-1" })
		{
			string q = quick;
			var b = new Button { Content = q, Theme = Build.StyleOf("GhostButton"), Padding = new Thickness(8, 3, 8, 3), FontSize = 11.5, Margin = new Thickness(2, 0, 2, 0) };
			b.Click += (_, _) => { formPicker.SelectedItem = q; Render(q); };
			quickPills.Children.Add(b);
		}

		DockPanel.SetDock(customBtn, Dock.Right);
		bar.Children.Add(customBtn);
		bar.Children.Add(quickPills);

		ReloadFormPicker("4-2-3-1");
		Render("4-2-3-1");

		var dock = new DockPanel();
		DockPanel.SetDock(bar, Dock.Top);
		dock.Children.Add(bar);
		dock.Children.Add(host);
		return dock;
	}

	private static async Task<string?> OpenCustomTacticBuilderAsync(Window? owner, Slot[] seed, string currentName)
	{
		var dlg = new Window
		{
			Title = Build.T("Özel Taktik & Rol Oluşturucu", "Custom Tactic & Role Builder"),
			Width = 520,
			Height = 560,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = Build.Font("UiFont")
		};
		WindowHelper.ApplyChrome(dlg);

		var root = new DockPanel { Margin = new Thickness(16) };
		var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
		var nameLbl = Build.Text(Build.T("Taktik Adı:", "Tactic Name:"), 12.5, Build.Res("Muted"), FontWeight.SemiBold);
		nameLbl.Margin = new Thickness(0, 0, 10, 0);
		DockPanel.SetDock(nameLbl, Dock.Left);
		var nameBox = new TextBox { Text = BuiltInFormations.ContainsKey(currentName) ? Build.T("Benim Taktiğim", "My Tactic") : currentName, Height = 28, MinHeight = 28, FontSize = 13 };
		nameRow.Children.Add(nameLbl);
		nameRow.Children.Add(nameBox);
		DockPanel.SetDock(nameRow, Dock.Top);
		root.Children.Add(nameRow);

		var roles = ScoutRating.AllRoles().Select(r => new KeyValuePair<string, string>(r.Code, r.Label)).ToList();
		var posList = PosCoords.Keys.ToList();
		var pickers = new List<(ComboBox Pos, ComboBox Role)>();
		var listPanel = new StackPanel();
		for (int i = 0; i < 11; i++)
		{
			Slot s = i < seed.Length ? seed[i] : seed[^1];
			var g = new Grid { Height = 34, Margin = new Thickness(0, 1, 0, 1) };
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			g.Children.Add(Build.Text($"#{i + 1}", 12, Build.Res("Faint"), FontWeight.SemiBold));
			var cbPos = new ComboBox { ItemsSource = posList, SelectedItem = posList.Contains(s.Label) ? s.Label : "MC", Height = 26, MinHeight = 26, Margin = new Thickness(0, 0, 8, 0) };
			Grid.SetColumn(cbPos, 1);
			g.Children.Add(cbPos);
			var cbRole = new ComboBox
			{
				ItemsSource = roles,
				DisplayMemberBinding = new Binding("Value"),
				SelectedValueBinding = new Binding("Key"),
				SelectedValue = s.Role,
				Height = 26,
				MinHeight = 26,
				HorizontalAlignment = HorizontalAlignment.Stretch
			};
			Grid.SetColumn(cbRole, 2);
			g.Children.Add(cbRole);
			pickers.Add((cbPos, cbRole));
			listPanel.Children.Add(g);
		}

		var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
		cancel.Click += (_, _) => dlg.Close(false);
		var save = new Button { Content = Icons.Label(IconKind.Save, Build.T("Taktiği Kaydet", "Save Tactic"), 13.5), Theme = Build.StyleOf("AccentButton"), IsDefault = true };
		string? result = null;
		save.Click += (_, _) =>
		{
			string tName = (nameBox.Text ?? "").Trim();
			if (tName.Length == 0) return;
			var all = LoadCustomTactics();
			all.RemoveAll(t => t.Name.Equals(tName, StringComparison.OrdinalIgnoreCase));
			all.Add(new CustomTacticDto
			{
				Name = tName,
				Slots = pickers.Select(p => new CustomSlotDto
				{
					Pos = p.Pos.SelectedItem as string ?? "MC",
					Role = p.Role.SelectedValue as string ?? "BBM"
				}).ToList()
			});
			SaveCustomTactics(all);
			result = tName;
			dlg.Close(true);
		};
		btns.Children.Add(cancel);
		btns.Children.Add(save);
		DockPanel.SetDock(btns, Dock.Bottom);
		root.Children.Add(btns);
		root.Children.Add(Build.Scroll(listPanel));
		dlg.Content = root;
		if (owner is { IsVisible: true })
		{
			return (await dlg.ShowDialog<bool?>(owner)) == true ? result : null;
		}
		var tcs = new TaskCompletionSource<bool>();
		dlg.Closed += (_, _) => tcs.TrySetResult(result != null);
		dlg.Show();
		await tcs.Task;
		return result;
	}

	private static void OpenTransferFinder(Window? owner, GameData data, ClubRow club, Slot slot, PlayerRow? currentStarter, long clubBudget, INavigator nav)
	{
		var dlg = new Window
		{
			Title = Build.F("{0} ({1}) Transfer Önerileri — {2}", "{0} ({1}) Transfer Targets — {2}", slot.Label, slot.Role, club.Name),
			Width = 940,
			Height = 560,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = Build.Font("UiFont")
		};
		WindowHelper.ApplyChrome(dlg);

		var root = new DockPanel { Margin = new Thickness(14) };
		var top = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };

		string starterText = currentStarter != null ? $"{currentStarter.Name} (CA {currentStarter.CA})" : Build.T("Eksik", "Missing");
		var info = Build.Text(Build.F("Mevcut: {0}  ·  Transfer Bütçesi: {1}", "Current: {0}  ·  Transfer Budget: {1}", starterText, Money.Format(Math.Max(0, clubBudget), false)), 12.5, Build.Res("Muted"));
		info.Margin = new Thickness(0, 0, 14, 0);
		top.Children.Add(info);

		var withinBudget = new CheckBox
		{
			Content = Build.T("Bütçe İçi", "Within Budget"),
			Theme = Build.StyleOf("FilterChip"),
			IsChecked = clubBudget > 0,
			Margin = new Thickness(0, 0, 6, 0)
		};
		var wonderkids = new CheckBox
		{
			Content = Build.T("Genç (Yaş ≤ 23)", "Young (Age ≤ 23)"),
			Theme = Build.StyleOf("FilterChip"),
			Margin = new Thickness(0, 0, 6, 0)
		};
		var upgradeOnly = new CheckBox
		{
			Content = Build.T("Mevcuttan İyi (CA ≥)", "Upgrade Only"),
			Theme = Build.StyleOf("FilterChip"),
			Margin = new Thickness(0, 0, 6, 0)
		};
		top.Children.Add(withinBudget);
		top.Children.Add(wonderkids);
		top.Children.Add(upgradeOnly);
		DockPanel.SetDock(top, Dock.Top);
		root.Children.Add(top);

		var gridHost = new ContentControl();
		void RefreshTargets()
		{
			bool fitBudget = withinBudget.IsChecked == true && clubBudget > 0;
			bool youngOnly = wonderkids.IsChecked == true;
			bool onlyUpgrade = upgradeOnly.IsChecked == true && currentStarter != null;
			long maxCost = (long)(clubBudget * 1.15);

			var targets = data.Players
				.Where(p => p.ClubAddress != club.Address
					&& p.Positions.Split(' ', '/', ',').Any(tok => slot.Codes.Contains(tok))
					&& (!fitBudget || (p.AskingPrice > 0 ? p.AskingPrice <= maxCost : p.Value <= maxCost))
					&& (!youngOnly || (p.Age > 0 && p.Age <= 23))
					&& (!onlyUpgrade || p.CA >= currentStarter!.CA))
				.OrderByDescending(p => p.BestRating * 0.50 + p.PotentialRating * 0.40 + (p.BestRole == slot.Role ? 6.0 : 0.0))
				.Take(40)
				.ToList();

			var pg = ClubPage.PlayerGrid(data, targets, nav, showClub: true);
			pg.DoubleTapped += (_, _) => { if (pg.SelectedItem is PlayerRow) dlg.Close(); };
			gridHost.Content = new Border { Classes = { "CardBorder" }, Padding = new Thickness(4), Child = pg };
		}

		foreach (var cb in new[] { withinBudget, wonderkids, upgradeOnly })
		{
			cb.IsCheckedChanged += (_, _) => RefreshTargets();
		}
		RefreshTargets();
		root.Children.Add(gridHost);
		dlg.Content = root;
		Window? resolvedOwner = owner ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
		if (resolvedOwner is { IsVisible: true }) _ = dlg.ShowDialog(resolvedOwner);
		else dlg.Show();
	}
}

/// <summary>Renders a shareable FM-style PNG player card and saves to file.</summary>
public static class PlayerCardExport
{
	public static async Task ExportAsync(Window? owner, GameData data, ulong address, string name, uint uid, INavigator nav)
	{
		PlayerRow? row = data.Players.FirstOrDefault(p => p.Address == address);
		if (row == null) return;
		byte[] raw69 = new byte[69];
		data.Memory.TryRead(address + FmOffsets.PlayerRaw69, raw69);

		var grad = new LinearGradientBrush
		{
			StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
			EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
		};
		grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x17, 0x26), 0));
		grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x19, 0x26, 0x42), 1));

		var card = new Border
		{
			Width = 760,
			Height = 380,
			CornerRadius = new CornerRadius(12),
			Background = grad,
			BorderBrush = Build.Res("Accent"),
			BorderThickness = new Thickness(1.5),
			Padding = new Thickness(24)
		};
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

		var left = new StackPanel();
		var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
		var av = Build.Avatar(ImageLookup.Graphics.Person(uid), name, 76, round: true);
		av.Margin = new Thickness(0, 0, 16, 0);
		top.Children.Add(av);
		var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		titleStack.Children.Add(Build.Text(name, 22, Brushes.White, FontWeight.Bold));
		titleStack.Children.Add(Build.Text($"{row.Positions}  ·  {row.Age} {Build.T("yaş", "yo")}  ·  {row.Nation}", 13, new SolidColorBrush(Color.FromRgb(0xA8, 0xB6, 0xCE))));
		titleStack.Children.Add(Build.Text($"{row.Club}  ·  {row.RoleDisplay}", 13.5, Build.Res("Accent"), FontWeight.SemiBold));
		top.Children.Add(titleStack);
		left.Children.Add(top);

		// Deterministic fixed-layout ability bar for offscreen RenderTargetBitmap rendering
		left.Children.Add(OffscreenAbilityBar(row.CA, row.PA, 438));

		var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 12) };
		void Box(string lbl, string val)
		{
			var b = new Border { Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8) };
			var s = new StackPanel();
			s.Children.Add(Build.Text(val, 15, Brushes.White, FontWeight.Bold));
			s.Children.Add(Build.Text(lbl, 10.5, new SolidColorBrush(Color.FromRgb(0x9C, 0xAB, 0xC4))));
			b.Child = s;
			stats.Children.Add(b);
		}
		Box("CA / PA", $"{row.CA} / {row.PA}");
		Box(Build.T("Değer", "Value"), Money.Format(row.Value, false));
		Box(Build.T("Maaş", "Wage"), Money.Format(row.Wage, true));
		Box(Build.T("Hız", "Pace"), row.Pace.ToString());
		Box(Build.T("Bitiricilik", "Finishing"), row.Finishing.ToString());
		Box(Build.T("Pas", "Passing"), row.Passing.ToString());
		left.Children.Add(stats);
		left.Children.Add(Build.Text("SquadEditor · FM27", 11, new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))));

		var right = new Border { VerticalAlignment = VerticalAlignment.Center, Child = PlayerVisuals.RadarChart(236, PlayerVisuals.ComputeRadar8(raw69)) };
		Grid.SetColumn(right, 1);
		grid.Children.Add(left);
		grid.Children.Add(right);
		card.Child = grid;

		card.Measure(new Size(760, 380));
		card.Arrange(new Rect(0, 0, 760, 380));

		using var rtb = new RenderTargetBitmap(new PixelSize(760, 380), new Vector(96, 96));
		rtb.Render(card);

		Window? resolvedOwner = owner ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
		if (resolvedOwner == null) return;

		var file = await resolvedOwner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = Build.T("Oyuncu Kartını Kaydet", "Save Player Card"),
			DefaultExtension = "png",
			SuggestedFileName = $"{string.Concat(name.Split(Path.GetInvalidFileNameChars()))}_FM27.png",
			FileTypeChoices = new[] { FilePickerFileTypes.ImagePng }
		});
		if (file != null)
		{
			await using var fs = await file.OpenWriteAsync();
			rtb.Save(fs);
			nav.SetStatus(Build.F("✓ PNG kart kaydedildi: {0}", "✓ PNG card saved: {0}", file.Name));
		}
	}

	private static Control OffscreenAbilityBar(int ca, int pa, double width)
	{
		var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 10), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
		int growth = Math.Max(0, pa - ca);
		var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
		var right = Build.Text(growth > 0 ? Build.F("+{0} potansiyel", "+{0} growth", growth) : Build.T("Zirvede", "At peak"),
			11, Build.Res("Positive"), FontWeight.SemiBold);
		DockPanel.SetDock(right, Dock.Right);
		top.Children.Add(right);
		top.Children.Add(Build.Text($"CA {ca} / PA {pa}", 11.5, new SolidColorBrush(Color.FromRgb(0xA8, 0xB6, 0xCE))));
		stack.Children.Add(top);

		var track = new Grid { Height = 7, Width = width, ClipToBounds = true };
		track.Children.Add(new Border { Width = width, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x15, 0x21)), CornerRadius = new CornerRadius(3.5) });
		track.Children.Add(new Border
		{
			Width = width * Math.Clamp(pa / 200.0, 0, 1),
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = Build.Res("AccentSoft"),
			CornerRadius = new CornerRadius(3.5)
		});
		track.Children.Add(new Border
		{
			Width = width * Math.Clamp(ca / 200.0, 0, 1),
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = Colors.Attribute((int)Math.Round(ca / 10.0)),
			CornerRadius = new CornerRadius(3.5)
		});
		stack.Children.Add(track);
		return stack;
	}
}
