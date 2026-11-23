using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadEditor.App.Ui;

/// <summary>VS Code / Spotlight-style Ctrl+K command palette for instant navigation and actions.</summary>
public sealed class CommandPaletteWindow : Window
{
	private sealed record CmdItem(string Icon, string Title, string Subtitle, string Search, Action Run);

	public CommandPaletteWindow(GameData data, INavigator nav, Action backup, Action reload, Action openSettings, Action openHistory, Action showFrozen, Action undo, Action<string> applyTheme)
	{
		Title = Build.T("Komut Paleti", "Command Palette");
		Width = 560;
		Height = 440;
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;

		var actions = new List<CmdItem>
		{
			new("💾", Build.T("Kayıt Yedeği Al", "Back up latest FM save"), Build.T("Aksiyon · .fm dosyasını yedekle", "Action · back up .fm save"), "yedek backup save", backup),
			new("↻", Build.T("Kaydı Yeniden Oku (F5)", "Reload save (F5)"), Build.T("Aksiyon · belleği tazele", "Action · refresh memory"), "yenile reload f5", reload),
			new("📋", Build.T("Değişiklik Geçmişini Aç", "Open Change History"), Build.T("Aksiyon · yapılan tüm düzenlemeler", "Action · view all edits"), "gecmis history undo", openHistory),
			new("❄", Build.T("Dondurulan Kayıtları Göster", "Show Frozen Records"), Build.T("Aksiyon · dondurulan oyuncular/personel", "Action · frozen list"), "dondurulan frozen", showFrozen),
			new("↶", Build.T("Son Değişikliği Geri Al (Ctrl+Z)", "Undo Last Change (Ctrl+Z)"), Build.T("Aksiyon · geri al", "Action · undo"), "geri al undo", undo),
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
			Margin = new Thickness(10, 10, 10, 8),
			ToolTip = Build.T("Oyuncu, kulüp, ülke, lig veya komut yaz… (Enter: çalıştır, Esc: kapat)", "Type player, club, nation, league or command… (Enter: run, Esc: close)")
		};
		var list = new ListBox
		{
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			Margin = new Thickness(6, 0, 6, 8)
		};

		List<CmdItem> current = new();
		void Refresh()
		{
			string q = GameData.Normalize(box.Text.Trim());
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
					items.Add(new("🌍", n.Name, Build.T($"Ülke · Gençlik {n.YouthRating}", $"Nation · Youth {n.YouthRating}"), n.Search, () => nav.OpenNation(nation.Address)));
				}
				foreach (CompRow c in data.Competitions.Where(x => x.Search.Contains(q)).OrderByDescending(x => x.Reputation).Take(3))
				{
					CompRow comp = c;
					items.Add(new("🏆", c.Name, Build.T($"Lig · İtibar {c.Reputation}", $"League · Rep {c.Reputation}"), c.Search, () => nav.OpenComp(comp.Address)));
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
				dp.Children.Add(Build.Text(it.Title, 13, Build.Res("Text"), FontWeights.SemiBold));
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
		box.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape) { Close(); e.Handled = true; }
			else if (e.Key == Key.Enter) { ExecuteSelected(); e.Handled = true; }
			else if (e.Key == Key.Down && list.SelectedIndex < current.Count - 1) { list.SelectedIndex++; list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
			else if (e.Key == Key.Up && list.SelectedIndex > 0) { list.SelectedIndex--; list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
		};
		list.MouseDoubleClick += (_, _) => ExecuteSelected();
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
			Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.55, Color = System.Windows.Media.Colors.Black },
			Child = dock
		};
		Loaded += (_, _) => { Refresh(); box.Focus(); };
	}
}

/// <summary>Builds an automatic Best XI pitch & Squad Depth / Gap Analysis view for any club.</summary>
public static class ClubBestXi
{
	private sealed record Slot(string Label, string[] Codes, double X, double Y);

	private static readonly Dictionary<string, Slot[]> Formations = new()
	{
		["4-2-3-1"] = new[]
		{
			new Slot("GK", new[] { "GK" }, 0.50, 0.90),
			new Slot("DL", new[] { "DL", "WBL" }, 0.16, 0.72),
			new Slot("DC", new[] { "DC" }, 0.38, 0.75),
			new Slot("DC", new[] { "DC" }, 0.62, 0.75),
			new Slot("DR", new[] { "DR", "WBR" }, 0.84, 0.72),
			new Slot("DM", new[] { "DM", "MC" }, 0.36, 0.55),
			new Slot("DM", new[] { "DM", "MC" }, 0.64, 0.55),
			new Slot("AML", new[] { "AML", "ML" }, 0.18, 0.33),
			new Slot("AMC", new[] { "AMC", "MC" }, 0.50, 0.33),
			new Slot("AMR", new[] { "AMR", "MR" }, 0.82, 0.33),
			new Slot("ST", new[] { "ST" }, 0.50, 0.12)
		},
		["4-3-3"] = new[]
		{
			new Slot("GK", new[] { "GK" }, 0.50, 0.90),
			new Slot("DL", new[] { "DL", "WBL" }, 0.16, 0.72),
			new Slot("DC", new[] { "DC" }, 0.38, 0.75),
			new Slot("DC", new[] { "DC" }, 0.62, 0.75),
			new Slot("DR", new[] { "DR", "WBR" }, 0.84, 0.72),
			new Slot("DM", new[] { "DM", "MC" }, 0.50, 0.56),
			new Slot("MC", new[] { "MC", "AMC" }, 0.28, 0.43),
			new Slot("MC", new[] { "MC", "AMC" }, 0.72, 0.43),
			new Slot("AML", new[] { "AML", "ML" }, 0.18, 0.21),
			new Slot("AMR", new[] { "AMR", "MR" }, 0.82, 0.21),
			new Slot("ST", new[] { "ST" }, 0.50, 0.12)
		},
		["4-4-2"] = new[]
		{
			new Slot("GK", new[] { "GK" }, 0.50, 0.90),
			new Slot("DL", new[] { "DL", "WBL" }, 0.16, 0.73),
			new Slot("DC", new[] { "DC" }, 0.38, 0.75),
			new Slot("DC", new[] { "DC" }, 0.62, 0.75),
			new Slot("DR", new[] { "DR", "WBR" }, 0.84, 0.73),
			new Slot("ML", new[] { "ML", "AML" }, 0.16, 0.46),
			new Slot("MC", new[] { "MC", "DM" }, 0.38, 0.49),
			new Slot("MC", new[] { "MC", "DM" }, 0.62, 0.49),
			new Slot("MR", new[] { "MR", "AMR" }, 0.84, 0.46),
			new Slot("ST", new[] { "ST" }, 0.37, 0.16),
			new Slot("ST", new[] { "ST" }, 0.63, 0.16)
		},
		["3-5-2"] = new[]
		{
			new Slot("GK", new[] { "GK" }, 0.50, 0.90),
			new Slot("DC", new[] { "DC" }, 0.24, 0.75),
			new Slot("DC", new[] { "DC" }, 0.50, 0.77),
			new Slot("DC", new[] { "DC" }, 0.76, 0.75),
			new Slot("WBL", new[] { "WBL", "DL", "ML" }, 0.14, 0.50),
			new Slot("DM", new[] { "DM", "MC" }, 0.36, 0.55),
			new Slot("DM", new[] { "DM", "MC" }, 0.64, 0.55),
			new Slot("WBR", new[] { "WBR", "DR", "MR" }, 0.86, 0.50),
			new Slot("AMC", new[] { "AMC", "MC" }, 0.50, 0.34),
			new Slot("ST", new[] { "ST" }, 0.37, 0.14),
			new Slot("ST", new[] { "ST" }, 0.63, 0.14)
		}
	};

	public static UIElement BuildView(GameData data, ClubRow club, INavigator nav)
	{
		var squad = data.Squads[club.Address].OrderByDescending(p => p.CA).ToList();
		var host = new ContentControl();
		var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
		bar.Children.Add(Build.Text(Build.T("Diziliş:", "Formation:"), 12.5, Build.Res("Muted")));

		void Render(string formName)
		{
			var slots = Formations[formName];
			var used = new HashSet<ulong>();
			var starters = new List<(Slot Slot, PlayerRow? Player, PlayerRow? Backup)>();

			bool Fits(PlayerRow p, string[] codes) =>
				p.Positions.Split(' ', '/', ',').Any(tok => codes.Contains(tok));

			// Pass 1: pick best starter for each slot
			var picks = new PlayerRow?[slots.Length];
			for (int i = 0; i < slots.Length; i++)
			{
				PlayerRow? p = squad.FirstOrDefault(x => !used.Contains(x.Address) && Fits(x, slots[i].Codes))
					?? squad.FirstOrDefault(x => !used.Contains(x.Address));
				picks[i] = p;
				if (p != null) used.Add(p.Address);
			}
			// Pass 2: pick best remaining backup
			for (int i = 0; i < slots.Length; i++)
			{
				PlayerRow? bk = squad.FirstOrDefault(x => !used.Contains(x.Address) && Fits(x, slots[i].Codes));
				if (bk != null) used.Add(bk.Address);
				starters.Add((slots[i], picks[i], bk));
			}

			double avgCa = starters.Where(s => s.Player != null).Select(s => s.Player!.CA).DefaultIfEmpty(100).Average();

			// Draw pitch
			double w = 440, h = 540;
			var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
			var lineBrush = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
			canvas.Children.Add(new Border
			{
				Width = w,
				Height = h,
				CornerRadius = new CornerRadius(8),
				Background = new SolidColorBrush(Color.FromRgb(0x14, 0x38, 0x26)),
				BorderBrush = lineBrush,
				BorderThickness = new Thickness(1)
			});
			var half = new Border { Width = w, Height = 1, Background = lineBrush };
			Canvas.SetTop(half, h / 2);
			canvas.Children.Add(half);
			double cr = w * 0.22;
			var circle = new System.Windows.Shapes.Ellipse { Width = cr, Height = cr, Stroke = lineBrush, StrokeThickness = 1 };
			Canvas.SetLeft(circle, (w - cr) / 2);
			Canvas.SetTop(circle, (h - cr) / 2);
			canvas.Children.Add(circle);

			foreach (var (slot, p, _) in starters)
			{
				string shortName = p == null ? "—" : (p.Name.Split(' ').Last());
				bool weak = p == null || p.CA < avgCa - 14;
				var badge = new Border
				{
					Width = 104,
					Padding = new Thickness(6, 4, 6, 4),
					CornerRadius = new CornerRadius(6),
					Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x12, 0x19, 0x26)),
					BorderBrush = weak ? Build.Res("Danger") : Build.Res("Positive"),
					BorderThickness = new Thickness(1.2),
					Cursor = p != null ? Cursors.Hand : Cursors.Arrow
				};
				var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
				var topLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
				topLine.Children.Add(Build.Text(slot.Label, 10, Build.Res("Muted"), FontWeights.SemiBold));
				if (p != null)
				{
					var caTxt = Build.Text(" " + p.CA, 10.5, Colors.Attribute((int)Math.Round(p.CA / 10.0)), FontWeights.Bold);
					topLine.Children.Add(caTxt);
				}
				sp.Children.Add(topLine);
				var nm = Build.Text(shortName, 11.5, Brushes.White, FontWeights.SemiBold);
				nm.HorizontalAlignment = HorizontalAlignment.Center;
				sp.Children.Add(nm);
				badge.Child = sp;
				if (p != null)
				{
					PlayerRow target = p;
					badge.MouseLeftButtonUp += (_, _) => nav.OpenPlayer(target.Address);
				}
				Canvas.SetLeft(badge, slot.X * w - 52);
				Canvas.SetTop(badge, slot.Y * h - 20);
				canvas.Children.Add(badge);
			}

			// Right side: Depth & Gap Analysis
			var depthStack = new StackPanel { Margin = new Thickness(12, 4, 12, 10) };
			depthStack.Children.Add(Build.Text(Build.T($"İlk 11 Ortalama CA: {avgCa:0.0}  ·  Kırmızı satırlar zayıf veya yedeksiz mevkiler",
				$"Best XI Avg CA: {avgCa:0.0}  ·  Red rows mark weak or uncovered slots"), 11.5, Build.Res("Muted")));
			foreach (var (slot, p, bk) in starters)
			{
				bool weak = p == null || p.CA < avgCa - 14 || bk == null;
				var g = new Grid { Height = 30, Margin = new Thickness(0, 2, 0, 2) };
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				g.Children.Add(Build.Text(slot.Label, 12, weak ? Build.Res("Danger") : Build.Res("Accent"), FontWeights.Bold));
				var pBtn = new Button
				{
					Content = p != null ? $"{p.Name} ({p.CA})" : Build.T("Eksik!", "Missing!"),
					Style = Build.StyleOf("LinkButton"),
					FontSize = 12,
					HorizontalAlignment = HorizontalAlignment.Left
				};
				if (p != null) { PlayerRow t = p; pBtn.Click += (_, _) => nav.OpenPlayer(t.Address); }
				Grid.SetColumn(pBtn, 1);
				g.Children.Add(pBtn);
				var bkTxt = Build.Text(bk != null ? $"{bk.Name} ({bk.CA})" : Build.T("Yedek yok", "No backup"), 11.5, bk == null ? Build.Res("Danger") : Build.Res("Muted"));
				Grid.SetColumn(bkTxt, 2);
				g.Children.Add(bkTxt);
				depthStack.Children.Add(g);
			}

			var split = new Grid { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
			split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
			var pitchCard = Build.Card(Build.T($"En İyi 11 ({formName})", $"Best XI ({formName})"), new Border { Padding = new Thickness(12, 4, 12, 12), Child = canvas });
			var depthCard = Build.Card(Build.T("Kadro Derinliği & Eksik Analizi", "Squad Depth & Gap Analysis"), depthStack);
			Grid.SetColumn(pitchCard, 0);
			Grid.SetColumn(depthCard, 1);
			split.Children.Add(pitchCard);
			split.Children.Add(depthCard);
			host.Content = Build.Scroll(split);
		}

		foreach (string f in Formations.Keys)
		{
			string form = f;
			var rb = new RadioButton { Content = form, Style = Build.StyleOf("Segment"), GroupName = "xi" + club.Address, Margin = new Thickness(6, 0, 0, 0), IsChecked = form == "4-2-3-1" };
			rb.Checked += (_, _) => Render(form);
			bar.Children.Add(rb);
		}

		var dock = new DockPanel();
		DockPanel.SetDock(bar, Dock.Top);
		dock.Children.Add(bar);
		dock.Children.Add(host);
		return dock;
	}
}

/// <summary>Renders a shareable FM-style PNG player card and copies it to clipboard + saves to file.</summary>
public static class PlayerCardExport
{
	public static void Export(GameData data, ulong address, string name, uint uid, INavigator nav)
	{
		PlayerRow? row = data.Players.FirstOrDefault(p => p.Address == address);
		if (row == null) return;
		byte[] raw69 = new byte[69];
		data.Memory.TryRead(address + 0x150, raw69);

		var card = new Border
		{
			Width = 760,
			Height = 380,
			CornerRadius = new CornerRadius(12),
			Background = new LinearGradientBrush(Color.FromRgb(0x10, 0x17, 0x26), Color.FromRgb(0x19, 0x26, 0x42), 45),
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
		titleStack.Children.Add(Build.Text(name, 22, Brushes.White, FontWeights.Bold));
		titleStack.Children.Add(Build.Text($"{row.Positions}  ·  {row.Age} {Build.T("yaş", "yo")}  ·  {row.Nation}", 13, new SolidColorBrush(Color.FromRgb(0xA8, 0xB6, 0xCE))));
		titleStack.Children.Add(Build.Text($"{row.Club}  ·  {row.RoleDisplay}", 13.5, Build.Res("Accent"), FontWeights.SemiBold));
		top.Children.Add(titleStack);
		left.Children.Add(top);

		left.Children.Add(PlayerVisuals.AbilityBar(row.CA, row.PA));

		var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 12) };
		void Box(string lbl, string val)
		{
			var b = new Border { Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8) };
			var s = new StackPanel();
			s.Children.Add(Build.Text(val, 15, Brushes.White, FontWeights.Bold));
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
		card.UpdateLayout();

		var rtb = new RenderTargetBitmap(760, 380, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(card);
		try { Clipboard.SetImage(rtb); } catch { }

		var dlg = new Microsoft.Win32.SaveFileDialog
		{
			Filter = "PNG (*.png)|*.png",
			FileName = $"{string.Concat(name.Split(Path.GetInvalidFileNameChars()))}_FM27.png"
		};
		if (dlg.ShowDialog() == true)
		{
			var enc = new PngBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(rtb));
			using var fs = File.Create(dlg.FileName);
			enc.Save(fs);
			nav.SetStatus(Build.T($"✓ PNG kart kaydedildi ve panoya kopyalandı: {Path.GetFileName(dlg.FileName)}",
				$"✓ PNG card saved & copied to clipboard: {Path.GetFileName(dlg.FileName)}"));
		}
		else
		{
			nav.SetStatus(Build.T("✓ Oyuncu kartı panoya kopyalandı (Ctrl+V ile yapıştırabilirsin).", "✓ Player card copied to clipboard (press Ctrl+V to paste)."));
		}
	}
}
