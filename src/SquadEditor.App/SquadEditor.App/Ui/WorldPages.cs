using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

/// <summary>Nation editor page: Youth Rating, State of Development, Game Importance, Economic Factor + quick presets.</summary>
public sealed class NationPage : UserControl, IPage
{
	private readonly GameData _data;
	private readonly NationRow _row;
	private readonly INavigator _nav;
	private readonly List<EditField> _fields;
	private readonly DockPanel _root = new DockPanel();
	private Border _header;

	public NationPage(GameData data, NationRow row, INavigator nav)
	{
		_data = data;
		_row = row;
		_nav = nav;
		string lang = Loc.Instance.Language;
		_fields = data.NationDef!.Fields.Select(f => new EditField(data, row.Address, f, data.Layout.Text(lang, f.Name))).ToList();
		Loaded += (_, _) => FieldBox.EntityLabel = row.Name;
		EditField.Written += OnWritten;
		Unloaded += (_, _) => EditField.Written -= OnWritten;

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);

		var grid = new Grid { Margin = new Thickness(16, 14, 16, 14) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });

		var left = new StackPanel();
		left.Children.Add(Build.Card(Build.T("Gelişim ve Altyapı", "Development & Youth"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Development"))));
		left.Children.Add(Build.Card(Build.T("Genel Bilgiler", "General Info"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "General"))));

		var right = new StackPanel();
		var quick = new WrapPanel { Margin = new Thickness(10, 0, 10, 12) };
		bool canWrite = !AppSettings.Current.ReadOnlyMode;
		void QBtn(string label, Action act)
		{
			var b = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5), FontSize = 12, IsEnabled = canWrite };
			b.Click += (_, _) => { act(); RefreshValues(); RebuildHeader(); };
			quick.Children.Add(b);
		}
		QBtn(Build.T("🌟 Altın Jenerasyon (Gençlik 200)", "🌟 Golden Generation (Youth 200)"), () =>
		{
			using (_data.Session.History.BeginBatch($"{_row.Name}: Youth 200"))
			{
				_data.Session.WriteField(_row.Address, F("Youth Rating").Def, "200", _row.Name);
				_data.Session.WriteField(_row.Address, F("State of Development").Def, "1", _row.Name);
				_data.Session.WriteField(_row.Address, F("Game Importance").Def, "5", _row.Name);
				_data.Session.WriteField(_row.Address, F("Economic Factor").Def, "20", _row.Name);
			}
			_nav.SetStatus(Build.F("✓ {0}: Gençlik Reytingi 200, Gelişmiş, Hayati, Ekonomi 20 yapıldı. Ctrl+Z geri alır.", "✓ {0}: Youth Rating 200, Developed, Crucial, Economy 20. Ctrl+Z undoes it.", _row.Name));
		});
		QBtn(Build.T("Gençlik +20", "Youth +20"), () =>
		{
			int next = Math.Min(200, F("Youth Rating").Number + 20);
			_data.Session.WriteField(_row.Address, F("Youth Rating").Def, next.ToString(), _row.Name);
			_nav.SetStatus(Build.F("✓ {0} Gençlik Reytingi → {1}", "✓ {0} Youth Rating → {1}", _row.Name, next));
		});
		right.Children.Add(Build.Card(Build.T("Hızlı İşlemler", "Quick Actions"), quick));

		var nationClubs = _data.Clubs
			.Where(c => c.SquadSize > 0 && c.Nation == _row.Name)
			.OrderByDescending(c => c.AverageCA)
			.Take(25)
			.ToList();
		if (nationClubs.Count > 0)
		{
			var clubGrid = ClubPage.ClubGrid(nationClubs, _nav);
			clubGrid.MaxHeight = 440;
			right.Children.Add(Build.Card(Build.F("Ülkedeki En İyi Kulüpler ({0})", "Top Clubs in Nation ({0})", nationClubs.Count), clubGrid));
		}

		System.Windows.Controls.Grid.SetColumn(right, 1);
		grid.Children.Add(left);
		grid.Children.Add(right);
		_root.Children.Add(Build.Scroll(grid));
		Content = _root;
		Background = Build.Res("Bg");
	}

	public string Key => "nation:" + _row.Address;
	public string Title => _row.Name;
	public ImageSource? Icon => null;

	public void RefreshValues()
	{
		foreach (EditField f in _fields) f.Refresh();
	}

	private EditField F(string name) => _fields.First(f => f.Key == name);

	private void OnWritten(object? sender, EditField field)
	{
		if (field.Entity == _row.Address) RebuildHeader();
	}

	private Border BuildHeader()
	{
		var line1 = Build.Text($"{_row.Code}  ·  UID {_row.Uid}", 13, new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
		int yr = F("Youth Rating").Number;
		var pills = new List<UIElement>
		{
			Build.Pill(yr.ToString(), Build.T("Gençlik Reytingi", "Youth Rating"), Colors.Attribute((int)Math.Round(yr / 10.0))),
			Build.Pill(F("Economic Factor").Number.ToString(), Build.T("Ekonomi", "Economy")),
			Build.Pill(F("State of Development").Display, Build.T("Gelişmişlik", "Development"))
		};
		return Build.Header(Build.Avatar(null, _row.Code.Length > 0 ? _row.Code : _row.Name, 68, round: false), _row.Name, line1, null, pills);
	}

	private void RebuildHeader()
	{
		Border fresh = BuildHeader();
		DockPanel.SetDock(fresh, Dock.Top);
		_root.Children.Remove(_header);
		_root.Children.Insert(0, fresh);
		_header = fresh;
	}

	public static DataGrid Grid(IEnumerable<NationRow> nations, INavigator nav)
	{
		var grid = new DataGrid { ItemsSource = nations.ToList(), RowHeight = AppSettings.Current.Compact ? 27 : 32 };
		grid.Columns.Add(Build.TextColumn(Build.T("Ülke", "Nation"), "Name", 220));
		grid.Columns.Add(Build.TextColumn(Build.T("Kod", "Code"), "Code", 68));
		grid.Columns.Add(Build.TextColumn(Build.T("Gençlik (1-200)", "Youth (1-200)"), "YouthRating", 115));
		grid.Columns.Add(Build.TextColumn(Build.T("Gelişmişlik", "Development"), "Development", 130));
		grid.Columns.Add(Build.TextColumn(Build.T("Futbolun Önemi", "Importance"), "Importance", 130));
		grid.Columns.Add(Build.TextColumn(Build.T("Ekonomi (1-20)", "Economy (1-20)"), "EconomicFactor", 115));
		grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is NationRow r) nav.OpenNation(r.Address); };
		grid.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && grid.SelectedItem is NationRow r) { nav.OpenNation(r.Address); e.Handled = true; } };
		return grid;
	}
}

/// <summary>Competition editor page: Reputation (1-200 & 1-10000) + quick boost.</summary>
public sealed class CompPage : UserControl, IPage
{
	private readonly GameData _data;
	private readonly CompRow _row;
	private readonly INavigator _nav;
	private readonly List<EditField> _fields;
	private readonly DockPanel _root = new DockPanel();
	private Border _header;

	public CompPage(GameData data, CompRow row, INavigator nav)
	{
		_data = data;
		_row = row;
		_nav = nav;
		string lang = Loc.Instance.Language;
		_fields = data.CompDef!.Fields.Select(f => new EditField(data, row.Address, f, data.Layout.Text(lang, f.Name))).ToList();
		Loaded += (_, _) => FieldBox.EntityLabel = row.Name;
		EditField.Written += OnWritten;
		Unloaded += (_, _) => EditField.Written -= OnWritten;

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);

		var grid = new Grid { Margin = new Thickness(16, 14, 16, 14) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });

		var left = new StackPanel();
		left.Children.Add(Build.Card(Build.T("Yarışma Bilgileri ve İtibar", "Competition Details & Reputation"), Build.Rows(_fields)));

		var right = new StackPanel();
		var quick = new WrapPanel { Margin = new Thickness(10, 0, 10, 12) };
		bool canWrite = !AppSettings.Current.ReadOnlyMode;
		void QBtn(string label, Action act)
		{
			var b = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5), FontSize = 12, IsEnabled = canWrite };
			b.Click += (_, _) => { act(); RefreshValues(); RebuildHeader(); };
			quick.Children.Add(b);
		}
		QBtn(Build.T("🏆 Dünya 1 Numarası (İtibar 200)", "🏆 World #1 League (Rep 200)"), () =>
		{
			using (_data.Session.History.BeginBatch($"{_row.Name}: Rep 200"))
			{
				_data.Session.WriteField(_row.Address, F("Reputation").Def, "200", _row.Name);
				_data.Session.WriteField(_row.Address, F("Reputation (10000)").Def, "10000", _row.Name);
			}
			_nav.SetStatus(Build.F("✓ {0} itibarı maksimum (200 / 10000) yapıldı. Ctrl+Z geri alır.", "✓ {0} reputation maxed (200 / 10000). Ctrl+Z undoes it.", _row.Name));
		});
		QBtn(Build.T("İtibar +20", "Reputation +20"), () =>
		{
			int next = Math.Min(200, F("Reputation").Number + 20);
			int next10k = Math.Min(10000, F("Reputation (10000)").Number + 1000);
			using (_data.Session.History.BeginBatch($"{_row.Name}: Rep +20"))
			{
				_data.Session.WriteField(_row.Address, F("Reputation").Def, next.ToString(), _row.Name);
				_data.Session.WriteField(_row.Address, F("Reputation (10000)").Def, next10k.ToString(), _row.Name);
			}
			_nav.SetStatus(Build.F("✓ {0} itibarı → {1} / {2}", "✓ {0} reputation → {1} / {2}", _row.Name, next, next10k));
		});
		QBtn(Build.T("🌟 Lig Altın Çağı (İtibar 200 + Tüm Tesisler 20)", "🌟 League Golden Age (Rep 200 + All Facilities 20)"), () =>
		{
			var leagueClubs = _data.Clubs.Where(c => c.SquadSize > 0 && (c.League == _row.Name || (_row.ShortName.Length > 0 && c.League == _row.ShortName))).ToList();
			string[] facFields = { "Training Facilities", "Youth Facilities", "Youth Coaching", "Youth Recruitment", "Corporate Facilities" };
			using (_data.Session.History.BeginBatch($"{_row.Name}: Golden Age"))
			{
				_data.Session.WriteField(_row.Address, F("Reputation").Def, "200", _row.Name);
				_data.Session.WriteField(_row.Address, F("Reputation (10000)").Def, "10000", _row.Name);
				foreach (ClubRow c in leagueClubs)
				{
					foreach (string fn in facFields)
					{
						var fd = _data.ClubDef.Fields.FirstOrDefault(x => x.Name == fn);
						if (fd != null)
						{
							try { _data.Session.WriteField(c.Address, fd, "20", c.Name); } catch { }
						}
					}
				}
			}
			_nav.SetStatus(Build.F("✓ {0}: İtibar 200 ve {1} kulübün tüm tesisleri 20 yapıldı. Ctrl+Z geri alır.", "✓ {0}: Rep 200 and all facilities maxed for {1} clubs. Ctrl+Z undoes it.", _row.Name, leagueClubs.Count));
		});
		QBtn(Build.T("Ligdeki Tüm Sakatlıkları İyileştir", "Heal All Injuries in League"), () =>
		{
			var leagueClubs = _data.Clubs
				.Where(c => c.SquadSize > 0 && (c.League == _row.Name || (_row.ShortName.Length > 0 && c.League == _row.ShortName)))
				.Select(c => c.Address)
				.ToHashSet();
			var leaguePlayers = _data.Players.Where(p => leagueClubs.Contains(p.ClubAddress)).ToList();
			int healed = 0;
			using (_data.Session.History.BeginBatch($"{_row.Name}: Heal League"))
			{
				foreach (PlayerRow p in leaguePlayers)
				{
					try { if (Medical.Heal(_data, p.Address, p.Name)) healed++; } catch { }
				}
			}
			_nav.SetStatus(healed == 0
				? Build.T("Bu ligde sakat oyuncu bulunamadı.", "No injured players found in this league.")
				: Build.F("✓ {0}: {1} sakat oyuncu iyileştirildi. Ctrl+Z geri alır.", "✓ {0}: Healed {1} injured players. Ctrl+Z undoes it.", _row.Name, healed));
		});
		QBtn(Build.T("Ligdeki Kulüpleri Toplu Düzenle…", "Mass Edit League Clubs…"), () =>
		{
			var targets = _data.Clubs
				.Where(c => c.SquadSize > 0 && (c.League == _row.Name || (_row.ShortName.Length > 0 && c.League == _row.ShortName)))
				.Select(c => new SquadEditor.Core.Editing.BulkTarget(c.Address, c.Name))
				.ToList();
			if (targets.Count == 0) return;
			new MassEditWindow(_data, _data.ClubDef, targets, _nav) { Owner = Window.GetWindow(this) }.ShowDialog();
		});
		QBtn(Build.T("Ligdeki Oyuncuları Toplu Düzenle…", "Mass Edit League Players…"), () =>
		{
			var leagueClubs = _data.Clubs
				.Where(c => c.SquadSize > 0 && (c.League == _row.Name || (_row.ShortName.Length > 0 && c.League == _row.ShortName)))
				.Select(c => c.Address)
				.ToHashSet();
			var targets = _data.Players
				.Where(p => leagueClubs.Contains(p.ClubAddress))
				.Select(p => new SquadEditor.Core.Editing.BulkTarget(p.Address, p.Name))
				.ToList();
			if (targets.Count == 0) return;
			new MassEditWindow(_data, _data.PlayerDef, targets, _nav) { Owner = Window.GetWindow(this) }.ShowDialog();
		});
		right.Children.Add(Build.Card(Build.T("Hızlı İşlemler", "Quick Actions"), quick));

		var clubsInLeague = _data.Clubs
			.Where(c => c.SquadSize > 0 && (c.League == _row.Name || (_row.ShortName.Length > 0 && c.League == _row.ShortName)))
			.OrderByDescending(c => c.AverageCA)
			.ToList();
		if (clubsInLeague.Count > 0)
		{
			var clubGrid = ClubPage.ClubGrid(clubsInLeague, _nav);
			clubGrid.MaxHeight = 440;
			right.Children.Add(Build.Card(Build.F("Ligdeki Kulüpler ({0})", "Clubs in League ({0})", clubsInLeague.Count), clubGrid));
		}

		System.Windows.Controls.Grid.SetColumn(right, 1);
		grid.Children.Add(left);
		grid.Children.Add(right);
		_root.Children.Add(Build.Scroll(grid));
		Content = _root;
		Background = Build.Res("Bg");
	}

	public string Key => "comp:" + _row.Address;
	public string Title => _row.ShortName.Length > 0 ? _row.ShortName : _row.Name;
	public ImageSource? Icon => null;

	public void RefreshValues()
	{
		foreach (EditField f in _fields) f.Refresh();
	}

	private EditField F(string name) => _fields.First(f => f.Key == name);

	private void OnWritten(object? sender, EditField field)
	{
		if (field.Entity == _row.Address) RebuildHeader();
	}

	private Border BuildHeader()
	{
		var line1 = Build.Text($"{_row.ShortName}  ·  UID {_row.Uid}", 13, new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
		int rep = F("Reputation").Number;
		var pills = new List<UIElement>
		{
			Build.Pill(rep.ToString(), Build.T("İtibar (1-200)", "Rep (1-200)"), Colors.Attribute((int)Math.Round(rep / 10.0))),
			Build.Pill(F("Reputation (10000)").Number.ToString("N0"), Build.T("İtibar (10000)", "Rep (10000)"))
		};
		return Build.Header(Build.Avatar(null, _row.Code.Length > 0 ? _row.Code : _row.Name, 68, round: false), _row.Name, line1, null, pills);
	}

	private void RebuildHeader()
	{
		Border fresh = BuildHeader();
		DockPanel.SetDock(fresh, Dock.Top);
		_root.Children.Remove(_header);
		_root.Children.Insert(0, fresh);
		_header = fresh;
	}

	public static DataGrid Grid(IEnumerable<CompRow> comps, INavigator nav)
	{
		var grid = new DataGrid { ItemsSource = comps.ToList(), RowHeight = AppSettings.Current.Compact ? 27 : 32 };
		grid.Columns.Add(Build.TextColumn(Build.T("Yarışma / Lig", "Competition"), "Name", 300));
		grid.Columns.Add(Build.TextColumn(Build.T("Kısa Ad", "Short Name"), "ShortName", 200));
		grid.Columns.Add(Build.TextColumn(Build.T("Kod", "Code"), "Code", 70));
		grid.Columns.Add(Build.TextColumn(Build.T("İtibar (1-200)", "Rep (1-200)"), "Reputation", 110));
		grid.Columns.Add(Build.TextColumn(Build.T("İtibar (10000)", "Rep (10000)"), "Reputation10000", 110));
		grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is CompRow r) nav.OpenComp(r.Address); };
		grid.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && grid.SelectedItem is CompRow r) { nav.OpenComp(r.Address); e.Handled = true; } };
		return grid;
	}
}
