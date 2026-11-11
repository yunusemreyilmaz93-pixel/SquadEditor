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

	public uint Uid => _club.Uid;
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
		return Build.Header(Build.Avatar(Icon, _club.Name, 92, round: false), _club.Name, line1, line2, pills);
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
		var hint = Build.Text(Build.T("Çift tık: oyuncuyu aç  ·  Sağ tık: seçilenlere ön ayar uygula", "Double-click: open player  ·  Right-click: apply a preset to the selection"), 11.5, Build.Res("Faint"));
		hint.Margin = new Thickness(16, 0, 0, 0);
		bar.Children.Add(hint);
		var dock = new DockPanel();
		DockPanel.SetDock(bar, Dock.Top);
		dock.Children.Add(bar);
		dock.Children.Add(host);
		string? first = _team ?? teams.FirstOrDefault();
		RadioButton? pick = bar.Children.OfType<RadioButton>().FirstOrDefault(r => first != null && (r.Content as string ?? "").StartsWith(first + " ("));
		(pick ?? everyone).IsChecked = true;
		return dock;
	}

	private UIElement StaffList()
	{
		var staff = _data.StaffByClub[_club.Address].OrderBy(s => s.Job).ThenBy(s => s.Name);
		return new Border { Style = Build.StyleOf("CardBorder"), Padding = new Thickness(4), Child = StaffGrid(staff, _nav, showClub: false) };
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
		return grid;
	}

	public static DataGrid StaffGrid(IEnumerable<StaffRow> staff, INavigator nav, bool showClub)
	{
		var grid = new DataGrid { ItemsSource = staff.ToList(), RowHeight = AppSettings.Current.Compact ? 27 : 34 };
		grid.Columns.Add(Build.TemplateColumn(Build.T("Personel", "Staff"), "PlayerNameCell", "Name", 250));
		grid.Columns.Add(Build.TextColumn(Build.T("Görev", "Job"), "Job", 190));
		grid.Columns.Add(Build.TextColumn(Build.T("Yaş", "Age"), "Age", 50));
		if (showClub)
		{
			grid.Columns.Add(Build.TemplateColumn(Build.T("Kulüp", "Club"), "PlayerClubCell", "Club", 190));
		}
		grid.Columns.Add(Build.TextColumn(Build.T("Uyruk", "Nation"), "Nation", 130));
		grid.Columns.Add(MoneyColumn(Build.T("Maaş", "Wage"), "Wage", true, 130));
		grid.Columns.Add(Build.TextColumn(Build.T("Sözleşme", "Contract"), "ContractEnd", 95));
		grid.MouseDoubleClick += (_, _) =>
		{
			if (grid.SelectedItem is StaffRow row)
			{
				nav.OpenStaff(row.Address);
			}
		};
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
	private readonly Dictionary<string, EditField> _f;

	private readonly GameData _data;
	private readonly INavigator _nav;

	public StaffPage(GameData data, StaffRow row, INavigator nav)
	{
		_data = data;
		_nav = nav;
		string lang = Loc.Instance.Language;
		Row = row;
		_f = data.StaffDef.Fields.Where(f => f.GroupKey != "Internal" && f.Concat.Count == 0)
			.ToDictionary(f => f.Name, f => new EditField(data, row.Address, f, data.Layout.Text(lang, f.Name)));
		Loaded += (_, _) => FieldBox.EntityLabel = row.Name;

		var root = new DockPanel();
		var line1 = Build.Text(string.Join("  ·  ", new[] { row.Job, row.Nation, row.Age > 0 ? Build.T($"{row.Age} yaşında", $"{row.Age} years old") : "" }.Where(s => s.Length > 0)), 13, new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
		line1.Margin = new Thickness(0, 4, 0, 0);
		var line2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
		if (data.ClubsByAddress.TryGetValue(row.ClubAddress, out ClubRow? club))
		{
			if (ImageLookup.Graphics.Club(club.Uid) is { } logo)
			{
				line2.Children.Add(new Image { Source = logo, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) });
			}
			var link = new Button { Content = club.Name, Style = Build.StyleOf("LinkButton"), FontSize = 14, FontWeight = FontWeights.SemiBold };
			link.Click += (_, _) => nav.OpenClub(club.Address);
			line2.Children.Add(link);
		}
		var pills = new List<UIElement>();
		if (row.Wage > 0)
		{
			pills.Add(Build.Pill(Money.Format(row.Wage, true), Build.T("Maaş", "Wage")));
		}
		if (row.ContractEnd.Length > 0)
		{
			pills.Add(Build.Pill(Dates.Show(row.ContractEnd), Build.T("Sözleşme bitişi", "Contract ends")));
		}
		Border header = Build.Header(Build.Avatar(Icon, row.Name, 92, round: true), row.Name, line1, line2, pills);
		DockPanel.SetDock(header, Dock.Top);
		root.Children.Add(header);
		Grid body = Build.PageBody(out StackPanel menu, out ContentControl content);
		Build.NavItem(menu, Build.T("Genel Bakış", "Overview"), () => content.Content = Overview(), isChecked: true);
		root.Children.Add(body);
		Content = root;
		Background = Build.Res("Bg");
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
		var rename = new Button { Content = Build.T("Adı değiştir", "Rename"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0, 14, 12), IsEnabled = !AppSettings.Current.ReadOnlyMode };
		rename.Click += (_, _) =>
		{
			var dialog = new RenameWindow(_data, Row.Address, Row.Name) { Owner = Window.GetWindow(this) };
			if (dialog.ShowDialog() == true)
			{
				RefreshValues();
				_nav.SetStatus(Build.T("Ad değiştirildi. Ctrl+Z geri alır.", "Renamed. Ctrl+Z undoes it."));
			}
		};
		c2.Children.Add(Build.Card(Build.T("İşlemler", "Actions"), rename));
		c2.Children.Add(Build.Card(Build.T("Kişisel bilgiler", "Personal details"), Build.Rows(Named("First Name", "Last Name", "Common Name", "Unique ID", "Date of Birth", "Nation", "City of Birth"))));
		Grid.SetColumn(c1, 1);
		Grid.SetColumn(c2, 2);
		grid.Children.Add(c0);
		grid.Children.Add(c1);
		grid.Children.Add(c2);
		return Build.Scroll(grid);
	}
}

/// <summary>Start page: browse and search every player, club and staff member of the save.</summary>
public sealed class HomePage : UserControl, IPage
{
	private readonly GameData _data;
	private readonly INavigator _nav;
	private readonly TextBox _search;
	private readonly ContentControl _host = new ContentControl();
	private readonly System.Windows.Threading.DispatcherTimer _debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
	private string _mode = "players";

	public HomePage(GameData data, INavigator nav)
	{
		_data = data;
		_nav = nav;
		var root = new DockPanel { Margin = new Thickness(24, 20, 24, 0) };

		var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
		var title = new StackPanel();
		title.Children.Add(new TextBlock { Text = Build.T("Veritabanı", "Database"), Style = Build.StyleOf("H1") });
		title.Children.Add(Build.Text($"{data.Players.Count:N0} {Build.T("oyuncu", "players")}  ·  {data.Staff.Count:N0} {Build.T("personel", "staff")}  ·  {data.Clubs.Count:N0} {Build.T("kulüp", "clubs")}  ·  {Build.T("Oyun tarihi", "Game date")} {data.GameDate:dd.MM.yyyy}", 13, Build.Res("Muted")));
		DockPanel.SetDock(title, Dock.Left);
		top.Children.Add(title);

		var segments = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
		foreach (var (key, label) in new[] { ("players", Build.T("Oyuncular", "Players")), ("staff", Build.T("Personel", "Staff")), ("clubs", Build.T("Kulüpler", "Clubs")) })
		{
			var b = new RadioButton { Content = label, Style = Build.StyleOf("Segment"), GroupName = "home", IsChecked = key == "players" };
			b.Checked += (_, _) => { _mode = key; Apply(); };
			segments.Children.Add(b);
		}
		_search = new TextBox { Style = Build.StyleOf("SearchBox"), Width = 360, Height = 34, Margin = new Thickness(16, 0, 0, 0), ToolTip = Build.T("İsim yaz (aksan ve büyük harf fark etmez)", "Type a name (accents and case are ignored)") };
		_search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
		_debounce.Tick += (_, _) => { _debounce.Stop(); Apply(); };
		var searchHost = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
		searchHost.Children.Add(_search);
		searchHost.Children.Add(new TextBlock { Text = "⌕", FontSize = 16, Foreground = Build.Res("Muted"), Margin = new Thickness(28, 0, 0, 2), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
		var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		right.Children.Add(segments);
		right.Children.Add(searchHost);
		top.Children.Add(right);
		DockPanel.SetDock(top, Dock.Top);
		root.Children.Add(top);

		root.Children.Add(new Border { Style = Build.StyleOf("CardBorder"), Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(4), Child = _host });
		Content = root;
		Background = Build.Res("Bg");
		Apply();
	}

	public string Key => "home";
	public string Title => Build.T("Ana Sayfa", "Home");
	public ImageSource? Icon => null;

	public void RefreshValues()
	{
	}

	private void Apply()
	{
		string q = GameData.Normalize(_search.Text.Trim());
		switch (_mode)
		{
		case "clubs":
			_host.Content = ClubPage.ClubGrid(_data.Clubs.Where(c => c.SquadSize > 0 && (q.Length == 0 || c.Search.Contains(q))).OrderByDescending(c => c.AverageCA), _nav);
			break;
		case "staff":
			_host.Content = ClubPage.StaffGrid(_data.Staff.Where(s => q.Length == 0 || s.Search.Contains(q)).OrderBy(s => s.Club.Length == 0).ThenBy(s => s.Club).ThenBy(s => s.Job), _nav, showClub: true);
			break;
		default:
			_host.Content = ClubPage.PlayerGrid(_data, _data.Players.Where(p => q.Length == 0 || p.Search.Contains(q)).OrderByDescending(p => p.CA), _nav, showClub: true);
			break;
		}
	}
}
