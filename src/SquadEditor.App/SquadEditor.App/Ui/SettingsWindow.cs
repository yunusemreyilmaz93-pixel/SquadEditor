using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SquadEditor.App.Ui;

/// <summary>
/// Settings with sections like the game's preferences: appearance, money, graphics, about.
/// Money changes apply at once; theme and language restart the editor.
/// </summary>
public sealed class SettingsWindow : Window
{
	public const string RepositoryUrl = "https://github.com/yunusemreyilmaz93-pixel/SquadEditor";

	private readonly ComboBox _theme = new ComboBox { Width = 220 };
	private readonly ComboBox _language = new ComboBox { Width = 220 };
	private readonly ComboBox _currency = new ComboBox { Width = 220 };
	private readonly ComboBox _period = new ComboBox { Width = 220 };
	private readonly TextBox _rate = new TextBox { Width = 120, Height = 30 };
	private readonly TextBox _folder = new TextBox { Height = 32 };
	private readonly TextBlock _info;
	private readonly ContentControl _page = new ContentControl();

	public SettingsWindow()
	{
		Title = Build.T("Ayarlar", "Settings");
		Width = 820;
		Height = 560;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"];
		AppSettings s = AppSettings.Current;

		Fill(_theme, new[] { ("dark", Build.T("Koyu", "Dark")), ("light", Build.T("Açık", "Light")) }, s.UiTheme);
		Fill(_language, new[] { ("tr", "Türkçe"), ("en", "English") }, s.EffectiveLanguage);
		Fill(_currency, Money.Currencies.Select(c => (c.Code, $"{c.Symbol}  {c.Code}")).ToArray(), s.Currency);
		Fill(_period, new[] { ("week", Build.T("Haftalık", "Weekly")), ("month", Build.T("Aylık", "Monthly")), ("year", Build.T("Yıllık", "Yearly")) }, s.WagePeriod);
		_rate.Text = Money.Rate.ToString("0.####", CultureInfo.InvariantCulture);
		_currency.SelectionChanged += (_, _) =>
		{
			var c = Money.Currencies.First(x => x.Code == (string)_currency.SelectedValue);
			_rate.Text = (s.Rates != null && s.Rates.TryGetValue(c.Code, out double r) ? r : c.DefaultRate).ToString("0.####", CultureInfo.InvariantCulture);
		};
		_folder.Text = s.GraphicsFolder ?? "";
		_info = Build.Text("", 12, Build.Res("Muted"));
		_info.TextWrapping = TextWrapping.Wrap;
		_info.Margin = new Thickness(0, 10, 0, 0);
		string? auto = GraphicsIndex.FindDefaultRoot();
		_info.Text = Build.T("Otomatik bulunan klasör: ", "Found automatically: ") + (auto ?? Build.T("yok", "none")) +
			$"\n{Build.T("Şu an yüklü", "Loaded now")}: {ImageLookup.Graphics.ClubLogos:N0} {Build.T("kulüp logosu", "club logos")}, {ImageLookup.Graphics.Faces:N0} {Build.T("yüz", "faces")}";

		var root = new Grid();
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
		root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		var nav = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
		root.Children.Add(new Border { Background = Build.Res("Chrome"), BorderBrush = Build.Res("StrokeSoft"), BorderThickness = new Thickness(0, 0, 1, 0), Child = nav });
		Grid.SetRowSpan(root.Children[0], 2);
		Build.NavItem(nav, Build.T("Görünüm", "Appearance"), () => _page.Content = Appearance(), isChecked: true);
		Build.NavItem(nav, Build.T("Para ve maaş", "Money & wages"), () => _page.Content = MoneyPage());
		Build.NavItem(nav, Build.T("Logo ve yüzler", "Logos & faces"), () => _page.Content = Graphics());
		Build.NavItem(nav, Build.T("Hakkında", "About"), () => _page.Content = About());
		_page.Margin = new Thickness(28, 22, 28, 0);
		Grid.SetColumn(_page, 1);
		root.Children.Add(_page);

		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 24, 20) };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true };
		var save = new Button { Content = Build.T("Kaydet", "Save"), Style = Build.StyleOf("AccentButton"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, MinWidth = 110 };
		save.Click += (_, _) => Save();
		buttons.Children.Add(cancel);
		buttons.Children.Add(save);
		Grid.SetColumn(buttons, 1);
		Grid.SetRow(buttons, 1);
		root.Children.Add(buttons);
		Content = root;
	}

	/// <summary>True when the theme or language changed: the editor restarts to apply it.</summary>
	public bool NeedsRestart { get; private set; }

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

	private static UIElement Section(string title, string? hint, params (string Label, UIElement Control)[] rows)
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
		foreach (var (label, control) in rows)
		{
			var row = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
			var l = Build.Text(label, 13, Build.Res("Muted"));
			l.Width = 190;
			DockPanel.SetDock(l, Dock.Left);
			row.Children.Add(l);
			if (control is FrameworkElement fe)
			{
				fe.HorizontalAlignment = HorizontalAlignment.Left;
			}
			row.Children.Add(control);
			stack.Children.Add(row);
		}
		return stack;
	}

	private UIElement Appearance() => Section(Build.T("Görünüm", "Appearance"),
		Build.T("Tema ve dil değişince editör yeniden açılır ve oyuna tekrar bağlanır.", "Changing theme or language restarts the editor and reconnects to the game."),
		(Build.T("Tema", "Theme"), _theme), (Build.T("Dil", "Language"), _language));

	private UIElement MoneyPage() => Section(Build.T("Para ve maaş", "Money & wages"),
		Build.T("Oyun parayı sterlin, maaşları haftalık tutar. Burada seçtiğin birimde gösterilir, yazdığın değer de bu birimden çevrilir. Kur: 1 £ karşılığı.",
			"The game keeps money in pounds and wages per week. Values are shown, and typed, in the unit you choose here. Rate: per 1 £."),
		(Build.T("Para birimi", "Currency"), _currency), (Build.T("Kur (1 £ =)", "Rate (1 £ =)"), _rate), (Build.T("Maaş gösterimi", "Wages shown"), _period));

	private UIElement Graphics()
	{
		var row = new DockPanel { Width = 470 };
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
		var stack = (StackPanel)Section(Build.T("Logo ve yüzler", "Logos & faces"),
			Build.T(@"Logo ve yüz paketleri FM'in kullandığı Belgeler\Sports Interactive\Football Manager 27\graphics klasöründen kendiliğinden okunur. Paketlerin başka yerdeyse klasörü seç; boş bırakırsan otomatik klasör kullanılır.",
				@"Logo and face packs are read automatically from Documents\Sports Interactive\Football Manager 27\graphics, like the game does. Pick a folder if yours are elsewhere; leave it empty for automatic."),
			(Build.T("Klasör", "Folder"), row));
		stack.Children.Add(_info);
		return stack;
	}

	private UIElement About()
	{
		var stack = (StackPanel)Section("SquadEditor", Build.T(
			"Football Manager 27 için ücretsiz ve açık kaynak gerçek zamanlı editör. Sports Interactive veya SEGA ile bağlantılı değildir. Düzenlemeden önce kaydını yedekle.",
			"Free and open-source real-time editor for Football Manager 27. Not affiliated with Sports Interactive or SEGA. Back up your save before editing."));
		string version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "";
		stack.Children.Add(Build.Text(Build.T("Sürüm ", "Version ") + version, 13, Build.Res("Muted")));
		var link = new Button { Content = RepositoryUrl, Style = Build.StyleOf("LinkButton"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Foreground = Build.Res("Accent") };
		link.Click += (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
		stack.Children.Add(link);
		return stack;
	}

	private void Save()
	{
		AppSettings s = AppSettings.Current;
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
		string theme = (string)_theme.SelectedValue, language = (string)_language.SelectedValue;
		NeedsRestart = theme != s.UiTheme || language != s.EffectiveLanguage;
		s.UiTheme = theme;
		s.Language = language;
		s.Currency = (string)_currency.SelectedValue;
		s.WagePeriod = (string)_period.SelectedValue;
		s.Rates ??= new Dictionary<string, double>();
		s.Rates[s.Currency] = rate;
		s.GraphicsFolder = path.Length > 0 ? path : null;
		s.Save();
		DialogResult = true;
	}
}
