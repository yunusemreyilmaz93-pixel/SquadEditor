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
	private string _highlightRole = "";
	private bool _paPreview;
	private ContentControl? _contentHost;
	private bool _onOverview = true;

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
		byte[] initRaw = new byte[69];
		if (data.Memory.TryRead(address + 0x150, initRaw))
		{
			_highlightRole = ScoutRating.Evaluate(initRaw).Role;
		}
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
		_contentHost = content;
		Build.NavItem(menu, Build.T("Genel Bakış", "Overview"), () => { _onOverview = true; content.Content = Overview(); }, isChecked: true);
		Build.NavItem(menu, Build.T("Kişisel ve Sözleşme", "Personal & Contract"), () => { _onOverview = false; content.Content = Personal(); });
		Build.NavItem(menu, Build.T("Mevkiler", "Positions"), () => { _onOverview = false; content.Content = Positions(); });
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
		string ageText = age > 0 ? Build.F("{0} yaşında ({1})", "{0} years old ({1})", age, Dates.Show(dob)) : "";
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
				var (_, pr) = ScoutRating.EvaluatePotential(raw69, ca, pa);
				string roleText = pa > ca + 2 && pr > rating + 0.4
					? $"{role} {rating:0.0}% → {pr:0.0}%"
					: $"{role} {rating:0.0}%";
				pills.Add(Build.Pill(roleText, pa > ca + 2 ? Build.T("Rol / PR %", "Role / PR %") : Build.T("En İyi Rol", "Best Role"), Colors.Attribute((int)Math.Round(rating / 5.0))));
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
		return group == "Positions" ? fields : fields.OrderBy(f => f.Label, StringComparer.Create(SquadEditor.Core.CoreText.Culture, true));
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
				stack.Children.Add(Build.Text(Build.F("Ceza: {0} maç", "Ban: {0} matches", b.Matches), 13, Build.Res("Danger")));
			}
			var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
			if (injuries.Count > 0)
			{
				var heal = new Button { Content = Build.T("İyileştir", "Heal"), Margin = new Thickness(0, 0, 6, 0), IsEnabled = !AppSettings.Current.ReadOnlyMode };
				heal.Click += (_, _) => { Medical.Heal(_data, Address, Name); _nav.SetStatus(Build.F("{0} iyileşti. Ctrl+Z geri alır.", "{0} healed. Ctrl+Z undoes it.", Name)); Fill(); };
				buttons.Children.Add(heal);
			}
			if (bans.Count > 0)
			{
				var lift = new Button { Content = Build.T("Cezayı kaldır", "Lift bans"), IsEnabled = !AppSettings.Current.ReadOnlyMode };
				lift.Click += (_, _) => { Medical.LiftBans(_data, Address, Name); _nav.SetStatus(Build.F("{0} için ceza kaldırıldı.", "Bans lifted for {0}.", Name)); Fill(); };
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
					_nav.SetStatus(Build.F("{0} artık dondurulmuş değil.", "{0} unfrozen.", Name));
				}
				else
				{
					int count = _data.Session.FreezeEntity(Address, Name, _data.PlayerDef, FreezeGroups);
					_nav.SetStatus(Build.F("{0} donduruldu ({1} alan kilitlendi).", "{0} frozen ({1} fields locked).", Name, count));
				}
				Fill();
			};
			bool inShortlist = AppSettings.Current.GetShortlist(_data.SaveKey).Contains(Uid);
			var shortlistBtn = new Button
			{
				Content = inShortlist ? Build.T("★ Kısa Listede", "★ Shortlisted") : Build.T("☆ Kısa Listeye Ekle", "☆ Add to Shortlist"),
				ToolTip = Build.T("Oyuncuyu kendi kısa listene ekle veya çıkar (Ana Sayfadaki ★ Kısa Liste filtresinde görünür)", "Add or remove this player from your save's shortlist"),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 6, 0)
			};
			if (inShortlist)
			{
				shortlistBtn.Style = Build.StyleOf("AccentButton");
			}
			shortlistBtn.Click += (_, _) =>
			{
				var list = new HashSet<uint>(AppSettings.Current.GetShortlist(_data.SaveKey));
				if (list.Contains(Uid))
				{
					list.Remove(Uid);
					_nav.SetStatus(Build.F("{0} kısa listeden çıkarıldı.", "{0} removed from shortlist.", Name));
				}
				else
				{
					list.Add(Uid);
					_nav.SetStatus(Build.F("★ {0} kısa listeye eklendi.", "★ {0} added to shortlist.", Name));
				}
				AppSettings.Current.SetShortlist(_data.SaveKey, list.ToList());
				AppSettings.Current.Save();
				Fill();
			};
			var healMax = new Button
			{
				Content = Build.T("⚡ Kondisyon, Moral & Mutluluk Fulle", "⚡ Max Fitness, Morale & Happiness"),
				ToolTip = Build.T("Sakatlık/cezayı kaldırır; kondisyon, maç formu, moral ve sözleşme mutluluğunu %100 yapar (yetenek özelliklerine dokunmaz)", "Heals injuries/bans and maxes condition, sharpness, morale & contract happiness (does not touch attributes)"),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 6, 0),
				IsEnabled = canWrite
			};
			healMax.Click += (_, _) =>
			{
				using (_data.Session.History.BeginBatch(Build.F("{0}: İyileştir & Fulle", "{0}: Heal & Max", Name)))
				{
					Medical.Heal(_data, Address, Name);
					Medical.LiftBans(_data, Address, Name);
					foreach (string fn in new[] { "Condition", "Match Sharpness", "Morale", "Contract Happiness" })
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
				_nav.SetStatus(Build.F("✓ {0} iyileştirildi; kondisyon, moral ve mutluluk fullendi. Ctrl+Z geri alır.", "✓ {0} healed; fitness, morale & happiness maxed. Ctrl+Z undoes it.", Name));
			};
			Button ExtendBtn(int years)
			{
				var btn = new Button
				{
					Content = Build.F("📝 +{0} Yıl", "📝 +{0}y", years),
					ToolTip = Build.F("Sözleşme bitişini {0} yıl uzat", "Extend contract expiry by {0} year(s)", years),
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
						_nav.SetStatus(Build.F("✓ {0} sözleşmesi {1} tarihine uzatıldı. Ctrl+Z geri alır.", "✓ {0}'s contract extended to {1}. Ctrl+Z undoes it.", Name, Dates.Show(next.ToString("dd.MM.yyyy"))));
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
					_nav.SetStatus(Build.F("{0} → {1}. Ctrl+Z geri alır. Oyunda görmek için profili yeniden aç.", "{0} → {1}. Ctrl+Z undoes it. Reopen the profile in the game to see it.", old, Name));
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
			panel.Children.Add(shortlistBtn);
			panel.Children.Add(healMax);
			var cohBtn = new Button
			{
				Content = Build.T("🤝 Uyum %100", "🤝 Max Cohesion"),
				ToolTip = Build.T("Oyuncunun kulüp uyumunu ve tüm taktik yatkınlıklarını %100 yapar (Ctrl+Z geri alır)",
					"Maxes player's club cohesion and tactical familiarity to 100% (Ctrl+Z undoes)"),
				Padding = new Thickness(9, 4, 9, 4),
				Margin = new Thickness(0, 0, 6, 0),
				IsEnabled = canWrite
			};
			cohBtn.Click += (_, _) =>
			{
				ulong clubAddr = ulong.TryParse(_data.Read(Address, _data.PlayerDef, "Club Ref"), out ulong ca) ? ca : 0;
				bool coh;
				int tac;
				using (_data.Session.History.BeginBatch(Build.F("{0}: Uyum & Taktik %100", "{0}: Max Cohesion & Tactics", Name)))
				{
					coh = TeamCohesion.MaxPlayerCohesion(_data, Address, clubAddr, Name);
					tac = TeamCohesion.MaxPlayerTacticalFamiliarity(_data, Address, Name);
				}
				_nav.SetStatus(coh || tac > 0
					? Build.F("✓ {0}: Takım uyumu ve taktik yatkınlığı ({1} alan) %100 yapıldı. Ctrl+Z geri alır.", "✓ {0}: Team cohesion & tactical familiarity ({1} entries) maxed to 100%. Ctrl+Z undoes it.", Name, tac)
					: Build.F("✓ {0} zaten %100 uyum ve taktik yatkınlığında.", "✓ {0} is already at 100% cohesion & tactical familiarity.", Name));
			};
			panel.Children.Add(cohBtn);
			panel.Children.Add(ExtendBtn(1));
			panel.Children.Add(ExtendBtn(3));
			panel.Children.Add(rename);
			panel.Children.Add(move);

			// Role highlight picker (FM-style key/secondary attribute highlighting)
			var roleItems = new List<KeyValuePair<string, string>> { new("", Build.T("Rol vurgusu: Kapalı", "Role highlight: Off")) };
			roleItems.AddRange(ScoutRating.AllRoles().Select(r => new KeyValuePair<string, string>(r.Code, r.Label)));
			var rolePicker = new ComboBox
			{
				Width = 178,
				Height = 26,
				FontSize = 11.5,
				Margin = new Thickness(4, 0, 6, 0),
				ItemsSource = roleItems,
				DisplayMemberPath = "Value",
				SelectedValuePath = "Key",
				SelectedValue = _highlightRole,
				ToolTip = Build.T("Seçili rolün kritik (yeşil) ve ikincil (mavi) özelliklerini vurgular", "Highlights key (green) and secondary (blue) attributes for the chosen role")
			};
			rolePicker.SelectionChanged += (_, _) =>
			{
				_highlightRole = rolePicker.SelectedValue as string ?? "";
				if (_onOverview && _contentHost != null) _contentHost.Content = Overview();
			};
			panel.Children.Add(rolePicker);

			var compare = new Button
			{
				Content = Build.T("⚖ Karşılaştır", "⚖ Compare"),
				ToolTip = Build.T("Başka bir oyuncuyla radar ve özellik farklarını yan yana kıyasla", "Compare radar & attribute deltas side-by-side with another player"),
				Padding = new Thickness(9, 4, 9, 4),
				Margin = new Thickness(0, 0, 6, 0)
			};
			compare.Click += (_, _) => OpenComparePicker();
			panel.Children.Add(compare);

			int curCa = _f["CA"].Number, curPa = _f["PA"].Number;
			if (curPa > curCa)
			{
				var paPrevBtn = new Button
				{
					Content = _paPreview ? Build.T("🔮 PA Önizleme: AÇIK", "🔮 PA Preview: ON") : Build.T("🔮 PA Önizleme", "🔮 PA Preview"),
					ToolTip = Build.T("Oyuncunun PA zirvesine ulaştığında özelliklerinin nasıl görüneceğini salt okunur önizle (belleğe yazmaz)", "Read-only preview of projected 1–20 attributes at peak PA (never writes to memory)"),
					Padding = new Thickness(9, 4, 9, 4),
					Margin = new Thickness(0, 0, 6, 0)
				};
				if (_paPreview) paPrevBtn.Style = Build.StyleOf("AccentButton");
				paPrevBtn.Click += (_, _) =>
				{
					_paPreview = !_paPreview;
					Fill();
					if (_onOverview && _contentHost != null) _contentHost.Content = Overview();
				};
				panel.Children.Add(paPrevBtn);
			}

			var pngBtn = new Button
			{
				Content = Build.T("📸 Kart PNG", "📸 Card PNG"),
				ToolTip = Build.T("Paylaşılabilir FM oyuncu kartını PNG olarak kaydet ve panoya kopyala", "Save shareable FM player card as PNG and copy to clipboard"),
				Padding = new Thickness(9, 4, 9, 4),
				Margin = new Thickness(0, 0, 6, 0)
			};
			pngBtn.Click += (_, _) => PlayerCardExport.Export(_data, Address, Name, Uid, _nav);
			panel.Children.Add(pngBtn);
			if (Transfers.Loan(_data.Memory, Address) is { } loan)
			{
				string from = _data.ClubsByAddress.TryGetValue(loan.FromClub, out ClubRow? f) ? f.Name : "?";
				string to = _data.ClubsByAddress.TryGetValue(loan.ToClub, out ClubRow? t) ? t.Name : "?";
				var info = Build.Text(Build.F("  ·  Kiralık: {0} → {1}", "  ·  Loan: {0} → {1}", from, to) + (loan.Until is { } u ? $" ({u:dd.MM.yyyy})" : ""), 12, Build.Res("Muted"));
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

	private void OpenComparePicker()
	{
		PlayerRow? self = _data.Players.FirstOrDefault(p => p.Address == Address);
		if (self == null) return;
		var dlg = new Window
		{
			Title = Build.F("{0} ile karşılaştırılacak oyuncuyu seç", "Pick player to compare with {0}", Name),
			Width = 460,
			Height = 460,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = Window.GetWindow(this),
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text")
		};
		var dock = new DockPanel { Margin = new Thickness(14) };
		var search = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 8), ToolTip = Build.T("Oyuncu adı yaz…", "Type player name…") };
		DockPanel.SetDock(search, Dock.Top);
		var list = new ListBox { Background = Build.Res("Panel"), BorderBrush = Build.Res("StrokeSoft") };
		void RefreshList()
		{
			string q = GameData.Normalize(search.Text.Trim());
			list.ItemsSource = _data.Players
				.Where(p => p.Address != Address && (q.Length == 0 || p.Search.Contains(q)))
				.OrderByDescending(p => p.CA)
				.Take(80)
				.Select(p => new KeyValuePair<PlayerRow, string>(p, $"{p.Name}  ·  {p.Positions}  ·  CA {p.CA}  ({p.Club})"))
				.ToList();
			list.DisplayMemberPath = "Value";
		}
		search.TextChanged += (_, _) => RefreshList();
		list.MouseDoubleClick += (_, _) =>
		{
			if (list.SelectedItem is KeyValuePair<PlayerRow, string> kv)
			{
				dlg.Close();
				new PlayerCompareWindow(_data, self, kv.Key, _nav) { Owner = Window.GetWindow(this) }.ShowDialog();
			}
		};
		RefreshList();
		dock.Children.Add(search);
		dock.Children.Add(list);
		dlg.Content = dock;
		dlg.ShowDialog();
	}

	private UIElement Overview()
	{
		var grid = new Grid();
		for (int i = 0; i < 4; i++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 230 });
		}
		Func<string, int> hl = key => ScoutRating.HighlightLevel(_highlightRole, key);
		int ca = _f["CA"].Number, pa = _f["PA"].Number;
		bool showProj = _paPreview && pa > ca;

		StackPanel AttrRows(IEnumerable<EditField> fields)
		{
			if (!showProj) return Build.Rows(fields, hl);
			var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
			foreach (EditField f in fields)
			{
				int cur = f.Number;
				int proj = ScoutRating.ProjectAttribute1to20(cur, ca, pa, f.Key);
				int diff = proj - cur;
				int h = hl(f.Key);
				var row = new Grid { Height = AppSettings.Current.Compact ? 24 : 28 };
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
				var lbl = Build.Text(f.Label, 12.5, h == 2 ? Build.Res("Positive") : (h == 1 ? Build.Res("Accent") : Build.Res("Muted")), h > 0 ? FontWeights.SemiBold : FontWeights.Normal);
				lbl.Margin = new Thickness(8, 0, 8, 0);
				row.Children.Add(lbl);
				var valPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
				if (diff > 0)
				{
					valPanel.Children.Add(Build.Text($"{cur} → ", 11, Build.Res("Faint")));
					valPanel.Children.Add(Build.Text(proj.ToString(), 13, Colors.Attribute(proj), FontWeights.Bold));
					var d = Build.Text($" (+{diff})", 11, Build.Res("Positive"), FontWeights.SemiBold);
					valPanel.Children.Add(d);
				}
				else
				{
					valPanel.Children.Add(Build.Text(cur.ToString(), 13, Colors.Attribute(cur), FontWeights.Bold));
				}
				Grid.SetColumn(valPanel, 1);
				row.Children.Add(valPanel);
				panel.Children.Add(new Border
				{
					Margin = new Thickness(6, 0, 6, 0),
					CornerRadius = new CornerRadius(3),
					BorderBrush = h == 2 ? Build.Res("Positive") : (h == 1 ? Build.Res("Accent") : Brushes.Transparent),
					BorderThickness = h > 0 ? new Thickness(2, 0, 0, 0) : new Thickness(0),
					Child = row
				});
			}
			return panel;
		}

		// Technical / Goalkeeping toggle, like the game.
		var host = new ContentControl();
		var toggle = new StackPanel { Orientation = Orientation.Horizontal };
		var tech = new RadioButton { Content = Build.T("Teknik", "Technical"), Style = Build.StyleOf("Segment"), GroupName = "tg" + Address, IsChecked = !_showGoalkeeping };
		var gk = new RadioButton { Content = Build.T("Kalecilik", "Goalkeeping"), Style = Build.StyleOf("Segment"), GroupName = "tg" + Address, IsChecked = _showGoalkeeping };
		tech.Checked += (_, _) => { _showGoalkeeping = false; host.Content = AttrRows(Group("Technical")); };
		gk.Checked += (_, _) => { _showGoalkeeping = true; host.Content = AttrRows(Group("Goalkeeping")); };
		toggle.Children.Add(tech);
		toggle.Children.Add(gk);
		host.Content = AttrRows(Group(_showGoalkeeping ? "Goalkeeping" : "Technical"));

		// Ability card with dual-layer CA/PA progress bar
		var abilityStack = Build.Rows(Named("CA", "PA"));
		abilityStack.Children.Add(PlayerVisuals.AbilityBar(ca, pa));

		// 8-axis Attribute Radar Polygon (with emerald Peak PA ghost overlay when PA > CA + 3)
		byte[] raw69 = new byte[69];
		UIElement? radarCard = null;
		if (_data.Memory.TryRead(Address + 0x150, raw69))
		{
			double[] curRad = PlayerVisuals.ComputeRadar8(raw69);
			double[]? peakRad = pa > ca + 3 ? PlayerVisuals.ComputeRadar8(ScoutRating.ProjectAttributes(raw69, ca, pa)) : null;
			var radarBody = new StackPanel { Margin = new Thickness(4, 2, 4, 8) };
			radarBody.Children.Add(PlayerVisuals.RadarChart(214, curRad, peakRad));
			if (peakRad != null)
			{
				var leg = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
				leg.Children.Add(Build.Text("● " + Build.T("Mevcut", "Current"), 10.5, Build.Res("Accent"), FontWeights.SemiBold));
				var pLeg = Build.Text("   ● " + Build.F("PA Zirve ({0})", "Peak PA ({0})", pa), 10.5, Build.Res("Positive"), FontWeights.SemiBold);
				leg.Children.Add(pLeg);
				radarBody.Children.Add(leg);
			}
			radarCard = Build.Card(Build.T("Özellik Radarı", "Attribute Radar"), radarBody);
		}

		AddColumn(grid, 0, Build.Card(Build.T("Teknik", "Technical"), host, toggle),
			Build.PresetsCard(_data, "Player", () => new[] { new SquadEditor.Core.Editing.BulkTarget(Address, Name) }, () => { RefreshValues(); RebuildHeader(); if (_onOverview && _contentHost != null) _contentHost.Content = Overview(); }, _nav));
		AddColumn(grid, 1, Build.Card(Build.T("Zihinsel", "Mental"), AttrRows(Group("Mental"))),
			Build.Card(Build.T("Gizli", "Hidden"), Build.Rows(Group("Hidden"))));
		AddColumn(grid, 2, Build.Card(Build.T("Fiziksel", "Physical"), AttrRows(Group("Physical"))),
			Build.Card(Build.T("Ayaklar", "Feet"), Build.Rows(Group("Feet"))),
			Build.Card(Build.T("Kişilik (1-20)", "Personality (1-20)"), Build.Rows(Group("Personality"))));
		var col3 = new List<UIElement>
		{
			Build.Card(Build.T("Yetenek", "Ability"), abilityStack),
			Build.Card(Build.T("Sağlık ve disiplin", "Fitness & discipline"), new ContentControl { Content = MedicalPanel() }),
			Build.Card(Build.T("Durum", "Status"), Build.Rows(Named("Condition", "Match Sharpness", "Jadedness", "Morale", "Value", "Asking Price")))
		};
		if (radarCard != null) col3.Add(radarCard);
		AddColumn(grid, 3, col3.ToArray());

		if (showProj)
		{
			var wrap = new StackPanel();
			var banner = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(0x22, 0x3D, 0xD6, 0x8C)),
				BorderBrush = Build.Res("Positive"),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(12, 7, 12, 7),
				Margin = new Thickness(6, 0, 6, 8),
				Child = Build.Text(Build.F("🔮 PA Önizleme (Salt Okunur) — {0} CA → {1} PA zirvesindeki tahmini 1–20 özellikleri gösteriliyor. Gerçek belleğe yazılmaz.", "🔮 PA Preview (Read-Only) — Showing projected 1–20 attributes at peak {1} PA (from {0} CA). Never writes to memory.", ca, pa), 12, Build.Res("Positive"), FontWeights.SemiBold)
			};
			wrap.Children.Add(banner);
			wrap.Children.Add(grid);
			return Build.Scroll(wrap);
		}
		return Build.Scroll(grid);
	}

	private UIElement Personal()
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 360 });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 360 });
		var personalRows = Build.Rows(Named("First Name", "Last Name", "Common Name", "Full Name", "Unique ID", "Date of Birth", "City of Birth", "Height"));
		personalRows.Children.Insert(6, NationPickerRow());
		AddColumn(grid, 0,
			Build.Card(Build.T("Kişisel bilgiler", "Personal details"), personalRows),
			Build.Card(Build.T("🌍 İkinci Uyruklar", "🌍 Second Nationalities"), SecondNationsPanel()),
			Build.Card(Build.T("Konuştuğu Diller (1-10)", "Spoken Languages (1-10)"), LanguagesPanel()),
			Build.Card(Build.T("Milli takım", "International"), Build.Rows(Group("International"))));
		AddColumn(grid, 1,
			Build.Card(Build.T("Sözleşme", "Contract"), Build.Rows(Named("Club", "League", "Squad Number", "Squad Status", "Contract Type", "Wage", "Loyalty Bonus", "Contract Signed", "Joined Club", "Contract Expires", "Contract Happiness", "Value", "Asking Price"))),
			Build.Card(Build.T("Sözleşme Maddeleri ve Bonuslar", "Contract Clauses & Bonuses"), ClausesPanel()),
			Build.Card(Build.T("İtibar (0-10000)", "Reputation (0-10000)"), Build.Rows(Named("Current Reputation", "Home Reputation", "World Reputation"))));
		AddColumn(grid, 2,
			Build.Card(Build.T("Tercih Ettiği Hareketler", "Player Traits"), TraitsPanel()),
			Build.Card(Build.T("⭐ Favori Kulüpler & Kişiler", "⭐ Favoured Clubs & Personnel"), PlayerRelationsPanel()));
		return Build.Scroll(grid);
	}

	private UIElement NationPickerRow()
	{
		var grid = new Grid { Margin = new Thickness(6, 0, 6, 0), Height = AppSettings.Current.Compact ? 25 : 30 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var label = Build.Text(Build.T("Uyruk", "Nation"), 13, Build.Res("Muted"));
		label.Margin = new Thickness(8, 0, 8, 0);
		grid.Children.Add(label);

		ulong curAddr = PlayerIdentity.ReadPrimaryNation(_data.Memory, Address);
		var allNations = _data.Nations.OrderBy(n => n.Name).ToList();
		var combo = new ComboBox
		{
			Width = 180,
			Height = 26,
			FontSize = 12.5,
			IsEditable = true,
			IsTextSearchEnabled = true,
			StaysOpenOnEdit = true,
			IsEnabled = !AppSettings.Current.ReadOnlyMode && allNations.Count > 0,
			ItemsSource = allNations,
			DisplayMemberPath = nameof(NationRow.Name),
			SelectedValuePath = nameof(NationRow.Address),
			SelectedValue = curAddr,
			ToolTip = Build.T("Ülke adı yazarak ara veya listeden seç (Ctrl+Z geri alır)", "Type to search or pick a nation (Ctrl+Z undoes)")
		};
		combo.SelectionChanged += (_, _) =>
		{
			if (combo.SelectedItem is NationRow nr && nr.Address != PlayerIdentity.ReadPrimaryNation(_data.Memory, Address))
			{
				string oldName = _f["Nation"].Value;
				try
				{
					PlayerIdentity.SetPrimaryNation(_data, Address, nr.Address, oldName, nr.Name, Name);
					_f["Nation"].Refresh();
					RebuildHeader();
					_nav.SetStatus(Build.F("✓ {0} uyruğu {1} olarak değiştirildi. Ctrl+Z geri alır.", "✓ {0}'s nation changed to {1}. Ctrl+Z undoes it.", Name, nr.Name));
				}
				catch (Exception ex)
				{
					_nav.SetStatus(ex.Message);
				}
			}
		};
		Grid.SetColumn(combo, 1);
		grid.Children.Add(combo);
		return grid;
	}

	private UIElement SecondNationsPanel()
	{
		var host = new ContentControl();
		void Fill()
		{
			var stack = new StackPanel { Margin = new Thickness(12, 2, 12, 10) };
			var sec = PlayerIdentity.ReadRelations(_data, Address).Where(r => r.IsSecondNation).ToList();
			if (sec.Count == 0)
			{
				stack.Children.Add(Build.Text(Build.T("Kayıtlı ikinci uyruk yok.", "No second nationalities recorded."), 12, Build.Res("Muted")));
			}
			bool canWrite = !AppSettings.Current.ReadOnlyMode;
			foreach (PlayerRelation rel in sec)
			{
				PlayerRelation current = rel;
				var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
				var del = new Button { Content = "✕", Style = Build.StyleOf("GhostButton"), Padding = new Thickness(6, 1, 6, 1), FontSize = 11, IsEnabled = canWrite, ToolTip = Build.T("İkinci uyruğu sil (Ctrl+Z geri alır)", "Remove second nationality (Ctrl+Z undoes)") };
				del.Click += (_, _) =>
				{
					PlayerIdentity.RemoveRelation(_data, Address, current.Index, current.TargetName, Name);
					_nav.SetStatus(Build.F("✓ {0}: {1} ikinci uyruğu silindi. Ctrl+Z geri alır.", "✓ {0}: removed {1}. Ctrl+Z undoes it.", Name, current.TargetName));
					Fill();
				};
				DockPanel.SetDock(del, Dock.Right);
				row.Children.Add(del);
				row.Children.Add(Build.Text("🌍 " + current.TargetName, 12.5, Build.Res("Text"), FontWeights.SemiBold));
				stack.Children.Add(row);
			}
			if (canWrite && _data.Nations.Count > 0)
			{
				var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
				var picker = new ComboBox
				{
					Height = 26,
					FontSize = 12,
					IsEditable = true,
					IsTextSearchEnabled = true,
					ItemsSource = _data.Nations.OrderBy(n => n.Name).ToList(),
					DisplayMemberPath = nameof(NationRow.Name),
					SelectedValuePath = nameof(NationRow.Address),
					Margin = new Thickness(0, 0, 6, 0)
				};
				var addBtn = new Button { Content = Build.T("+ Uyruk Ekle", "+ Add Nation"), Height = 26, Padding = new Thickness(10, 2, 10, 2), FontSize = 11.5 };
				addBtn.Click += (_, _) =>
				{
					if (picker.SelectedItem is not NationRow nr) return;
					try
					{
						PlayerIdentity.AddSecondNation(_data, Address, nr.Address, nr.Name, Name);
						_nav.SetStatus(Build.F("✓ {1} için {0} ikinci uyruk eklendi. Ctrl+Z geri alır.", "✓ Added {0} as second nationality for {1}. Ctrl+Z undoes it.", nr.Name, Name));
						Fill();
					}
					catch (Exception ex) { _nav.SetStatus(ex.Message); }
				};
				DockPanel.SetDock(addBtn, Dock.Right);
				addRow.Children.Add(addBtn);
				addRow.Children.Add(picker);
				stack.Children.Add(addRow);
			}
			host.Content = stack;
		}
		Fill();
		return host;
	}

	private UIElement PlayerRelationsPanel()
	{
		var host = new ContentControl();
		void Fill()
		{
			var stack = new StackPanel { Margin = new Thickness(12, 2, 12, 10) };
			var rels = PlayerIdentity.ReadRelations(_data, Address).Where(r => !r.IsSecondNation).ToList();
			if (rels.Count == 0)
			{
				stack.Children.Add(Build.Text(Build.T("Kayıtlı favori kulüp veya kişi yok.", "No favoured clubs or personnel recorded."), 12, Build.Res("Muted")));
			}
			bool canWrite = !AppSettings.Current.ReadOnlyMode;
			foreach (PlayerRelation rel in rels)
			{
				PlayerRelation current = rel;
				var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
				var del = new Button { Content = "✕", Style = Build.StyleOf("GhostButton"), Padding = new Thickness(6, 1, 6, 1), FontSize = 11, Margin = new Thickness(6, 0, 0, 0), IsEnabled = canWrite };
				del.Click += (_, _) =>
				{
					PlayerIdentity.RemoveRelation(_data, Address, current.Index, current.TargetName, Name);
					_nav.SetStatus(Build.F("✓ {0}: {1} ilişkisi silindi. Ctrl+Z geri alır.", "✓ {0}: removed relation with {1}. Ctrl+Z undoes it.", Name, current.TargetName));
					Fill();
				};
				var strBox = new TextBox { Text = current.Strength.ToString(), Width = 50, Height = 26, FontSize = 12, Padding = new Thickness(4, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsReadOnly = !canWrite };
				strBox.KeyDown += (_, e) =>
				{
					if (e.Key == System.Windows.Input.Key.Enter && byte.TryParse(strBox.Text.Trim(), out byte v))
					{
						PlayerIdentity.SetRelationStrength(_data, current, v, Name);
						_nav.SetStatus(Build.F("✓ {0} · {1} gücü {2} yapıldı.", "✓ {0} · {1} strength set to {2}.", Name, current.TargetName, Math.Clamp(v, (byte)1, (byte)100)));
						Fill();
					}
				};
				DockPanel.SetDock(del, Dock.Right);
				DockPanel.SetDock(strBox, Dock.Right);
				row.Children.Add(del);
				row.Children.Add(strBox);
				row.Children.Add(Build.Text($"{current.Badge} · {current.TargetName}", 12, Build.Res("Text")));
				stack.Children.Add(row);
			}
			if (canWrite && _data.Clubs.Count > 0)
			{
				var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
				var clubPick = new ComboBox
				{
					Height = 26,
					FontSize = 11.5,
					IsEditable = true,
					IsTextSearchEnabled = true,
					ItemsSource = _data.Clubs.Where(c => c.SquadSize > 0).OrderBy(c => c.Name).ToList(),
					DisplayMemberPath = nameof(ClubRow.Name),
					SelectedValuePath = nameof(ClubRow.Address),
					Margin = new Thickness(0, 0, 6, 0)
				};
				var addBtn = new Button { Content = Build.T("+ Favori Kulüp", "+ Fav Club"), Height = 26, Padding = new Thickness(9, 2, 9, 2), FontSize = 11.5 };
				addBtn.Click += (_, _) =>
				{
					if (clubPick.SelectedItem is not ClubRow cr) return;
					try
					{
						PlayerIdentity.AddFavClub(_data, Address, cr.Address, cr.Name, 100, Name);
						_nav.SetStatus(Build.F("✓ {1} için {0} favori kulüp eklendi. Ctrl+Z geri alır.", "✓ Added {0} as favoured club for {1}. Ctrl+Z undoes it.", cr.Name, Name));
						Fill();
					}
					catch (Exception ex) { _nav.SetStatus(ex.Message); }
				};
				DockPanel.SetDock(addBtn, Dock.Right);
				addRow.Children.Add(addBtn);
				addRow.Children.Add(clubPick);
				stack.Children.Add(addRow);
			}
			host.Content = stack;
		}
		Fill();
		return host;
	}

	private UIElement LanguagesPanel()
	{
		var host = new ContentControl();
		void Fill()
		{
			var stack = new StackPanel { Margin = new Thickness(12, 2, 12, 10) };
			var langs = PlayerIdentity.ReadLanguages(_data.Memory, Address);
			if (langs.Count == 0)
			{
				stack.Children.Add(Build.Text(Build.T("Kayıtlı ekstra dil yok (ana dili uyruğundan gelir).", "No extra languages recorded (native language comes from nation)."), 12, Build.Res("Muted")));
			}
			bool canWrite = !AppSettings.Current.ReadOnlyMode;
			foreach (SpokenLanguage lang in langs)
			{
				SpokenLanguage current = lang;
				var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
				var del = new Button { Content = "✕", Style = Build.StyleOf("GhostButton"), Padding = new Thickness(6, 1, 6, 1), FontSize = 11, Margin = new Thickness(6, 0, 0, 0), IsEnabled = canWrite, ToolTip = Build.T("Dili sil (Ctrl+Z geri alır)", "Remove language (Ctrl+Z undoes)") };
				del.Click += (_, _) =>
				{
					PlayerIdentity.RemoveLanguage(_data, Address, current.Index, current.Name, Name);
					_nav.SetStatus(Build.F("✓ {0}: {1} dili silindi. Ctrl+Z geri alır.", "✓ {0}: removed {1}. Ctrl+Z undoes it.", Name, current.Name));
					Fill();
				};
				var fluencyBox = new ComboBox
				{
					Width = 110,
					Height = 25,
					FontSize = 11.5,
					IsEnabled = canWrite,
					ItemsSource = Enumerable.Range(1, 10).Select(v => new KeyValuePair<int, string>(v, v == 10 ? Build.T("10 (Akıcı)", "10 (Fluent)") : v >= 7 ? Build.F("{0} (İyi)", "{0} (Good)", v) : Build.F("{0} (Temel)", "{0} (Basic)", v))).ToList(),
					DisplayMemberPath = "Value",
					SelectedValuePath = "Key",
					SelectedValue = current.Fluency
				};
				fluencyBox.SelectionChanged += (_, _) =>
				{
					if (fluencyBox.SelectedValue is int v && v != current.Fluency)
					{
						PlayerIdentity.SetFluency(_data, current, v, Name);
						_nav.SetStatus(Build.F("✓ {0} · {1} akıcılığı {2}/10 yapıldı. Ctrl+Z geri alır.", "✓ {0} · {1} fluency set to {2}/10. Ctrl+Z undoes it.", Name, current.Name, v));
						Fill();
					}
				};
				DockPanel.SetDock(del, Dock.Right);
				DockPanel.SetDock(fluencyBox, Dock.Right);
				row.Children.Add(del);
				row.Children.Add(fluencyBox);
				row.Children.Add(Build.Text(current.Name, 12.5, Build.Res("Text")));
				stack.Children.Add(row);
			}
			host.Content = stack;
		}
		Fill();
		return host;
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
					_nav.SetStatus(Build.F("✓ {0} · {1} güncellendi. Ctrl+Z geri alır.", "✓ {0} · {1} updated. Ctrl+Z undoes it.", Name, label));
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
					_nav.SetStatus(Build.F("✓ {0}: \"{1}\" silindi. Ctrl+Z geri alır.", "✓ {0}: removed \"{1}\". Ctrl+Z undoes it.", Name, current.Label));
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
							_nav.SetStatus(Build.F("✓ {0} · {1} güncellendi. Ctrl+Z geri alır.", "✓ {0} · {1} updated. Ctrl+Z undoes it.", Name, current.Label));
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
					ToolTip = Build.F("Tutar ({0}) veya yüzde (örn. 15)", "Amount ({0}) or percent (e.g. 15)", Money.Current.Code)
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
						_nav.SetStatus(Build.F("✓ {0} · {2} eklendi. Ctrl+Z geri alır.", "✓ {0} · added {1}. Ctrl+Z undoes it.", Name, preset.En, Build.T(preset.Tr, preset.En)));
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
		var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
		Border pitchCard = Build.Card(Build.T("Saha", "Pitch"), MiniPitch(360));
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
		var canvas = PlayerVisuals.CreatePitchCanvas(w, h);

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
