using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SquadEditor.Core.Editing;

namespace SquadEditor.App.Ui;

public sealed class ClubPage : UserControl, IPage
{
	private readonly GameData _data;
	private readonly INavigator _nav;
	private readonly ClubRow _club;
	private readonly List<EditField> _fields;
	private readonly DockPanel _root = new DockPanel();
	private Border _header;
	private string? _team;

	public ClubPage(GameData data, ClubRow club, INavigator nav)
	{
		_data = data;
		_nav = nav;
		_club = club;
		string lang = Loc.Instance.Language;
		_fields = data.ClubDef.Fields.Where(f => f.GroupKey != "Internal").Select(f => new EditField(data, club.Address, f, data.Layout.Text(lang, f.Name))).ToList();
		Loaded += (_, _) => FieldBox.EntityLabel = club.Name;
		EditField.Written += OnWritten;
		Unloaded += (_, _) => EditField.Written -= OnWritten;
		Loaded += (_, _) => { EditField.Written -= OnWritten; EditField.Written += OnWritten; };

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);
		Grid body = Build.PageBody(out StackPanel menu, out ContentControl content);
		Build.NavItem(menu, Build.T("Kadro", "Squad"), () => content.Content = Squad(), isChecked: true);
		Build.NavItem(menu, Build.T("Personel", "Staff"), () => content.Content = StaffList());
		Build.NavItem(menu, Build.T("Finans", "Finances"), () => content.Content = Finances());
		Build.NavItem(menu, Build.T("Bilgi ve tesisler", "Info & facilities"), () => content.Content = Info());
		_root.Children.Add(body);
		Content = _root;
		Background = Build.Res("Bg");
	}

	public new uint Uid => _club.Uid;
	public string Key => "club:" + _club.Address;
	public string Title => _club.Name;
	public ImageSource? Icon => ImageLookup.Graphics.Club(_club.Uid);

	public void RefreshValues()
	{
		foreach (EditField f in _fields)
		{
			f.Refresh();
		}
	}

	private EditField F(string name) => _fields.First(f => f.Key == name);

	private void OnWritten(object? sender, EditField field)
	{
		if (field.Entity == _club.Address)
		{
			Border fresh = BuildHeader();
			DockPanel.SetDock(fresh, Dock.Top);
			_root.Children.Remove(_header);
			_root.Children.Insert(0, fresh);
			_header = fresh;
		}
	}

	private Border BuildHeader()
	{
		var line1 = Build.Text(string.Join("  ·  ", new[] { _club.FullName, _club.Nation }.Where(s => s.Length > 0)), 13, new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
		line1.Margin = new Thickness(0, 4, 0, 0);
		var line2 = Build.Text(_club.League, 14, Brushes.White, FontWeights.SemiBold);
		line2.Margin = new Thickness(0, 6, 0, 0);
		var pills = new List<UIElement>
		{
			Build.Pill(_club.SquadSize.ToString(), Build.T("Oyuncu", "Players")),
			Build.Pill(_club.AverageCA.ToString("0"), Build.T("Ortalama CA", "Average CA"), Colors.Attribute((int)Math.Round(_club.AverageCA / 10.0))),
		};
		EditField balance = F("Balance"), budget = F("Transfer Budget");
		if (balance.Value.Length > 0)
		{
			pills.Add(Build.Pill(Money.Format(balance.Number, false), Build.T("Bakiye", "Balance"), balance.Number < 0 ? Build.Res("Danger") : null));
		}
		if (budget.Value.Length > 0)
		{
			pills.Add(Build.Pill(Money.Format(budget.Number, false), Build.T("Transfer bütçesi", "Transfer budget")));
		}
		return Build.Header(Build.Avatar(Icon, _club.Name, 74, round: false), _club.Name, line1, line2, pills);
	}

	private UIElement Squad()
	{
		var all = _data.Squads[_club.Address].ToList();
		var teams = all.Where(p => p.Team.Length > 0).GroupBy(p => p.Team)
			.OrderByDescending(g => g.Average(p => p.CA)).Select(g => g.Key).ToList();
		var host = new ContentControl();
		var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
		void Show(string? team)
		{
			_team = team;
			var rows = (team == null ? all : all.Where(p => p.Team == team)).OrderByDescending(p => p.CA);
			host.Content = new Border { Style = Build.StyleOf("CardBorder"), Padding = new Thickness(4), Child = PlayerGrid(_data, rows, _nav, showClub: false, showTeam: team == null) };
		}
		var everyone = new RadioButton { Content = Build.T($"Tümü ({all.Count})", $"All ({all.Count})"), Style = Build.StyleOf("Segment"), GroupName = "teams" + _club.Address };
		everyone.Checked += (_, _) => Show(null);
		bar.Children.Add(everyone);
		foreach (string team in teams)
		{
			int count = all.Count(p => p.Team == team);
			var button = new RadioButton { Content = $"{team} ({count})", Style = Build.StyleOf("Segment"), GroupName = "teams" + _club.Address };
			button.Checked += (_, _) => Show(team);
			bar.Children.Add(button);
		}
		var hint = Build.Text(Build.T("Ctrl+A: tümünü seç · Çift tık: aç · Sağ tık: toplu düzenle", "Ctrl+A: select all · Double-click: open · Right-click: bulk edit"), 11.5, Build.Res("Faint"));
		hint.Margin = new Thickness(12, 0, 0, 0);
		bar.Children.Add(hint);

		var quickBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		var prepare = new Button
		{
			Content = Build.T("⚡ Tüm Kadroyu Maça Hazırla", "⚡ Prepare Squad for Match"),
			ToolTip = Build.T("Kadroda görünen tüm oyuncuların sakatlıklarını iyileştirir, cezalarını kaldırır, kondisyon ve morallerini fuller (Ctrl+Z geri alır)",
				"Heals injuries, lifts bans, and maxes condition & morale for all players in the current view (Ctrl+Z undoes it)"),
			Style = Build.StyleOf("AccentButton"),
			Padding = new Thickness(10, 4, 10, 4),
			FontSize = 12,
			IsEnabled = !AppSettings.Current.ReadOnlyMode
		};
		prepare.Click += (_, _) =>
		{
			var rows = (_team == null ? all : all.Where(p => p.Team == _team)).ToList();
			if (rows.Count == 0)
			{
				return;
			}
			int healed = 0, lifted = 0, boosted = 0;
			using (_data.Session.History.BeginBatch(Build.T($"{_club.Name}: Kadroyu maça hazırla", $"{_club.Name}: Prepare squad")))
			{
				foreach (PlayerRow r in rows)
				{
					if (Medical.Heal(_data, r.Address, r.Name)) healed++;
					if (Medical.LiftBans(_data, r.Address, r.Name)) lifted++;
					r.Status = Medical.Status(_data.Memory, r.Address, _data.GameDate);
				}
				var actions = new[]
				{
					new SquadEditor.Core.Layout.PresetAction { Field = "Condition", Value = "max" },
					new SquadEditor.Core.Layout.PresetAction { Field = "Match Sharpness", Value = "max" },
					new SquadEditor.Core.Layout.PresetAction { Field = "Morale", Value = "max" }
				};
				var targets = rows.Select(r => new BulkTarget(r.Address, r.Name)).ToList();
				foreach (var t in targets)
				{
					foreach (var a in actions)
					{
						var fd = _data.PlayerDef.Fields.FirstOrDefault(f => f.Name == a.Field && f.IsWritable && f.IsNumeric);
						if (fd != null && fd.Max.HasValue)
						{
							try
							{
								_data.Session.WriteField(t.Address, fd, fd.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Label);
								boosted++;
							}
							catch (Exception)
							{
							}
						}
					}
				}
			}
			Show(_team);
			_nav.SetStatus(Build.T($"✓ {rows.Count} oyuncu maça hazırlandı ({healed} sakatlık iyileşti, {lifted} ceza kalktı, {boosted} kondisyon/moral güncellendi). Ctrl+Z geri alır.",
				$"✓ {rows.Count} players prepared ({healed} healed, {lifted} bans lifted, {boosted} fitness/morale updated). Ctrl+Z undoes it."));
		};
		quickBar.Children.Add(prepare);
		var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
		DockPanel.SetDock(quickBar, Dock.Right);
		topBar.Children.Add(quickBar);
		bar.Margin = new Thickness(0);
		topBar.Children.Add(bar);

		var dock = new DockPanel();
		DockPanel.SetDock(topBar, Dock.Top);
		dock.Children.Add(topBar);
		dock.Children.Add(host);
		string? first = _team ?? teams.FirstOrDefault();
		RadioButton? pick = bar.Children.OfType<RadioButton>().FirstOrDefault(r => first != null && (r.Content as string ?? "").StartsWith(first + " ("));
		(pick ?? everyone).IsChecked = true;
		return dock;
	}

	private UIElement StaffList()
	{
		var staff = _data.StaffByClub[_club.Address].OrderBy(s => s.Job).ThenBy(s => s.Name);
		return new Border { Style = Build.StyleOf("CardBorder"), Padding = new Thickness(4), Child = StaffGrid(_data, staff, _nav, showClub: false) };
	}

	private UIElement Finances()
	{
		var stack = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
		var note = Build.Text(Build.T(
			$"Tutarlar {Money.Current.Code} olarak gösterilir, maaşlar {Money.WageSuffix.TrimStart('/')} bazında. Ayarlardan değiştirebilirsin.",
			$"Amounts are shown in {Money.Current.Code}, wages per {Money.WageSuffix.TrimStart('/')}. Change this in Settings."), 12, Build.Res("Muted"));
		note.Margin = new Thickness(14, 0, 14, 10);
		note.TextWrapping = TextWrapping.Wrap;
		var rows = Build.Rows(_fields.Where(f => f.Def.GroupKey == "Finances"));
		rows.Children.Add(note);
		stack.Children.Add(Build.Card(Build.T("Finans", "Finances"), rows));
		stack.Children.Add(Build.PresetsCard(_data, "Club", () => new[] { new BulkTarget(_club.Address, _club.Name) }, () => { RefreshValues(); OnWritten(null, F("Balance")); }, _nav));
		return Build.Scroll(stack);
	}

	private UIElement Info()
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
		var left = new StackPanel();
		left.Children.Add(Build.Card(Build.T("Genel", "General"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "General"))));
		left.Children.Add(Build.Card(Build.T("Taraftarlar (1-20)", "Supporters (1-20)"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Supporters"))));
		var right = new StackPanel();
		right.Children.Add(Build.Card(Build.T("Tesisler (1-20)", "Facilities (1-20)"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Facilities"))));
		right.Children.Add(Build.Card(Build.T("Stadyum", "Stadium"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Stadium"))));
		Grid.SetColumn(right, 1);
		grid.Children.Add(left);
		grid.Children.Add(right);
		return Build.Scroll(grid);
	}

	/// <summary>Sortable player table shared by squads and the player browser. Multi-select + right-click applies presets.</summary>
	public static DataGrid PlayerGrid(GameData data, IEnumerable<PlayerRow> players, INavigator nav, bool showClub, bool showTeam = false)
	{
		var grid = new DataGrid { ItemsSource = players.ToList(), RowHeight = AppSettings.Current.Compact ? 27 : 34 };
		grid.Columns.Add(Build.TemplateColumn(Build.T("Oyuncu", "Player"), "PlayerNameCell", "Name", 250));
		grid.Columns.Add(Build.TextColumn(Build.T("Mevki", "Position"), "Positions", 105));
		grid.Columns.Add(Build.TextColumn(Build.T("Yaş", "Age"), "Age", 50));
		var status = Build.TextColumn(Build.T("Durum", "Status"), "Status", 120);
		status.Foreground = Build.Res("Danger");
		grid.Columns.Add(status);
		if (showClub)
		{
			grid.Columns.Add(Build.TemplateColumn(Build.T("Kulüp", "Club"), "PlayerClubCell", "Club", 190));
		}
		if (showTeam)
		{
			grid.Columns.Add(Build.TextColumn(Build.T("Takım", "Team"), "Team", 120));
		}
		grid.Columns.Add(Build.TextColumn(Build.T("Uyruk", "Nation"), "Nation", 120));
		grid.Columns.Add(Build.TemplateColumn("CA", "CACell", "CA", 52));
		grid.Columns.Add(Build.TemplateColumn("PA", "PACell", "PA", 52));
		HashSet<string> extras = new HashSet<string>(AppSettings.Current.ExtraColumns);
		if (extras.Contains("Growth")) grid.Columns.Add(Build.TextColumn("PA-CA", "Growth", 54));
		if (extras.Contains("Role"))
		{
			var col = Build.TextColumn(Build.T("En İyi Rol", "Best Role"), "RoleDisplay", 86);
			col.SortMemberPath = "BestRating";
			grid.Columns.Add(col);
		}
		if (extras.Contains("Pro")) grid.Columns.Add(Build.TextColumn(Build.T("Prof.", "Pro"), "Professionalism", 48));
		if (extras.Contains("Cons")) grid.Columns.Add(Build.TextColumn(Build.T("İstik.", "Cons"), "Consistency", 48));
		if (extras.Contains("Imp")) grid.Columns.Add(Build.TextColumn(Build.T("Ön.Maç", "Imp.M"), "ImportantMatches", 56));
		if (extras.Contains("Inj")) grid.Columns.Add(Build.TextColumn(Build.T("Sak.Eğ.", "Inj.P"), "InjuryProneness", 56));
		if (extras.Contains("Det")) grid.Columns.Add(Build.TextColumn(Build.T("Karar.", "Det"), "Determination", 52));
		grid.Columns.Add(MoneyColumn(Build.T("Değer", "Value"), "Value", false, 110));
		grid.Columns.Add(MoneyColumn(Build.T("Maaş", "Wage"), "Wage", true, 130));
		grid.Columns.Add(Build.TextColumn(Build.T("Sözleşme", "Contract"), "ContractEnd", 95));
		grid.MouseDoubleClick += (_, _) =>
		{
			if (grid.SelectedItem is PlayerRow row)
			{
				nav.OpenPlayer(row.Address);
			}
		};
		grid.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == System.Windows.Input.Key.Enter && grid.SelectedItem is PlayerRow row)
			{
				nav.OpenPlayer(row.Address);
				e.Handled = true;
			}
		};
		grid.ContextMenu = PresetMenu(data, "Player", () => grid.SelectedItems.OfType<PlayerRow>().Select(p => new BulkTarget(p.Address, p.Name)).ToList(), nav);
		// Medical actions for the selection.
		grid.ContextMenu.Items.Add(new Separator());
		void Action(string tr, string en, Func<PlayerRow, bool> act)
		{
			var item = new MenuItem { Header = Build.T(tr, en), IsEnabled = !AppSettings.Current.ReadOnlyMode };
			item.Click += (_, _) =>
			{
				var rows = grid.SelectedItems.OfType<PlayerRow>().ToList();
				int done = 0;
				using (data.Session.History.BeginBatch(Build.T(tr, en)))
				{
					foreach (PlayerRow row in rows)
					{
						if (act(row))
						{
							done++;
							row.Status = Medical.Status(data.Memory, row.Address, data.GameDate);
						}
					}
				}
				grid.Items.Refresh();
				nav.SetStatus(Build.T($"{tr}: {done} oyuncu.", $"{en}: {done} players."));
			};
			grid.ContextMenu.Items.Add(item);
		}
		Action("Seçilenleri iyileştir", "Heal selected", r => Medical.Heal(data, r.Address, r.Name));
		Action("Seçilenlerin cezasını kaldır", "Lift bans of selected", r => Medical.LiftBans(data, r.Address, r.Name));
		grid.ContextMenu.Items.Add(new Separator());
		var freezeSel = new MenuItem { Header = Build.T("❄ Seçilenleri dondur", "❄ Freeze selected"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		freezeSel.Click += (_, _) =>
		{
			var rows = grid.SelectedItems.OfType<PlayerRow>().ToList();
			int done = 0;
			foreach (PlayerRow row in rows)
			{
				if (data.Session.FreezeEntity(row.Address, row.Name, data.PlayerDef, PlayerPage.FreezeGroups) > 0)
				{
					done++;
				}
			}
			nav.SetStatus(Build.T($"Donduruldu: {done} oyuncu.", $"Frozen: {done} players."));
		};
		grid.ContextMenu.Items.Add(freezeSel);
		var unfreezeSel = new MenuItem { Header = Build.T("Seçilenlerin dondurmasını kaldır", "Unfreeze selected") };
		unfreezeSel.Click += (_, _) =>
		{
			var rows = grid.SelectedItems.OfType<PlayerRow>().ToList();
			int done = 0;
			foreach (PlayerRow row in rows)
			{
				if (data.Session.Freezer.IsFrozen(row.Address))
				{
					data.Session.Freezer.Unfreeze(row.Address);
					done++;
				}
			}
			nav.SetStatus(Build.T($"Dondurma kaldırıldı: {done} oyuncu.", $"Unfrozen: {done} players."));
		};
		grid.ContextMenu.Items.Add(unfreezeSel);
		grid.ContextMenu.Items.Add(new Separator());
		var massEdit = new MenuItem { Header = Build.T("✎ Toplu düzenle…", "✎ Mass edit…"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		massEdit.Click += (_, _) =>
		{
			var targets = grid.SelectedItems.OfType<PlayerRow>().Select(p => new BulkTarget(p.Address, p.Name)).ToList();
			if (targets.Count == 0)
			{
				return;
			}
			var dlg = new MassEditWindow(data, data.PlayerDef, targets, nav) { Owner = Window.GetWindow(grid) };
			dlg.ShowDialog();
		};
		grid.ContextMenu.Items.Add(massEdit);
		var cloneSel = new MenuItem { Header = Build.T("⧉ Özellikleri klonla (ilk seçilenden diğerlerine)", "⧉ Clone attributes (first selected to others)"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		cloneSel.Click += (_, _) =>
		{
			var rows = grid.SelectedItems.OfType<PlayerRow>().ToList();
			if (rows.Count < 2)
			{
				nav.SetStatus(Build.T("Klonlamak için en az 2 oyuncu seç (önce kaynak oyuncuyu seç).", "Select at least 2 players to clone (select the source player first)."));
				return;
			}
			PlayerRow src = rows[0];
			var targets = rows.Skip(1).Select(p => new BulkTarget(p.Address, p.Name)).ToList();
			if (AppSettings.Current.ConfirmBulkEdits && MessageBox.Show(
				Build.T($"{src.Name} oyuncusunun özellikleri {targets.Count} oyuncuya kopyalansın mı? Ctrl+Z geri alır.", $"Clone {src.Name}'s attributes to {targets.Count} players? Ctrl+Z undoes it."),
				"SquadEditor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			BulkResult res = BulkEdit.Clone(data.Session, data.PlayerDef, src.Address, PlayerPage.FreezeGroups, targets, Build.T($"Klonla: {src.Name}", $"Clone: {src.Name}"));
			nav.SetStatus(Build.T($"✓ {src.Name} → {targets.Count} oyuncu klonlandı ({res.Written} değer). Ctrl+Z geri alır.",
				$"✓ Cloned {src.Name} → {targets.Count} players ({res.Written} values). Ctrl+Z undoes it."));
		};
		grid.ContextMenu.Items.Add(cloneSel);
		grid.ContextMenu.Items.Add(new Separator());
		var shortlistAdd = new MenuItem { Header = Build.T("★ Kısa listeye ekle / çıkar", "★ Toggle shortlist") };
		shortlistAdd.Click += (_, _) =>
		{
			var rows = grid.SelectedItems.OfType<PlayerRow>().Where(r => r.Uid > 0).ToList();
			if (rows.Count == 0) return;
			var set = new HashSet<uint>(AppSettings.Current.ShortlistUids);
			bool allIn = rows.All(r => set.Contains(r.Uid));
			foreach (PlayerRow r in rows)
			{
				if (allIn) set.Remove(r.Uid); else set.Add(r.Uid);
			}
			AppSettings.Current.ShortlistUids = set.ToList();
			AppSettings.Current.Save();
			nav.SetStatus(allIn
				? Build.T($"★ {rows.Count} oyuncu kısa listeden çıkarıldı.", $"★ Removed {rows.Count} players from shortlist.")
				: Build.T($"★ {rows.Count} oyuncu kısa listeye eklendi (toplam {set.Count}).", $"★ Added {rows.Count} players to shortlist ({set.Count} total)."));
		};
		grid.ContextMenu.Items.Add(shortlistAdd);
		return grid;
	}

	public static DataGrid StaffGrid(GameData data, IEnumerable<StaffRow> staff, INavigator nav, bool showClub)
	{
		var grid = new DataGrid { ItemsSource = staff.ToList(), RowHeight = AppSettings.Current.Compact ? 27 : 34 };
		grid.Columns.Add(Build.TemplateColumn(Build.T("Personel", "Staff"), "PlayerNameCell", "Name", 240));
		grid.Columns.Add(Build.TextColumn(Build.T("Görev", "Job"), "Job", 175));
		grid.Columns.Add(Build.TextColumn(Build.T("Yaş", "Age"), "Age", 48));
		if (showClub)
		{
			grid.Columns.Add(Build.TemplateColumn(Build.T("Kulüp", "Club"), "PlayerClubCell", "Club", 180));
		}
		grid.Columns.Add(Build.TextColumn(Build.T("Uyruk", "Nation"), "Nation", 120));
		grid.Columns.Add(Build.TemplateColumn("CA", "CACell", "CA", 52));
		grid.Columns.Add(Build.TemplateColumn("PA", "PACell", "PA", 52));
		grid.Columns.Add(MoneyColumn(Build.T("Maaş", "Wage"), "Wage", true, 125));
		grid.Columns.Add(Build.TextColumn(Build.T("Sözleşme", "Contract"), "ContractEnd", 95));
		grid.MouseDoubleClick += (_, _) =>
		{
			if (grid.SelectedItem is StaffRow row)
			{
				nav.OpenStaff(row.Address);
			}
		};
		grid.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == System.Windows.Input.Key.Enter && grid.SelectedItem is StaffRow row)
			{
				nav.OpenStaff(row.Address);
				e.Handled = true;
			}
		};
		var menu = new ContextMenu();
		var freezeStaff = new MenuItem { Header = Build.T("❄ Seçilenleri dondur", "❄ Freeze selected"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		freezeStaff.Click += (_, _) =>
		{
			int done = grid.SelectedItems.OfType<StaffRow>().Count(r => data.Session.FreezeEntity(r.Address, r.Name, data.StaffDef, StaffPage.StaffFreezeGroups) > 0);
			nav.SetStatus(Build.T($"Donduruldu: {done} personel.", $"Frozen: {done} staff."));
		};
		var unfreezeStaff = new MenuItem { Header = Build.T("Seçilenlerin dondurmasını kaldır", "Unfreeze selected") };
		unfreezeStaff.Click += (_, _) =>
		{
			int done = 0;
			foreach (StaffRow r in grid.SelectedItems.OfType<StaffRow>())
			{
				if (data.Session.Freezer.IsFrozen(r.Address))
				{
					data.Session.Freezer.Unfreeze(r.Address);
					done++;
				}
			}
			nav.SetStatus(Build.T($"Dondurma kaldırıldı: {done} personel.", $"Unfrozen: {done} staff."));
		};
		var massStaff = new MenuItem { Header = Build.T("✎ Toplu düzenle…", "✎ Mass edit…"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		massStaff.Click += (_, _) =>
		{
			var targets = grid.SelectedItems.OfType<StaffRow>().Select(s => new BulkTarget(s.Address, s.Name)).ToList();
			if (targets.Count > 0)
			{
				new MassEditWindow(data, data.StaffDef, targets, nav) { Owner = Window.GetWindow(grid) }.ShowDialog();
			}
		};
		menu.Items.Add(freezeStaff);
		menu.Items.Add(unfreezeStaff);
		menu.Items.Add(new Separator());
		menu.Items.Add(massStaff);
		grid.ContextMenu = menu;
		return grid;
	}

	public static DataGrid ClubGrid(IEnumerable<ClubRow> clubs, INavigator nav)
	{
		var grid = new DataGrid { ItemsSource = clubs.ToList(), RowHeight = AppSettings.Current.Compact ? 27 : 34 };
		grid.Columns.Add(Build.TemplateColumn(Build.T("Kulüp", "Club"), "ClubNameCell", "Name", 280));
		grid.Columns.Add(Build.TextColumn(Build.T("Lig", "League"), "League", 220));
		grid.Columns.Add(Build.TextColumn(Build.T("Ülke", "Nation"), "Nation", 150));
		grid.Columns.Add(Build.TextColumn(Build.T("Oyuncu", "Players"), "SquadSize", 80));
		grid.Columns.Add(Build.TextColumn(Build.T("Ort. CA", "Avg CA"), "AverageCA", 80, "0"));
		grid.Columns.Add(Build.TextColumn(Build.T("En iyi CA", "Best CA"), "BestCA", 90));
		grid.MouseDoubleClick += (_, _) =>
		{
			if (grid.SelectedItem is ClubRow row)
			{
				nav.OpenClub(row.Address);
			}
		};
		grid.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == System.Windows.Input.Key.Enter && grid.SelectedItem is ClubRow row)
			{
				nav.OpenClub(row.Address);
				e.Handled = true;
			}
		};
		return grid;
	}

	private static DataGridTextColumn MoneyColumn(string header, string path, bool wage, double width)
	{
		return new DataGridTextColumn
		{
			Header = header,
			Binding = new Binding(path) { Converter = (IValueConverter)Application.Current.Resources["MoneyText"], ConverterParameter = wage ? "wage" : "value" },
			SortMemberPath = path,
			Width = new DataGridLength(width)
		};
	}

	/// <summary>Right-click menu: apply any preset of the entity to the selected rows (bulk edit).</summary>
	private static ContextMenu PresetMenu(GameData data, string entity, Func<List<BulkTarget>> targets, INavigator nav)
	{
		var menu = new ContextMenu();
		string lang = Loc.Instance.Language;
		foreach (var preset in data.Layout.Presets.Where(p => p.Entity == entity))
		{
			var item = new MenuItem { Header = data.Layout.Text(lang, preset.Name), ToolTip = preset.Description == null ? null : data.Layout.Text(lang, preset.Description) };
			item.IsEnabled = !AppSettings.Current.ReadOnlyMode;
			item.Click += (_, _) =>
			{
				List<BulkTarget> list = targets();
				if (list.Count == 0)
				{
					return;
				}
				if (list.Count > 1 && AppSettings.Current.ConfirmBulkEdits && MessageBox.Show(
					Build.T($"\"{data.Layout.Text(lang, preset.Name)}\" {list.Count} oyuncuya uygulansın mı? Ctrl+Z ile geri alabilirsin.", $"Apply \"{preset.Name}\" to {list.Count} players? Ctrl+Z undoes it."),
					"SquadEditor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
				{
					return;
				}
				BulkResult result = BulkEdit.ApplyPreset(data.Session, data.Layout.GetEntity(entity), preset, list, data.Layout.Text(lang, preset.Name));
				nav.SetStatus(Build.T($"{data.Layout.Text(lang, preset.Name)}: {list.Count} kayıt, {result.Written} değer yazıldı" + (result.Failed > 0 ? $", {result.Failed} hata" : "") + ".",
					$"{preset.Name}: {list.Count} records, {result.Written} values written" + (result.Failed > 0 ? $", {result.Failed} failed" : "") + "."));
			};
			menu.Items.Add(item);
		}
		return menu;
	}
}

/// <summary>Staff member page: personal details and contract.</summary>
public sealed class StaffPage : UserControl, IPage
{
	public static readonly HashSet<string> StaffFreezeGroups = new HashSet<string>
	{
		"Coaching", "Medical", "Mental", "Knowledge", "Personality"
	};

	private readonly Dictionary<string, EditField> _f;
	private readonly GameData _data;
	private readonly INavigator _nav;
	private readonly DockPanel _root = new DockPanel();
	private Border _header;

	public StaffPage(GameData data, StaffRow row, INavigator nav)
	{
		_data = data;
		_nav = nav;
		string lang = Loc.Instance.Language;
		Row = row;
		_f = data.StaffDef.Fields.Where(f => f.GroupKey != "Internal" && f.Concat.Count == 0)
			.ToDictionary(f => f.Name, f => new EditField(data, row.Address, f, data.Layout.Text(lang, f.Name)));
		Loaded += (_, _) => FieldBox.EntityLabel = Row.Name;

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);
		Grid body = Build.PageBody(out StackPanel menu, out ContentControl content);
		Build.NavItem(menu, Build.T("Genel Bakış", "Overview"), () => content.Content = Overview(), isChecked: true);
		_root.Children.Add(body);
		Content = _root;
		Background = Build.Res("Bg");
	}

	private Border BuildHeader()
	{
		var line1 = Build.Text(string.Join("  ·  ", new[] { Row.Job, Row.Nation, Row.Age > 0 ? Build.T($"{Row.Age} yaşında", $"{Row.Age} years old") : "" }.Where(s => s.Length > 0)), 13, new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
		line1.Margin = new Thickness(0, 4, 0, 0);
		var line2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
		if (_data.ClubsByAddress.TryGetValue(Row.ClubAddress, out ClubRow? club))
		{
			if (ImageLookup.Graphics.Club(club.Uid) is { } logo)
			{
				line2.Children.Add(new Image { Source = logo, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) });
			}
			var link = new Button { Content = club.Name, Style = Build.StyleOf("LinkButton"), FontSize = 14, FontWeight = FontWeights.SemiBold };
			link.Click += (_, _) => _nav.OpenClub(club.Address);
			line2.Children.Add(link);
		}
		else
		{
			line2.Children.Add(Build.Text(Build.T("Kulüpsüz", "Unemployed"), 13, Build.Res("Muted")));
		}
		var pills = new List<UIElement>();
		if (_f.TryGetValue("CA", out EditField? ca) && _f.TryGetValue("PA", out EditField? pa))
		{
			pills.Add(Build.Pill($"{ca.Number} / {pa.Number}", "CA / PA", Colors.Attribute((int)Math.Round(ca.Number / 10.0))));
		}
		if (Row.Wage > 0)
		{
			pills.Add(Build.Pill(Money.Format(Row.Wage, true), Build.T("Maaş", "Wage")));
		}
		if (Row.ContractEnd.Length > 0)
		{
			pills.Add(Build.Pill(Dates.Show(Row.ContractEnd), Build.T("Sözleşme bitişi", "Contract ends")));
		}
		return Build.Header(Build.Avatar(Icon, Row.Name, 74, round: true), Row.Name, line1, line2, pills);
	}

	private void RebuildHeader()
	{
		Border fresh = BuildHeader();
		DockPanel.SetDock(fresh, Dock.Top);
		_root.Children.Remove(_header);
		_root.Children.Insert(0, fresh);
		_header = fresh;
	}

	public StaffRow Row { get; }
	public string Key => "staff:" + Row.Address;
	public string Title => Row.Name;
	public ImageSource? Icon => ImageLookup.Graphics.Person(Row.Uid);

	public void RefreshValues()
	{
		foreach (EditField f in _f.Values)
		{
			f.Refresh();
		}
	}

	private UIElement Overview()
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
		IEnumerable<EditField> Group(string g) => _f.Values.Where(f => f.Def.GroupKey == g).OrderBy(f => f.Label, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(Loc.Instance.Language == "tr" ? "tr-TR" : "en-GB"), true));
		IEnumerable<EditField> Named(params string[] names) => names.Where(_f.ContainsKey).Select(n => _f[n]);
		var c0 = new StackPanel();
		c0.Children.Add(Build.Card(Build.T("Antrenörlük", "Coaching"), Build.Rows(Group("Coaching"))));
		c0.Children.Add(Build.Card(Build.T("Medikal", "Medical"), Build.Rows(Group("Medical"))));
		var c1 = new StackPanel();
		c1.Children.Add(Build.Card(Build.T("Zihinsel", "Mental"), Build.Rows(Group("Mental"))));
		c1.Children.Add(Build.Card(Build.T("Bilgi seviyesi", "Knowledge"), Build.Rows(Group("Knowledge"))));
		c1.Children.Add(Build.Card(Build.T("Kişilik", "Personality"), Build.Rows(Group("Personality"))));
		var c2 = new StackPanel();
		c2.Children.Add(Build.Card(Build.T("Yetenek ve itibar", "Ability & reputation"), Build.Rows(Named("CA", "PA", "Home Reputation", "Current Reputation", "World Reputation"))));
		c2.Children.Add(Build.Card(Build.T("Sözleşme", "Contract"), Build.Rows(Named("Club", "Job", "Wage", "Contract Expires"))));
		var actions = new WrapPanel { Margin = new Thickness(12, 0, 12, 10) };
		var freeze = new Button { Margin = new Thickness(0, 0, 6, 6) };
		void UpdateFreeze()
		{
			bool frozen = _data.Session.Freezer.IsFrozen(Row.Address);
			freeze.Content = frozen ? Build.T("❄ Donduruldu", "❄ Frozen") : Build.T("❄ Dondur", "❄ Freeze");
			freeze.IsEnabled = !AppSettings.Current.ReadOnlyMode || frozen;
			freeze.Style = frozen ? Build.StyleOf("AccentButton") : (Style)Application.Current.FindResource(typeof(Button));
		}
		UpdateFreeze();
		freeze.Click += (_, _) =>
		{
			if (_data.Session.Freezer.IsFrozen(Row.Address))
			{
				_data.Session.Freezer.Unfreeze(Row.Address);
				_nav.SetStatus(Build.T($"{Row.Name} artık dondurulmuş değil.", $"{Row.Name} unfrozen."));
			}
			else
			{
				int count = _data.Session.FreezeEntity(Row.Address, Row.Name, _data.StaffDef, StaffFreezeGroups);
				_nav.SetStatus(Build.T($"{Row.Name} donduruldu ({count} alan kilitlendi).", $"{Row.Name} frozen ({count} fields locked)."));
			}
			UpdateFreeze();
		};
		var rename = new Button { Content = Build.T("Adı değiştir", "Rename"), Margin = new Thickness(0, 0, 6, 6), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		rename.Click += (_, _) =>
		{
			var dialog = new RenameWindow(_data, Row.Address, Row.Name) { Owner = Window.GetWindow(this) };
			if (dialog.ShowDialog() == true)
			{
				RefreshValues();
				_data.Renamed(Row.Address, _data.Read(Row.Address, _data.StaffDef, "Name"));
				RebuildHeader();
				_nav.SetStatus(Build.T("Ad değiştirildi. Ctrl+Z geri alır.", "Renamed. Ctrl+Z undoes it."));
			}
		};
		var move = new Button { Content = Build.T("Kulübe taşı / İşe al", "Move / Hire"), Margin = new Thickness(0, 0, 6, 6), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		move.Click += (_, _) =>
		{
			var dialog = MoveWindow.ForStaff(_data, Row.Address, Row.Name);
			dialog.Owner = Window.GetWindow(this);
			if (dialog.ShowDialog() == true && dialog.Done is { } done)
			{
				_data.StaffMoved(Row.Address, done.NewClub);
				RefreshValues();
				RebuildHeader();
				_nav.SetStatus(done.Summary + Build.T(" Ctrl+Z geri alır.", " Ctrl+Z undoes it."));
			}
		};
		actions.Children.Add(freeze);
		actions.Children.Add(rename);
		actions.Children.Add(move);
		c2.Children.Add(Build.Card(Build.T("İşlemler", "Actions"), actions));
		c2.Children.Add(Build.Card(Build.T("Kişisel bilgiler", "Personal details"), Build.Rows(Named("First Name", "Last Name", "Common Name", "Unique ID", "Date of Birth", "Nation", "City of Birth"))));
		Grid.SetColumn(c1, 1);
		Grid.SetColumn(c2, 2);
		grid.Children.Add(c0);
		grid.Children.Add(c1);
		grid.Children.Add(c2);
		return Build.Scroll(grid);
	}
}

/// <summary>Start page: browse, filter and scout every player, club, staff member and frozen entity of the save.</summary>
public sealed class HomePage : UserControl, IPage
{
	private static readonly string[] Positions = { "", "GK", "DC", "DL", "DR", "WBL", "WBR", "DM", "MC", "ML", "MR", "AMC", "AML", "AMR", "ST" };

	private readonly GameData _data;
	private readonly INavigator _nav;
	private readonly TextBox _search;
	private readonly Border _filterBar;
	private readonly Border _staffFilterBar;
	private readonly ComboBox _pos;
	private readonly TextBox _ageMin, _ageMax, _caMin, _caMax, _paMin, _maxValM;
	private readonly CheckBox _freeOnly, _expiringOnly, _injuredOnly, _wonderkid, _shortlistOnly;
	private readonly Button _advFilterBtn;
	private readonly ScoutFilter _scoutFilter = new ScoutFilter();
	private readonly ComboBox _staffJob;
	private readonly TextBox _staffCaMin, _staffPaMin;
	private readonly CheckBox _staffFreeOnly;
	private readonly TextBlock _matchCount;
	private readonly TextBlock _staffMatchCount;
	private readonly RadioButton _frozenTab;
	private readonly ContentControl _host = new ContentControl();
	private readonly System.Windows.Threading.DispatcherTimer _debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
	private List<PlayerRow> _lastPlayers = new List<PlayerRow>();
	private string _mode = "players";

	public HomePage(GameData data, INavigator nav)
	{
		_data = data;
		_nav = nav;
		var root = new DockPanel { Margin = new Thickness(16, 12, 16, 0) };

		var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
		var leftHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		title.Children.Add(Build.Text(Build.T("Veritabanı", "Database"), 17, Build.Res("Text"), FontWeights.SemiBold));
		title.Children.Add(Build.Text($"{data.Players.Count:N0} {Build.T("oyuncu", "players")}  ·  {data.Staff.Count:N0} {Build.T("personel", "staff")}  ·  {data.Clubs.Count:N0} {Build.T("kulüp", "clubs")}  ·  {data.GameDate:dd.MM.yyyy}", 12, Build.Res("Muted")));
		leftHeader.Children.Add(title);
		if (AppSettings.Current.LastClubUid > 0 && data.Clubs.FirstOrDefault(c => c.Uid == AppSettings.Current.LastClubUid) is { } lastClub)
		{
			var clubBtn = new Button
			{
				Margin = new Thickness(14, 0, 0, 0),
				Padding = new Thickness(10, 4, 10, 4),
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = Build.T("Son açtığın kulübe hızlı git", "Jump to your last opened club")
			};
			var cbRow = new StackPanel { Orientation = Orientation.Horizontal };
			if (ImageLookup.Graphics.Club(lastClub.Uid) is { } logo)
			{
				cbRow.Children.Add(new Image { Source = logo, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
			}
			cbRow.Children.Add(Build.Text(lastClub.Name, 12, Build.Res("Text"), FontWeights.SemiBold));
			clubBtn.Content = cbRow;
			clubBtn.Click += (_, _) => _nav.OpenClub(lastClub.Address);
			leftHeader.Children.Add(clubBtn);
		}
		DockPanel.SetDock(leftHeader, Dock.Left);
		top.Children.Add(leftHeader);

		var segments = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		RadioButton? frozenBtn = null;
		foreach (var (key, label) in new[]
		{
			("players", Build.T("Oyuncular", "Players")),
			("staff", Build.T("Personel", "Staff")),
			("clubs", Build.T("Kulüpler", "Clubs")),
			("frozen", Build.T("❄ Dondurulanlar", "❄ Frozen"))
		})
		{
			var b = new RadioButton { Content = label, Style = Build.StyleOf("Segment"), GroupName = "home", IsChecked = key == "players" };
			b.Checked += (_, _) => { _mode = key; Apply(); };
			segments.Children.Add(b);
			if (key == "frozen")
			{
				frozenBtn = b;
			}
		}
		_frozenTab = frozenBtn!;
		_search = new TextBox { Style = Build.StyleOf("SearchBox"), Width = 280, Height = 30, Margin = new Thickness(12, 0, 0, 0), ToolTip = Build.T("İsim, kulüp veya ülke yaz (aksan fark etmez)", "Type name, club or nation (accents ignored)") };
		_search.TextChanged += (_, _) => ScheduleApply();
		_debounce.Tick += (_, _) => { _debounce.Stop(); Apply(); };
		var searchHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
		searchHost.Children.Add(_search);
		searchHost.Children.Add(new TextBlock { Text = "⌕", FontSize = 15, Foreground = Build.Res("Muted"), Margin = new Thickness(22, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
		var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		right.Children.Add(segments);
		right.Children.Add(searchHost);
		top.Children.Add(right);
		DockPanel.SetDock(top, Dock.Top);
		root.Children.Add(top);

		TextBox NumBox(string tip, double w = 38)
		{
			var tb = new TextBox { Width = w, Height = 24, FontSize = 12, Padding = new Thickness(3, 1, 3, 1), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = tip };
			tb.TextChanged += (_, _) => ScheduleApply();
			return tb;
		}
		TextBlock Lbl(string text) => new TextBlock { Text = text, Foreground = Build.Res("Muted"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };

		// Player scout filter bar
		_pos = new ComboBox { Width = 72, Height = 24, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		_pos.ItemsSource = Positions.Select(p => new KeyValuePair<string, string>(p, p.Length == 0 ? Build.T("Tümü", "All") : p)).ToList();
		_pos.DisplayMemberPath = "Value";
		_pos.SelectedValuePath = "Key";
		_pos.SelectedValue = "";
		_pos.SelectionChanged += (_, _) => Apply();

		_ageMin = NumBox(Build.T("En az yaş", "Min age"), 34);
		_ageMax = NumBox(Build.T("En fazla yaş", "Max age"), 34);
		_caMin = NumBox(Build.T("En az CA", "Min CA"), 38);
		_caMax = NumBox(Build.T("En fazla CA", "Max CA"), 38);
		_paMin = NumBox(Build.T("En az PA", "Min PA"), 38);
		_maxValM = NumBox(Build.T($"En fazla değer (milyon {Money.Current.Symbol})", $"Max value (million {Money.Current.Symbol})"), 44);

		_freeOnly = new CheckBox { Content = Build.T("Serbest", "Free"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(8, 0, 0, 0) };
		_expiringOnly = new CheckBox { Content = Build.T("Sözl. ≤1y", "Exp. ≤1y"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(5, 0, 0, 0), ToolTip = Build.T("Sözleşmesi 1 yıl içinde bitenler", "Contract expiring within 1 year") };
		_injuredOnly = new CheckBox { Content = Build.T("Sakat/Cezalı", "Inj/Ban"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(5, 0, 0, 0) };
		_wonderkid = new CheckBox { Content = Build.T("Wonderkid", "Wonderkid"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(5, 0, 0, 0), ToolTip = Build.T("Yaş ≤ 21 ve PA ≥ 150", "Age ≤ 21 and PA ≥ 150") };
		_shortlistOnly = new CheckBox { Content = Build.T("★ Kısa Liste", "★ Shortlist"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(5, 0, 0, 0), ToolTip = Build.T("Sağ tıkla yıldızladığın oyuncular", "Players starred via right-click") };
		foreach (CheckBox cb in new[] { _freeOnly, _expiringOnly, _injuredOnly, _wonderkid, _shortlistOnly })
		{
			cb.Checked += (_, _) => Apply();
			cb.Unchecked += (_, _) => Apply();
		}

		_advFilterBtn = new Button { Content = Build.T("🔍 Gelişmiş", "🔍 Advanced"), Padding = new Thickness(8, 2, 8, 2), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), ToolTip = Build.T("Genie Scout filtreleri (PA-CA, Rol %, Profesyonellik, İstikrar, Sakatlık Eğilimi, Hız, Maaş…)", "Genie Scout filters (PA-CA, Role %, Professionalism, Consistency, Injury Proneness, Pace, Wage…)") };
		_advFilterBtn.Click += (_, _) =>
		{
			var win = new ScoutFilterWindow(_scoutFilter) { Owner = Window.GetWindow(this) };
			if (win.ShowDialog() == true)
			{
				_advFilterBtn.Style = _scoutFilter.IsActive ? Build.StyleOf("AccentButton") : (Style)Application.Current.FindResource(typeof(Button));
				Apply();
			}
		};

		var colBtn = new Button { Content = Build.T("Sütunlar", "Columns"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(7, 2, 7, 2), FontSize = 11.5, Margin = new Thickness(4, 0, 0, 0), ToolTip = Build.T("Tabloda gösterilecek Genie Scout sütunlarını seç", "Choose extra Genie Scout columns") };
		colBtn.Click += (_, _) =>
		{
			var menu = new ContextMenu();
			var opts = new[]
			{
				("Growth", Build.T("PA − CA (Gelişim Payı)", "PA − CA (Growth)")),
				("Role", Build.T("En İyi Rol (%)", "Best Role (%)")),
				("Pro", Build.T("Profesyonellik", "Professionalism")),
				("Cons", Build.T("İstikrar", "Consistency")),
				("Imp", Build.T("Önemli Maçlar", "Important Matches")),
				("Inj", Build.T("Sakatlık Eğilimi", "Injury Proneness")),
				("Det", Build.T("Kararlılık", "Determination"))
			};
			var active = new HashSet<string>(AppSettings.Current.ExtraColumns);
			foreach (var (key, label) in opts)
			{
				string k = key;
				var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = active.Contains(k) };
				mi.Click += (_, _) =>
				{
					var set = new HashSet<string>(AppSettings.Current.ExtraColumns);
					if (mi.IsChecked) set.Add(k); else set.Remove(k);
					AppSettings.Current.ExtraColumns = set.ToList();
					AppSettings.Current.Save();
					Apply();
				};
				menu.Items.Add(mi);
			}
			menu.PlacementTarget = colBtn;
			menu.IsOpen = true;
		};

		var clear = new Button { Content = Build.T("Temizle", "Clear"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(7, 2, 7, 2), FontSize = 11.5, Margin = new Thickness(4, 0, 0, 0) };
		clear.Click += (_, _) =>
		{
			_pos.SelectedValue = "";
			_ageMin.Text = _ageMax.Text = _caMin.Text = _caMax.Text = _paMin.Text = _maxValM.Text = "";
			_freeOnly.IsChecked = _expiringOnly.IsChecked = _injuredOnly.IsChecked = _wonderkid.IsChecked = _shortlistOnly.IsChecked = false;
			_scoutFilter.Clear();
			_advFilterBtn.Style = (Style)Application.Current.FindResource(typeof(Button));
			Apply();
		};
		var exportCsv = new Button { Content = Build.T("CSV", "CSV"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(7, 2, 7, 2), FontSize = 11.5, Margin = new Thickness(4, 0, 0, 0), ToolTip = Build.T("Filtrelenmiş listeyi CSV olarak dışa aktar", "Export filtered list to CSV") };
		exportCsv.Click += (_, _) => ExportPlayersCsv();

		_matchCount = Build.Text("", 11.5, Build.Res("Faint"));
		_matchCount.Margin = new Thickness(8, 0, 4, 0);

		var filterDock = new DockPanel();
		DockPanel.SetDock(_matchCount, Dock.Right);
		DockPanel.SetDock(exportCsv, Dock.Right);
		DockPanel.SetDock(colBtn, Dock.Right);
		filterDock.Children.Add(_matchCount);
		filterDock.Children.Add(exportCsv);
		filterDock.Children.Add(colBtn);
		var filters = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		var posLbl = Lbl(Build.T("Mevki", "Pos"));
		posLbl.Margin = new Thickness(0, 0, 4, 0);
		filters.Children.Add(posLbl);
		filters.Children.Add(_pos);
		filters.Children.Add(Lbl(Build.T("Yaş", "Age")));
		filters.Children.Add(_ageMin);
		filters.Children.Add(new TextBlock { Text = "–", Foreground = Build.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
		filters.Children.Add(_ageMax);
		filters.Children.Add(Lbl("CA"));
		filters.Children.Add(_caMin);
		filters.Children.Add(new TextBlock { Text = "–", Foreground = Build.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
		filters.Children.Add(_caMax);
		filters.Children.Add(Lbl("PA ≥"));
		filters.Children.Add(_paMin);
		filters.Children.Add(Lbl(Build.T($"≤M{Money.Current.Symbol}", $"≤M{Money.Current.Symbol}")));
		filters.Children.Add(_maxValM);
		filters.Children.Add(_freeOnly);
		filters.Children.Add(_expiringOnly);
		filters.Children.Add(_injuredOnly);
		filters.Children.Add(_wonderkid);
		filters.Children.Add(_shortlistOnly);
		filters.Children.Add(_advFilterBtn);
		filters.Children.Add(clear);
		filterDock.Children.Add(filters);

		_filterBar = new Border
		{
			Background = Build.Res("Panel"),
			BorderBrush = Build.Res("StrokeSoft"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(5),
			Padding = new Thickness(10, 6, 10, 6),
			Margin = new Thickness(0, 0, 0, 10),
			Child = filterDock
		};
		DockPanel.SetDock(_filterBar, Dock.Top);
		root.Children.Add(_filterBar);

		// Staff filter bar
		var jobs = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", Build.T("Tüm görevler", "All jobs")) };
		jobs.AddRange(data.Staff.Select(s => s.Job).Where(j => j.Length > 0).Distinct().OrderBy(j => j).Select(j => new KeyValuePair<string, string>(j, j)));
		_staffJob = new ComboBox { Width = 180, Height = 24, FontSize = 12, ItemsSource = jobs, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = "" };
		_staffJob.SelectionChanged += (_, _) => Apply();
		_staffCaMin = NumBox(Build.T("En az CA", "Min CA"), 42);
		_staffPaMin = NumBox(Build.T("En az PA", "Min PA"), 42);
		_staffFreeOnly = new CheckBox { Content = Build.T("Boşta (Kulüpsüz)", "Unemployed"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(10, 0, 0, 0) };
		_staffFreeOnly.Checked += (_, _) => Apply();
		_staffFreeOnly.Unchecked += (_, _) => Apply();
		var clearStaff = new Button { Content = Build.T("Temizle", "Clear"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(7, 2, 7, 2), FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0) };
		clearStaff.Click += (_, _) => { _staffJob.SelectedValue = ""; _staffCaMin.Text = _staffPaMin.Text = ""; _staffFreeOnly.IsChecked = false; Apply(); };
		_staffMatchCount = Build.Text("", 11.5, Build.Res("Faint"));

		var staffDock = new DockPanel();
		DockPanel.SetDock(_staffMatchCount, Dock.Right);
		staffDock.Children.Add(_staffMatchCount);
		var staffFilters = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		var jobLbl = Lbl(Build.T("Görev", "Job"));
		jobLbl.Margin = new Thickness(0, 0, 4, 0);
		staffFilters.Children.Add(jobLbl);
		staffFilters.Children.Add(_staffJob);
		staffFilters.Children.Add(Lbl("CA ≥"));
		staffFilters.Children.Add(_staffCaMin);
		staffFilters.Children.Add(Lbl("PA ≥"));
		staffFilters.Children.Add(_staffPaMin);
		staffFilters.Children.Add(_staffFreeOnly);
		staffFilters.Children.Add(clearStaff);
		staffDock.Children.Add(staffFilters);
		_staffFilterBar = new Border
		{
			Background = Build.Res("Panel"),
			BorderBrush = Build.Res("StrokeSoft"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(5),
			Padding = new Thickness(10, 6, 10, 6),
			Margin = new Thickness(0, 0, 0, 10),
			Visibility = Visibility.Collapsed,
			Child = staffDock
		};
		DockPanel.SetDock(_staffFilterBar, Dock.Top);
		root.Children.Add(_staffFilterBar);

		root.Children.Add(new Border { Style = Build.StyleOf("CardBorder"), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(3), Child = _host });
		Content = root;
		Background = Build.Res("Bg");
		_data.Session.Freezer.Changed += OnFreezerChanged;
		Unloaded += (_, _) => _data.Session.Freezer.Changed -= OnFreezerChanged;
		Apply();
	}

	public string Key => "home";
	public string Title => Build.T("Ana Sayfa", "Home");
	public ImageSource? Icon => null;

	public void RefreshValues()
	{
	}

	public void ShowFrozen()
	{
		_frozenTab.IsChecked = true;
		_mode = "frozen";
		Apply();
	}

	private void OnFreezerChanged(object? sender, EventArgs e)
	{
		if (_mode == "frozen")
		{
			Dispatcher.Invoke(Apply);
		}
	}

	private void ScheduleApply()
	{
		_debounce.Stop();
		_debounce.Start();
	}

	private static int? ParseInt(TextBox box) => int.TryParse(box.Text.Trim(), out int v) ? v : null;

	private static double? ParseDouble(TextBox box) => double.TryParse(box.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;

	private void Apply()
	{
		_filterBar.Visibility = _mode == "players" ? Visibility.Visible : Visibility.Collapsed;
		_staffFilterBar.Visibility = _mode == "staff" ? Visibility.Visible : Visibility.Collapsed;
		string q = GameData.Normalize(_search.Text.Trim());
		switch (_mode)
		{
		case "clubs":
			_host.Content = ClubPage.ClubGrid(_data.Clubs.Where(c => c.SquadSize > 0 && (q.Length == 0 || c.Search.Contains(q))).OrderByDescending(c => c.AverageCA), _nav);
			break;
		case "staff":
		{
			string job = _staffJob.SelectedValue as string ?? "";
			int? caMin = ParseInt(_staffCaMin), paMin = ParseInt(_staffPaMin);
			bool freeOnly = _staffFreeOnly.IsChecked == true;
			IEnumerable<StaffRow> sq = _data.Staff;
			if (q.Length > 0) sq = sq.Where(s => s.Search.Contains(q));
			if (job.Length > 0) sq = sq.Where(s => s.Job == job);
			if (caMin.HasValue) sq = sq.Where(s => s.CA >= caMin.Value);
			if (paMin.HasValue) sq = sq.Where(s => s.PA >= paMin.Value);
			if (freeOnly) sq = sq.Where(s => s.ClubAddress == 0);
			List<StaffRow> list = sq.OrderByDescending(s => s.CA).ThenBy(s => s.Club.Length == 0).ToList();
			_staffMatchCount.Text = $"{list.Count:N0} / {_data.Staff.Count:N0}";
			_host.Content = ClubPage.StaffGrid(_data, list, _nav, showClub: true);
			break;
		}
		case "frozen":
			_host.Content = FrozenView(q);
			break;
		default:
		{
			string pos = _pos.SelectedValue as string ?? "";
			int? ageMin = ParseInt(_ageMin), ageMax = ParseInt(_ageMax), caMin = ParseInt(_caMin), caMax = ParseInt(_caMax), paMin = ParseInt(_paMin);
			double? maxValM = ParseDouble(_maxValM);
			long? maxValGbp = maxValM.HasValue ? (long)Math.Round(Money.FromDisplay(maxValM.Value * 1_000_000.0, false)) : null;
			bool freeOnly = _freeOnly.IsChecked == true;
			bool expiringOnly = _expiringOnly.IsChecked == true;
			bool injuredOnly = _injuredOnly.IsChecked == true;
			bool wonderkid = _wonderkid.IsChecked == true;
			bool shortlistOnly = _shortlistOnly.IsChecked == true;
			HashSet<uint>? shortlist = shortlistOnly ? new HashSet<uint>(AppSettings.Current.ShortlistUids) : null;
			long? maxWageGbp = _scoutFilter.MaxWageDisplay.HasValue ? (long)Math.Round(Money.FromDisplay(_scoutFilter.MaxWageDisplay.Value, true)) : null;
			DateTime expireCutoff = _data.GameDate.AddYears(1);
			IEnumerable<PlayerRow> query = _data.Players;
			if (q.Length > 0) query = query.Where(p => p.Search.Contains(q));
			if (pos.Length > 0) query = query.Where(p => p.Positions.Split(' ', '/', ',').Any(tok => tok == pos));
			if (ageMin.HasValue) query = query.Where(p => p.Age >= ageMin.Value);
			if (ageMax.HasValue) query = query.Where(p => p.Age > 0 && p.Age <= ageMax.Value);
			if (caMin.HasValue) query = query.Where(p => p.CA >= caMin.Value);
			if (caMax.HasValue) query = query.Where(p => p.CA <= caMax.Value);
			if (paMin.HasValue) query = query.Where(p => p.PA >= paMin.Value);
			if (maxValGbp.HasValue) query = query.Where(p => p.Value <= maxValGbp.Value);
			if (freeOnly) query = query.Where(p => p.ClubAddress == 0);
			if (expiringOnly) query = query.Where(p => p.ClubAddress != 0 && p.ContractDate is { } cd && cd <= expireCutoff);
			if (injuredOnly) query = query.Where(p => p.Status.Length > 0);
			if (wonderkid) query = query.Where(p => p.Age > 0 && p.Age <= 21 && p.PA >= 150);
			if (shortlist != null) query = query.Where(p => shortlist.Contains(p.Uid));
			if (_scoutFilter.IsActive) query = query.Where(p => _scoutFilter.Matches(p, maxWageGbp));
			_lastPlayers = query.OrderByDescending(p => p.CA).ToList();
			_matchCount.Text = $"{_lastPlayers.Count:N0} / {_data.Players.Count:N0}";
			_host.Content = ClubPage.PlayerGrid(_data, _lastPlayers, _nav, showClub: true);
			break;
		}
		}
	}

	private void ExportPlayersCsv()
	{
		if (_lastPlayers.Count == 0)
		{
			return;
		}
		var dlg = new Microsoft.Win32.SaveFileDialog
		{
			Filter = "CSV (*.csv)|*.csv",
			FileName = $"squadeditor_scout_{_data.GameDate:yyyyMMdd}.csv"
		};
		if (dlg.ShowDialog() != true)
		{
			return;
		}
		static string Esc(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
		var lines = new List<string>(_lastPlayers.Count + 1)
		{
			$"UID,Name,Positions,Age,Club,Nation,CA,PA,PA-CA,BestRole,BestRating,Pro,Consistency,ImportantMatches,InjuryProneness,Determination,Value ({Money.Current.Code}),Wage ({Money.Current.Code}{Money.WageSuffix}),ContractEnd,Status"
		};
		foreach (PlayerRow p in _lastPlayers)
		{
			lines.Add($"{p.Uid},{Esc(p.Name)},{Esc(p.Positions)},{p.Age},{Esc(p.Club)},{Esc(p.Nation)},{p.CA},{p.PA},{p.Growth},{Esc(p.BestRole)},{p.BestRating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},{p.Professionalism},{p.Consistency},{p.ImportantMatches},{p.InjuryProneness},{p.Determination},{Esc(Money.Format(p.Value, false))},{Esc(Money.Format(p.Wage, true))},{Esc(p.ContractEnd)},{Esc(p.Status)}");
		}
		System.IO.File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
		_nav.SetStatus(Build.T($"✓ {_lastPlayers.Count:N0} oyuncu dışa aktarıldı: {System.IO.Path.GetFileName(dlg.FileName)}",
			$"✓ Exported {_lastPlayers.Count:N0} players to {System.IO.Path.GetFileName(dlg.FileName)}"));
	}

	private UIElement FrozenView(string q)
	{
		var entities = _data.Session.Freezer.Entities
			.Where(e => q.Length == 0 || GameData.Normalize(e.Label).Contains(q))
			.ToList();
		var panel = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
		var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
		var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };
		var allowInc = new CheckBox
		{
			Content = Build.T("Özellikler artabilir ama düşmesin", "Attributes can increase (never drop)"),
			Style = Build.StyleOf("FilterChip"),
			IsChecked = _data.Session.Freezer.AllowIncrease,
			Margin = new Thickness(0, 0, 8, 0)
		};
		allowInc.Checked += (_, _) => { _data.Session.Freezer.AllowIncrease = AppSettings.Current.FreezeAllowIncrease = true; AppSettings.Current.Save(); };
		allowInc.Unchecked += (_, _) => { _data.Session.Freezer.AllowIncrease = AppSettings.Current.FreezeAllowIncrease = false; AppSettings.Current.Save(); };
		rightBtns.Children.Add(allowInc);
		if (entities.Count > 0)
		{
			var clearAll = new Button { Content = Build.T("Tüm dondurmaları kaldır", "Unfreeze all") };
			clearAll.Click += (_, _) =>
			{
				_data.Session.Freezer.Clear();
				_nav.SetStatus(Build.T("Tüm dondurmalar kaldırıldı.", "All frozen records unfrozen."));
			};
			rightBtns.Children.Add(clearAll);
		}
		DockPanel.SetDock(rightBtns, Dock.Right);
		bar.Children.Add(rightBtns);
		bar.Children.Add(Build.Text(entities.Count == 0
			? Build.T("Henüz dondurulmuş kayıt yok. Bir profil açıp \"❄ Dondur\" diyebilir ya da listede sağ tıkla seçilenleri dondurabilirsin.",
				"No frozen records yet. Open a profile and click \"❄ Freeze\", or right-click a selection in any list.")
			: Build.T($"{entities.Count} kayıt dondurulmuş (kapanışta ve F5'te korunur).", $"{entities.Count} records frozen (persisted across reloads and restarts)."), 12.5, Build.Res("Muted")));
		DockPanel.SetDock(bar, Dock.Top);
		panel.Children.Add(bar);

		var list = new StackPanel();
		foreach (FrozenEntity entity in entities)
		{
			FrozenEntity current = entity;
			var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
			var unfreeze = new Button { Content = Build.T("Kaldır", "Unfreeze"), Padding = new Thickness(10, 3, 10, 3), FontSize = 12 };
			unfreeze.Click += (_, _) =>
			{
				_data.Session.Freezer.Unfreeze(current.Entity);
				_nav.SetStatus(Build.T($"{current.Label} artık dondurulmuş değil.", $"{current.Label} unfrozen."));
			};
			var open = new Button { Content = Build.T("Aç", "Open"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(10, 3, 10, 3), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
			open.Click += (_, _) =>
			{
				if (_data.Staff.Any(s => s.Address == current.Entity))
				{
					_nav.OpenStaff(current.Entity);
				}
				else
				{
					_nav.OpenPlayer(current.Entity);
				}
			};
			DockPanel.SetDock(unfreeze, Dock.Right);
			DockPanel.SetDock(open, Dock.Right);
			row.Children.Add(unfreeze);
			row.Children.Add(open);
			row.Children.Add(Build.Text($"{current.Label}   ·   {current.FieldCount} {Build.T("alan", "fields")}", 13));
			list.Children.Add(new Border
			{
				Background = Build.Res("Panel"),
				BorderBrush = Build.Res("StrokeSoft"),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(5),
				Padding = new Thickness(12, 7, 10, 7),
				Margin = new Thickness(0, 0, 0, 4),
				Child = row
			});
		}
		panel.Children.Add(Build.Scroll(list));
		return panel;
	}
}
