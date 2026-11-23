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
	private UIElement? _squadView;
	private string? _squadSortMember;
	private System.ComponentModel.ListSortDirection? _squadSortDir;

	public ClubPage(GameData data, ClubRow club, INavigator nav)
	{
		_data = data;
		_nav = nav;
		_club = club;
		string lang = Loc.Instance.Language;
		_fields = data.ClubDef.Fields.Where(f => f.GroupKey != "Internal")
			.Select(f => new EditField(data, club.Address, f, f.Name == "Balance" ? Build.T("Bakiye", "Balance") : data.Layout.Text(lang, f.Name))).ToList();
		Loaded += (_, _) => FieldBox.EntityLabel = club.Name;
		EditField.Written += OnWritten;
		Unloaded += (_, _) => EditField.Written -= OnWritten;
		Loaded += (_, _) => { EditField.Written -= OnWritten; EditField.Written += OnWritten; };

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);
		Grid body = Build.PageBody(out StackPanel menu, out ContentControl content);
		Build.NavItem(menu, Build.T("Kadro", "Squad"), () => content.Content = _squadView ??= Squad(), isChecked: true);
		Build.NavItem(menu, Build.T("En İyi 11", "Best XI"), () => content.Content = ClubBestXi.BuildView(_data, _club, _nav));
		Build.NavItem(menu, Build.T("Personel", "Staff"), () => content.Content = StaffList());
		Build.NavItem(menu, Build.T("Finans", "Finances"), () => content.Content = Finances());
		Build.NavItem(menu, Build.T("İlişkiler", "Relationships"), () => content.Content = ClubRelationships.BuildTab(_data, _club, _nav));
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
		DataGrid? currentGrid = null;
		var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
		void Show(string? team)
		{
			_team = team;
			var rows = (team == null ? all : all.Where(p => p.Team == team)).OrderByDescending(p => p.CA);
			currentGrid = PlayerGrid(_data, rows, _nav, showClub: false, showTeam: team == null);
			// Remember user's sort column/direction across team tabs (A Takım / U19 / Tümü)
			currentGrid.Sorting += (_, e) =>
			{
				string? path = e.Column.SortMemberPath;
				if (!string.IsNullOrEmpty(path))
				{
					var nextDir = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending
						? System.ComponentModel.ListSortDirection.Descending
						: System.ComponentModel.ListSortDirection.Ascending;
					_squadSortMember = path;
					_squadSortDir = nextDir;
				}
			};
			if (!string.IsNullOrEmpty(_squadSortMember) && _squadSortDir.HasValue)
			{
				currentGrid.Items.SortDescriptions.Clear();
				currentGrid.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription(_squadSortMember, _squadSortDir.Value));
				foreach (var col in currentGrid.Columns)
				{
					if (col.SortMemberPath == _squadSortMember)
					{
						col.SortDirection = _squadSortDir.Value;
					}
				}
			}
			host.Content = new Border { Style = Build.StyleOf("CardBorder"), Padding = new Thickness(4), Child = currentGrid };
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
			ToolTip = Build.T("Kadroda görünen tüm oyuncuların sakatlıklarını iyileştirir, cezalarını kaldırır; kondisyon, maç formu, moral ve mutluluklarını %100 yapar (Ctrl+Z geri alır)",
				"Heals injuries, lifts bans, and maxes condition, sharpness, morale & contract happiness for all players in the current view (Ctrl+Z undoes it)"),
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
					r.Morale = 20;
				}
				var actions = new[]
				{
					new SquadEditor.Core.Layout.PresetAction { Field = "Condition", Value = "max" },
					new SquadEditor.Core.Layout.PresetAction { Field = "Match Sharpness", Value = "max" },
					new SquadEditor.Core.Layout.PresetAction { Field = "Morale", Value = "max" },
					new SquadEditor.Core.Layout.PresetAction { Field = "Contract Happiness", Value = "max" }
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
					var jaded = _data.PlayerDef.Fields.FirstOrDefault(f => f.Name == "Jadedness" && f.IsWritable);
					if (jaded != null)
					{
						try { _data.Session.WriteField(t.Address, jaded, "-500", t.Label); } catch (Exception) { }
					}
				}
			}
			currentGrid?.Items.Refresh();
			_nav.SetStatus(Build.T($"✓ {rows.Count} oyuncu maça hazırlandı ({healed} sakatlık, {lifted} ceza, kondisyon/moral/mutluluk fullendi · {boosted} alan). Ctrl+Z geri alır.",
				$"✓ {rows.Count} players prepared ({healed} healed, {lifted} bans lifted, fitness/morale/happiness maxed · {boosted} fields). Ctrl+Z undoes it."));
		};
		quickBar.Children.Add(prepare);
		var contractsBtn = new Button
		{
			Content = Build.T("📝 Toplu Sözleşme ▾", "📝 Bulk Contracts ▾"),
			ToolTip = Build.T("Kadroda görünen tüm oyuncuların sözleşmelerini topluca uzat (+1/+3/+4/+5 yıl) veya feshet", "Bulk extend (+1/+3/+4/+5y) or terminate contracts for all players in the current view"),
			Padding = new Thickness(9, 4, 9, 4),
			Margin = new Thickness(6, 0, 0, 0),
			FontSize = 12,
			IsEnabled = !AppSettings.Current.ReadOnlyMode
		};
		void BulkContractAction(int? addYears, bool terminate)
		{
			var rows = (_team == null ? all : all.Where(p => p.Team == _team)).Where(p => p.ContractDate != null).ToList();
			if (rows.Count == 0) return;
			string desc = terminate
				? Build.T($"{_club.Name} ({rows.Count} oyuncu) tüm sözleşmeler yarın bitecek şekilde feshedilsin mi? (Ctrl+Z geri alır)",
					$"Terminate contracts for {rows.Count} players at {_club.Name} (expire tomorrow)? (Ctrl+Z undoes it)")
				: Build.T($"{_club.Name} ({rows.Count} oyuncu) tüm sözleşmeler +{addYears} yıl uzatılsın mı? (Ctrl+Z geri alır)",
					$"Extend contracts for {rows.Count} players at {_club.Name} by +{addYears} year(s)? (Ctrl+Z undoes it)");
			if (AppSettings.Current.ConfirmBulkEdits && MessageBox.Show(desc, "SquadEditor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			var ceField = _data.PlayerDef.Fields.First(f => f.Name == "Contract Expires");
			int done = 0;
			string batchLabel = terminate
				? Build.T($"{_club.Name}: Toplu sözleşme feshi", $"{_club.Name}: Terminate squad contracts")
				: Build.T($"{_club.Name}: Toplu sözleşme +{addYears} yıl", $"{_club.Name}: Extend squad contracts +{addYears}y");
			using (_data.Session.History.BeginBatch(batchLabel))
			{
				foreach (PlayerRow r in rows)
				{
					DateTime next = terminate ? _data.GameDate.AddDays(1) : r.ContractDate!.Value.AddYears(addYears!.Value);
					string txt = next.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
					try
					{
						_data.Session.WriteField(r.Address, ceField, txt, r.Name);
						r.ContractDate = next;
						r.ContractEnd = Dates.Show(txt);
						done++;
					}
					catch (Exception)
					{
					}
				}
			}
			currentGrid?.Items.Refresh();
			_nav.SetStatus(terminate
				? Build.T($"✓ {_club.Name}: {done} oyuncunun sözleşmesi yarın bitecek şekilde feshedildi. Ctrl+Z geri alır.", $"✓ {_club.Name}: {done} contracts set to expire tomorrow. Ctrl+Z undoes it.")
				: Build.T($"✓ {_club.Name}: {done} oyuncunun sözleşmesi +{addYears} yıl uzatıldı. Ctrl+Z geri alır.", $"✓ {_club.Name}: {done} contracts extended by +{addYears}y. Ctrl+Z undoes it."));
		}
		contractsBtn.Click += (_, _) =>
		{
			var menu = new ContextMenu();
			foreach (int y in new[] { 1, 3, 4, 5 })
			{
				int years = y;
				var mi = new MenuItem { Header = Build.T($"📝 Tüm Kadro Sözleşme +{years} Yıl", $"📝 Extend Squad Contracts +{years}y") };
				mi.Click += (_, _) => BulkContractAction(years, terminate: false);
				menu.Items.Add(mi);
			}
			menu.Items.Add(new Separator());
			var term = new MenuItem { Header = Build.T("✂ Tüm Kadro Sözleşmelerini Feshet (Yarın Bitir)", "✂ Terminate Squad Contracts (Expire Tomorrow)") };
			term.Click += (_, _) => BulkContractAction(null, terminate: true);
			menu.Items.Add(term);
			menu.PlacementTarget = contractsBtn;
			menu.IsOpen = true;
		};
		quickBar.Children.Add(contractsBtn);
		var destroy = new Button
		{
			Content = Build.T("💀 Takımı Yık", "💀 Destroy Team"),
			ToolTip = Build.T("FMRTE klasiği: tüm kadronun kondisyon ve moralini 1 yapar, yorgunluğu artırır (rakip takımlar için · Ctrl+Z geri alır)",
				"FMRTE classic: drops condition & morale to 1 and spikes jadedness for the current squad (for rivals · Ctrl+Z undoes it)"),
			Padding = new Thickness(9, 4, 9, 4),
			Margin = new Thickness(6, 0, 0, 0),
			FontSize = 12,
			IsEnabled = !AppSettings.Current.ReadOnlyMode
		};
		destroy.Click += (_, _) =>
		{
			var rows = (_team == null ? all : all.Where(p => p.Team == _team)).ToList();
			if (rows.Count == 0) return;
			if (AppSettings.Current.ConfirmBulkEdits && MessageBox.Show(
				Build.T($"{_club.Name} ({rows.Count} oyuncu) kondisyon ve morali sıfırlansın mı? Ctrl+Z geri alır.",
					$"Destroy team {_club.Name} ({rows.Count} players: min condition/morale, high jadedness)? Ctrl+Z undoes it."),
				"SquadEditor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			int written = 0;
			using (_data.Session.History.BeginBatch(Build.T($"{_club.Name}: Takımı yık", $"{_club.Name}: Destroy team")))
			{
				var targets = new (string Field, string Value)[] { ("Condition", "1"), ("Match Sharpness", "1"), ("Morale", "1"), ("Jadedness", "800") };
				foreach (PlayerRow r in rows)
				{
					foreach (var (fn, val) in targets)
					{
						var fd = _data.PlayerDef.Fields.FirstOrDefault(f => f.Name == fn && f.IsWritable && f.IsNumeric);
						if (fd != null)
						{
							try { _data.Session.WriteField(r.Address, fd, val, r.Name); written++; } catch (Exception) { }
						}
					}
				}
			}
			_nav.SetStatus(Build.T($"💀 {_club.Name}: {rows.Count} oyuncu yıpratıldı ({written} değer). Ctrl+Z geri alır.",
				$"💀 {_club.Name}: {rows.Count} players destroyed ({written} values). Ctrl+Z undoes it."));
		};
		quickBar.Children.Add(destroy);
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
		var grid = new Grid { MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });

		var left = new StackPanel();
		var note = Build.Text(Build.T(
			$"Tutarlar {Money.Current.Code} olarak gösterilir, maaşlar {Money.WageSuffix.TrimStart('/')} bazında. Ayarlardan değiştirebilirsin.",
			$"Amounts are shown in {Money.Current.Code}, wages per {Money.WageSuffix.TrimStart('/')}. Change this in Settings."), 12, Build.Res("Muted"));
		note.Margin = new Thickness(14, 0, 14, 10);
		note.TextWrapping = TextWrapping.Wrap;
		var rows = Build.Rows(_fields.Where(f => f.Def.GroupKey == "Finances"));
		rows.Children.Add(note);
		left.Children.Add(Build.Card(Build.T("Finans", "Finances"), rows));

		// Live squad payroll & valuation summary
		var squad = _data.Squads[_club.Address].ToList();
		long totalWages = squad.Sum(p => p.Wage);
		long totalValue = squad.Sum(p => p.Value);
		long wageBudget = F("Wage Budget").Number;
		double usage = wageBudget > 0 ? (double)totalWages / wageBudget * 100.0 : 0;
		var summary = new StackPanel { Margin = new Thickness(14, 4, 14, 12) };
		void StatLine(string label, string val, Brush? brush = null)
		{
			var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
			var v = Build.Text(val, 13, brush ?? Build.Res("Text"), FontWeights.SemiBold);
			DockPanel.SetDock(v, Dock.Right);
			d.Children.Add(v);
			d.Children.Add(Build.Text(label, 12.5, Build.Res("Muted")));
			summary.Children.Add(d);
		}
		StatLine(Build.T("Toplam Kadro Değeri", "Total Squad Value"), Money.Format(totalValue, false));
		StatLine(Build.T("Mevcut Kadro Maaş Yükü", "Current Squad Wage Bill"), Money.Format(totalWages, true));
		StatLine(Build.T("Maaş Bütçesi Kullanımı", "Wage Budget Usage"), wageBudget > 0 ? $"%{usage:0.0}" : "—", usage > 100 ? Build.Res("Danger") : Build.Res("Positive"));
		StatLine(Build.T("Sözleşmesi 1 Yıl İçinde Biten", "Expiring Within 1 Year"), squad.Count(p => p.ContractDate is { } cd && cd <= _data.GameDate.AddYears(1)).ToString());
		left.Children.Add(Build.Card(Build.T("Kadro Finans Özeti", "Squad Financial Summary"), summary));

		var right = new StackPanel();
		var quick = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(10, 2, 10, 10) };
		bool canWrite = !AppSettings.Current.ReadOnlyMode;
		void QuickBtn(string label, Action act)
		{
			var b = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(8, 6, 8, 6), FontSize = 12, IsEnabled = canWrite };
			b.Click += (_, _) =>
			{
				act();
				RefreshValues();
				OnWritten(null, F("Balance"));
			};
			quick.Children.Add(b);
		}
		void AddMoney(string fieldName, long deltaGbp, string label)
		{
			EditField ef = F(fieldName);
			long next = Math.Clamp(ef.Number + deltaGbp, (long)(ef.Def.Min ?? -2_000_000_000), (long)(ef.Def.Max ?? 2_000_000_000));
			_data.Session.WriteField(_club.Address, ef.Def, next.ToString(System.Globalization.CultureInfo.InvariantCulture), _club.Name);
			_nav.SetStatus(Build.T($"✓ {_club.Name}: {label} uygulandı. Ctrl+Z geri alır.", $"✓ {_club.Name}: {label} applied. Ctrl+Z undoes it."));
		}
		QuickBtn(Build.T("+10M Bakiye", "+10M Balance"), () => AddMoney("Balance", (long)Math.Round(Money.FromDisplay(10_000_000, false)), "+10M"));
		QuickBtn(Build.T("+50M Bakiye", "+50M Balance"), () => AddMoney("Balance", (long)Math.Round(Money.FromDisplay(50_000_000, false)), "+50M"));
		QuickBtn(Build.T("+100M Bakiye", "+100M Balance"), () => AddMoney("Balance", (long)Math.Round(Money.FromDisplay(100_000_000, false)), "+100M"));
		QuickBtn(Build.T("+50M Transfer Bütçesi", "+50M Transfer Budget"), () => AddMoney("Transfer Budget", (long)Math.Round(Money.FromDisplay(50_000_000, false)), "+50M Transfer"));
		QuickBtn(Build.T("Maaş Bütçesini 2x Yap", "Double Wage Budget"), () =>
		{
			EditField wb = F("Wage Budget"), swb = F("Spending Wage Budget");
			long next = Math.Clamp(Math.Max(wb.Number, 100_000) * 2L, 0, (long)(wb.Def.Max ?? 200_000_000));
			using (_data.Session.History.BeginBatch(Build.T($"{_club.Name}: Maaş bütçesi 2x", $"{_club.Name}: Double wage budget")))
			{
				_data.Session.WriteField(_club.Address, wb.Def, next.ToString(System.Globalization.CultureInfo.InvariantCulture), _club.Name);
				_data.Session.WriteField(_club.Address, swb.Def, next.ToString(System.Globalization.CultureInfo.InvariantCulture), _club.Name);
			}
			_nav.SetStatus(Build.T($"✓ {_club.Name} maaş bütçesi iki katına çıkarıldı.", $"✓ {_club.Name} wage budget doubled."));
		});
		QuickBtn(Build.T("+100M Transfer Bütçesi", "+100M Transfer Budget"), () => AddMoney("Transfer Budget", (long)Math.Round(Money.FromDisplay(100_000_000, false)), "+100M Transfer"));
		QuickBtn(Build.T("💎 Zengin Patron (+500M / +200M)", "💎 Rich Owner (+500M / +200M)"), () =>
		{
			using (_data.Session.History.BeginBatch(Build.T($"{_club.Name}: Zengin patron", $"{_club.Name}: Rich owner")))
			{
				AddMoney("Balance", (long)Math.Round(Money.FromDisplay(500_000_000, false)), "+500M");
				AddMoney("Transfer Budget", (long)Math.Round(Money.FromDisplay(200_000_000, false)), "+200M");
			}
		});
		QuickBtn(Build.T("Transfer Geliri %100", "Transfer Revenue 100%"), () =>
		{
			EditField tr = F("Transfer Revenue %");
			_data.Session.WriteField(_club.Address, tr.Def, "100", _club.Name);
			_nav.SetStatus(Build.T($"✓ {_club.Name} transfer gelir yüzdesi %100 yapıldı.", $"✓ {_club.Name} transfer revenue set to 100%."));
		});
		right.Children.Add(Build.Card(Build.T("Hızlı Finans İşlemleri", "Quick Finance Actions"), quick));

		Grid.SetColumn(right, 1);
		grid.Children.Add(left);
		grid.Children.Add(right);
		return Build.Scroll(grid);
	}

	private UIElement Info()
	{
		var grid = new Grid { MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });
		var left = new StackPanel();
		left.Children.Add(Build.Card(Build.T("Genel", "General"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "General"))));
		left.Children.Add(Build.Card(Build.T("Taraftarlar (1-20)", "Supporters (1-20)"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Supporters"))));
		var right = new StackPanel();
		var facQuick = new Button
		{
			Content = Build.T("⭐ Tüm Tesisleri 20 Yap", "⭐ Max All Facilities (20)"),
			Padding = new Thickness(8, 2, 8, 2),
			FontSize = 11,
			IsEnabled = !AppSettings.Current.ReadOnlyMode,
			ToolTip = Build.T("Antrenman, Altyapı, Gençlik ve Stadyum Zeminini maksimuma çıkarır (Ctrl+Z geri alır)", "Maxes Training, Youth, Junior Coaching, Recruitment & Pitch Condition (Ctrl+Z undoes)")
		};
		facQuick.Click += (_, _) =>
		{
			using (_data.Session.History.BeginBatch(Build.T($"{_club.Name}: Tesisleri fulle", $"{_club.Name}: Max facilities")))
			{
				foreach (EditField ef in _fields.Where(f => f.Def.GroupKey == "Facilities" && f.Def.IsWritable))
				{
					if (ef.Def.Max is double mv)
					{
						try { _data.Session.WriteField(_club.Address, ef.Def, mv.ToString(System.Globalization.CultureInfo.InvariantCulture), _club.Name); } catch { }
					}
				}
				var pitch = _fields.FirstOrDefault(f => f.Key == "Pitch Condition" && f.Def.IsWritable);
				if (pitch?.Def.Max is double pm)
				{
					try { _data.Session.WriteField(_club.Address, pitch.Def, pm.ToString(System.Globalization.CultureInfo.InvariantCulture), _club.Name); } catch { }
				}
			}
			RefreshValues();
			_nav.SetStatus(Build.T($"✓ {_club.Name}: Tüm tesisler ve saha zemini maksimuma çıkarıldı. Ctrl+Z geri alır.", $"✓ {_club.Name}: All facilities & pitch condition maxed. Ctrl+Z undoes it."));
		};
		right.Children.Add(Build.Card(Build.T("Tesisler (1-20)", "Facilities (1-20)"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Facilities")), facQuick));
		right.Children.Add(Build.Card(Build.T("Stadyum", "Stadium"), Build.Rows(_fields.Where(f => f.Def.GroupKey == "Stadium"))));
		Grid.SetColumn(right, 1);
		grid.Children.Add(left);
		grid.Children.Add(right);
		return Build.Scroll(grid);
	}

	/// <summary>Sortable player table shared by squads and the player browser. Multi-select + right-click applies presets.</summary>
	public static DataGrid PlayerGrid(GameData data, IEnumerable<PlayerRow> players, INavigator nav, bool showClub, bool showTeam = false)
	{
		var shortlist = new HashSet<uint>(AppSettings.Current.GetShortlist(data.SaveKey));
		var list = players.ToList();
		foreach (PlayerRow p in list) p.IsShortlisted = shortlist.Contains(p.Uid);
		var grid = new DataGrid { ItemsSource = list, RowHeight = AppSettings.Current.Compact ? 27 : 34 };

		// One-click ☆/★ shortlist star column
		var starCol = new DataGridTemplateColumn { Header = "★", Width = 32, SortMemberPath = "IsShortlisted" };
		var starFactory = new FrameworkElementFactory(typeof(Button));
		starFactory.SetValue(Control.BackgroundProperty, Brushes.Transparent);
		starFactory.SetValue(Control.BorderThicknessProperty, new Thickness(0));
		starFactory.SetValue(Control.PaddingProperty, new Thickness(0));
		starFactory.SetValue(Control.FontSizeProperty, 14.0);
		starFactory.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
		starFactory.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PlayerRow.ShortlistStar)));
		starFactory.SetBinding(Control.ForegroundProperty, new Binding(nameof(PlayerRow.IsShortlisted))
		{
			Converter = new ShortlistStarBrushConverter()
		});
		starFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, ev) =>
		{
			if (s is Button b && b.DataContext is PlayerRow row)
			{
				var cur = new HashSet<uint>(AppSettings.Current.GetShortlist(data.SaveKey));
				if (cur.Contains(row.Uid))
				{
					cur.Remove(row.Uid);
					row.IsShortlisted = false;
					nav.SetStatus(Build.T($"{row.Name} kısa listeden çıkarıldı.", $"{row.Name} removed from shortlist."));
				}
				else
				{
					cur.Add(row.Uid);
					row.IsShortlisted = true;
					nav.SetStatus(Build.T($"★ {row.Name} kısa listeye eklendi.", $"★ {row.Name} added to shortlist."));
				}
				AppSettings.Current.SetShortlist(data.SaveKey, cur.ToList());
				AppSettings.Current.Save();
				grid.Items.Refresh();
				ev.Handled = true;
			}
		}));
		starCol.CellTemplate = new DataTemplate { VisualTree = starFactory };
		grid.Columns.Add(starCol);

		grid.Columns.Add(Build.TemplateColumn(Build.T("Oyuncu", "Player"), "PlayerNameCell", "Name", 240));
		var posCol = Build.TextColumn(Build.T("Mevki", "Position"), "Positions", 105);
		posCol.SortMemberPath = "PositionOrder";
		grid.Columns.Add(posCol);
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
		var compareTwo = new MenuItem { Header = Build.T("⚖ Seçilen 2 oyuncuyu karşılaştır", "⚖ Compare 2 selected players") };
		compareTwo.Click += (_, _) =>
		{
			var sel = grid.SelectedItems.OfType<PlayerRow>().Take(2).ToList();
			if (sel.Count == 2)
			{
				new PlayerCompareWindow(data, sel[0], sel[1], nav) { Owner = Window.GetWindow(grid) }.ShowDialog();
			}
			else
			{
				nav.SetStatus(Build.T("Karşılaştırmak için Ctrl ile 2 oyuncu seç.", "Hold Ctrl and select 2 players to compare."));
			}
		};
		grid.ContextMenu.Items.Add(compareTwo);
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
			var set = new HashSet<uint>(AppSettings.Current.GetShortlist(data.SaveKey));
			bool allIn = rows.All(r => set.Contains(r.Uid));
			foreach (PlayerRow r in rows)
			{
				if (allIn) set.Remove(r.Uid); else set.Add(r.Uid);
			}
			AppSettings.Current.SetShortlist(data.SaveKey, set.ToList());
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
		var staffDef = row.IsHuman ? data.HumanDef : data.StaffDef;
		_f = staffDef.Fields.Where(f => f.GroupKey != "Internal" && f.Concat.Count == 0)
			.ToDictionary(f => f.Name, f => new EditField(data, row.Address, f, data.Layout.Text(lang, f.Name)));
		Loaded += (_, _) => FieldBox.EntityLabel = Row.Name;

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);
		Border ribbon = new Border
		{
			Background = Build.Res("Chrome"),
			BorderBrush = Build.Res("StrokeSoft"),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(16, 6, 16, 6),
			Child = BuildActionsRibbon()
		};
		DockPanel.SetDock(ribbon, Dock.Top);
		_root.Children.Add(ribbon);
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
		c2.Children.Add(Build.Card(Build.T("Kişisel bilgiler", "Personal details"), Build.Rows(Named("First Name", "Last Name", "Common Name", "Unique ID", "Date of Birth", "Nation", "City of Birth"))));
		Grid.SetColumn(c1, 1);
		Grid.SetColumn(c2, 2);
		grid.Children.Add(c0);
		grid.Children.Add(c1);
		grid.Children.Add(c2);
		return Build.Scroll(grid);
	}

	private UIElement BuildActionsRibbon()
	{
		var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		bool canWrite = !AppSettings.Current.ReadOnlyMode;
		var freeze = new Button { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
		void UpdateFreeze()
		{
			bool frozen = _data.Session.Freezer.IsFrozen(Row.Address);
			freeze.Content = frozen ? Build.T("❄ Donduruldu", "❄ Frozen") : Build.T("❄ Dondur", "❄ Freeze");
			freeze.IsEnabled = canWrite || frozen;
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
				int count = _data.Session.FreezeEntity(Row.Address, Row.Name, Row.IsHuman ? _data.HumanDef : _data.StaffDef, StaffFreezeGroups);
				_nav.SetStatus(Build.T($"{Row.Name} donduruldu ({count} alan kilitlendi).", $"{Row.Name} frozen ({count} fields locked)."));
			}
			UpdateFreeze();
		};
		Button ExtendBtn(int years)
		{
			var btn = new Button
			{
				Content = Build.T($"📝 +{years} Yıl", $"📝 +{years}y"),
				ToolTip = Build.T($"Sözleşme bitişini {years} yıl uzat", $"Extend contract expiry by {years} year(s)"),
				Padding = new Thickness(9, 4, 9, 4),
				Margin = new Thickness(0, 0, 6, 0),
				IsEnabled = canWrite && _f.TryGetValue("Contract Expires", out EditField? ce) && GameData.ParseDate(ce.Value) != null
			};
			btn.Click += (_, _) =>
			{
				if (_f.TryGetValue("Contract Expires", out EditField? ce) && GameData.ParseDate(ce.Value) is { } cur)
				{
					DateTime next = cur.AddYears(years);
					_data.Session.WriteField(Row.Address, ce.Def, next.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture), Row.Name);
					RefreshValues();
					Row.ContractEnd = next.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
					RebuildHeader();
					_nav.SetStatus(Build.T($"✓ {Row.Name} sözleşmesi {Dates.Show(Row.ContractEnd)} tarihine uzatıldı.", $"✓ {Row.Name}'s contract extended to {Dates.Show(Row.ContractEnd)}."));
				}
			};
			return btn;
		}
		var rename = new Button { Content = Build.T("Adı değiştir", "Rename"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = canWrite };
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
		var move = new Button { Content = Build.T("Kulübe taşı / İşe al", "Move / Hire"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = canWrite };
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
		actions.Children.Add(ExtendBtn(1));
		actions.Children.Add(ExtendBtn(3));
		actions.Children.Add(rename);
		actions.Children.Add(move);
		return actions;
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
	private readonly ComboBox _pos, _region;
	private readonly ComboBox _ageMin, _ageMax, _caMin, _caMax, _paMin, _maxValM;
	private readonly CheckBox _freeOnly, _expiringOnly, _injuredOnly, _wonderkid, _regenOnly, _shortlistOnly, _previewToggle;
	private readonly Button _advFilterBtn;
	private readonly ScoutFilter _scoutFilter = new ScoutFilter();
	private readonly ComboBox _staffJob;
	private readonly TextBox _staffCaMin, _staffPaMin;
	private readonly CheckBox _staffFreeOnly;
	private readonly TextBlock _matchCount;
	private readonly TextBlock _staffMatchCount;
	private readonly RadioButton _frozenTab;
	private readonly ContentControl _host = new ContentControl();
	private readonly Border _previewCard = new Border { Width = 282, Margin = new Thickness(10, 0, 0, 12), Visibility = Visibility.Collapsed };
	private readonly ContentControl _previewContent = new ContentControl();
	private readonly WrapPanel _activeChips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
	private readonly System.Windows.Threading.DispatcherTimer _debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
	private List<PlayerRow> _lastPlayers = new List<PlayerRow>();
	private string _mode = "players";

	public HomePage(GameData data, INavigator nav)
	{
		_data = data;
		_nav = nav;
		var root = new DockPanel { Margin = new Thickness(16, 12, 16, 0) };

		var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
		var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		title.Children.Add(Build.Text(Build.T("Veritabanı", "Database"), 17, Build.Res("Text"), FontWeights.SemiBold));
		title.Children.Add(Build.Text($"{data.Players.Count:N0} {Build.T("oyuncu", "players")}  ·  {data.Staff.Count:N0} {Build.T("personel", "staff")}  ·  {data.Clubs.Count:N0} {Build.T("kulüp", "clubs")}  ·  {data.GameDate:dd.MM.yyyy}", 12, Build.Res("Muted")));
		DockPanel.SetDock(title, Dock.Left);
		top.Children.Add(title);

		var segments = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		RadioButton? frozenBtn = null;
		foreach (var (key, label) in new[]
		{
			("players", Build.T("Oyuncular", "Players")),
			("staff", Build.T("Personel", "Staff")),
			("clubs", Build.T("Kulüpler", "Clubs")),
			("nations", Build.T("Ülkeler", "Nations")),
			("comps", Build.T("Ligler", "Competitions")),
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
		_search = new TextBox { Style = Build.StyleOf("SearchBox"), Width = 260, Height = 32, Margin = new Thickness(10, 0, 0, 0), ToolTip = Build.T("İsim, kulüp veya ülke yaz (aksan fark etmez)", "Type name, club or nation (accents ignored)") };
		_search.TextChanged += (_, _) => ScheduleApply();
		_debounce.Tick += (_, _) => { _debounce.Stop(); Apply(); };
		var searchHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
		searchHost.Children.Add(_search);
		searchHost.Children.Add(new TextBlock { Text = "⌕", FontSize = 15, Foreground = Build.Res("Muted"), Margin = new Thickness(20, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
		var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
		if (AppSettings.Current.LastClubUid > 0 && data.Clubs.FirstOrDefault(c => c.Uid == AppSettings.Current.LastClubUid) is { } lastClub)
		{
			var clubBtn = new Button
			{
				Margin = new Thickness(0, 0, 10, 0),
				Padding = new Thickness(10, 5, 10, 5),
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
			right.Children.Add(clubBtn);
		}
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

		// Player scout filter bar (2 spacious rows with editable 0-200 presets)
		_pos = new ComboBox { Width = 84, Height = 28, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
		_pos.ItemsSource = Positions.Select(p => new KeyValuePair<string, string>(p, p.Length == 0 ? Build.T("Tümü", "All") : p)).ToList();
		_pos.DisplayMemberPath = "Value";
		_pos.SelectedValuePath = "Key";
		_pos.SelectedValue = "";
		_pos.SelectionChanged += (_, _) => Apply();

		_region = new ComboBox { Width = 112, Height = 28, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, ToolTip = Build.T("Bölgesel uyruk / yabancı kuralı filtresi", "Regional nationality filter") };
		_region.ItemsSource = new[]
		{
			new KeyValuePair<string, string>("", Build.T("Tüm Bölgeler", "All Regions")),
			new KeyValuePair<string, string>("TR", Build.T("🇹🇷 Türkiye", "🇹🇷 Türkiye")),
			new KeyValuePair<string, string>("EU", Build.T("🇪🇺 AB (EU)", "🇪🇺 EU Nationals")),
			new KeyValuePair<string, string>("SA", Build.T("🌎 Güney Amerika", "🌎 South America")),
			new KeyValuePair<string, string>("BALKAN", Build.T("🌍 Balkanlar", "🌍 Balkans")),
			new KeyValuePair<string, string>("AFRICA", Build.T("🌍 Afrika", "🌍 Africa"))
		};
		_region.DisplayMemberPath = "Value";
		_region.SelectedValuePath = "Key";
		_region.SelectedValue = "";
		_region.SelectionChanged += (_, _) => Apply();

		ComboBox RangeCombo(string tip, double w, IEnumerable<string> items)
		{
			var cb = new ComboBox
			{
				Width = w,
				Height = 28,
				FontSize = 12.5,
				IsEditable = true,
				VerticalAlignment = VerticalAlignment.Center,
				ItemsSource = items.ToList(),
				ToolTip = tip
			};
			cb.SelectionChanged += (_, _) => ScheduleApply();
			cb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ScheduleApply()));
			return cb;
		}
		var ages0to99 = Enumerable.Range(0, 100).Select(i => i.ToString()).ToList();
		var ability0to200 = Enumerable.Range(0, 201).Select(i => i.ToString()).ToList();
		var valuePresets = new[] { "0", "1", "2", "3", "5", "10", "15", "20", "25", "30", "40", "50", "75", "100", "150", "200", "300" };
		_ageMin = RangeCombo(Build.T("En az yaş (0–99, seç veya yaz)", "Min age (0–99, pick or type)"), 66, ages0to99);
		_ageMax = RangeCombo(Build.T("En fazla yaş (0–99, seç veya yaz)", "Max age (0–99, pick or type)"), 66, ages0to99);
		_caMin = RangeCombo(Build.T("En az CA (0–200, seç veya yaz)", "Min CA (0–200, pick or type)"), 72, ability0to200);
		_caMax = RangeCombo(Build.T("En fazla CA (0–200, seç veya yaz)", "Max CA (0–200, pick or type)"), 72, ability0to200);
		_paMin = RangeCombo(Build.T("En az PA (0–200, seç veya yaz)", "Min PA (0–200, pick or type)"), 72, ability0to200);
		_maxValM = RangeCombo(Build.T($"En fazla değer — milyon {Money.Current.Symbol} (seç veya yaz)", $"Max value in million {Money.Current.Symbol} (pick or type)"), 72, valuePresets);

		_freeOnly = new CheckBox { Content = Build.T("Serbest", "Free"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(0, 0, 6, 0) };
		_expiringOnly = new CheckBox { Content = Build.T("Sözl. ≤1y", "Exp. ≤1y"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(0, 0, 6, 0), ToolTip = Build.T("Sözleşmesi 1 yıl içinde bitenler", "Contract expiring within 1 year") };
		_injuredOnly = new CheckBox { Content = Build.T("Sakat/Cezalı", "Inj/Ban"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(0, 0, 6, 0) };
		_wonderkid = new CheckBox { Content = Build.T("Wonderkid", "Wonderkid"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(0, 0, 6, 0), ToolTip = Build.T("Yaş ≤ 21 ve PA ≥ 150", "Age ≤ 21 and PA ≥ 150") };
		_regenOnly = new CheckBox { Content = Build.T("🧬 Regen", "🧬 Newgen"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(0, 0, 6, 0), ToolTip = Build.T("Sadece oyunun sonradan ürettiği Regen / Newgen oyuncular (UID ≥ 2.000.000.000)", "Only game-generated Newgens / Regens (UID ≥ 2,000,000,000)") };
		_shortlistOnly = new CheckBox { Content = Build.T("★ Kısa Liste", "★ Shortlist"), Style = Build.StyleOf("FilterChip"), Margin = new Thickness(0, 0, 6, 0), ToolTip = Build.T("Oyuncu profilinde ☆ Kısa Listeye Ekle veya listede sağ tıkla yıldızladığın oyuncular", "Players starred from their profile or via right-click") };
		_previewToggle = new CheckBox { Content = Build.T("◧ Önizleme", "◧ Preview"), Style = Build.StyleOf("FilterChip"), IsChecked = true, Margin = new Thickness(4, 0, 0, 0), ToolTip = Build.T("Seçili oyuncunun sağda hızlı özet kartını göster/gizle", "Show/hide quick player preview drawer on the right") };
		_previewToggle.Checked += (_, _) => UpdatePreviewVisibility();
		_previewToggle.Unchecked += (_, _) => UpdatePreviewVisibility();
		foreach (CheckBox cb in new[] { _freeOnly, _expiringOnly, _injuredOnly, _wonderkid, _regenOnly, _shortlistOnly })
		{
			cb.Checked += (_, _) => Apply();
			cb.Unchecked += (_, _) => Apply();
		}

		_advFilterBtn = new Button { Content = Build.T("🔍 Gelişmiş", "🔍 Advanced"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, Margin = new Thickness(4, 0, 6, 0), ToolTip = Build.T("Genie Scout filtreleri (PA-CA, Rol %, Profesyonellik, İstikrar, Sakatlık Eğilimi, Hız, Maaş…)", "Genie Scout filters (PA-CA, Role %, Professionalism, Consistency, Injury Proneness, Pace, Wage…)") };
		_advFilterBtn.Click += (_, _) =>
		{
			var win = new ScoutFilterWindow(_scoutFilter) { Owner = Window.GetWindow(this) };
			if (win.ShowDialog() == true)
			{
				_advFilterBtn.Style = _scoutFilter.IsActive ? Build.StyleOf("AccentButton") : (Style)Application.Current.FindResource(typeof(Button));
				Apply();
			}
		};

		var colBtn = new Button { Content = Build.T("Sütunlar", "Columns"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(8, 3, 8, 3), FontSize = 12, Margin = new Thickness(4, 0, 0, 0), ToolTip = Build.T("Tabloda gösterilecek Genie Scout sütunlarını seç", "Choose extra Genie Scout columns") };
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

		var presetsBtn = new Button
		{
			Content = Build.T("⚡ Şablonlar ▾", "⚡ Presets ▾"),
			Padding = new Thickness(9, 4, 9, 4),
			FontSize = 12,
			Margin = new Thickness(0, 0, 6, 0),
			ToolTip = Build.T("Tek tıkla hazır scout arama şablonlarını uygula", "Apply 1-click ready-made scout search presets")
		};
		void ResetAllFilters()
		{
			_pos.SelectedValue = "";
			_region.SelectedValue = "";
			foreach (ComboBox cb in new[] { _ageMin, _ageMax, _caMin, _caMax, _paMin, _maxValM })
			{
				cb.SelectedIndex = -1;
				cb.Text = "";
			}
			_search.Text = "";
			_freeOnly.IsChecked = _expiringOnly.IsChecked = _injuredOnly.IsChecked = _wonderkid.IsChecked = _regenOnly.IsChecked = _shortlistOnly.IsChecked = false;
			_scoutFilter.Clear();
			_advFilterBtn.Style = (Style)Application.Current.FindResource(typeof(Button));
		}
		presetsBtn.Click += (_, _) =>
		{
			var menu = new ContextMenu();
			void AddPreset(string tr, string en, Action setup)
			{
				var mi = new MenuItem { Header = Build.T(tr, en) };
				mi.Click += (_, _) => { ResetAllFilters(); setup(); Apply(); };
				menu.Items.Add(mi);
			}
			AddPreset("⭐ Ucuz Wonderkidler (Yaş ≤ 20 · PA ≥ 155 · ≤ 15M)", "⭐ Bargain Wonderkids (Age ≤ 20 · PA ≥ 155 · ≤ 15M)", () => { _ageMax.Text = "20"; _paMin.Text = "155"; _maxValM.Text = "15"; });
			AddPreset("⭐ Sözleşmesi Biten Yıldızlar (CA ≥ 145 · Sözl. ≤ 1y)", "⭐ Expiring Stars (CA ≥ 145 · Exp ≤ 1y)", () => { _caMin.Text = "145"; _expiringOnly.IsChecked = true; });
			AddPreset("⭐ Yerli Genç Yetenekler (Türkiye · Yaş ≤ 21 · PA ≥ 135)", "⭐ Turkish Wonderkids (Türkiye · Age ≤ 21 · PA ≥ 135)", () => { _search.Text = "Türkiye"; _ageMax.Text = "21"; _paMin.Text = "135"; });
			AddPreset("⭐ Genç Kaleciler (GK · Yaş ≤ 23 · PA ≥ 150)", "⭐ Young Goalkeepers (GK · Age ≤ 23 · PA ≥ 150)", () => { _pos.SelectedValue = "GK"; _ageMax.Text = "23"; _paMin.Text = "150"; });
			AddPreset("⭐ Boştaki Yıldızlar (Serbest · CA ≥ 125)", "⭐ Best Free Agents (Free · CA ≥ 125)", () => { _freeOnly.IsChecked = true; _caMin.Text = "125"; });
			AddPreset("🧬 En İyi Regenler (Newgen · PA ≥ 150)", "🧬 Best Newgens (Regen · PA ≥ 150)", () => { _regenOnly.IsChecked = true; _paMin.Text = "150"; });
			menu.PlacementTarget = presetsBtn;
			menu.IsOpen = true;
		};

		var clear = new Button { Content = Build.T("Temizle", "Clear"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(8, 3, 8, 3), FontSize = 12, Margin = new Thickness(2, 0, 0, 0) };
		clear.Click += (_, _) => { ResetAllFilters(); Apply(); };
		var exportCsv = new Button { Content = Build.T("CSV", "CSV"), Style = Build.StyleOf("GhostButton"), Padding = new Thickness(8, 3, 8, 3), FontSize = 12, Margin = new Thickness(4, 0, 0, 0), ToolTip = Build.T("Filtrelenmiş listeyi CSV olarak dışa aktar", "Export filtered list to CSV") };
		exportCsv.Click += (_, _) => ExportPlayersCsv();

		_matchCount = Build.Text("", 12, Build.Res("Muted"));
		_matchCount.Margin = new Thickness(10, 0, 4, 0);

		var row1 = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
		var posLbl = Lbl(Build.T("Mevki", "Pos"));
		posLbl.Margin = new Thickness(0, 0, 5, 0);
		row1.Children.Add(posLbl);
		row1.Children.Add(_pos);
		row1.Children.Add(Lbl(Build.T("Yaş", "Age")));
		row1.Children.Add(_ageMin);
		row1.Children.Add(new TextBlock { Text = "–", Foreground = Build.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) });
		row1.Children.Add(_ageMax);
		row1.Children.Add(Lbl("CA"));
		row1.Children.Add(_caMin);
		row1.Children.Add(new TextBlock { Text = "–", Foreground = Build.Res("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) });
		row1.Children.Add(_caMax);
		row1.Children.Add(Lbl("PA ≥"));
		row1.Children.Add(_paMin);
		row1.Children.Add(Lbl(Build.T($"Değer ≤M{Money.Current.Symbol}", $"Val ≤M{Money.Current.Symbol}")));
		row1.Children.Add(_maxValM);
		row1.Children.Add(Lbl(Build.T("Bölge", "Region")));
		row1.Children.Add(_region);

		var row2 = new DockPanel();
		DockPanel.SetDock(_matchCount, Dock.Right);
		DockPanel.SetDock(_previewToggle, Dock.Right);
		DockPanel.SetDock(exportCsv, Dock.Right);
		DockPanel.SetDock(colBtn, Dock.Right);
		row2.Children.Add(_matchCount);
		row2.Children.Add(_previewToggle);
		row2.Children.Add(exportCsv);
		row2.Children.Add(colBtn);
		var chips = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		chips.Children.Add(presetsBtn);
		chips.Children.Add(_freeOnly);
		chips.Children.Add(_expiringOnly);
		chips.Children.Add(_injuredOnly);
		chips.Children.Add(_wonderkid);
		chips.Children.Add(_regenOnly);
		chips.Children.Add(_shortlistOnly);
		chips.Children.Add(_advFilterBtn);
		chips.Children.Add(clear);
		row2.Children.Add(chips);

		var filterStack = new StackPanel();
		filterStack.Children.Add(row1);
		filterStack.Children.Add(row2);
		filterStack.Children.Add(_activeChips);

		_filterBar = new Border
		{
			Background = Build.Res("Panel"),
			BorderBrush = Build.Res("StrokeSoft"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(12, 8, 12, 8),
			Margin = new Thickness(0, 0, 0, 10),
			Child = filterStack
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

		_previewCard.Style = Build.StyleOf("CardBorder");
		_previewCard.Margin = new Thickness(10, 0, 0, 12);
		_previewCard.Child = _previewContent;
		var split = new Grid();
		split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var tableBorder = new Border { Style = Build.StyleOf("CardBorder"), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(3), Child = _host };
		Grid.SetColumn(tableBorder, 0);
		Grid.SetColumn(_previewCard, 1);
		split.Children.Add(tableBorder);
		split.Children.Add(_previewCard);
		root.Children.Add(split);
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

	private static int? ParseInt(ComboBox box) => int.TryParse((box.Text ?? "").Trim(), out int v) ? v : null;

	private static double? ParseDouble(ComboBox box) => double.TryParse((box.Text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;

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
		case "nations":
			_host.Content = NationPage.Grid(_data.Nations.Where(n => q.Length == 0 || n.Search.Contains(q)).OrderByDescending(n => n.YouthRating), _nav);
			break;
		case "comps":
			_host.Content = CompPage.Grid(_data.Competitions.Where(c => q.Length == 0 || c.Search.Contains(q)).OrderByDescending(c => c.Reputation), _nav);
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
			string region = _region.SelectedValue as string ?? "";
			int? ageMin = ParseInt(_ageMin), ageMax = ParseInt(_ageMax), caMin = ParseInt(_caMin), caMax = ParseInt(_caMax), paMin = ParseInt(_paMin);
			double? maxValM = ParseDouble(_maxValM);
			long? maxValGbp = maxValM.HasValue ? (long)Math.Round(Money.FromDisplay(maxValM.Value * 1_000_000.0, false)) : null;
			bool freeOnly = _freeOnly.IsChecked == true;
			bool expiringOnly = _expiringOnly.IsChecked == true;
			bool injuredOnly = _injuredOnly.IsChecked == true;
			bool wonderkid = _wonderkid.IsChecked == true;
			bool regenOnly = _regenOnly.IsChecked == true;
			bool shortlistOnly = _shortlistOnly.IsChecked == true;
			HashSet<uint>? shortlist = shortlistOnly ? new HashSet<uint>(AppSettings.Current.GetShortlist(_data.SaveKey)) : null;
			long? maxWageGbp = _scoutFilter.MaxWageDisplay.HasValue ? (long)Math.Round(Money.FromDisplay(_scoutFilter.MaxWageDisplay.Value, true)) : null;
			DateTime expireCutoff = _data.GameDate.AddYears(1);
			IEnumerable<PlayerRow> query = _data.Players;
			if (q.Length > 0) query = query.Where(p => p.Search.Contains(q));
			if (pos.Length > 0) query = query.Where(p => p.Positions.Split(' ', '/', ',').Any(tok => tok == pos));
			if (region.Length > 0) query = query.Where(p => MatchesRegion(p.Nation, region));
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
			if (regenOnly) query = query.Where(p => p.IsRegen);
			if (shortlist != null) query = query.Where(p => shortlist.Contains(p.Uid));
			if (_scoutFilter.IsActive) query = query.Where(p => _scoutFilter.Matches(p, maxWageGbp));
			_lastPlayers = query.OrderByDescending(p => p.CA).ToList();
			_matchCount.Text = $"{_lastPlayers.Count:N0} / {_data.Players.Count:N0}";
			RebuildActiveChips(pos, region, ageMin, ageMax, caMin, caMax, paMin, maxValM, freeOnly, expiringOnly, injuredOnly, wonderkid, regenOnly, shortlistOnly);
			var pg = ClubPage.PlayerGrid(_data, _lastPlayers, _nav, showClub: true);
			pg.SelectionChanged += (_, _) =>
			{
				if (pg.SelectedItem is PlayerRow sel) ShowPreview(sel, pg);
			};
			_host.Content = pg;
			UpdatePreviewVisibility();
			if (_lastPlayers.Count > 0) ShowPreview(_lastPlayers[0], pg);
			break;
		}
		}
		UpdatePreviewVisibility();
	}

	private static readonly Dictionary<string, string[]> RegionNations = new()
	{
		["TR"] = new[] { "turkiye", "turkey" },
		["EU"] = new[] { "almanya", "germany", "fransa", "france", "ispanya", "spain", "italya", "italy", "hollanda", "netherlands", "portekiz", "portugal", "belcika", "belgium", "avusturya", "austria", "hirvatistan", "croatia", "polonya", "poland", "danimarka", "denmark", "isvec", "sweden", "cekya", "czech", "macaristan", "hungary", "yunanistan", "greece", "romanya", "romania", "bulgaristan", "bulgaria", "slovenya", "slovenia", "slovakya", "slovakia", "finlandiya", "finland", "irlanda", "ireland" },
		["SA"] = new[] { "brezilya", "brazil", "arjantin", "argentina", "uruguay", "kolombiya", "colombia", "sili", "chile", "ekvador", "ecuador", "paraguay", "peru", "venezuela", "bolivya", "bolivia" },
		["BALKAN"] = new[] { "sirbistan", "serbia", "hirvatistan", "croatia", "bosna", "bosnia", "karadag", "montenegro", "arnavutluk", "albania", "kosova", "kosovo", "kuzey makedonya", "north macedonia", "yunanistan", "greece", "bulgaristan", "bulgaria", "slovenya", "slovenia", "romanya", "romania" },
		["AFRICA"] = new[] { "nijerya", "nigeria", "fas", "morocco", "senegal", "fildisi", "ivory", "gana", "ghana", "kamerun", "cameroon", "misir", "egypt", "cezayir", "algeria", "mali", "tunus", "tunisia", "gine", "guinea", "burkina", "kongo", "congo", "guney afrika", "south africa" }
	};

	private static bool MatchesRegion(string nation, string region)
	{
		if (!RegionNations.TryGetValue(region, out string[]? keys) || nation.Length == 0) return false;
		string norm = GameData.Normalize(nation);
		return keys.Any(k => norm.Contains(k));
	}

	private void UpdatePreviewVisibility()
	{
		_previewCard.Visibility = _mode == "players" && _previewToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
	}

	private void RebuildActiveChips(string pos, string region, int? ageMin, int? ageMax, int? caMin, int? caMax, int? paMin, double? maxValM,
		bool freeOnly, bool expiringOnly, bool injuredOnly, bool wonderkid, bool regenOnly, bool shortlistOnly)
	{
		_activeChips.Children.Clear();
		void Chip(string text, Action remove)
		{
			var btn = new Button
			{
				Content = text + "  ✕",
				Background = Build.Res("AccentSoft"),
				BorderBrush = Build.Res("Accent"),
				Foreground = Build.Res("Text"),
				FontSize = 11,
				Padding = new Thickness(8, 2, 8, 2),
				Margin = new Thickness(0, 0, 5, 2)
			};
			btn.Click += (_, _) => { remove(); Apply(); };
			_activeChips.Children.Add(btn);
		}
		if (pos.Length > 0) Chip(Build.T($"Mevki: {pos}", $"Pos: {pos}"), () => _pos.SelectedValue = "");
		if (region.Length > 0) Chip(Build.T($"Bölge: {region}", $"Region: {region}"), () => _region.SelectedValue = "");
		if (ageMin.HasValue) Chip(Build.T($"Yaş ≥ {ageMin}", $"Age ≥ {ageMin}"), () => { _ageMin.SelectedIndex = -1; _ageMin.Text = ""; });
		if (ageMax.HasValue) Chip(Build.T($"Yaş ≤ {ageMax}", $"Age ≤ {ageMax}"), () => { _ageMax.SelectedIndex = -1; _ageMax.Text = ""; });
		if (caMin.HasValue) Chip($"CA ≥ {caMin}", () => { _caMin.SelectedIndex = -1; _caMin.Text = ""; });
		if (caMax.HasValue) Chip($"CA ≤ {caMax}", () => { _caMax.SelectedIndex = -1; _caMax.Text = ""; });
		if (paMin.HasValue) Chip($"PA ≥ {paMin}", () => { _paMin.SelectedIndex = -1; _paMin.Text = ""; });
		if (maxValM.HasValue) Chip($"≤ {maxValM:0.#}M{Money.Current.Symbol}", () => { _maxValM.SelectedIndex = -1; _maxValM.Text = ""; });
		if (freeOnly) Chip(Build.T("Serbest", "Free"), () => _freeOnly.IsChecked = false);
		if (expiringOnly) Chip(Build.T("Sözl. ≤1y", "Exp. ≤1y"), () => _expiringOnly.IsChecked = false);
		if (injuredOnly) Chip(Build.T("Sakat/Cezalı", "Inj/Ban"), () => _injuredOnly.IsChecked = false);
		if (wonderkid) Chip("Wonderkid", () => _wonderkid.IsChecked = false);
		if (regenOnly) Chip(Build.T("🧬 Regen", "🧬 Newgen"), () => _regenOnly.IsChecked = false);
		if (shortlistOnly) Chip(Build.T("★ Kısa Liste", "★ Shortlist"), () => _shortlistOnly.IsChecked = false);
		if (_scoutFilter.IsActive) Chip(Build.T("🔍 Gelişmiş", "🔍 Advanced"), () => { _scoutFilter.Clear(); _advFilterBtn.Style = (Style)Application.Current.FindResource(typeof(Button)); });
		_activeChips.Visibility = _activeChips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	private void ShowPreview(PlayerRow p, DataGrid grid)
	{
		var stack = new StackPanel { Margin = new Thickness(12, 12, 12, 12) };
		var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
		var av = Build.Avatar(ImageLookup.Graphics.Person(p.Uid), p.Name, 52, round: true);
		av.Margin = new Thickness(0, 0, 10, 0);
		head.Children.Add(av);
		var hText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		hText.Children.Add(Build.Text(p.Name, 14.5, Build.Res("Text"), FontWeights.Bold));
		hText.Children.Add(Build.Text($"{p.Positions} · {p.Age} {Build.T("yaş", "yo")}", 12, Build.Res("Muted")));
		if (p.Club.Length > 0) hText.Children.Add(Build.Text(p.Club, 11.5, Build.Res("Accent"), FontWeights.SemiBold));
		head.Children.Add(hText);
		stack.Children.Add(head);

		stack.Children.Add(PlayerVisuals.AbilityBar(p.CA, p.PA));

		byte[] raw69 = new byte[69];
		if (_data.Memory.TryRead(p.Address + 0x150, raw69))
		{
			stack.Children.Add(PlayerVisuals.RadarChart(204, PlayerVisuals.ComputeRadar8(raw69)));
			var top3 = ScoutRating.EvaluateTop3(raw69);
			if (top3.Count > 0)
			{
				var roles = Build.Text(string.Join("  ·  ", top3.Select(r => $"{r.Role} {r.Rating:0.0}%")), 11.5, Build.Res("Muted"), FontWeights.SemiBold);
				roles.HorizontalAlignment = HorizontalAlignment.Center;
				roles.Margin = new Thickness(0, 4, 0, 8);
				stack.Children.Add(roles);
			}
		}

		void InfoLine(string k, string v)
		{
			var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
			var vt = Build.Text(v, 12, Build.Res("Text"), FontWeights.SemiBold);
			DockPanel.SetDock(vt, Dock.Right);
			d.Children.Add(vt);
			d.Children.Add(Build.Text(k, 11.5, Build.Res("Muted")));
			stack.Children.Add(d);
		}
		InfoLine(Build.T("Değer", "Value"), Money.Format(p.Value, false));
		InfoLine(Build.T("Maaş", "Wage"), Money.Format(p.Wage, true));
		InfoLine(Build.T("Sözleşme", "Contract"), p.ContractEnd.Length > 0 ? p.ContractEnd : "—");
		InfoLine(Build.T("Profesyonellik / İstikrar", "Pro / Consistency"), $"{p.Professionalism} / {p.Consistency}");

		var btns = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
		var openBtn = new Button { Content = Build.T("Profili Aç", "Open Profile"), Style = Build.StyleOf("AccentButton"), Margin = new Thickness(0, 0, 0, 6) };
		openBtn.Click += (_, _) => _nav.OpenPlayer(p.Address);
		btns.Children.Add(openBtn);
		var subBtns = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
		var starBtn = new Button
		{
			Content = p.IsShortlisted ? Build.T("★ Listede", "★ Listed") : Build.T("☆ Kısa Liste", "☆ Shortlist"),
			Margin = new Thickness(0, 0, 3, 0),
			FontSize = 11.5
		};
		starBtn.Click += (_, _) =>
		{
			var cur = new HashSet<uint>(AppSettings.Current.GetShortlist(_data.SaveKey));
			if (cur.Contains(p.Uid)) { cur.Remove(p.Uid); p.IsShortlisted = false; }
			else { cur.Add(p.Uid); p.IsShortlisted = true; }
			AppSettings.Current.SetShortlist(_data.SaveKey, cur.ToList());
			AppSettings.Current.Save();
			grid.Items.Refresh();
			ShowPreview(p, grid);
		};
		var cmpBtn = new Button { Content = Build.T("⚖ Karşılaştır", "⚖ Compare"), Margin = new Thickness(3, 0, 0, 0), FontSize = 11.5 };
		cmpBtn.Click += (_, _) =>
		{
			var other = _lastPlayers.FirstOrDefault(x => x.Address != p.Address);
			if (other != null) new PlayerCompareWindow(_data, p, other, _nav) { Owner = Window.GetWindow(this) }.ShowDialog();
		};
		subBtns.Children.Add(starBtn);
		subBtns.Children.Add(cmpBtn);
		btns.Children.Add(subBtns);
		stack.Children.Add(btns);

		_previewContent.Content = Build.Scroll(stack);
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
