using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SquadEditor.App.Ui;

/// <summary>
/// Saves lightweight local JSON snapshots of a club's squad and computes CA, Value, and key attribute
/// progression deltas across the save (inspired by long-term FM development tracking).
/// </summary>
public static class SquadSnapshot
{
	private sealed class SnapshotFile
	{
		public string ClubName { get; set; } = "";
		public string GameDate { get; set; } = "";
		public string SavedUtc { get; set; } = "";
		public List<PlayerSnap> Players { get; set; } = new();
	}

	private sealed class PlayerSnap
	{
		public uint Uid { get; set; }
		public string Name { get; set; } = "";
		public int Age { get; set; }
		public int CA { get; set; }
		public int PA { get; set; }
		public long Value { get; set; }
		public double BestRating { get; set; }
		public int Pace { get; set; }
		public int Finishing { get; set; }
		public int Passing { get; set; }
		public int Determination { get; set; }
	}

	private sealed class DeltaRow
	{
		public ulong Address { get; init; }
		public string Name { get; init; } = "";
		public string Positions { get; init; } = "";
		public int Age { get; init; }
		public int OldCA { get; init; }
		public int NewCA { get; init; }
		public int DeltaCA => NewCA - OldCA;
		public string DeltaCAText => DeltaCA > 0 ? $"+{DeltaCA}" : (DeltaCA < 0 ? DeltaCA.ToString() : "0");
		public int PA { get; init; }
		public long DeltaValue { get; init; }
		public string DeltaValueText => DeltaValue == 0 ? "—" : (DeltaValue > 0 ? "+" : "-") + Money.Format(Math.Abs(DeltaValue), false);
		public string AttrDelta { get; init; } = "—";
	}

	private static string SnapDir => Path.Combine(PlatformPaths.LocalAppDataDir, "snapshots");

	private static string PathFor(string saveKey, uint clubUid)
	{
		string safe = string.Concat(saveKey.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
		return Path.Combine(SnapDir, $"{safe}_{clubUid}.json");
	}

	public static void OpenTracker(GameData data, ClubRow club, INavigator nav)
	{
		string path = PathFor(data.SaveKey, club.Uid);

		SnapshotFile? Load()
		{
			try
			{
				if (!File.Exists(path)) return null;
				return JsonSerializer.Deserialize<SnapshotFile>(File.ReadAllText(path));
			}
			catch
			{
				return null;
			}
		}

		void SaveNow()
		{
			Directory.CreateDirectory(SnapDir);
			var squad = data.Squads[club.Address].ToList();
			var snap = new SnapshotFile
			{
				ClubName = club.Name,
				GameDate = data.GameDate.ToString("dd.MM.yyyy"),
				SavedUtc = DateTime.UtcNow.ToString("O"),
				Players = squad.Select(p => new PlayerSnap
				{
					Uid = p.Uid,
					Name = p.Name,
					Age = p.Age,
					CA = p.CA,
					PA = p.PA,
					Value = p.Value,
					BestRating = p.BestRating,
					Pace = p.Pace,
					Finishing = p.Finishing,
					Passing = p.Passing,
					Determination = p.Determination
				}).ToList()
			};
			File.WriteAllText(path, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
		}

		var dlg = new Window
		{
			Title = Build.F("📸 Kadro Gelişim Takibi — {0}", "📸 Squad Progress Tracker — {0}", club.Name),
			Width = 860,
			Height = 560,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = Build.Font("UiFont")
		};
		WindowHelper.ApplyChrome(dlg);

		var root = new DockPanel { Margin = new Thickness(16) };
		var host = new ContentControl();
		var statusTxt = Build.Text("", 12, Build.Res("Muted"));

		void Render()
		{
			SnapshotFile? prev = Load();
			if (prev == null)
			{
				statusTxt.Text = Build.T("Henüz kayıtlı kadro fotoğrafı yok. Şimdi bir fotoğraf çek, ilerleyen aylarda geri gelip gelişimi izle!", "No squad snapshot saved yet. Take a snapshot now and check back later in the season to track player growth!");
				host.Content = new Border
				{
					Classes = { "CardBorder" },
					Padding = new Thickness(24),
					Child = Build.Text(Build.T("Yukarıdaki '📸 Yeni Fotoğraf Çek' butonuna tıklayarak mevcut kadronun CA, değer ve temel özelliklerini kaydet.", "Click '📸 Take New Snapshot' above to record your squad's current CA, value, and key attributes."), 13, Build.Res("Muted"))
				};
				return;
			}

			var byUid = prev.Players.GroupBy(p => p.Uid).ToDictionary(g => g.Key, g => g.First());
			var squad = data.Squads[club.Address].ToList();
			var rows = new List<DeltaRow>();
			foreach (PlayerRow p in squad)
			{
				if (!byUid.TryGetValue(p.Uid, out PlayerSnap? old)) continue;
				var parts = new List<string>();
				if (p.Pace != old.Pace) parts.Add($"{Build.T("Hız", "Pac")} {(p.Pace > old.Pace ? "+" : "")}{p.Pace - old.Pace}");
				if (p.Finishing != old.Finishing) parts.Add($"{Build.T("Bit", "Fin")} {(p.Finishing > old.Finishing ? "+" : "")}{p.Finishing - old.Finishing}");
				if (p.Passing != old.Passing) parts.Add($"{Build.T("Pas", "Pas")} {(p.Passing > old.Passing ? "+" : "")}{p.Passing - old.Passing}");
				if (p.Determination != old.Determination) parts.Add($"{Build.T("Kar", "Det")} {(p.Determination > old.Determination ? "+" : "")}{p.Determination - old.Determination}");
				rows.Add(new DeltaRow
				{
					Address = p.Address,
					Name = p.Name,
					Positions = p.Positions,
					Age = p.Age,
					OldCA = old.CA,
					NewCA = p.CA,
					PA = p.PA,
					DeltaValue = p.Value - old.Value,
					AttrDelta = parts.Count > 0 ? string.Join(" · ", parts) : "—"
				});
			}

			rows = rows.OrderByDescending(r => r.DeltaCA).ThenByDescending(r => r.NewCA).ToList();
			int improved = rows.Count(r => r.DeltaCA > 0);
			int declined = rows.Count(r => r.DeltaCA < 0);
			int netCa = rows.Sum(r => r.DeltaCA);
			string netSign = netCa > 0 ? "+" : "";
			statusTxt.Text = Build.F("Referans: {0}  ·  {1} gelişen · {2} düşen · Net ΔCA: {3}{4}", "Baseline: {0}  ·  {1} improved · {2} declined · Net ΔCA: {3}{4}", Dates.Show(prev.GameDate), improved, declined, netSign, netCa);

			var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true };
			grid.Columns.Add(Build.TextColumn(Build.T("Oyuncu", "Player"), nameof(DeltaRow.Name), 200));
			grid.Columns.Add(Build.TextColumn(Build.T("Mevki", "Pos"), nameof(DeltaRow.Positions), 78));
			grid.Columns.Add(Build.TextColumn(Build.T("Yaş", "Age"), nameof(DeltaRow.Age), 46));
			grid.Columns.Add(Build.TextColumn(Build.T("Eski CA", "Old CA"), nameof(DeltaRow.OldCA), 64));
			grid.Columns.Add(Build.TextColumn(Build.T("Yeni CA", "New CA"), nameof(DeltaRow.NewCA), 64));
			var dCol = Build.TextColumn("ΔCA", nameof(DeltaRow.DeltaCAText), 56);
			dCol.SortMemberPath = nameof(DeltaRow.DeltaCA);
			grid.Columns.Add(dCol);
			grid.Columns.Add(Build.TextColumn("PA", nameof(DeltaRow.PA), 50));
			var vCol = Build.TextColumn(Build.T("ΔDeğer", "ΔValue"), nameof(DeltaRow.DeltaValueText), 100);
			vCol.SortMemberPath = nameof(DeltaRow.DeltaValue);
			grid.Columns.Add(vCol);
			grid.Columns.Add(Build.TextColumn(Build.T("Özellik Değişimi", "Attribute Changes"), nameof(DeltaRow.AttrDelta), 170));

			grid.DoubleTapped += (_, _) =>
			{
				if (grid.SelectedItem is DeltaRow r)
				{
					nav.OpenPlayer(r.Address);
					dlg.Close();
				}
			};
			host.Content = new Border { Classes = { "CardBorder" }, Padding = new Thickness(4), Child = grid };
		}

		var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
		var takeBtn = new Button
		{
			Content = Build.T("📸 Yeni Fotoğraf Çek", "📸 Take New Snapshot"),
			Theme = Build.StyleOf("AccentButton"),
			Padding = new Thickness(12, 5, 12, 5),
			FontSize = 12
		};
		takeBtn.Click += (_, _) =>
		{
			SaveNow();
			nav.SetStatus(Build.F("✓ {0} kadro fotoğrafı kaydedildi ({1}).", "✓ Saved squad snapshot for {0} ({1}).", club.Name, Dates.Show(data.GameDate.ToString("dd.MM.yyyy"))));
			Render();
		};
		DockPanel.SetDock(takeBtn, Dock.Right);
		top.Children.Add(takeBtn);
		top.Children.Add(statusTxt);
		DockPanel.SetDock(top, Dock.Top);
		root.Children.Add(top);
		root.Children.Add(host);

		Render();
		dlg.Content = root;
		Window? owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
		if (owner is { IsVisible: true }) _ = dlg.ShowDialog(owner);
		else dlg.Show();
	}
}
