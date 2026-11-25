using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace SquadEditor.App.Ui;

/// <summary>
/// Settings grouped like the game's preferences. Most changes apply at once;
/// theme, accent colour and language restart or refresh the editor.
/// </summary>
public sealed class SettingsWindow : Window
{
	public const string RepositoryUrl = "https://github.com/yunusemreyilmaz93-pixel/SquadEditor";

	private static readonly (string Hex, string Tr, string En)[] Accents =
	{
		("#4F8CFF", "Mavi", "Blue"), ("#8B5CF6", "Mor", "Purple"), ("#E64980", "Pembe", "Pink"),
		("#F59F00", "Sarı", "Yellow"), ("#20C997", "Turkuaz", "Teal"), ("#FA5252", "Kırmızı", "Red"), ("#40C057", "Yeşil", "Green")
	};

	private readonly AppSettings _s = AppSettings.Current;
	private readonly ComboBox _theme = Combo(), _language = Combo(), _accent = Combo(), _scale = Combo();
	private readonly ComboBox _currency = Combo(), _period = Combo(), _height = Combo(), _date = Combo(), _attributes = Combo(), _refresh = Combo(), _roleWeights = Combo();
	private readonly CheckBox _compact = Check(), _readOnly = Check(), _confirm = Check(), _live = Check(), _auto = Check(), _tabs = Check();
	private readonly TextBox _rate = new TextBox { Width = 120, Height = 30 };
	private readonly TextBox _folder = new TextBox { Height = 32 };
	private readonly TextBlock _info;
	private readonly ContentControl _page = new ContentControl();
	private bool _saved;

	public SettingsWindow()
	{
		Title = Build.T("Ayarlar", "Settings");
		Width = 900;
		Height = 620;
		CanResize = false;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = Build.Font("UiFont");
		WindowHelper.ApplyChrome(this);

		Fill(_theme, new[]
		{
			("dark", Build.T("FM Gece Laciverti", "FM Night Navy")),
			("oled", Build.T("OLED Saf Siyah", "OLED Pure Black")),
			("charcoal", Build.T("WTCS Kömür & Altın", "WTCS Charcoal & Gold")),
			("light", Build.T("Açık Editorial", "Light Editorial"))
		}, _s.UiTheme);
		Fill(_language, Loc.Languages.Select(l => (l.Code, l.Name)).ToArray(), _s.EffectiveLanguage);
		Fill(_accent, Accents.Select(a => (a.Hex, "●  " + Build.T(a.Tr, a.En))).ToArray(), _s.Accent);
		string origTheme = _s.UiTheme, origAccent = _s.Accent;
		void PreviewTheme()
		{
			if (_theme.SelectedValue is string t && _accent.SelectedValue is string a)
			{
				Ui.Theme.ApplyLive(t, a);
				Background = Build.Res("Bg");
				Foreground = Build.Res("Text");
			}
		}
		_theme.SelectionChanged += (_, _) => PreviewTheme();
		_accent.SelectionChanged += (_, _) => PreviewTheme();
		Closed += (_, _) =>
		{
			if (!_saved)
			{
				Ui.Theme.ApplyLive(origTheme, origAccent);
			}
		};
		Fill(_scale, new[] { 0.9, 1.0, 1.1, 1.2, 1.3 }.Select(v => (v.ToString(CultureInfo.InvariantCulture), $"%{v * 100:0}")).ToArray(), _s.UiScale.ToString(CultureInfo.InvariantCulture));
		Fill(_currency, Money.Currencies.Select(c => (c.Code, $"{c.Symbol}  {c.Code}")).ToArray(), _s.Currency);
		Fill(_period, new[] { ("week", Build.T("Haftalık", "Weekly")), ("month", Build.T("Aylık", "Monthly")), ("year", Build.T("Yıllık", "Yearly")) }, _s.WagePeriod);
		Fill(_height, new[] { ("cm", "cm"), ("ftin", Build.T("feet / inç", "feet / inches")) }, _s.HeightUnit);
		Fill(_date, Dates.Formats.Select(f => (f, DateTime.Today.ToString(f, CultureInfo.InvariantCulture) + "   (" + f.ToLowerInvariant() + ")")).ToArray(), _s.DateFormat);
		Fill(_attributes, new[] { ("20", Build.T("1-20 (oyundaki gibi)", "1-20 (like the game)")), ("100", Build.T("1-100 (oyunun iç değeri)", "1-100 (the game's internal value)")) }, _s.Attributes100 ? "100" : "20");
		Fill(_refresh, new[] { 1000, 1500, 3000, 5000 }.Select(v => (v.ToString(), Build.F("{0:0.#} saniyede bir", "every {0:0.#} s", v / 1000.0))).ToArray(), _s.LiveRefreshMs.ToString());
		Fill(_roleWeights, new[]
		{
			("classic", Build.T("Klasik FM Rol Ağırlıkları", "Classic FM Role Weights")),
			("meta", Build.T("Maç Motoru Meta (Hız/İvme/Sıçrama)", "Match Engine Meta (Pace/Acc/Jump)"))
		}, _s.RoleWeightMode);
		_compact.IsChecked = _s.Compact;
		_readOnly.IsChecked = _s.ReadOnlyMode;
		_confirm.IsChecked = _s.ConfirmBulkEdits;
		_live.IsChecked = _s.LiveRefresh;
		_auto.IsChecked = _s.AutoConnect;
		_tabs.IsChecked = _s.RestoreTabs;
		_rate.Text = Money.Rate.ToString("0.####", CultureInfo.InvariantCulture);
		_currency.SelectionChanged += (_, _) =>
		{
			if (_currency.SelectedValue is string code)
			{
				var c = Money.Currencies.First(x => x.Code == code);
				_rate.Text = (_s.Rates != null && _s.Rates.TryGetValue(c.Code, out double r) ? r : c.DefaultRate).ToString("0.####", CultureInfo.InvariantCulture);
			}
		};
		_folder.Text = _s.GraphicsFolder ?? "";
		_info = Build.Text("", 12, Build.Res("Muted"));
		_info.TextWrapping = TextWrapping.Wrap;
		_info.Margin = new Thickness(0, 12, 0, 0);
		_info.Text = Build.T("Otomatik bulunan klasör: ", "Found automatically: ") + (GraphicsIndex.FindDefaultRoot() ?? Build.T("yok", "none")) +
			$"\n{Build.T("Şu an yüklü", "Loaded now")}: {ImageLookup.Graphics.ClubLogos:N0} {Build.T("kulüp logosu", "club logos")}, {ImageLookup.Graphics.Faces:N0} {Build.T("yüz", "faces")}";

		var root = new Grid();
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		var nav = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
		var navBorder = new Border { Background = Build.Res("Chrome"), BorderBrush = Build.Res("StrokeSoft"), BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };
		Grid.SetRowSpan(navBorder, 2);
		root.Children.Add(navBorder);
		Build.NavItem(nav, Build.T("Görünüm", "Appearance"), () => _page.Content = Appearance(), isChecked: true);
		Build.NavItem(nav, Build.T("Birimler", "Units"), () => _page.Content = Units());
		Build.NavItem(nav, Build.T("Düzenleme", "Editing"), () => _page.Content = Editing());
		Build.NavItem(nav, Build.T("Logo ve yüzler", "Logos & faces"), () => _page.Content = Graphics());
		Build.NavItem(nav, Build.T("Klasörler", "Folders"), () => _page.Content = Folders());
		Build.NavItem(nav, Build.T("Hakkında", "About"), () => _page.Content = About());
		_page.Margin = new Thickness(28, 22, 28, 0);
		Grid.SetColumn(_page, 1);
		root.Children.Add(_page);

		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 24, 20) };
		var reset = new Button { Content = Build.T("Varsayılanlar", "Defaults"), Margin = new Thickness(0, 0, 16, 0), Theme = Build.StyleOf("GhostButton") };
		reset.Click += (_, _) => ResetDefaults();
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true };
		cancel.Click += (_, _) => Close(false);
		var save = new Button { Content = Build.T("Kaydet", "Save"), Theme = Build.StyleOf("AccentButton"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, MinWidth = 110 };
		save.Click += (_, _) => Save();
		buttons.Children.Add(reset);
		buttons.Children.Add(cancel);
		buttons.Children.Add(save);
		Grid.SetColumn(buttons, 1);
		Grid.SetRow(buttons, 1);
		root.Children.Add(buttons);
		Content = root;
	}

	/// <summary>True when the theme, accent, language or role weight mode requires a reload.</summary>
	public bool NeedsRestart { get; private set; }

	private static ComboBox Combo() => new ComboBox { Width = 260 };

	private static CheckBox Check() => new CheckBox { VerticalAlignment = VerticalAlignment.Center };

	private static void Fill(ComboBox box, (string Key, string Label)[] items, string selected)
	{
		box.ItemsSource = items.Select(i => new KeyValuePair<string, string>(i.Key, i.Label)).ToList();
		box.DisplayMemberBinding = new Binding("Value");
		box.SelectedValueBinding = new Binding("Key");
		box.SelectedValue = selected;
		if (box.SelectedIndex < 0)
		{
			box.SelectedIndex = 0;
		}
	}

	private static StackPanel Section(string title, string? hint, params (string Label, Control Control, string? Help)[] rows)
	{
		var stack = new StackPanel();
		stack.Children.Add(Build.Text(title, 18, null, FontWeight.SemiBold));
		if (hint != null)
		{
			var h = Build.Text(hint, 12, Build.Res("Muted"));
			h.TextWrapping = TextWrapping.Wrap;
			h.Margin = new Thickness(0, 6, 0, 0);
			stack.Children.Add(h);
		}
		foreach (var (label, control, help) in rows)
		{
			var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
			var l = Build.Text(label, 13, Build.Res("Text"));
			l.Width = 220;
			DockPanel.SetDock(l, Dock.Left);
			row.Children.Add(l);
			var right = new StackPanel();
			control.HorizontalAlignment = HorizontalAlignment.Left;
			right.Children.Add(control);
			if (help != null)
			{
				var t = Build.Text(help, 11.5, Build.Res("Faint"));
				t.TextWrapping = TextWrapping.Wrap;
				t.Margin = new Thickness(0, 3, 0, 0);
				right.Children.Add(t);
			}
			row.Children.Add(right);
			stack.Children.Add(row);
		}
		return stack;
	}

	private Control Appearance() => Section(Build.T("Görünüm", "Appearance"),
		Build.T("Tema ve vurgu rengi anında uygulanır; dil değişince editör yeniden açılır.", "Theme and accent apply immediately; changing language restarts the editor."),
		(Build.T("Tema", "Theme"), _theme, null),
		(Build.T("Vurgu rengi", "Accent colour"), _accent, null),
		(Build.T("Dil", "Language"), _language, null),
		(Build.T("Arayüz boyutu", "Interface size"), _scale, Build.T("Yüksek çözünürlüklü ekranlarda büyütmek işe yarar.", "Useful on high-resolution screens.")),
		(Build.T("Sık satırlar", "Compact rows"), _compact, Build.T("Listelerde ve kartlarda aynı anda daha çok satır görünür.", "Fit more rows on screen in lists and cards.")));

	private Control Units() => Section(Build.T("Birimler", "Units"),
		Build.T("Oyun parayı sterlin, maaşları haftalık tutar. Değerler burada seçtiğin birimde gösterilir, yazdığın değer de bu birimden çevrilir.",
			"The game keeps money in pounds and wages per week. Values are shown, and typed, in the units you choose here."),
		(Build.T("Para birimi", "Currency"), _currency, null),
		(Build.T("Kur (1 £ =)", "Rate (1 £ =)"), _rate, Build.T("€ kuru FM27'nin kendi kurudur. Oyunda başka kur kullanıyorsan buradan düzelt.", "The € rate is FM27's own. Adjust it if your game uses another rate.")),
		(Build.T("Maaş gösterimi", "Wages shown"), _period, null),
		(Build.T("Boy", "Height"), _height, null),
		(Build.T("Tarih biçimi", "Date format"), _date, null),
		(Build.T("Özellik ölçeği", "Attribute scale"), _attributes, Build.T("1-100, oyunun sakladığı ince ayarlı değerdir (1-20'deki 15 = 71-75 arası).", "1-100 is the finer value the game stores (15 on 1-20 = 71-75).")));

	private Control Editing() => Section(Build.T("Düzenleme", "Editing"), null,
		(Build.T("Rol reyting ağırlıkları", "Role rating weights"), _roleWeights, Build.T("Klasik FM rol ağırlıkları veya gerçek maç motorunda en etkili özelliklere (Hız, İvme, Sıçrama, Çeviklik, Sezgi) odaklanan ME Meta ağırlıkları.", "Classic FM role weights, or Match Engine Meta weights prioritizing real ME impact (Pace, Acceleration, Jumping Reach, Agility, Anticipation).")),
		(Build.T("Salt okunur mod", "Read-only mode"), _readOnly, Build.T("Sadece bakmak için: oyuna hiçbir şey yazılmaz.", "Look only: nothing is written to the game.")),
		(Build.T("Toplu düzenlemede onay sor", "Confirm bulk edits"), _confirm, Build.T("Birden fazla oyuncuya ön ayar uygulamadan önce sorar.", "Asks before a preset is applied to several players.")),
		(Build.T("Canlı yenileme", "Live refresh"), _live, Build.T("Açık sayfadaki değerler oyundaki değişiklikleri izler.", "Values on the open page follow changes made by the game.")),
		(Build.T("Yenileme sıklığı", "Refresh rate"), _refresh, null),
		(Build.T("Otomatik bağlan", "Connect automatically"), _auto, Build.T("Oyun açılınca kendiliğinden bağlanır. Kapalıysa Bağlan düğmesi çıkar.", "Connects when the game starts. When off, a Connect button is shown.")),
		(Build.T("Sekmeleri hatırla", "Restore tabs"), _tabs, Build.T("Kapatırken açık olan oyuncu, kulüp ve personel sekmeleri bir sonraki açılışta geri gelir.", "Players, clubs and staff open at exit come back next time.")));

	private Control Graphics()
	{
		var row = new DockPanel { Width = 480 };
		var browse = new Button { Content = Build.T("Gözat…", "Browse…"), Margin = new Thickness(8, 0, 0, 0) };
		browse.Click += async (_, _) =>
		{
			var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = Build.T("Grafik klasörünü seç", "Choose the graphics folder"),
				AllowMultiple = false
			});
			if (picked.Count > 0)
			{
				string? localPath = picked[0].TryGetLocalPath();
				if (!string.IsNullOrEmpty(localPath))
				{
					_folder.Text = localPath;
				}
			}
		};
		DockPanel.SetDock(browse, Dock.Right);
		row.Children.Add(browse);
		row.Children.Add(_folder);
		StackPanel stack = Section(Build.T("Logo ve yüzler", "Logos & faces"),
			Build.T(@"Logo ve yüz paketleri, FM'in kullandığı Belgeler\Sports Interactive\Football Manager 27\graphics klasöründen kendiliğinden okunur. Paketlerin başka yerdeyse klasörü seç; boş bırakırsan otomatik klasör kullanılır.",
				@"Logo and face packs are read automatically from Sports Interactive / Football Manager 27 / graphics, like the game does. Pick a folder if yours are elsewhere; leave it empty for automatic."),
			(Build.T("Klasör", "Folder"), row, null));
		stack.Children.Add(_info);
		return stack;
	}

	private Control Folders()
	{
		string fm = PlatformPaths.PrimaryFm27Folder();
		Button Open(string text, string path)
		{
			var b = new Button { Content = text, IsEnabled = Directory.Exists(path), Padding = new Thickness(14, 7, 14, 7) };
			b.Click += (_, _) => PlatformPaths.OpenFolder(path);
			return b;
		}
		return Section(Build.T("Klasörler", "Folders"),
			Build.T("Düzenlemeye başlamadan önce kaydının kopyasını almak en güvenli yol.", "Copying your save before editing is the safest way."),
			(Build.T("Kayıtlar", "Saves"), Open(Build.T("Kayıt klasörünü aç", "Open saves folder"), Path.Combine(fm, "games")), null),
			(Build.T("Grafikler", "Graphics"), Open(Build.T("Grafik klasörünü aç", "Open graphics folder"), Path.Combine(fm, "graphics")), null),
			(Build.T("Ayar dosyası", "Settings file"), Open(Build.T("Ayar klasörünü aç", "Open settings folder"), PlatformPaths.AppDataDir), null));
	}

	private Control About()
	{
		var stack = new StackPanel();

		var head = new StackPanel { Margin = new Thickness(14, 6, 14, 12) };
		var titleRow = new DockPanel();
		var verText = Build.Text("v1.6.0 · MIT", 12, Build.Res("Muted"), FontWeight.SemiBold);
		verText.VerticalAlignment = VerticalAlignment.Center;
		DockPanel.SetDock(verText, Dock.Right);
		titleRow.Children.Add(verText);
		titleRow.Children.Add(Build.Text("SquadEditor", 20, Build.Res("Text"), FontWeight.Bold));
		head.Children.Add(titleRow);

		var desc = Build.Text(Build.T(
			"Football Manager 27 için ücretsiz ve açık kaynaklı gerçek zamanlı editör & gözlemci aracı. Sports Interactive veya SEGA ile resmi bağlantısı yoktur.",
			"Free, open-source real-time editor & scout for Football Manager 27. Not affiliated with Sports Interactive or SEGA."), 12.5, Build.Res("Muted"));
		desc.TextWrapping = TextWrapping.Wrap;
		desc.Margin = new Thickness(0, 6, 0, 10);
		head.Children.Add(desc);

		var devRow = new StackPanel { Orientation = Orientation.Horizontal };
		devRow.Children.Add(Build.Text(Build.T("Geliştiren: ", "Developed by: "), 12.5, Build.Res("Faint")));
		devRow.Children.Add(Build.Text("Yunus Emre Yılmaz (@basitbioyun)", 12.5, Build.Res("Text"), FontWeight.SemiBold));
		head.Children.Add(devRow);

		var links = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
		void LinkBtn(string label, string url, bool primary = false)
		{
			var b = new Button
			{
				Content = label,
				Padding = new Thickness(11, 5, 11, 5),
				Margin = new Thickness(0, 0, 6, 6),
				FontSize = 12
			};
			if (primary) b.Theme = Build.StyleOf("AccentButton");
			b.Click += (_, _) => PlatformPaths.OpenUrl(url);
			links.Children.Add(b);
		}
		LinkBtn(Build.T("Web Sitesi", "Website"), "https://squadeditor.pages.dev", primary: true);
		LinkBtn("GitHub", RepositoryUrl);
		LinkBtn("X (@basitbioyun)", "https://x.com/basitbioyun");
		LinkBtn("YouTube", "https://youtube.com/@basitbioyun");
		LinkBtn("Instagram", "https://instagram.com/basitbioyun");
		head.Children.Add(links);
		stack.Children.Add(Build.Card(Build.T("Hakkında", "About"), head));

		var scPanel = new StackPanel { Margin = new Thickness(14, 4, 14, 12) };
		void Shortcut(string key, string descText)
		{
			var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
			var k = Build.Text(key, 12, Build.Res("Accent"), FontWeight.SemiBold);
			k.Width = 130;
			DockPanel.SetDock(k, Dock.Left);
			d.Children.Add(k);
			d.Children.Add(Build.Text(descText, 12, Build.Res("Muted")));
			scPanel.Children.Add(d);
		}
		Shortcut(PlatformPaths.SearchHint, Build.T("Oyuncu, kulüp, lig veya ülke ara", "Search players, clubs, leagues or nations"));
		Shortcut(PlatformPaths.PaletteHint, Build.T("Komut paletini aç", "Open Command Palette"));
		Shortcut(PlatformPaths.UndoHint, Build.T("Son değişikliği veya toplu işlemi geri al", "Undo last change or batch edit"));
		Shortcut("↑ / ↓ (+ Shift)", Build.T("Sayısal alanı ±1 (Shift ile ±10) değiştir", "Increment/decrement number by ±1 (±10 with Shift)"));
		Shortcut(Build.T($"{PlatformPaths.CloseTabHint} / Orta Tık", $"{PlatformPaths.CloseTabHint} / Middle Click"), Build.T("Aktif sekmeyi kapat", "Close active tab"));
		Shortcut("F5", Build.T("Kayıt verisini yeniden oku", "Reload save data"));
		stack.Children.Add(Build.Card(Build.T("Klavye Kısayolları", "Keyboard Shortcuts"), scPanel));

		return stack;
	}

	private void ResetDefaults()
	{
		var d = new AppSettings();
		_theme.SelectedValue = d.UiTheme;
		_accent.SelectedValue = d.Accent;
		_scale.SelectedValue = d.UiScale.ToString(CultureInfo.InvariantCulture);
		_compact.IsChecked = d.Compact;
		_currency.SelectedValue = d.Currency;
		_period.SelectedValue = d.WagePeriod;
		_height.SelectedValue = d.HeightUnit;
		_date.SelectedValue = d.DateFormat;
		_attributes.SelectedValue = "20";
		_roleWeights.SelectedValue = d.RoleWeightMode;
		_readOnly.IsChecked = d.ReadOnlyMode;
		_confirm.IsChecked = d.ConfirmBulkEdits;
		_live.IsChecked = d.LiveRefresh;
		_refresh.SelectedValue = d.LiveRefreshMs.ToString();
		_auto.IsChecked = d.AutoConnect;
		_tabs.IsChecked = d.RestoreTabs;
		_folder.Text = "";
	}

	private void Save()
	{
		string path = (_folder.Text ?? "").Trim();
		if (path.Length > 0 && !Directory.Exists(path))
		{
			_info.Text = Build.T("Grafik klasörü bulunamadı.", "Graphics folder not found.");
			return;
		}
		if (!double.TryParse((_rate.Text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || rate <= 0)
		{
			_rate.Focus();
			return;
		}
		string theme = (string)_theme.SelectedValue!, language = (string)_language.SelectedValue!, accent = (string)_accent.SelectedValue!;
		string roleWeights = (string)_roleWeights.SelectedValue!;
		NeedsRestart = language != _s.EffectiveLanguage || roleWeights != _s.RoleWeightMode;
		_s.UiTheme = theme;
		_s.Language = language;
		_s.Accent = accent;
		_s.RoleWeightMode = roleWeights;
		_s.UiScale = double.Parse((string)_scale.SelectedValue!, CultureInfo.InvariantCulture);
		_s.Compact = _compact.IsChecked == true;
		_s.Currency = (string)_currency.SelectedValue!;
		_s.WagePeriod = (string)_period.SelectedValue!;
		_s.Rates ??= new Dictionary<string, double>();
		_s.Rates[_s.Currency] = rate;
		_s.HeightUnit = (string)_height.SelectedValue!;
		_s.DateFormat = (string)_date.SelectedValue!;
		_s.Attributes100 = (string)_attributes.SelectedValue! == "100";
		_s.ReadOnlyMode = _readOnly.IsChecked == true;
		_s.ConfirmBulkEdits = _confirm.IsChecked == true;
		_s.LiveRefresh = _live.IsChecked == true;
		_s.LiveRefreshMs = int.Parse((string)_refresh.SelectedValue!);
		_s.AutoConnect = _auto.IsChecked == true;
		_s.RestoreTabs = _tabs.IsChecked == true;
		_s.GraphicsFolder = path.Length > 0 ? path : null;
		_s.Save();
		_saved = true;
		Close(true);
	}
}
