using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>Base of the small dialogs for person actions: title, body, a result line and buttons.</summary>
public abstract class ActionWindow : Window
{
	protected readonly StackPanel Body = new StackPanel();
	protected readonly TextBlock Result;
	protected readonly Button Ok;

	protected ActionWindow(string title, string okText)
	{
		Title = title;
		Width = 560;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
		Result = Build.Text("", 12.5, Build.Res("Muted"));
		Result.TextWrapping = TextWrapping.Wrap;
		Result.Margin = new Thickness(0, 14, 0, 0);
		Ok = new Button { Content = okText, Style = Build.StyleOf("AccentButton"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 120, IsEnabled = false };
		var cancel = new Button { Content = Build.T("Kapat", "Close"), IsCancel = true };
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
		buttons.Children.Add(cancel);
		buttons.Children.Add(Ok);
		var warn = Build.Text(Build.T("Yazmadan önce oyunu kaydet ve kaydın yedeğini al. Yazarken oyunda Devam'a basma.",
			"Save the game and back up the save before writing. Do not press Continue while writing."), 12, Build.Res("Faint"));
		warn.TextWrapping = TextWrapping.Wrap;
		warn.Margin = new Thickness(0, 14, 0, 0);
		var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
		root.Children.Add(Body);
		root.Children.Add(Result);
		root.Children.Add(warn);
		root.Children.Add(buttons);
		Content = root;
	}

	protected static TextBlock Label(string text)
	{
		var t = Build.Text(text, 12.5, Build.Res("Muted"));
		t.Margin = new Thickness(0, 10, 0, 4);
		return t;
	}

	protected void ShowError(Exception e)
	{
		Result.Foreground = Build.Res("Danger");
		Result.Text = e.Message;
		Ok.IsEnabled = false;
	}
}

/// <summary>Renames a player or member of staff (first, last and common name).</summary>
public sealed class RenameWindow : ActionWindow
{
	private readonly GameData _data;
	private readonly ulong _block;
	private readonly TextBox _first = Box(), _last = Box(), _common = Box();
	private List<NamePart>? _plan;

	public RenameWindow(GameData data, ulong personBlock, string who) : base(Build.T("Adı değiştir", "Rename") + " · " + who, Build.T("Uygula", "Apply"))
	{
		_data = data;
		_block = personBlock;
		_first.Text = Names.ReadEntry(data.Memory, data.Memory.ReadPointer(personBlock + Names.FirstName));
		_last.Text = Names.ReadEntry(data.Memory, data.Memory.ReadPointer(personBlock + Names.LastName));
		_common.Text = Names.ReadEntry(data.Memory, data.Memory.ReadPointer(personBlock + Names.CommonName));
		var hint = Build.Text(Build.T(
			"Oyunda kayıtlı bir isim yazarsan o kullanılır. Kayıtlı olmayan bir isim ancak eskisini yalnız bu kişi kullanıyorsa ve uzunluğu yakınsa yerinde yazılır. Oyuna yeni bellek eklenmez.",
			"A name the game already has is used as it is. A new name is only written over the old one when nobody else uses it and its length is close. No memory is added to the game."), 12, Build.Res("Muted"));
		hint.TextWrapping = TextWrapping.Wrap;
		Body.Children.Add(hint);
		Body.Children.Add(Label(Build.T("Ad", "First name")));
		Body.Children.Add(_first);
		Body.Children.Add(Label(Build.T("Soyad", "Last name")));
		Body.Children.Add(_last);
		Body.Children.Add(Label(Build.T("Bilinen ad (boş bırakılabilir)", "Common name (may be empty)")));
		Body.Children.Add(_common);
		var check = new Button { Content = Build.T("Kontrol et", "Check"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
		check.Click += async (_, _) => await CheckAsync();
		Body.Children.Add(check);
		foreach (TextBox box in new[] { _first, _last, _common })
		{
			box.TextChanged += (_, _) => { _plan = null; Ok.IsEnabled = false; };
		}
		Ok.Click += (_, _) => Apply();
	}

	private static TextBox Box() => new TextBox { Height = 32, FontSize = 14 };

	private async System.Threading.Tasks.Task CheckAsync()
	{
		Result.Foreground = Build.Res("Muted");
		Result.Text = Build.T("Kontrol ediliyor... (ilk seferde oyunun isim tablosu taranır, birkaç saniye sürer)", "Checking... (the first time the game's name table is indexed, a few seconds)");
		Ok.IsEnabled = false;
		try
		{
			_plan = await Names.PlanAsync(_data, _block, _first.Text, _last.Text, _common.Text);
			var lines = _plan.Where(p => p.Change != NameChange.None).Select(p =>
				$"{(p.Change == NameChange.Refused ? "✕" : "✓")}  {p.Label}: {Show(p.From)} → {Show(p.To)}  —  {p.Note}").ToList();
			bool refused = _plan.Any(p => p.Change == NameChange.Refused);
			bool any = _plan.Any(p => p.Writes.Count > 0);
			Result.Foreground = Build.Res(refused ? "Danger" : "Text");
			Result.Text = lines.Count == 0 ? Build.T("Değişiklik yok.", "Nothing changes.") : string.Join("\n", lines);
			Ok.IsEnabled = !refused && any && !AppSettings.Current.ReadOnlyMode;
		}
		catch (Exception e)
		{
			ShowError(e);
		}
	}

	private static string Show(string s) => s.Length == 0 ? Build.T("(yok)", "(none)") : "“" + s + "”";

	private void Apply()
	{
		if (_plan == null)
		{
			return;
		}
		try
		{
			string label = Build.T("Ad değişikliği", "Rename");
			GameWrite.Apply(_data, label, _plan.SelectMany(p => p.Writes).ToList());
			DialogResult = true;
		}
		catch (Exception e)
		{
			ShowError(e);
		}
	}
}

/// <summary>Moves a player to another club's senior team, or releases him.</summary>
public sealed class MoveWindow : ActionWindow
{
	private readonly GameData _data;
	private readonly ulong _player;
	private readonly TextBox _search = new TextBox { Height = 32, FontSize = 14 };
	private readonly ListBox _clubs = new ListBox { Height = 220, Background = Build.Res("Field"), Foreground = Build.Res("Text"), BorderBrush = Build.Res("Stroke") };
	private readonly RadioButton _move, _release;
	private MovePlan? _plan;

	public MoveWindow(GameData data, ulong player, string who) : base(Build.T("Kulübe taşı", "Move to club") + " · " + who, Build.T("Uygula", "Apply"))
	{
		_data = data;
		_player = player;
		_move = new RadioButton { Content = Build.T("Başka kulübe taşı (bonservissiz)", "Move to another club (free transfer)"), IsChecked = true, GroupName = "mv", Margin = new Thickness(0, 0, 0, 4) };
		_release = new RadioButton { Content = Build.T("Serbest bırak (sözleşmesi feshedilir)", "Release (contract ended)"), GroupName = "mv" };
		Body.Children.Add(_move);
		Body.Children.Add(_release);
		Body.Children.Add(Label(Build.T("Kulüp ara", "Search club")));
		Body.Children.Add(_search);
		_clubs.Margin = new Thickness(0, 6, 0, 0);
		_clubs.DisplayMemberPath = nameof(ClubRow.Name);
		Body.Children.Add(_clubs);
		var hint = Build.Text(Build.T("Oyuncu A takıma katılır; sözleşmesi (maaş, bitiş) aynı kalır. Sadece oyunun belleğinde yeri olan kadrolara taşınır.",
			"He joins the senior team on his contract (same wage and end date). Only squads with room in the game's memory take him."), 12, Build.Res("Muted"));
		hint.TextWrapping = TextWrapping.Wrap;
		hint.Margin = new Thickness(0, 8, 0, 0);
		Body.Children.Add(hint);
		_search.TextChanged += (_, _) => Filter();
		_clubs.SelectionChanged += (_, _) => Check();
		_move.Checked += (_, _) => { _clubs.IsEnabled = _search.IsEnabled = true; Check(); };
		_release.Checked += (_, _) => { _clubs.IsEnabled = _search.IsEnabled = false; Check(); };
		Ok.Click += (_, _) => Apply();
		Filter();
	}

	public MovePlan? Done { get; private set; }

	private void Filter()
	{
		string q = GameData.Normalize(_search.Text.Trim());
		_clubs.ItemsSource = _data.Clubs.Where(c => c.SquadSize > 0 && (q.Length == 0 || c.Search.Contains(q)))
			.OrderByDescending(c => c.AverageCA).Take(200).ToList();
	}

	private int _checks;

	private async void Check()
	{
		int mine = ++_checks;
		_plan = null;
		Ok.IsEnabled = false;
		ulong club = _release.IsChecked == true ? 0 : (_clubs.SelectedItem as ClubRow)?.Address ?? 0;
		if (_release.IsChecked != true && club == 0)
		{
			Result.Text = "";
			return;
		}
		try
		{
			Cursor = System.Windows.Input.Cursors.Wait;
			Result.Foreground = Build.Res("Muted");
			Result.Text = Build.T("Kontrol ediliyor...", "Checking...");
			MovePlan plan = await System.Threading.Tasks.Task.Run(() => Transfers.Plan(_data, _player, club));
			if (mine != _checks)
			{
				return;
			}
			_plan = plan;
			Result.Foreground = Build.Res("Text");
			Result.Text = _plan.Summary + "\n" + string.Join("\n", _plan.Steps.Select(s => "•  " + s));
			Ok.IsEnabled = !AppSettings.Current.ReadOnlyMode;
		}
		catch (Exception e)
		{
			if (mine == _checks)
			{
				ShowError(e);
			}
		}
		finally
		{
			Cursor = null;
		}
	}

	private void Apply()
	{
		if (_plan == null)
		{
			return;
		}
		try
		{
			GameWrite.Apply(_data, _release.IsChecked == true ? Build.T("Serbest bırakma", "Release") : Build.T("Kulüp değişikliği", "Club move"), _plan.Writes);
			Done = _plan;
			DialogResult = true;
		}
		catch (Exception e)
		{
			ShowError(e);
		}
	}
}
