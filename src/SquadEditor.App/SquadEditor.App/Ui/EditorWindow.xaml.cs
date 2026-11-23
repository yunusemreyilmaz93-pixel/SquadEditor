using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SquadEditor.Core.Layout;

namespace SquadEditor.App.Ui;

public sealed class SearchHit
{
	public string Title { get; init; } = "";
	public string Subtitle { get; init; } = "";
	public ImageSource? Image { get; init; }
	public string Initials => string.Concat(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
	public CornerRadius Corner { get; init; } = new CornerRadius(16);
	public string Badge { get; init; } = "";
	public Brush BadgeBrush { get; init; } = Colors.Text;
	public Action Open { get; init; } = () => { };
}

public partial class EditorWindow : Window, INavigator
{
	private readonly List<IPage> _pages = new List<IPage>();
	private readonly DispatcherTimer _connectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
	private readonly DispatcherTimer _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
	private readonly DispatcherTimer _freezeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
	private readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
	private GameData? _data;
	private IPage? _current;
	private bool _connecting;
	private int _failedPid;

	public EditorWindow()
	{
		InitializeComponent();
		Title = "SquadEditor";
		// Number and date formats in grids follow the app language (576.610 in Turkish).
		Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Instance.Language == "tr" ? "tr-TR" : "en-GB");
		TopSearchHint.Text = Build.T("Oyuncu veya kulüp ara…  (Ctrl+F)", "Search players or clubs…  (Ctrl+F)");
		UndoText.Text = Build.T("Geri al", "Undo");
		ConnectButton.Content = Build.T("Bağlan", "Connect");
		ReadOnlyBadge.ToolTip = Build.T("Salt okunur mod: oyuna hiçbir şey yazılmaz", "Read-only mode: nothing is written to the game");
		((TextBlock)ReadOnlyBadge.Child).Text = Build.T("🔒 Salt okunur", "🔒 Read-only");
		UndoButton.ToolTip = Build.T("Son değişikliği geri al (Ctrl+Z)", "Undo the last change (Ctrl+Z)");
		ReloadButton.ToolTip = Build.T("Kaydı yeniden oku (F5)", "Reload save from game (F5)");
		FrozenButton.ToolTip = Build.T("Dondurulan kayıtları göster", "Show frozen records");
		_graphics = ReloadGraphicsAsync();
		ApplyPreferences();
		_connectTimer.Tick += (_, _) => TryConnect();
		_liveTimer.Tick += (_, _) => LiveRefresh();
		_freezeTimer.Tick += (_, _) => _data?.Session.Freezer.Tick(_data.Memory);
		_searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
		EditField.Written += (_, f) => UpdateCounts();
		SourceInitialized += (_, _) => DarkTitleBar();
		Loaded += (_, _) =>
		{
			ShowWaiting();
			if (AppSettings.Current.AutoConnect)
			{
				TryConnect();
				_connectTimer.Start();
			}
		};
		Closed += (_, _) =>
		{
			RememberTabs();
			_data?.Dispose();
		};
		InputBindings.Add(new KeyBinding(new RelayCommand(() => { TopSearch.Focus(); TopSearch.SelectAll(); }), Key.F, ModifierKeys.Control));
		InputBindings.Add(new KeyBinding(new RelayCommand(Undo), Key.Z, ModifierKeys.Control));
		InputBindings.Add(new KeyBinding(new RelayCommand(() => { if (_current != null && _current.Key != "home") ClosePage(_current); }), Key.W, ModifierKeys.Control));
		InputBindings.Add(new KeyBinding(new RelayCommand(ReloadSave), Key.F5, ModifierKeys.None));
	}

	// ---------------------------------------------------------------- connection

	private void TryConnect()
	{
		if (_data != null)
		{
			if (_data.Memory.HasExited)
			{
				Disconnect(Build.T("Oyun kapandı.", "The game was closed."));
			}
			return;
		}
		if (_connecting)
		{
			return;
		}
		foreach (LayoutFile file in Layouts.All())
		{
			GameLayout layout;
			try
			{
				layout = file.Json != null ? GameLayout.Parse(file.Json) : GameLayout.Load(file.Path!);
			}
			catch (LayoutException)
			{
				continue;
			}
			Process? process = Process.GetProcessesByName(layout.ProcessName).FirstOrDefault();
			if (process == null || process.Id == _failedPid)
			{
				continue;
			}
			_ = ConnectAsync(layout, process.Id);
			return;
		}
	}

	private async Task ConnectAsync(GameLayout layout, int pid)
	{
		_connecting = true;
		OverlayTitle.Text = Build.T("Kayıt okunuyor…", "Reading the save…");
		OverlayText.Text = $"{layout.Game} {layout.GameVersion}  ·  PID {pid}";
		OverlayProgress.Visibility = Visibility.Visible;
		try
		{
			GameData data = GameData.Open(layout);
			var progress = new Progress<string>(step => OverlayText.Text = step switch
			{
				"clubs" => Build.T("Bellek taranıyor, kulüpler okunuyor…", "Scanning memory, reading clubs…"),
				"staff" => Build.T("Personel okunuyor…", "Reading staff…"),
				_ => Build.T("Oyuncular okunuyor…", "Reading players…")
			});
			var watch = Stopwatch.StartNew();
			await data.LoadAsync(progress);
			await _graphics;
			_data = data;
			_data.Session.History.Changed += (_, _) => Dispatcher.Invoke(UpdateCounts);
			_data.Session.Freezer.Changed += (_, _) => Dispatcher.Invoke(UpdateCounts);
			StatusDot.Fill = Build.Res("Positive");
			StatusText.Text = $"{layout.Game}  ·  {layout.GameVersion}";
			StatusPill.ToolTip = $"PID {pid}";
			ReloadButton.Visibility = Visibility.Visible;
			Overlay.Visibility = Visibility.Collapsed;
			_pages.Clear();
			OpenPage(new HomePage(data, this));
			UpdateCounts();
			SetStatus(Build.T($"Bağlandı. {data.Players.Count:N0} oyuncu, {data.Staff.Count:N0} personel ve {data.Clubs.Count:N0} kulüp {watch.Elapsed.TotalSeconds:0.0} saniyede okundu.",
				$"Connected. {data.Players.Count:N0} players, {data.Staff.Count:N0} staff and {data.Clubs.Count:N0} clubs read in {watch.Elapsed.TotalSeconds:0.0} s."));
			if (AppSettings.Current.LiveRefresh)
			{
				_liveTimer.Start();
			}
			_freezeTimer.Start();
			RestoreTabs(data);
			OpenFromCommandLine(data);
			if (Environment.GetCommandLineArgs().Contains("--settings"))
			{
				Settings_Click(this, new RoutedEventArgs());
			}
		}
		catch (Exception ex)
		{
			_failedPid = pid;
			OverlayTitle.Text = Build.T("Bağlanılamadı", "Could not connect");
			OverlayText.Text = ex.Message + "\n\n" + Build.T("Kaydın yüklü olduğundan emin ol, sonra Tekrar dene'ye bas.", "Make sure a save is loaded, then press Retry.");
			ConnectButton.Content = Build.T("Tekrar dene", "Retry");
			ConnectButton.Visibility = Visibility.Visible;
			OverlayProgress.Visibility = Visibility.Collapsed;
		}
		finally
		{
			_connecting = false;
		}
	}

	/// <summary>--open "name" opens a player, --club "name" a club (handy for shortcuts and testing).</summary>
	private void OpenFromCommandLine(GameData data)
	{
		string[] args = Environment.GetCommandLineArgs();
		for (int i = 1; i + 1 < args.Length; i++)
		{
			string q = GameData.Normalize(args[i + 1]);
			if (args[i] == "--club" && data.Clubs.Where(c => c.Search.Contains(q)).OrderByDescending(c => c.SquadSize).FirstOrDefault() is { } club)
			{
				OpenClub(club.Address);
			}
			else if (args[i] == "--staff" && data.Staff.Where(s => s.Search.Contains(q)).FirstOrDefault() is { } staff)
			{
				OpenStaff(staff.Address);
			}
			else if (args[i] == "--open" && data.Players.Where(p => p.Search.Contains(q)).OrderByDescending(p => p.CA).FirstOrDefault() is { } player)
			{
				OpenPlayer(player.Address);
			}
		}
	}

	private void Disconnect(string reason)
	{
		_liveTimer.Stop();
		_freezeTimer.Stop();
		_data?.Dispose();
		_data = null;
		_pages.Clear();
		_current = null;
		Host.Content = null;
		TabStrip.Children.Clear();
		ReloadButton.Visibility = Visibility.Collapsed;
		FrozenButton.Visibility = Visibility.Collapsed;
		ShowWaiting();
		SetStatus(reason);
	}

	/// <summary>Zoom, live refresh and the read-only badge from the settings.</summary>
	private void ApplyPreferences()
	{
		double scale = Math.Clamp(AppSettings.Current.UiScale, 0.8, 1.5);
		((FrameworkElement)Content).LayoutTransform = Math.Abs(scale - 1.0) < 0.01 ? Transform.Identity : new ScaleTransform(scale, scale);
		_liveTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(AppSettings.Current.LiveRefreshMs, 500, 10000));
		if (_data != null)
		{
			if (AppSettings.Current.LiveRefresh) _liveTimer.Start(); else _liveTimer.Stop();
		}
		ReadOnlyBadge.Visibility = AppSettings.Current.ReadOnlyMode ? Visibility.Visible : Visibility.Collapsed;
		if (AppSettings.Current.AutoConnect)
		{
			_connectTimer.Start();
		}
		else
		{
			_connectTimer.Stop();
		}
		ConnectButton.Visibility = AppSettings.Current.AutoConnect ? Visibility.Collapsed : Visibility.Visible;
	}

	private void Connect_Click(object sender, RoutedEventArgs e)
	{
		_failedPid = 0;
		ConnectButton.Visibility = AppSettings.Current.AutoConnect ? Visibility.Collapsed : Visibility.Visible;
		TryConnect();
	}

	private void Reload_Click(object sender, RoutedEventArgs e) => ReloadSave();

	private void ReloadSave()
	{
		if (_data == null || _connecting)
		{
			return;
		}
		RememberTabs();
		Disconnect(Build.T("Kayıt yeniden okunuyor…", "Reloading save…"));
		_failedPid = 0;
		TryConnect();
	}

	private void Frozen_Click(object sender, RoutedEventArgs e)
	{
		if (_pages.OfType<HomePage>().FirstOrDefault() is { } home)
		{
			OpenPage(home);
			home.ShowFrozen();
		}
	}

	/// <summary>Open tabs are remembered by unique ID: memory addresses change every session.</summary>
	private void RememberTabs()
	{
		if (_data == null || !AppSettings.Current.RestoreTabs)
		{
			return;
		}
		AppSettings.Current.LastTabs = _pages.Select(p => p switch
		{
			PlayerPage pp => "player:" + pp.Uid,
			ClubPage cp => "club:" + cp.Uid,
			StaffPage sp => "staff:" + sp.Row.Uid,
			_ => ""
		}).Where(k => k.Length > 0).ToList();
		AppSettings.Current.Save();
	}

	private void RestoreTabs(GameData data)
	{
		if (!AppSettings.Current.RestoreTabs)
		{
			return;
		}
		IPage? home = _current;
		foreach (string key in AppSettings.Current.LastTabs)
		{
			string[] parts = key.Split(':');
			if (parts.Length != 2 || !uint.TryParse(parts[1], out uint uid) || uid == 0)
			{
				continue;
			}
			switch (parts[0])
			{
			case "player" when data.Players.FirstOrDefault(p => p.Uid == uid) is { } player: OpenPlayer(player.Address); break;
			case "club" when data.Clubs.FirstOrDefault(c => c.Uid == uid) is { } club: OpenClub(club.Address); break;
			case "staff" when data.Staff.FirstOrDefault(s => s.Uid == uid) is { } staff: OpenStaff(staff.Address); break;
			}
		}
		if (home != null)
		{
			OpenPage(home);
		}
	}

	private void ShowWaiting()
	{
		Overlay.Visibility = Visibility.Visible;
		OverlayTitle.Text = Build.T("Football Manager bekleniyor", "Waiting for Football Manager");
		OverlayText.Text = AppSettings.Current.AutoConnect
			? Build.T("Oyunu aç ve kaydını yükle. SquadEditor kendiliğinden bağlanır.", "Start the game and load your save. SquadEditor connects on its own.")
			: Build.T("Oyunu aç, kaydını yükle ve Bağlan'a bas.", "Start the game, load your save and press Connect.");
		OverlayProgress.Visibility = Visibility.Visible;
		StatusDot.Fill = Build.Res("Faint");
		StatusText.Text = Build.T("Bağlı değil", "Not connected");
		Counts.Text = "";
	}

	// ---------------------------------------------------------------- pages and tabs

	public void OpenPlayer(ulong address)
	{
		if (_data != null)
		{
			OpenPage(_pages.FirstOrDefault(p => p.Key == "player:" + address) ?? new PlayerPage(_data, address, this));
		}
	}

	public void OpenStaff(ulong address)
	{
		if (_data != null && _data.Staff.FirstOrDefault(s => s.Address == address) is { } row)
		{
			OpenPage(_pages.FirstOrDefault(p => p.Key == "staff:" + address) ?? new StaffPage(_data, row, this));
		}
	}

	public void OpenClub(ulong address)
	{
		if (_data != null && _data.ClubsByAddress.TryGetValue(address, out ClubRow? club))
		{
			OpenPage(_pages.FirstOrDefault(p => p.Key == "club:" + address) ?? new ClubPage(_data, club, this));
		}
	}

	private void OpenPage(IPage page)
	{
		if (!_pages.Contains(page))
		{
			_pages.Add(page);
		}
		_current = page;
		Host.Content = page;
		RebuildTabs();
	}

	private void ClosePage(IPage page)
	{
		int index = _pages.IndexOf(page);
		_pages.Remove(page);
		if (_current == page)
		{
			IPage next = _pages[Math.Max(0, index - 1)];
			_current = next;
			Host.Content = next;
		}
		RebuildTabs();
	}

	private void RebuildTabs()
	{
		TabStrip.Children.Clear();
		foreach (IPage page in _pages)
		{
			bool active = page == _current;
			var row = new StackPanel { Orientation = Orientation.Horizontal };
			if (page.Icon != null)
			{
				row.Children.Add(new Image { Source = page.Icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
			}
			row.Children.Add(new TextBlock { Text = page.Title, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis });
			if (page.Key != "home")
			{
				var close = new Button { Content = "✕", Style = Build.StyleOf("GhostButton"), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(8, 0, -6, 0), FontSize = 10, Foreground = Build.Res("Muted") };
				close.Click += (_, e) => { ClosePage(page); e.Handled = true; };
				row.Children.Add(close);
			}
			var tab = new Border
			{
				Child = row,
				Padding = new Thickness(12, 0, 12, 0),
				Margin = new Thickness(0, 0, 2, 0),
				Background = active ? Build.Res("Bg") : Brushes.Transparent,
				BorderBrush = active ? Build.Res("Accent") : Brushes.Transparent,
				BorderThickness = new Thickness(0, 0, 0, 2),
				Cursor = Cursors.Hand
			};
			tab.MouseLeftButtonUp += (_, _) => OpenPage(page);
			tab.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle && page.Key != "home") ClosePage(page); };
			TabStrip.Children.Add(tab);
		}
	}

	private void LiveRefresh()
	{
		if (_data == null || _current == null || Keyboard.FocusedElement is TextBox { IsReadOnly: false } box && box != TopSearch && IsDescendant(box))
		{
			return;
		}
		try
		{
			_current.RefreshValues();
		}
		catch (Exception)
		{
		}
	}

	private bool IsDescendant(DependencyObject element)
	{
		return Host.Content is DependencyObject root && (element == root || IsAncestor(root, element));
	}

	private static bool IsAncestor(DependencyObject ancestor, DependencyObject element)
	{
		for (DependencyObject? d = element; d != null; d = VisualTreeHelper.GetParent(d))
		{
			if (d == ancestor)
			{
				return true;
			}
		}
		return false;
	}

	// ---------------------------------------------------------------- search

	private void TopSearch_TextChanged(object sender, TextChangedEventArgs e)
	{
		TopSearchHint.Visibility = TopSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
		_searchTimer.Stop();
		_searchTimer.Start();
	}

	private void RunSearch()
	{
		string q = GameData.Normalize(TopSearch.Text.Trim());
		if (_data == null || q.Length < 2)
		{
			SearchPopup.IsOpen = false;
			return;
		}
		var hits = new List<SearchHit>();
		foreach (ClubRow c in _data.Clubs.Where(c => c.SquadSize > 0 && c.Search.Contains(q)).OrderByDescending(c => c.Search.StartsWith(q)).ThenByDescending(c => c.AverageCA).Take(5))
		{
			ClubRow club = c;
			hits.Add(new SearchHit
			{
				Title = c.Name,
				Subtitle = string.Join("  ·  ", new[] { c.League, c.Nation }.Where(s => s.Length > 0)),
				Image = ImageLookup.Graphics.Club(c.Uid),
				Corner = new CornerRadius(6),
				Badge = Build.T("Kulüp", "Club"),
				BadgeBrush = Build.Res("Muted"),
				Open = () => OpenClub(club.Address)
			});
		}
		foreach (PlayerRow p in _data.Players.Where(p => p.Search.Contains(q)).OrderByDescending(p => p.CA).Take(10))
		{
			PlayerRow player = p;
			hits.Add(new SearchHit
			{
				Title = p.Name,
				Subtitle = string.Join("  ·  ", new[] { p.Positions, p.Club, p.Nation }.Where(s => s.Length > 0)),
				Image = ImageLookup.Graphics.Person(p.Uid),
				Badge = p.CA.ToString(),
				BadgeBrush = Colors.Attribute((int)Math.Round(p.CA / 10.0)),
				Open = () => OpenPlayer(player.Address)
			});
		}
		foreach (StaffRow s in _data.Staff.Where(s => s.Search.Contains(q)).OrderBy(s => s.Club.Length == 0).Take(5))
		{
			StaffRow staff = s;
			hits.Add(new SearchHit
			{
				Title = s.Name,
				Subtitle = string.Join("  ·  ", new[] { s.Job, s.Club, s.Nation }.Where(x => x.Length > 0)),
				Image = ImageLookup.Graphics.Person(s.Uid),
				Badge = Build.T("Personel", "Staff"),
				BadgeBrush = Build.Res("Muted"),
				Open = () => OpenStaff(staff.Address)
			});
		}
		SearchResults.ItemsSource = hits;
		SearchPopup.IsOpen = hits.Count > 0;
		if (hits.Count > 0)
		{
			SearchResults.SelectedIndex = 0;
		}
	}

	private void OpenHit(SearchHit? hit)
	{
		if (hit == null)
		{
			return;
		}
		SearchPopup.IsOpen = false;
		TopSearch.Text = "";
		hit.Open();
	}

	private void TopSearch_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
		case Key.Down when SearchPopup.IsOpen:
			SearchResults.SelectedIndex = Math.Min(SearchResults.SelectedIndex + 1, SearchResults.Items.Count - 1);
			e.Handled = true;
			break;
		case Key.Up when SearchPopup.IsOpen:
			SearchResults.SelectedIndex = Math.Max(SearchResults.SelectedIndex - 1, 0);
			e.Handled = true;
			break;
		case Key.Enter:
			_searchTimer.Stop();
			RunSearch();
			OpenHit(SearchResults.SelectedItem as SearchHit);
			e.Handled = true;
			break;
		case Key.Escape:
			SearchPopup.IsOpen = false;
			e.Handled = true;
			break;
		}
	}

	private void SearchResults_MouseUp(object sender, MouseButtonEventArgs e) => OpenHit(SearchResults.SelectedItem as SearchHit);

	private void SearchResults_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			OpenHit(SearchResults.SelectedItem as SearchHit);
			e.Handled = true;
		}
	}

	// ---------------------------------------------------------------- undo, settings, status

	private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

	private void Undo()
	{
		if (_data == null)
		{
			return;
		}
		SquadEditor.Core.Editing.EditBatch? batch;
		try
		{
			batch = _data.Session.History.UndoLast(_data.Memory);
		}
		catch (SquadEditor.Core.Editing.GuardedUndoException ex)
		{
			SetStatus(Build.T($"\"{ex.Batch.Label}\" geri alınamadı: oyun bu arada değiştirdi (Devam'a basıldı). Geçmişten çıkarıldı; geri dönmek için yedeği yükle.",
				$"\"{ex.Batch.Label}\" could not be undone: the game has changed it since. It was dropped from the history; load your backup to go back."));
			UpdateCounts();
			return;
		}
		_current?.RefreshValues();
		SetStatus(batch == null ? Build.T("Geri alınacak değişiklik yok.", "Nothing to undo.") : Build.T("Geri alındı.", "Undone."));
		UpdateCounts();
	}

	private void UpdateCounts()
	{
		if (_data == null)
		{
			return;
		}
		int changes = _data.Session.History.Entries.Count;
		int frozen = _data.Session.Freezer.Entities.Count;
		FrozenText.Text = $"❄ {frozen}";
		FrozenButton.Visibility = frozen > 0 ? Visibility.Visible : Visibility.Collapsed;
		Counts.Text = Build.T($"Oyuncu {_data.Players.Count:N0}  ·  Kulüp {_data.Clubs.Count:N0}  ·  Değişiklik {changes}" + (frozen > 0 ? $"  ·  Dondurulan {frozen}" : "") + $"  ·  Logo {ImageLookup.Graphics.ClubLogos:N0}  ·  Yüz {ImageLookup.Graphics.Faces:N0}",
			$"Players {_data.Players.Count:N0}  ·  Clubs {_data.Clubs.Count:N0}  ·  Changes {changes}" + (frozen > 0 ? $"  ·  Frozen {frozen}" : "") + $"  ·  Logos {ImageLookup.Graphics.ClubLogos:N0}  ·  Faces {ImageLookup.Graphics.Faces:N0}");
		UndoButton.IsEnabled = changes > 0;
	}

	public void SetStatus(string text) => Message.Text = text;

	private Task _graphics = Task.CompletedTask;

	/// <summary>Indexes logo and face packs in the background (tens of thousands of files).</summary>
	private async Task ReloadGraphicsAsync()
	{
		string? root = AppSettings.Current.GraphicsFolder is { Length: > 0 } custom && Directory.Exists(custom) ? custom : GraphicsIndex.FindDefaultRoot();
		ImageLookup.Graphics = await Task.Run(() => GraphicsIndex.Build(root));
	}

	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new SettingsWindow { Owner = this };
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		if (dialog.NeedsRestart && Environment.ProcessPath is { } exe)
		{
			Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
			Application.Current.Shutdown();
			return;
		}
		await ReloadGraphicsAsync();
		// Pages are rebuilt so money, wages and images follow the new settings.
		string? currentKey = _current?.Key;
		var keys = _pages.Select(p => p.Key).ToList();
		_pages.Clear();
		_current = null;
		if (_data != null)
		{
			foreach (string key in keys)
			{
				string[] parts = key.Split(':');
				ulong address = parts.Length == 2 ? ulong.Parse(parts[1]) : 0;
				switch (parts[0])
				{
				case "home": _pages.Add(new HomePage(_data, this)); break;
				case "player": _pages.Add(new PlayerPage(_data, address, this)); break;
				case "club" when _data.ClubsByAddress.TryGetValue(address, out ClubRow? club): _pages.Add(new ClubPage(_data, club, this)); break;
				case "staff" when _data.Staff.FirstOrDefault(s => s.Address == address) is { } staff: _pages.Add(new StaffPage(_data, staff, this)); break;
				}
			}
			OpenPage(_pages.FirstOrDefault(p => p.Key == currentKey) ?? _pages[0]);
		}
		ApplyPreferences();
		UpdateCounts();
		SetStatus(Build.T("Ayarlar kaydedildi.", "Settings saved."));
	}

	private void DarkTitleBar()
	{
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		int on = 1;
		DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
