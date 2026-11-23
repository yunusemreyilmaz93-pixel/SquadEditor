using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;
using SquadEditor.Core.Layout;

namespace SquadEditor.App.Ui;

public interface IPage
{
	string Key { get; }
	string Title { get; }
	ImageSource? Icon { get; }
	void RefreshValues();
}

public interface INavigator
{
	void OpenPlayer(ulong address);
	void OpenClub(ulong address);
	void OpenStaff(ulong address);
	void OpenNation(ulong address);
	void OpenComp(ulong address);
	void SetStatus(string text);
}

public sealed class PlayerPage : UserControl, IPage
{
	private static readonly (string Code, double X, double Y)[] Pitch =
	{
		("ST", 0.5, 0.09), ("AML", 0.21, 0.24), ("AMC", 0.5, 0.24), ("AMR", 0.79, 0.24),
		("ML", 0.21, 0.40), ("MC", 0.5, 0.40), ("MR", 0.79, 0.40),
		("WBL", 0.21, 0.555), ("DM", 0.5, 0.555), ("WBR", 0.79, 0.555),
		("DL", 0.21, 0.71), ("DC", 0.5, 0.71), ("DR", 0.79, 0.71),
		("SW", 0.5, 0.83), ("GK", 0.5, 0.94)
	};

	public static readonly HashSet<string> FreezeGroups = new HashSet<string>
	{
		"Technical", "Goalkeeping", "Mental", "Physical", "Feet", "Positions", "Hidden", "Personality"
	};

	private readonly GameData _data;
	private readonly INavigator _nav;
	private readonly Dictionary<string, EditField> _f;
	private readonly DockPanel _root = new DockPanel();
	private Border? _header;
	private bool _showGoalkeeping;

	public PlayerPage(GameData data, ulong address, INavigator nav)
	{
		_data = data;
		_nav = nav;
		Address = address;
		string lang = Loc.Instance.Language;
		_f = data.PlayerDef.Fields
			.Where(f => f.GroupKey != "Internal" && f.Concat.Count == 0)
			.ToDictionary(f => f.Name, f => new EditField(data, address, f, data.Layout.Text(lang, f.Name)));
		Name = data.Read(address, data.PlayerDef, "Name");
		Uid = (uint)_f["Unique ID"].Number;
		_showGoalkeeping = _f["GK"].Number >= 15;
		EditField.Written += OnWritten;
		Unloaded += (_, _) => EditField.Written -= OnWritten;
		Loaded += (_, _) => { EditField.Written -= OnWritten; EditField.Written += OnWritten; FieldBox.EntityLabel = Name; };

		_header = BuildHeader();
		DockPanel.SetDock(_header, Dock.Top);
		_root.Children.Add(_header);
		Border ribbon = new Border
		{
			Background = Build.Res("Chrome"),
			BorderBrush = Build.Res("StrokeSoft"),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(16, 6, 16, 6),
			Child = ActionsPanel()
		};
		DockPanel.SetDock(ribbon, Dock.Top);
		_root.Children.Add(ribbon);
		Grid body = Build.PageBody(out StackPanel menu, out ContentControl content);
		Build.NavItem(menu, Build.T("Genel Bakış", "Overview"), () => content.Content = Overview(), isChecked: true);
		Build.NavItem(menu, Build.T("Kişisel ve Sözleşme", "Personal & Contract"), () => content.Content = Personal());
		Build.NavItem(menu, Build.T("Mevkiler", "Positions"), () => content.Content = Positions());
		_root.Children.Add(body);
		Content = _root;
		Background = Build.Res("Bg");
	}

	public ulong Address { get; }
	public new string Name { get; private set; }
	public new uint Uid { get; }
	public string Key => "player:" + Address;
	public string Title => Name;
	public ImageSource? Icon => ImageLookup.Graphics.Person(Uid);

	public void RefreshValues()
	{
		foreach (EditField f in _f.Values)
		{
			f.Refresh();
		}
	}

	private void OnWritten(object? sender, EditField field)
	{
		if (field.Entity != Address)
		{
			return;
		}
		if (field.Key is "CA" or "PA" or "First Name" or "Last Name" or "Wage" or "Value" or "Date of Birth" or "Contract Expires" || _f.ContainsKey(field.Key) && field.Def.GroupKey == "Positions")
		{
			Name = _data.Read(Address, _data.PlayerDef, "Name");
			Border fresh = BuildHeader();
			DockPanel.SetDock(fresh, Dock.Top);
			_root.Children.Remove(_header);
			_root.Children.Insert(0, fresh);
			_header = fresh;
		}
	}

	private Border BuildHeader()
	{
		string positions = _data.ReadPositions(Address);
		string nation = _f["Nation"].Value;
		string dob = _f["Date of Birth"].Value;
		int age = _data.AgeOn(GameData.ParseDate(dob));
		string ageText = age > 0 ? Build.T($"{age} yaşında ({Dates.Show(dob)})", $"{age} years old ({Dates.Show(dob)})") : "";
		var line1 = Build.Text(string.Join("  ·  ", new[] { positions, nation, ageText }.Where(s => s.Length > 0)), 13, new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
		line1.Margin = new Thickness(0, 4, 0, 0);

		var line2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
		ulong clubAddress = ulong.TryParse(_data.Read(Address, _data.PlayerDef, "Club Ref"), out ulong c) ? c : 0;
		if (_data.ClubsByAddress.TryGetValue(clubAddress, out ClubRow? club))
		{
			var logo = ImageLookup.Graphics.Club(club.Uid);
			if (logo != null)
			{
				line2.Children.Add(new Image { Source = logo, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) });
			}
			var link = new Button { Content = club.Name, Style = Build.StyleOf("LinkButton"), FontSize = 14, FontWeight = FontWeights.SemiBold };
			link.Click += (_, _) => _nav.OpenClub(club.Address);
			line2.Children.Add(link);
			string league = _f["League"].Value;
			if (league.Length > 0)
			{
				line2.Children.Add(Build.Text("   ·   " + league, 13, new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF))));
			}
		}
		else
		{
			line2.Children.Add(Build.Text(Build.T("Kulüpsüz", "No club"), 13, Build.Res("Muted")));
		}

		int ca = _f["CA"].Number, pa = _f["PA"].Number;
		var pills = new List<UIElement>
		{
			Build.Pill($"{ca} / {pa}", "CA / PA", Colors.Attribute((int)Math.Round(ca / 10.0))),
		};
		byte[] raw69 = new byte[69];
		if (_data.Memory.TryRead(Address + 0x150, raw69))
		{
			var (role, rating) = ScoutRating.Evaluate(raw69);
			if (role.Length > 0)
			{
				pills.Add(Build.Pill($"{role} {rating:0.0}%", Build.T("En İyi Rol", "Best Role"), Colors.Attribute((int)Math.Round(rating / 5.0))));
			}
		}
		if (_f["Value"].Number > 0)
		{
			pills.Add(Build.Pill(Money.Format(_f["Value"].Number, false), Build.T("Değer", "Value")));
		}
		if (_f["Wage"].Value.Length > 0)
		{
			pills.Add(Build.Pill(Money.Format(_f["Wage"].Number, true), Build.T("Maaş", "Wage")));
		}
		if (_f["Contract Expires"].Value.Length > 0)
		{
			pills.Add(Build.Pill(Dates.Show(_f["Contract Expires"].Value), Build.T("Sözleşme bitişi", "Contract ends")));
		}
		return Build.Header(Build.Avatar(Icon, Name, 74, round: true), Name, line1, line2, pills);
	}

	/// <summary>Attributes are listed alphabetically in the user's language, like the game.</summary>
	private IEnumerable<EditField> Group(string group)
	{
		var fields = _f.Values.Where(f => f.Def.GroupKey == group);
		return group == "Positions" ? fields : fields.OrderBy(f => f.Label, StringComparer.Create(CultureInfo.GetCultureInfo(Loc.Instance.Language == "tr" ? "tr-TR" : "en-GB"), true));
	}

	/// <summary>Current injuries and bans with Heal / Lift buttons.</summary>
	private UIElement MedicalPanel()
	{
		var host = new ContentControl();
		void Fill()
		{
			var stack = new StackPanel { Margin = new Thickness(14, 0, 14, 12) };
			var injuries = Medical.Injuries(_data.Memory, Address);
			var bans = Medical.Bans(_data.Memory, Address);
			if (injuries.Count == 0 && bans.Count == 0)
			{
				stack.Children.Add(Build.Text(Build.T("Sağlıklı, cezası yok.", "Fit, no bans."), 13, Build.Res("Positive")));
			}
			foreach (Injury i in injuries)
			{
				var t = Build.Text($"{i.Name}  ·  {i.ShortestDays}-{i.LongestDays} {Build.T("gün", "days")}" + (i.BackBy is { } back ? $"  ·  {Build.T("dönüş", "back")} {Dates.Show(back.ToString("dd.MM.yyyy"))}" : ""), 13, Build.Res("Danger"));
				t.TextWrapping = TextWrapping.Wrap;
				t.Margin = new Thickness(0, 0, 0, 4);
				stack.Children.Add(t);
			}
			foreach (Ban b in bans)
			{
				stack.Children.Add(Build.Text(Build.T($"Ceza: {b.Matches} maç", $"Ban: {b.Matches} matches"), 13, Build.Res("Danger")));
			}
			var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
			if (injuries.Count > 0)
			{
				var heal = new Button { Content = Build.T("İyileştir", "Heal"), Margin = new Thickness(0, 0, 6, 0), IsEnabled = !AppSettings.Current.ReadOnlyMode };
				heal.Click += (_, _) => { Medical.Heal(_data, Address, Name); _nav.SetStatus(Build.T($"{Name} iyileşti. Ctrl+Z geri alır.", $"{Name} healed. Ctrl+Z undoes it.")); Fill(); };
				buttons.Children.Add(heal);
			}
			if (bans.Count > 0)
			{
				var lift = new Button { Content = Build.T("Cezayı kaldır", "Lift bans"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
				lift.Click += (_, _) => { Medical.LiftBans(_data, Address, Name); _nav.SetStatus(Build.T($"{Name} için ceza kaldırıldı.", $"Bans lifted for {Name}.")); Fill(); };
				buttons.Children.Add(lift);
			}
			if (buttons.Children.Count > 0)
			{
				stack.Children.Add(buttons);
			}
			host.Content = stack;
		}
		Fill();
		return host;
	}

	/// <summary>FMRTE-style top action ribbon: freeze, heal/max fitness, extend contract, rename, transfer, loan.</summary>
	private UIElement ActionsPanel()
	{
		var host = new ContentControl();
		void Fill()
		{
			var panel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			bool canWrite = !AppSettings.Current.ReadOnlyMode;
			bool frozen = _data.Session.Freezer.IsFrozen(Address);
			var freeze = new Button
			{
				Content = frozen ? Build.T("❄ Donduruldu", "❄ Frozen") : Build.T("❄ Dondur", "❄ Freeze"),
				ToolTip = Build.T("Oyuncunun özelliklerini oyun değiştiremesin diye kilitle", "Lock player attributes so the game cannot lower them"),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 6, 0),
				IsEnabled = canWrite || frozen
			};
			if (frozen)
			{
				freeze.Style = Build.StyleOf("AccentButton");
			}
			freeze.Click += (_, _) =>
			{
				if (_data.Session.Freezer.IsFrozen(Address))
				{
					_data.Session.Freezer.Unfreeze(Address);
					_nav.SetStatus(Build.T($"{Name} artık dondurulmuş değil.", $"{Name} unfrozen."));
				}
				else
				{
					int count = _data.Session.FreezeEntity(Address, Name, _data.PlayerDef, FreezeGroups);
					_nav.SetStatus(Build.T($"{Name} donduruldu ({count} alan kilitlendi).", $"{Name} frozen ({count} fields locked)."));
				}
				Fill();
			};
			var healMax = new Button
			{
				Content = Build.T("⚡ Kondisyon & Moral Fulle", "⚡ Max Fitness & Morale"),
				ToolTip = Build.T("Sadece sakatlık/cezayı kaldırır ve kondisyon, maç formu, morali %100 yapar (yetenek özelliklerine dokunmaz)", "Only heals injuries/bans and maxes condition, sharpness & morale (does not touch attributes)"),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 6, 0),
				IsEnabled = canWrite
			};
			healMax.Click += (_, _) =>
			{
				using (_data.Session.History.BeginBatch(Build.T($"{Name}: İyileştir & Fulle", $"{Name}: Heal & Max")))
				{
					Medical.Heal(_data, Address, Name);
					Medical.LiftBans(_data, Address, Name);
					foreach (string fn in new[] { "Condition", "Match Sharpness", "Morale" })
					{
						if (_f.TryGetValue(fn, out EditField? ef) && ef.Def.Max.HasValue)
						{
							try { _data.Session.WriteField(Address, ef.Def, ef.Def.Max.Value.ToString(CultureInfo.InvariantCulture), Name); } catch (Exception) { }
						}
					}
					if (_f.TryGetValue("Jadedness", out EditField? jad))
					{
						try { _data.Session.WriteField(Address, jad.Def, "-500", Name); } catch (Exception) { }
					}
				}
				RefreshValues();
				_nav.SetStatus(Build.T($"✓ {Name} iyileştirildi ve maça hazırlandı. Ctrl+Z geri alır.", $"✓ {Name} healed and maxed. Ctrl+Z undoes it."));
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
						_data.Session.WriteField(Address, ce.Def, next.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), Name);
						RefreshValues();
						RebuildHeader();
						_nav.SetStatus(Build.T($"✓ {Name} sözleşmesi {Dates.Show(next.ToString("dd.MM.yyyy"))} tarihine uzatıldı. Ctrl+Z geri alır.",
							$"✓ {Name}'s contract extended to {Dates.Show(next.ToString("dd.MM.yyyy"))}. Ctrl+Z undoes it."));
					}
				};
				return btn;
			}
			var rename = new Button { Content = Build.T("Adı değiştir", "Rename"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = canWrite };
			rename.Click += (_, _) =>
			{
				var dialog = new RenameWindow(_data, Address + 0x288, Name) { Owner = Window.GetWindow(this) };
				if (dialog.ShowDialog() == true)
				{
					string old = Name;
					RefreshValues();
					RebuildHeader();
					_data.Renamed(Address, Name);
					_nav.SetStatus(Build.T($"{old} → {Name}. Ctrl+Z geri alır. Oyunda görmek için profili yeniden aç.", $"{old} → {Name}. Ctrl+Z undoes it. Reopen the profile in the game to see it."));
				}
			};
			var move = new Button { Content = Build.T("Kulübe taşı", "Move to club"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = canWrite };
			move.Click += (_, _) =>
			{
				bool freeAgent = _data.Memory.Read<ulong>(Address + 0x130) == 0 && _data.Memory.Read<ulong>(Address + 0x330) == 0;
				var dialog = MoveWindow.ForPlayer(_data, Address, Name, freeAgent);
				dialog.Owner = Window.GetWindow(this);
				if (dialog.ShowDialog() == true && dialog.Done is { } done)
				{
					_data.PlayerMoved(Address, done.NewClub, done.NewTeam);
					RefreshValues();
					RebuildHeader();
					Fill();
					_nav.SetStatus(done.Summary + Build.T(" Ctrl+Z geri alır.", " Ctrl+Z undoes it."));
				}
			};
			panel.Children.Add(freeze);
			panel.Children.Add(healMax);
			panel.Children.Add(ExtendBtn(1));
			panel.Children.Add(ExtendBtn(3));
			panel.Children.Add(rename);
			panel.Children.Add(move);
			if (Transfers.Loan(_data.Memory, Address) is { } loan)
			{
				string from = _data.ClubsByAddress.TryGetValue(loan.FromClub, out ClubRow? f) ? f.Name : "?";
				string to = _data.ClubsByAddress.TryGetValue(loan.ToClub, out ClubRow? t) ? t.Name : "?";
				var info = Build.Text(Build.T($"  ·  Kiralık: {from} → {to}", $"  ·  Loan: {from} → {to}") + (loan.Until is { } u ? $" ({u:dd.MM.yyyy})" : ""), 12, Build.Res("Muted"));
				info.Margin = new Thickness(6, 0, 8, 0);
				panel.Children.Add(info);
				var recall = new Button { Content = Build.T("Kiradan geri çağır", "Recall"), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = canWrite };
				recall.Click += (_, _) => { if (PlanConfirm.Run(Window.GetWindow(this), _data, () => Transfers.PlanRecall(_data, Address), _nav)) { RefreshValues(); Fill(); } };
				var permanent = new Button { Content = Build.T("Kalıcı yap", "Make permanent"), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = canWrite };
				permanent.Click += (_, _) =>
				{
					if (PlanConfirm.Run(Window.GetWindow(this), _data, () => Transfers.PlanPermanent(_data, Address), _nav))
					{
						_data.PlayerMoved(Address, loan.ToClub, _data.Memory.Read<ulong>(Address + 0x130));
						RefreshValues();
						RebuildHeader();
						Fill();
					}
				};
				panel.Children.Add(recall);
				panel.Children.Add(permanent);
			}
			host.Content = panel;
		}
		Fill();
		return host;
	}

	private void RebuildHeader()
	{
		Name = _data.Read(Address, _data.PlayerDef, "Name");
		Border fresh = BuildHeader();
		DockPanel.SetDock(fresh, Dock.Top);
		_root.Children.Remove(_header);
		_root.Children.Insert(0, fresh);
		_header = fresh;
	}

	private IEnumerable<EditField> Named(params string[] names) => names.Where(_f.ContainsKey).Select(n => _f[n]);

	private UIElement Overview()
	{
		var grid = new Grid();
		for (int i = 0; i < 4; i++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 230 });
		}

		// Technical / Goalkeeping toggle, like the game.
		var host = new ContentControl();
		var toggle = new StackPanel { Orientation = Orientation.Horizontal };
		var tech = new RadioButton { Content = Build.T("Teknik", "Technical"), Style = Build.StyleOf("Segment"), GroupName = "tg" + Address, IsChecked = !_showGoalkeeping };
		var gk = new RadioButton { Content = Build.T("Kalecilik", "Goalkeeping"), Style = Build.StyleOf("Segment"), GroupName = "tg" + Address, IsChecked = _showGoalkeeping };
		tech.Checked += (_, _) => { _showGoalkeeping = false; host.Content = Build.Rows(Group("Technical")); };
		gk.Checked += (_, _) => { _showGoalkeeping = true; host.Content = Build.Rows(Group("Goalkeeping")); };
		toggle.Children.Add(tech);
		toggle.Children.Add(gk);
		host.Content = Build.Rows(Group(_showGoalkeeping ? "Goalkeeping" : "Technical"));

		AddColumn(grid, 0, Build.Card(Build.T("Teknik", "Technical"), host, toggle),
			Build.PresetsCard(_data, "Player", () => new[] { new SquadEditor.Core.Editing.BulkTarget(Address, Name) }, RefreshValues, _nav));
		AddColumn(grid, 1, Build.Card(Build.T("Zihinsel", "Mental"), Build.Rows(Group("Mental"))),
			Build.Card(Build.T("Gizli", "Hidden"), Build.Rows(Group("Hidden"))));
		AddColumn(grid, 2, Build.Card(Build.T("Fiziksel", "Physical"), Build.Rows(Group("Physical"))),
			Build.Card(Build.T("Ayaklar", "Feet"), Build.Rows(Group("Feet"))),
			Build.Card(Build.T("Kişilik (1-20)", "Personality (1-20)"), Build.Rows(Group("Personality"))));
		AddColumn(grid, 3,
			Build.Card(Build.T("Yetenek", "Ability"), Build.Rows(Named("CA", "PA"))),
			Build.Card(Build.T("Sağlık ve disiplin", "Fitness & discipline"), new ContentControl { Content = MedicalPanel() }),
			Build.Card(Build.T("Durum", "Status"), Build.Rows(Named("Condition", "Match Sharpness", "Jadedness", "Morale", "Value", "Asking Price"))),
			Build.Card(Build.T("Mevki", "Position"), MiniPitch(244)));
		return Build.Scroll(grid);
	}

	private UIElement Personal()
	{
		var grid = new Grid { MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Left };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
		AddColumn(grid, 0, Build.Card(Build.T("Kişisel bilgiler", "Personal details"),
			Build.Rows(Named("First Name", "Last Name", "Common Name", "Full Name", "Unique ID", "Date of Birth", "Nation", "City of Birth", "Height"))),
			Build.Card(Build.T("Tercih Ettiği Hareketler", "Player Traits"), TraitsPanel()),
			Build.Card(Build.T("Milli takım", "International"), Build.Rows(Group("International"))));
		AddColumn(grid, 1, Build.Card(Build.T("Sözleşme", "Contract"), Build.Rows(Named("Club", "League", "Squad Number", "Squad Status", "Contract Type", "Wage", "Loyalty Bonus", "Contract Signed", "Joined Club", "Contract Expires", "Contract Happiness", "Value", "Asking Price"))),
			Build.Card(Build.T("Sözleşme Maddeleri ve Bonuslar", "Contract Clauses & Bonuses"), ClausesPanel()),
			Build.Card(Build.T("İtibar (0-10000)", "Reputation (0-10000)"), Build.Rows(Named("Current Reputation", "Home Reputation", "World Reputation"))));
		return Build.Scroll(grid);
	}

	private UIElement TraitsPanel()
	{
		var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(12, 4, 12, 10) };
		ulong mask = PlayerTraits.ReadMask(_data.Memory, Address);
		bool canWrite = !AppSettings.Current.ReadOnlyMode;
		foreach (var (bit, tr, en) in PlayerTraits.All)
		{
			int b = bit;
			string label = Build.T(tr, en);
			var cb = new CheckBox
			{
				Content = label,
				Style = Build.StyleOf("FilterChip"),
				IsChecked = (mask & (1UL << b)) != 0,
				IsEnabled = canWrite,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Margin = new Thickness(3)
			};
			cb.Click += (_, _) =>
			{
				try
				{
					PlayerTraits.Toggle(_data, Address, b, label, Name);
					_nav.SetStatus(Build.T($"✓ {Name} · {label} güncellendi. Ctrl+Z geri alır.", $"✓ {Name} · {label} updated. Ctrl+Z undoes it."));
				}
				catch (Exception ex)
				{
					_nav.SetStatus(ex.Message);
				}
			};
			grid.Children.Add(cb);
		}
		return grid;
	}

	private UIElement ClausesPanel()
	{
		var host = new ContentControl();
		void Fill()
		{
			var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
			var clauses = ContractClauses.Read(_data.Memory, Address);
			if (clauses.Count == 0)
			{
				stack.Children.Add(Build.Text(Build.T("Aktif sözleşme maddesi veya bonus yok.", "No active contract clauses or bonuses."), 12.5, Build.Res("Muted")));
			}
			bool canWrite = !AppSettings.Current.ReadOnlyMode;
			foreach (ContractClause clause in clauses)
			{
				ContractClause current = clause;
				var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
				var del = new Button { Content = "✕", Style = Build.StyleOf("GhostButton"), Padding = new Thickness(6, 1, 6, 1), FontSize = 11, Margin = new Thickness(6, 0, 0, 0), IsEnabled = canWrite, ToolTip = Build.T("Maddeyi sil (Ctrl+Z geri alır)", "Remove clause (Ctrl+Z undoes)") };
				del.Click += (_, _) =>
				{
					ContractClauses.Remove(_data, Address, current.Index, Name);
					_nav.SetStatus(Build.T($"✓ {Name}: \"{current.Label}\" silindi. Ctrl+Z geri alır.", $"✓ {Name}: removed \"{current.Label}\". Ctrl+Z undoes it."));
					Fill();
				};
				var box = new TextBox
				{
					Text = current.ValueDisplay,
					Width = 128,
					Height = 26,
					FontSize = 12,
					Padding = new Thickness(6, 1, 6, 1),
					VerticalContentAlignment = VerticalAlignment.Center,
					IsReadOnly = !canWrite,
					HorizontalContentAlignment = HorizontalAlignment.Right
				};
				box.KeyDown += (_, e) =>
				{
					if (e.Key == System.Windows.Input.Key.Enter)
					{
						try
						{
							ContractClauses.UpdateValue(_data, current, box.Text, Name);
							_nav.SetStatus(Build.T($"✓ {Name} · {current.Label} güncellendi. Ctrl+Z geri alır.", $"✓ {Name} · {current.Label} updated. Ctrl+Z undoes it."));
							Fill();
						}
						catch (Exception ex)
						{
							_nav.SetStatus(ex.Message);
						}
					}
				};
				DockPanel.SetDock(del, Dock.Right);
				DockPanel.SetDock(box, Dock.Right);
				row.Children.Add(del);
				row.Children.Add(box);
				row.Children.Add(Build.Text(current.Label, 12.5, Build.Res("Muted")));
				stack.Children.Add(row);
			}
			if (canWrite)
			{
				bool hasRoom = ContractClauses.CanAdd(_data.Memory, Address);
				var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
				var typeBox = new ComboBox
				{
					Height = 26,
					FontSize = 11.5,
					ItemsSource = ContractClauses.Presets.Select(p => new KeyValuePair<byte, string>(p.TypeId, Build.T(p.Tr, p.En))).ToList(),
					DisplayMemberPath = "Value",
					SelectedValuePath = "Key",
					SelectedIndex = 0,
					Margin = new Thickness(0, 0, 6, 0)
				};
				var valBox = new TextBox
				{
					Width = 92,
					Height = 26,
					FontSize = 12,
					Padding = new Thickness(6, 1, 6, 1),
					VerticalContentAlignment = VerticalAlignment.Center,
					HorizontalContentAlignment = HorizontalAlignment.Right,
					Margin = new Thickness(0, 0, 6, 0),
					ToolTip = Build.T($"Tutar ({Money.Current.Code}) veya yüzde (örn. 15)", $"Amount ({Money.Current.Code}) or percent (e.g. 15)")
				};
				var addBtn = new Button
				{
					Content = Build.T("+ Ekle", "+ Add"),
					Padding = new Thickness(10, 2, 10, 2),
					Height = 26,
					FontSize = 11.5,
					ToolTip = hasRoom
						? Build.T("Yeni sözleşme maddesi veya bonus ekle (Ctrl+Z geri alır)", "Add a new contract clause or bonus (Ctrl+Z undoes)")
						: Build.T("Oyun belleğinde bu sözleşme için boş yer yok; önce mevcut bir maddeyi silersen yer açılır", "No spare capacity in this contract vector; removing a clause frees a slot")
				};
				addBtn.Click += (_, _) =>
				{
					if (typeBox.SelectedValue is not byte tid) return;
					var preset = ContractClauses.Presets.First(p => p.TypeId == tid);
					try
					{
						ContractClauses.Add(_data, Address, preset.TypeId, preset.IsMoney, valBox.Text, Name);
						_nav.SetStatus(Build.T($"✓ {Name} · {Build.T(preset.Tr, preset.En)} eklendi. Ctrl+Z geri alır.", $"✓ {Name} · added {preset.En}. Ctrl+Z undoes it."));
						Fill();
					}
					catch (Exception ex)
					{
						_nav.SetStatus(ex.Message);
					}
				};
				DockPanel.SetDock(addBtn, Dock.Right);
				DockPanel.SetDock(valBox, Dock.Right);
				addRow.Children.Add(addBtn);
				addRow.Children.Add(valBox);
				addRow.Children.Add(typeBox);
				stack.Children.Add(addRow);
			}
			host.Content = stack;
		}
		Fill();
		return host;
	}

	private UIElement Positions()
	{
		var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
		Border pitchCard = Build.Card(Build.T("Saha", "Pitch"), MiniPitch(345));
		pitchCard.VerticalAlignment = VerticalAlignment.Stretch;
		Border ratingsCard = Build.Card(Build.T("Mevki puanları (1-20)", "Position ratings (1-20)"), Build.Rows(Group("Positions")));
		ratingsCard.VerticalAlignment = VerticalAlignment.Stretch;
		Grid.SetColumn(pitchCard, 0);
		Grid.SetColumn(ratingsCard, 1);
		grid.Children.Add(pitchCard);
		grid.Children.Add(ratingsCard);
		return Build.Scroll(grid);
	}

	/// <summary>Real football pitch with penalty boxes, center circle, grass stripes and crisp square position badges.</summary>
	private FrameworkElement MiniPitch(double width)
	{
		double w = width, h = width * 1.32;
		var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
		var lineBrush = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
		var turf = new Border
		{
			Width = w,
			Height = h,
			CornerRadius = new CornerRadius(6),
			Background = new SolidColorBrush(Color.FromRgb(0x14, 0x38, 0x26)),
			BorderBrush = lineBrush,
			BorderThickness = new Thickness(1)
		};
		canvas.Children.Add(turf);
		// Subtle mowed grass stripes
		int stripes = 8;
		double sh = h / stripes;
		for (int i = 0; i < stripes; i += 2)
		{
			var stripe = new Border { Width = w - 2, Height = sh, Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)) };
			Canvas.SetLeft(stripe, 1);
			Canvas.SetTop(stripe, i * sh);
			canvas.Children.Add(stripe);
		}
		// Halfway line & center circle
		var half = new Border { Width = w, Height = 1, Background = lineBrush };
		Canvas.SetTop(half, h / 2);
		canvas.Children.Add(half);
		double cr = w * 0.24;
		var circle = new System.Windows.Shapes.Ellipse { Width = cr, Height = cr, Stroke = lineBrush, StrokeThickness = 1 };
		Canvas.SetLeft(circle, (w - cr) / 2);
		Canvas.SetTop(circle, (h - cr) / 2);
		canvas.Children.Add(circle);
		// Top & bottom penalty boxes and 6-yard boxes
		double pbW = w * 0.56, pbH = h * 0.145, gbW = w * 0.28, gbH = h * 0.055;
		void Box(double bw2, double bh2, double top, Thickness th)
		{
			var b = new Border { Width = bw2, Height = bh2, BorderBrush = lineBrush, BorderThickness = th };
			Canvas.SetLeft(b, (w - bw2) / 2);
			Canvas.SetTop(b, top);
			canvas.Children.Add(b);
		}
		Box(pbW, pbH, 0, new Thickness(1, 0, 1, 1));
		Box(gbW, gbH, 0, new Thickness(1, 0, 1, 1));
		Box(pbW, pbH, h - pbH, new Thickness(1, 1, 1, 0));
		Box(gbW, gbH, h - gbH, new Thickness(1, 1, 1, 0));

		bool large = width >= 320;
		double bw = large ? 82 : 66, bh = large ? 28 : 24;
		foreach (var (code, x, y) in Pitch)
		{
			EditField f = _f[code];
			var cell = new DockPanel { LastChildFill = true };
			var label = Build.Text(code, large ? 11.5 : 10, Brushes.White, FontWeights.SemiBold);
			label.Margin = new Thickness(6, 0, 2, 0);
			label.TextTrimming = TextTrimming.None;
			var box = new TextBox
			{
				Width = large ? 30 : 26,
				Height = bh - 4,
				FontSize = large ? 12.5 : 11.5,
				FontWeight = FontWeights.Bold,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				Padding = new Thickness(0),
				Margin = new Thickness(0, 0, 2, 0),
				Background = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)),
				BorderThickness = new Thickness(0)
			};
			FieldBox.SetField(box, f);
			DockPanel.SetDock(box, Dock.Right);
			cell.Children.Add(box);
			cell.Children.Add(label);
			var chip = new Border
			{
				Width = bw,
				Height = bh,
				CornerRadius = new CornerRadius(5),
				Background = PositionBrush(f.Number),
				BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
				BorderThickness = new Thickness(1),
				Child = cell
			};
			f.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EditField.Value)) chip.Background = PositionBrush(f.Number); };
			Canvas.SetLeft(chip, x * w - bw / 2);
			Canvas.SetTop(chip, y * h - bh / 2);
			canvas.Children.Add(chip);
		}
		return new Border { Padding = new Thickness(10, 4, 10, 12), HorizontalAlignment = HorizontalAlignment.Center, Child = canvas };
	}

	private static Brush PositionBrush(int rating) => new SolidColorBrush(rating switch
	{
		>= 20 => Color.FromArgb(0xE0, 0x2F, 0xA8, 0x5A),
		>= 15 => Color.FromArgb(0xD0, 0x7C, 0xB8, 0x3A),
		>= 12 => Color.FromArgb(0xD0, 0xC9, 0xA2, 0x27),
		>= 9 => Color.FromArgb(0xC0, 0xD0, 0x76, 0x2C),
		>= 2 => Color.FromArgb(0xB0, 0xA8, 0x3E, 0x3E),
		_ => Color.FromArgb(0x50, 0x10, 0x18, 0x24)
	});

	private static void AddColumn(Grid grid, int column, params UIElement[] cards)
	{
		var stack = new StackPanel();
		foreach (UIElement c in cards)
		{
			stack.Children.Add(c);
		}
		Grid.SetColumn(stack, column);
		grid.Children.Add(stack);
	}
}
