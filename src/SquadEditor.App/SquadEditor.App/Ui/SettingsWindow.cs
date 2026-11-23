using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SquadEditor.App.Ui;

/// <summary>
/// Settings grouped like the game's preferences. Most changes apply at once;
/// theme, accent colour and language restart the editor.
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
	private readonly ComboBox _currency = Combo(), _period = Combo(), _height = Combo(), _date = Combo(), _attributes = Combo(), _refresh = Combo();
	private readonly CheckBox _compact = Check(), _readOnly = Check(), _confirm = Check(), _live = Check(), _auto = Check(), _tabs = Check();
	private readonly TextBox _rate = new TextBox { Width = 120, Height = 30 };
	private readonly TextBox _folder = new TextBox { Height = 32 };
	private readonly TextBlock _info;
	private readonly ContentControl _page = new ContentControl();

	public SettingsWindow()
	{
		Title = Build.T("Ayarlar", "Settings");
		Width = 900;
		Height = 620;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

		Fill(_theme, new[]
		{
			("dark", Build.T("FM Gece Laciverti", "FM Night Navy")),
			("oled", Build.T("OLED Saf Siyah", "OLED Pure Black")),
			("charcoal", Build.T("WTCS Kömür & Altın", "WTCS Charcoal & Gold")),
			("light", Build.T("Açık Editorial", "Light Editorial"))
		}, _s.UiTheme);
		Fill(_language, new[] { ("tr", "Türkçe"), ("en", "English") }, _s.EffectiveLanguage);
		Fill(_accent, Accents.Select(a => (a.Hex, "●  " + Build.T(a.Tr, a.En))).ToArray(), _s.Accent);
		string origTheme = _s.UiTheme, origAccent = _s.Accent;
		void PreviewTheme()
		{
			if (_theme.SelectedValue is string t && _accent.SelectedValue is string a)
			{
				Theme.ApplyLive(t, a);
				Background = Build.Res("Bg");
				Foreground = Build.Res("Text");
			}
		}
		_theme.SelectionChanged += (_, _) => PreviewTheme();
		_accent.SelectionChanged += (_, _) => PreviewTheme();
		Closed += (_, _) =>
		{
			if (DialogResult != true)
			{
				Theme.ApplyLive(origTheme, origAccent);
			}
		};
		Fill(_scale, new[] { 0.9, 1.0, 1.1, 1.2, 1.3 }.Select(v => (v.ToString(CultureInfo.InvariantCulture), $"%{v * 100:0}")).ToArray(), _s.UiScale.ToString(CultureInfo.InvariantCulture));
		Fill(_currency, Money.Currencies.Select(c => (c.Code, $"{c.Symbol}  {c.Code}")).ToArray(), _s.Currency);
		Fill(_period, new[] { ("week", Build.T("Haftalık", "Weekly")), ("month", Build.T("Aylık", "Monthly")), ("year", Build.T("Yıllık", "Yearly")) }, _s.WagePeriod);
		Fill(_height, new[] { ("cm", "cm"), ("ftin", Build.T("feet / inç", "feet / inches")) }, _s.HeightUnit);
		Fill(_date, Dates.Formats.Select(f => (f, DateTime.Today.ToString(f, CultureInfo.InvariantCulture) + "   (" + f.ToLowerInvariant() + ")")).ToArray(), _s.DateFormat);
		Fill(_attributes, new[] { ("20", Build.T("1-20 (oyundaki gibi)", "1-20 (like the game)")), ("100", Build.T("1-100 (oyunun iç değeri)", "1-100 (the game's internal value)")) }, _s.Attributes100 ? "100" : "20");
		Fill(_refresh, new[] { 1000, 1500, 3000, 5000 }.Select(v => (v.ToString(), Build.T($"{v / 1000.0:0.#} saniyede bir", $"every {v / 1000.0:0.#} s"))).ToArray(), _s.LiveRefreshMs.ToString());
		_compact.IsChecked = _s.Compact;
		_readOnly.IsChecked = _s.ReadOnlyMode;
		_confirm.IsChecked = _s.ConfirmBulkEdits;
		_live.IsChecked = _s.LiveRefresh;
		_auto.IsChecked = _s.AutoConnect;
		_tabs.IsChecked = _s.RestoreTabs;
		_rate.Text = Money.Rate.ToString("0.####", CultureInfo.InvariantCulture);
		_currency.SelectionChanged += (_, _) =>
		{
			var c = Money.Currencies.First(x => x.Code == (string)_currency.SelectedValue);
			_rate.Text = (_s.Rates != null && _s.Rates.TryGetValue(c.Code, out double r) ? r : c.DefaultRate).ToString("0.####", CultureInfo.InvariantCulture);
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
		var reset = new Button { Content = Build.T("Varsayılanlar", "Defaults"), Margin = new Thickness(0, 0, 16, 0), Style = Build.StyleOf("GhostButton") };
		reset.Click += (_, _) => ResetDefaults();
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true };
		var save = new Button { Content = Build.T("Kaydet", "Save"), Style = Build.StyleOf("AccentButton"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, MinWidth = 110 };
		save.Click += (_, _) => Save();
		buttons.Children.Add(reset);
		buttons.Children.Add(cancel);
		buttons.Children.Add(save);
		Grid.SetColumn(buttons, 1);
		Grid.SetRow(buttons, 1);
		root.Children.Add(buttons);
		Content = root;
	}

	/// <summary>True when the theme, accent or language changed: the editor restarts to apply it.</summary>
	public bool NeedsRestart { get; private set; }

	private static ComboBox Combo() => new ComboBox { Width = 260 };

	private static CheckBox Check() => new CheckBox { VerticalAlignment = VerticalAlignment.Center };

	private static void Fill(ComboBox box, (string Key, string Label)[] items, string selected)
	{
		box.ItemsSource = items.Select(i => new KeyValuePair<string, string>(i.Key, i.Label)).ToList();
		box.DisplayMemberPath = "Value";
		box.SelectedValuePath = "Key";
		box.SelectedValue = selected;
		if (box.SelectedIndex < 0)
		{
			box.SelectedIndex = 0;
		}
	}

	private static StackPanel Section(string title, string? hint, params (string Label, UIElement Control, string? Help)[] rows)
	{
		var stack = new StackPanel();
		stack.Children.Add(Build.Text(title, 18, null, FontWeights.SemiBold));
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
			if (control is FrameworkElement fe)
			{
				fe.HorizontalAlignment = HorizontalAlignment.Left;
			}
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

	private UIElement Appearance() => Section(Build.T("Görünüm", "Appearance"),
		Build.T("Tema, vurgu rengi ve dil değişince editör yeniden açılır ve oyuna tekrar bağlanır.", "Changing theme, accent or language restarts the editor and reconnects to the game."),
		(Build.T("Tema", "Theme"), _theme, null),
		(Build.T("Vurgu rengi", "Accent colour"), _accent, null),
		(Build.T("Dil", "Language"), _language, null),
		(Build.T("Arayüz boyutu", "Interface size"), _scale, Build.T("Yüksek çözünürlüklü ekranlarda büyütmek işe yarar.", "Useful on high-resolution screens.")),
		(Build.T("Sık satırlar", "Compact rows"), _compact, Build.T("Listelerde ve kartlarda aynı anda daha çok satır görünür.", "Fit more rows on screen in lists and cards.")));

	private UIElement Units() => Section(Build.T("Birimler", "Units"),
		Build.T("Oyun parayı sterlin, maaşları haftalık tutar. Değerler burada seçtiğin birimde gösterilir, yazdığın değer de bu birimden çevrilir.",
			"The game keeps money in pounds and wages per week. Values are shown, and typed, in the units you choose here."),
		(Build.T("Para birimi", "Currency"), _currency, null),
		(Build.T("Kur (1 £ =)", "Rate (1 £ =)"), _rate, Build.T("€ kuru FM27'nin kendi kurudur. Oyunda başka kur kullanıyorsan buradan düzelt.", "The € rate is FM27's own. Adjust it if your game uses another rate.")),
		(Build.T("Maaş gösterimi", "Wages shown"), _period, null),
		(Build.T("Boy", "Height"), _height, null),
		(Build.T("Tarih biçimi", "Date format"), _date, null),
		(Build.T("Özellik ölçeği", "Attribute scale"), _attributes, Build.T("1-100, oyunun sakladığı ince ayarlı değerdir (1-20'deki 15 = 71-75 arası).", "1-100 is the finer value the game stores (15 on 1-20 = 71-75).")));

	private UIElement Editing() => Section(Build.T("Düzenleme", "Editing"), null,
		(Build.T("Salt okunur mod", "Read-only mode"), _readOnly, Build.T("Sadece bakmak için: oyuna hiçbir şey yazılmaz.", "Look only: nothing is written to the game.")),
		(Build.T("Toplu düzenlemede onay sor", "Confirm bulk edits"), _confirm, Build.T("Birden fazla oyuncuya ön ayar uygulamadan önce sorar.", "Asks before a preset is applied to several players.")),
		(Build.T("Canlı yenileme", "Live refresh"), _live, Build.T("Açık sayfadaki değerler oyundaki değişiklikleri izler.", "Values on the open page follow changes made by the game.")),
		(Build.T("Yenileme sıklığı", "Refresh rate"), _refresh, null),
		(Build.T("Otomatik bağlan", "Connect automatically"), _auto, Build.T("Oyun açılınca kendiliğinden bağlanır. Kapalıysa Bağlan düğmesi çıkar.", "Connects when the game starts. When off, a Connect button is shown.")),
		(Build.T("Sekmeleri hatırla", "Restore tabs"), _tabs, Build.T("Kapatırken açık olan oyuncu, kulüp ve personel sekmeleri bir sonraki açılışta geri gelir.", "Players, clubs and staff open at exit come back next time.")));

	private UIElement Graphics()
	{
		var row = new DockPanel { Width = 480 };
		var browse = new Button { Content = Build.T("Gözat…", "Browse…"), Margin = new Thickness(8, 0, 0, 0) };
		browse.Click += (_, _) =>
		{
			var dialog = new OpenFolderDialog { Title = Build.T("Grafik klasörünü seç", "Choose the graphics folder") };
			if (dialog.ShowDialog(this) == true)
			{
				_folder.Text = dialog.FolderName;
			}
		};
		DockPanel.SetDock(browse, Dock.Right);
		row.Children.Add(browse);
		row.Children.Add(_folder);
		StackPanel stack = Section(Build.T("Logo ve yüzler", "Logos & faces"),
			Build.T(@"Logo ve yüz paketleri, FM'in kullandığı Belgeler\Sports Interactive\Football Manager 27\graphics klasöründen kendiliğinden okunur. Paketlerin başka yerdeyse klasörü seç; boş bırakırsan otomatik klasör kullanılır.",
				@"Logo and face packs are read automatically from Documents\Sports Interactive\Football Manager 27\graphics, like the game does. Pick a folder if yours are elsewhere; leave it empty for automatic."),
			(Build.T("Klasör", "Folder"), row, null));
		stack.Children.Add(_info);
		return stack;
	}

	private UIElement Folders()
	{
		string fm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 27");
		Button Open(string text, string path)
		{
			var b = new Button { Content = text, IsEnabled = Directory.Exists(path), Padding = new Thickness(14, 7, 14, 7) };
			b.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
			return b;
		}
		return Section(Build.T("Klasörler", "Folders"),
			Build.T("Düzenlemeye başlamadan önce kaydının kopyasını almak en güvenli yol.", "Copying your save before editing is the safest way."),
			(Build.T("Kayıtlar", "Saves"), Open(Build.T("Kayıt klasörünü aç", "Open saves folder"), Path.Combine(fm, "games")), null),
			(Build.T("Grafikler", "Graphics"), Open(Build.T("Grafik klasörünü aç", "Open graphics folder"), Path.Combine(fm, "graphics")), null),
			(Build.T("Ayar dosyası", "Settings file"), Open(Build.T("Ayar klasörünü aç", "Open settings folder"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SquadEditor")), null));
	}

	private UIElement About()
	{
		StackPanel stack = Section("SquadEditor", Build.T(
			"Football Manager 27 için ücretsiz ve açık kaynak gerçek zamanlı editör. Sports Interactive veya SEGA ile bağlantılı değildir. Düzenlemeden önce kaydını yedekle.",
			"Free and open-source real-time editor for Football Manager 27. Not affiliated with Sports Interactive or SEGA. Back up your save before editing."));
		string version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "";
		var v = Build.Text(Build.T("Sürüm ", "Version ") + version, 13, Build.Res("Muted"));
		v.Margin = new Thickness(0, 12, 0, 0);
		stack.Children.Add(v);
		var link = new Button { Content = RepositoryUrl, Style = Build.StyleOf("LinkButton"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Foreground = Build.Res("Accent") };
		link.Click += (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
		stack.Children.Add(link);
		var keys = Build.Text(Build.T(
			"Kısayollar: Ctrl+F ara · Enter yaz · Esc vazgeç · ↑/↓ ±1 (Shift ile ±10) · Ctrl+Z geri al · Ctrl+W sekmeyi kapat · orta tık sekmeyi kapat",
			"Shortcuts: Ctrl+F search · Enter write · Esc cancel · ↑/↓ ±1 (Shift ±10) · Ctrl+Z undo · Ctrl+W close tab · middle-click close tab"), 12, Build.Res("Muted"));
		keys.TextWrapping = TextWrapping.Wrap;
		keys.Margin = new Thickness(0, 18, 0, 0);
		stack.Children.Add(keys);
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
		string path = _folder.Text.Trim();
		if (path.Length > 0 && !Directory.Exists(path))
		{
			_info.Text = Build.T("Grafik klasörü bulunamadı.", "Graphics folder not found.");
			return;
		}
		if (!double.TryParse(_rate.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || rate <= 0)
		{
			_rate.Focus();
			return;
		}
		string theme = (string)_theme.SelectedValue, language = (string)_language.SelectedValue, accent = (string)_accent.SelectedValue;
		NeedsRestart = language != _s.EffectiveLanguage;
		_s.UiTheme = theme;
		_s.Language = language;
		_s.Accent = accent;
		_s.UiScale = double.Parse((string)_scale.SelectedValue, CultureInfo.InvariantCulture);
		_s.Compact = _compact.IsChecked == true;
		_s.Currency = (string)_currency.SelectedValue;
		_s.WagePeriod = (string)_period.SelectedValue;
		_s.Rates ??= new Dictionary<string, double>();
		_s.Rates[_s.Currency] = rate;
		_s.HeightUnit = (string)_height.SelectedValue;
		_s.DateFormat = (string)_date.SelectedValue;
		_s.Attributes100 = (string)_attributes.SelectedValue == "100";
		_s.ReadOnlyMode = _readOnly.IsChecked == true;
		_s.ConfirmBulkEdits = _confirm.IsChecked == true;
		_s.LiveRefresh = _live.IsChecked == true;
		_s.LiveRefreshMs = int.Parse((string)_refresh.SelectedValue);
		_s.AutoConnect = _auto.IsChecked == true;
		_s.RestoreTabs = _tabs.IsChecked == true;
		_s.GraphicsFolder = path.Length > 0 ? path : null;
		_s.Save();
		DialogResult = true;
	}
}
