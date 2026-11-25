using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Layout;

namespace SquadEditor.App.Ui;

public sealed class SearchHit
{
	public string Title { get; init; } = "";
	public string Subtitle { get; init; } = "";
	public IImage? Image { get; init; }
	public string Initials => string.Concat(Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
	public CornerRadius Corner { get; init; } = new CornerRadius(16);
	public string Badge { get; init; } = "";
	public IBrush BadgeBrush { get; init; } = Colors.Text;
	public Action Open { get; init; } = () => { };
}

public partial class EditorWindow : Window, INavigator
{
	private readonly List<IPage> _pages = new List<IPage>();
	private readonly DispatcherTimer _connectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
	private readonly DispatcherTimer _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
	private readonly DispatcherTimer _freezeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
	private readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
	private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3600) };
	private GameData? _data;
	private IPage? _current;
	private bool _connecting;
	private int _failedPid;

	public EditorWindow()
	{
		InitializeComponent();
		Title = "SquadEditor";
		TopSearchHint.Text = Build.T("Oyuncu, kulüp, ülke veya lig ara…", "Search players, clubs, nations or leagues…");
		TopSearchShortcutText.Text = PlatformPaths.SearchHint;
		CommandShortcutText.Text = PlatformPaths.IsMac ? "⌘K" : $"⌘ {PlatformPaths.PaletteHint}";
		ToolTip.SetTip(CommandButton, Build.T($"Hızlı komut paleti ({PlatformPaths.PaletteHint})", $"Quick command palette ({PlatformPaths.PaletteHint})"));
		ToastUndoBtn.Content = Build.T("↶ Geri Al", "↶ Undo");
		_toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastBanner.IsVisible = false; };
		UndoText.Text = Build.T("Geri al", "Undo");
		ConnectButton.Content = Build.T("Bağlan", "Connect");
		ToolTip.SetTip(ReadOnlyBadge, Build.T("Salt okunur mod: oyuna hiçbir şey yazılmaz", "Read-only mode: nothing is written to the game"));
		if (ReadOnlyBadge.Child is TextBlock rob) rob.Text = Build.T("🔒 Salt okunur", "🔒 Read-only");
		ToolTip.SetTip(UndoButton, Build.T($"Son değişikliği geri al ({PlatformPaths.UndoHint})", $"Undo the last change ({PlatformPaths.UndoHint})"));
		ToolTip.SetTip(ReloadButton, Build.T("Kaydı yeniden oku (F5)", "Reload save from game (F5)"));
		ToolTip.SetTip(FrozenButton, Build.T("Dondurulan kayıtları göster", "Show frozen records"));
		BackupText.Text = Build.T("💾 Yedek", "💾 Backup");
		ToolTip.SetTip(BackupButton, Build.T("En güncel FM kaydının yedeğini al", "Back up your latest FM save file"));
		ToolTip.SetTip(HistoryButton, Build.T("Değişiklik geçmişini aç", "Open change history"));
		ToolTip.SetTip(SettingsButton, Build.T("Ayarlar", "Settings"));

		TopSearch.AddHandler(InputElement.KeyDownEvent, TopSearch_PreviewKeyDown, RoutingStrategies.Tunnel);
		SearchResults.AddHandler(InputElement.KeyDownEvent, SearchResults_PreviewKeyDown, RoutingStrategies.Tunnel);
		SearchResults.PointerReleased += SearchResults_PointerReleased;

		_graphics = ReloadGraphicsAsync();
		ApplyPreferences();
		_connectTimer.Tick += (_, _) => TryConnect();
		_liveTimer.Tick += (_, _) => LiveRefresh();
		_freezeTimer.Tick += (_, _) => { if (!AppSettings.Current.ReadOnlyMode) _data?.Session.Freezer.Tick(_data.Memory); };
		_searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
		EditField.Written += (_, f) =>
		{
			UpdateCounts();
			SetStatus(Build.F("✓ {0} → {1}  ({2} geri alır · Oyunda görmek için profili kapatıp aç veya Devam'a bas)", "✓ {0} → {1}  ({2} undoes · Reopen profile or press Continue in FM to see it)", f.Label, f.Display, PlatformPaths.UndoHint));
		};
		Opened += (_, _) =>
		{
			WindowHelper.FitToScreen(this, isMain: true);
			WindowHelper.ApplyNativeTitleBar(this);
		};
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

		void AddShortcut(Key key, KeyModifiers mod, Action act) =>
			KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, mod), Command = new RelayCommand(act) });

		Action focusSearch = () => { TopSearch.Focus(); TopSearch.SelectAll(); };
		Action closeTab = () => { if (_current != null && _current.Key != "home") ClosePage(_current); };
		AddShortcut(Key.F, PlatformPaths.ActionModifier, focusSearch);
		AddShortcut(Key.K, PlatformPaths.ActionModifier, OpenCommandPalette);
		AddShortcut(Key.Z, PlatformPaths.ActionModifier, Undo);
		AddShortcut(Key.W, PlatformPaths.ActionModifier, closeTab);
		if (PlatformPaths.IsMac)
		{
			AddShortcut(Key.F, KeyModifiers.Control, focusSearch);
			AddShortcut(Key.K, KeyModifiers.Control, OpenCommandPalette);
			AddShortcut(Key.Z, KeyModifiers.Control, Undo);
			AddShortcut(Key.W, KeyModifiers.Control, closeTab);
		}
		AddShortcut(Key.F5, KeyModifiers.None, ReloadSave);
	}

	// ---------------------------------------------------------------- connection

	private void TryConnect()
	{
		if (_data != null)
		{
			if (_data.HasExited)
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
			using Process? process = GameData.FindGameProcess(layout.ProcessName);
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
		OverlayProgress.IsVisible = true;
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
			Build.Translator = s => data.Layout.Text(Loc.Instance.Language, s);
			_data.Session.Freezer.AllowIncrease = AppSettings.Current.FreezeAllowIncrease;
			RestoreFrozen(data);
			_data.Session.History.Changed += (_, _) => Dispatcher.UIThread.Post(UpdateCounts);
			_data.Session.Freezer.Changed += (_, _) => Dispatcher.UIThread.Post(() => { SaveFrozen(); UpdateCounts(); });
			StatusDot.Fill = Build.Res("Positive");
			StatusText.Text = $"{layout.Game}  ·  {layout.GameVersion}";
			ToolTip.SetTip(StatusPill, $"PID {pid}");
			ReloadButton.IsVisible = true;
			Overlay.IsVisible = false;
			_pages.Clear();
			OpenPage(new HomePage(data, this));
			UpdateCounts();
			SetStatus(Build.F("Bağlandı. {0:N0} oyuncu, {1:N0} personel ve {2:N0} kulüp {3:0.0} saniyede okundu.", "Connected. {0:N0} players, {1:N0} staff and {2:N0} clubs read in {3:0.0} s.", data.Players.Count, data.Staff.Count, data.Clubs.Count, watch.Elapsed.TotalSeconds));
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
			OverlayTitle.Text = Build.T("Oyun kaydına bağlanılamadı", "Could not connect to save");
			OverlayText.Text = Build.T("Oyun açık görünüyor ama henüz bir kariyer kaydı yüklenmemiş olabilir. FM27'de kaydını yükleyip Tekrar dene'ye bas.",
				"The game appears to be running, but a save may not be loaded yet. Load your save in FM27 and press Retry.")
				+ $"\n\n({ex.Message})";
			ConnectButton.Content = Build.T("Tekrar dene", "Retry");
			ConnectButton.IsVisible = true;
			OverlayProgress.IsVisible = false;
		}
		finally
		{
			_connecting = false;
		}
	}

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
		ReloadButton.IsVisible = false;
		FrozenButton.IsVisible = false;
		HistoryButton.IsVisible = false;
		ShowWaiting();
		SetStatus(reason);
	}

	/// <summary>Zoom, live refresh and the read-only badge from the settings.</summary>
	private void ApplyPreferences()
	{
		double scale = Math.Clamp(AppSettings.Current.UiScale, 0.8, 1.5);
		ZoomRoot.LayoutTransform = Math.Abs(scale - 1.0) < 0.01 ? null : new ScaleTransform(scale, scale);
		_liveTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(AppSettings.Current.LiveRefreshMs, 500, 10000));
		if (_data != null)
		{
			if (AppSettings.Current.LiveRefresh) _liveTimer.Start(); else _liveTimer.Stop();
		}
		ReadOnlyBadge.IsVisible = AppSettings.Current.ReadOnlyMode;
		if (AppSettings.Current.AutoConnect)
		{
			_connectTimer.Start();
		}
		else
		{
			_connectTimer.Stop();
		}
		ConnectButton.IsVisible = !AppSettings.Current.AutoConnect;
	}

	private void Connect_Click(object? sender, RoutedEventArgs e)
	{
		_failedPid = 0;
		ConnectButton.IsVisible = !AppSettings.Current.AutoConnect;
		TryConnect();
	}

	private void Reload_Click(object? sender, RoutedEventArgs e) => ReloadSave();

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

	private void Frozen_Click(object? sender, RoutedEventArgs e)
	{
		if (_pages.OfType<HomePage>().FirstOrDefault() is { } home)
		{
			OpenPage(home);
			home.ShowFrozen();
		}
	}

	private void History_Click(object? sender, RoutedEventArgs e)
	{
		if (_data == null)
		{
			return;
		}
		var win = new HistoryWindow(_data, () => { _current?.RefreshValues(); UpdateCounts(); });
		_ = win.ShowDialog(this);
	}

	private void Backup_Click(object? sender, RoutedEventArgs e)
	{
		FileInfo? save = SaveBackup.FindLatestSave();
		if (save == null)
		{
			SetStatus(Build.T("Sports Interactive\\Football Manager 27\\games klasöründe .fm kaydı bulunamadı.",
				"No .fm save file found in Sports Interactive / Football Manager 27 / games."));
			return;
		}
		try
		{
			string dest = SaveBackup.Create(save);
			SetStatus(Build.F("✓ Yedek alındı: {0} (SquadEditor_Backups)", "✓ Backup created: {0} (SquadEditor_Backups)", Path.GetFileName(dest)));
		}
		catch (Exception ex)
		{
			SetStatus(Build.T("Yedek alınamadı: ", "Backup failed: ") + ex.Message);
		}
	}

	private void SaveFrozen()
	{
		if (_data == null)
		{
			return;
		}
		var uids = new List<string>();
		foreach (var e in _data.Session.Freezer.Entities)
		{
			if (_data.Players.FirstOrDefault(p => p.Address == e.Entity) is { } p && p.Uid > 0)
			{
				uids.Add("player:" + p.Uid);
			}
			else if (_data.Staff.FirstOrDefault(s => s.Address == e.Entity) is { } s && s.Uid > 0)
			{
				uids.Add("staff:" + s.Uid);
			}
		}
		AppSettings.Current.SetFrozen(_data.SaveKey, uids);
		AppSettings.Current.Save();
	}

	private static void RestoreFrozen(GameData data)
	{
		foreach (string key in AppSettings.Current.GetFrozen(data.SaveKey))
		{
			string[] parts = key.Split(':');
			if (parts.Length != 2 || !uint.TryParse(parts[1], out uint uid) || uid == 0)
			{
				continue;
			}
			if (parts[0] == "player" && data.Players.FirstOrDefault(p => p.Uid == uid) is { } p)
			{
				data.Session.FreezeEntity(p.Address, p.Name, data.PlayerDef, PlayerPage.FreezeGroups);
			}
			else if (parts[0] == "staff" && data.Staff.FirstOrDefault(s => s.Uid == uid) is { } s)
			{
				data.Session.FreezeEntity(s.Address, s.Name, data.StaffDef, StaffPage.StaffFreezeGroups);
			}
		}
	}

	private void RememberTabs()
	{
		if (_data == null || !AppSettings.Current.RestoreTabs)
		{
			return;
		}
		AppSettings.Current.LastTabs = _pages.Select(PageUidKey).Where(k => k.Length > 0).ToList();
		AppSettings.Current.LastActiveTab = _current == null ? null : PageUidKey(_current);
		AppSettings.Current.Save();
	}

	private static string PageUidKey(IPage p) => p switch
	{
		PlayerPage pp => "player:" + pp.Uid,
		ClubPage cp => "club:" + cp.Uid,
		StaffPage sp => "staff:" + sp.Row.Uid,
		_ => ""
	};

	private void RestoreTabs(GameData data)
	{
		if (!AppSettings.Current.RestoreTabs)
		{
			return;
		}
		IPage? home = _current;
		IPage? active = null;
		string? wanted = AppSettings.Current.LastActiveTab;
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
			if (key == wanted)
			{
				active = _current;
			}
		}
		if (active != null)
		{
			OpenPage(active);
		}
		else if (home != null)
		{
			OpenPage(home);
		}
	}

	private void ShowWaiting()
	{
		Overlay.IsVisible = true;
		OverlayTitle.Text = Build.T("Football Manager bekleniyor", "Waiting for Football Manager");
		OverlayText.Text = AppSettings.Current.AutoConnect
			? Build.T("Oyunu aç ve kaydını yükle. SquadEditor kendiliğinden bağlanır.", "Start the game and load your save. SquadEditor connects on its own.")
			: Build.T("Oyunu aç, kaydını yükle ve Bağlan'a bas.", "Start the game, load your save and press Connect.");
		OverlayProgress.IsVisible = true;
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
			if (club.Uid > 0 && AppSettings.Current.LastClubUid != club.Uid)
			{
				AppSettings.Current.LastClubUid = club.Uid;
				AppSettings.Current.Save();
			}
			OpenPage(_pages.FirstOrDefault(p => p.Key == "club:" + address) ?? new ClubPage(_data, club, this));
		}
	}

	public void OpenNation(ulong address)
	{
		if (_data != null && _data.Nations.FirstOrDefault(n => n.Address == address) is { } nation)
		{
			OpenPage(_pages.FirstOrDefault(p => p.Key == "nation:" + address) ?? new NationPage(_data, nation, this));
		}
	}

	public void OpenComp(ulong address)
	{
		if (_data != null && _data.Competitions.FirstOrDefault(c => c.Address == address) is { } comp)
		{
			OpenPage(_pages.FirstOrDefault(p => p.Key == "comp:" + address) ?? new CompPage(_data, comp, this));
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
			IPage target = page;
			bool active = target == _current;
			var row = new StackPanel { Orientation = Orientation.Horizontal };
			if (target.Icon != null)
			{
				row.Children.Add(new Image { Source = target.Icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
			}
			row.Children.Add(new TextBlock { Text = target.Title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis });
			if (target.Key != "home")
			{
				var close = new Button { Content = "✕", Theme = Build.StyleOf("GhostButton"), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(8, 0, -6, 0), FontSize = 10, Foreground = Build.Res("Muted") };
				close.Click += (_, e) => { ClosePage(target); e.Handled = true; };
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
				Cursor = new Cursor(StandardCursorType.Hand)
			};
			tab.PointerReleased += (_, e) =>
			{
				if (e.InitialPressMouseButton == MouseButton.Middle && target.Key != "home")
				{
					ClosePage(target);
				}
				else if (e.InitialPressMouseButton == MouseButton.Left)
				{
					OpenPage(target);
				}
			};
			TabStrip.Children.Add(tab);
		}
	}

	private void LiveRefresh()
	{
		if (_data == null || _current == null)
		{
			return;
		}
		if (FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false } box && box != TopSearch && IsDescendant(box))
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

	private bool IsDescendant(Visual element)
	{
		if (Host.Content is not Visual root) return false;
		return element == root || element.GetVisualAncestors().Any(a => a == root);
	}

	// ---------------------------------------------------------------- search

	private void TopSearch_TextChanged(object? sender, TextChangedEventArgs e)
	{
		bool empty = string.IsNullOrEmpty(TopSearch.Text);
		TopSearchHint.IsVisible = empty;
		TopSearchShortcut.IsVisible = empty;
		TopSearchClear.IsVisible = !empty;
		_searchTimer.Stop();
		_searchTimer.Start();
	}

	private void TopSearchClear_Click(object? sender, RoutedEventArgs e)
	{
		TopSearch.Text = "";
		SearchPopup.IsOpen = false;
		TopSearch.Focus();
	}

	private void CommandButton_Click(object? sender, RoutedEventArgs e) => OpenCommandPalette();

	private void RunSearch()
	{
		string q = GameData.Normalize((TopSearch.Text ?? "").Trim());
		if (_data == null || q.Length < 2)
		{
			SearchPopup.IsOpen = false;
			return;
		}
		var hits = new List<SearchHit>();
		foreach (NationRow n in _data.Nations.Where(n => n.Search.Contains(q)).OrderByDescending(n => n.Search.StartsWith(q)).ThenByDescending(n => n.YouthRating).Take(3))
		{
			NationRow nation = n;
			hits.Add(new SearchHit
			{
				Title = n.Name,
				Subtitle = Build.F("Gençlik {0}  ·  {1}", "Youth {0}  ·  {1}", n.YouthRating, n.Development),
				Corner = new CornerRadius(6),
				Badge = Build.T("Ülke", "Nation"),
				BadgeBrush = Build.Res("Positive"),
				Open = () => OpenNation(nation.Address)
			});
		}
		foreach (CompRow comp in _data.Competitions
			.Where(c => c.Search.Contains(q))
			.OrderByDescending(c => GameData.Normalize(c.ShortName) == q || GameData.Normalize(c.Name) == q)
			.ThenByDescending(c => GameData.Normalize(c.ShortName).StartsWith(q) || GameData.Normalize(c.Name).StartsWith(q))
			.ThenByDescending(c => c.Reputation)
			.Take(6))
		{
			CompRow league = comp;
			hits.Add(new SearchHit
			{
				Title = comp.Name,
				Subtitle = Build.F("İtibar {0} / 200", "Reputation {0} / 200", comp.Reputation),
				Corner = new CornerRadius(6),
				Badge = Build.T("Lig", "League"),
				BadgeBrush = Build.Res("Accent"),
				Open = () => OpenComp(league.Address)
			});
		}
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

	private void TopSearch_PreviewKeyDown(object? sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
		case Key.Down when SearchPopup.IsOpen:
			SearchResults.SelectedIndex = Math.Min(SearchResults.SelectedIndex + 1, SearchResults.ItemCount - 1);
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

	private void SearchResults_PointerReleased(object? sender, PointerReleasedEventArgs e) => OpenHit(SearchResults.SelectedItem as SearchHit);

	private void SearchResults_PreviewKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			OpenHit(SearchResults.SelectedItem as SearchHit);
			e.Handled = true;
		}
	}

	// ---------------------------------------------------------------- undo, settings, status

	private void Undo_Click(object? sender, RoutedEventArgs e) => Undo();

	private void Undo()
	{
		if (_data == null)
		{
			return;
		}
		EditBatch? batch;
		try
		{
			batch = _data.Session.History.UndoLast(_data.Memory);
		}
		catch (GuardedUndoException ex)
		{
			SetStatus(Build.F("\"{0}\" geri alınamadı: oyun bu arada değiştirdi (Devam'a basıldı). Geçmişten çıkarıldı; geri dönmek için yedeği yükle.", "\"{0}\" could not be undone: the game has changed it since. It was dropped from the history; load your backup to go back.", ex.Batch.Label));
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
		int batches = _data.Session.History.Batches.Count;
		int frozen = _data.Session.Freezer.Entities.Count;
		FrozenText.Text = $"❄ {frozen}";
		FrozenButton.IsVisible = frozen > 0;
		HistoryText.Text = $"📋 {batches}";
		HistoryButton.IsVisible = batches > 0;
		Counts.Text = Build.T($"Oyuncu {_data.Players.Count:N0}  ·  Kulüp {_data.Clubs.Count:N0}  ·  Değişiklik {changes}" + (frozen > 0 ? $"  ·  Dondurulan {frozen}" : "") + $"  ·  Logo {ImageLookup.Graphics.ClubLogos:N0}  ·  Yüz {ImageLookup.Graphics.Faces:N0}",
			$"Players {_data.Players.Count:N0}  ·  Clubs {_data.Clubs.Count:N0}  ·  Changes {changes}" + (frozen > 0 ? $"  ·  Frozen {frozen}" : "") + $"  ·  Logos {ImageLookup.Graphics.ClubLogos:N0}  ·  Faces {ImageLookup.Graphics.Faces:N0}");
		UndoButton.IsEnabled = changes > 0;
		UndoText.Text = changes > 0 ? Build.F("Geri al ({0})", "Undo ({0})", changes) : Build.T("Geri al", "Undo");
	}

	public void SetStatus(string text)
	{
		Message.Text = text;
		if (text.Length > 0 && (text.StartsWith("✓") || text.StartsWith("★") || text.StartsWith("⚡") || text.StartsWith("❄") || text.StartsWith("💀")))
		{
			ToastText.Text = text;
			ToastUndoBtn.IsVisible = UndoButton.IsEnabled;
			ToastBanner.IsVisible = true;
			_toastTimer.Stop();
			_toastTimer.Start();
		}
	}

	private void OpenCommandPalette()
	{
		if (_data == null) return;
		var dlg = new CommandPaletteWindow(_data, this,
			() => Backup_Click(this, new RoutedEventArgs()),
			ReloadSave,
			() => Settings_Click(this, new RoutedEventArgs()),
			() => History_Click(this, new RoutedEventArgs()),
			() => Frozen_Click(this, new RoutedEventArgs()),
			Undo,
			theme =>
			{
				AppSettings.Current.UiTheme = theme;
				AppSettings.Current.Save();
				Ui.Theme.ApplyLive(theme, AppSettings.Current.Accent);
				WindowHelper.ApplyNativeTitleBar(this);
				RebuildOpenPages();
				SetStatus(Build.F("✓ Tema değiştirildi: {0}", "✓ Theme switched: {0}", theme));
			});
		_ = dlg.ShowDialog(this);
	}

	private void RebuildOpenPages()
	{
		if (_data == null) return;
		string? currentKey = _current?.Key;
		var keys = _pages.Select(p => p.Key).ToList();
		_pages.Clear();
		_current = null;
		foreach (string key in keys)
		{
			string[] parts = key.Split(':');
			ulong address = parts.Length == 2 && ulong.TryParse(parts[1], out ulong a) ? a : 0;
			switch (parts[0])
			{
			case "home": _pages.Add(new HomePage(_data, this)); break;
			case "player": _pages.Add(new PlayerPage(_data, address, this)); break;
			case "club": if (_data.ClubsByAddress.TryGetValue(address, out ClubRow? c)) _pages.Add(new ClubPage(_data, c, this)); break;
			case "staff": if (_data.Staff.FirstOrDefault(s => s.Address == address) is { } s) _pages.Add(new StaffPage(_data, s, this)); break;
			case "nation": if (_data.Nations.FirstOrDefault(n => n.Address == address) is { } n) _pages.Add(new NationPage(_data, n, this)); break;
			case "comp": if (_data.Competitions.FirstOrDefault(x => x.Address == address) is { } x) _pages.Add(new CompPage(_data, x, this)); break;
			}
		}
		IPage? target = _pages.FirstOrDefault(p => p.Key == currentKey) ?? _pages.FirstOrDefault();
		if (target != null) OpenPage(target);
	}

	private Task _graphics = Task.CompletedTask;

	/// <summary>Indexes logo and face packs in the background (tens of thousands of files).</summary>
	private async Task ReloadGraphicsAsync()
	{
		string? root = AppSettings.Current.GraphicsFolder is { Length: > 0 } custom && Directory.Exists(custom) ? custom : GraphicsIndex.FindDefaultRoot();
		ImageLookup.Graphics = await Task.Run(() => GraphicsIndex.Build(root));
	}

	private async void Settings_Click(object? sender, RoutedEventArgs e)
	{
		var dialog = new SettingsWindow();
		if ((await dialog.ShowDialog<bool?>(this)) != true)
		{
			return;
		}
		if (dialog.NeedsRestart && Environment.ProcessPath is { } exe)
		{
			Process.Start(new ProcessStartInfo(exe) { UseShellExecute = !PlatformPaths.IsMac });
			if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.Shutdown();
			}
			return;
		}
		Ui.Theme.ApplyLive(AppSettings.Current.UiTheme, AppSettings.Current.Accent);
		WindowHelper.ApplyNativeTitleBar(this);
		await ReloadGraphicsAsync();
		string? currentKey = _current?.Key;
		var keys = _pages.Select(p => p.Key).ToList();
		_pages.Clear();
		_current = null;
		if (_data != null)
		{
			foreach (string key in keys)
			{
				string[] parts = key.Split(':');
				ulong address = parts.Length == 2 && ulong.TryParse(parts[1], out ulong a) ? a : 0;
				switch (parts[0])
				{
				case "home": _pages.Add(new HomePage(_data, this)); break;
				case "player": _pages.Add(new PlayerPage(_data, address, this)); break;
				case "club" when _data.ClubsByAddress.TryGetValue(address, out ClubRow? club): _pages.Add(new ClubPage(_data, club, this)); break;
				case "staff" when _data.Staff.FirstOrDefault(s => s.Address == address) is { } staff: _pages.Add(new StaffPage(_data, staff, this)); break;
				case "nation" when _data.Nations.FirstOrDefault(n => n.Address == address) is { } nation: _pages.Add(new NationPage(_data, nation, this)); break;
				case "comp" when _data.Competitions.FirstOrDefault(c => c.Address == address) is { } comp: _pages.Add(new CompPage(_data, comp, this)); break;
				}
			}
			OpenPage(_pages.FirstOrDefault(p => p.Key == currentKey) ?? _pages[0]);
		}
		ApplyPreferences();
		UpdateCounts();
		SetStatus(Build.T("Ayarlar kaydedildi.", "Settings saved."));
	}
}
