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
	private readonly Func<ulong, long, MovePlan> _planner;
	private readonly TextBox _search = new TextBox { Height = 32, FontSize = 14 };
	private readonly TextBox _fee = new TextBox { Height = 28, Width = 140, FontSize = 13, Text = "0", Padding = new Thickness(6, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
	private readonly ListBox _clubs = new ListBox { Height = 200, Background = Build.Res("Field"), Foreground = Build.Res("Text"), BorderBrush = Build.Res("Stroke") };
	private readonly RadioButton _move, _release;
	private MovePlan? _plan;

	/// <summary>A player: moved (with optional transfer fee), signed when he is a free agent, or released.</summary>
	public static MoveWindow ForPlayer(GameData data, ulong player, string who, bool freeAgent)
	{
		return new MoveWindow(data, (club, feeGbp) => Transfers.Plan(data, player, club, feeGbp), who,
			freeAgent ? Build.T("Kulübe imzalat", "Sign for a club") : Build.T("Başka kulübe transfer et", "Transfer to another club"),
			freeAgent ? null : Build.T("Serbest bırak (sözleşmesi feshedilir)", "Release (contract ended)"),
			freeAgent ? Build.T("Oyunun kendi sözleşme havuzundan yeni bir sözleşme alır; A takıma katılır.", "Gets a new contract from the game's own contract pool and joins the senior team.")
				: Build.T("Oyuncu A takıma katılır; sözleşmesi (maaş, bitiş) aynı kalır. Bonservis girersen alıcı kasasından düşülüp satıcıya eklenir.",
					"Joins the senior team on his contract. If you enter a transfer fee, it is deducted from the buyer and paid to the seller."),
			showFee: !freeAgent);
	}

	/// <summary>A member of staff (person block): moved keeping his job, hired, or released.</summary>
	public static MoveWindow ForStaff(GameData data, ulong block, string who)
	{
		return new MoveWindow(data, (club, _) => StaffMoves.Plan(data, block, club), who,
			Build.T("Başka kulübe taşı / İşe al", "Move / Hire to club"), Build.T("Görevden al (kulüpsüz kalır)", "Release (no club)"),
			Build.T("Aynı personel listelerine (antrenör, sağlık, gözlemci) eklenir; sözleşmesi aynı kalır.", "Joins the same staff lists (coaching, medical, scouting); same contract."),
			showFee: false);
	}

	private MoveWindow(GameData data, Func<ulong, long, MovePlan> planner, string who, string moveText, string? releaseText, string hintText, bool showFee)
		: base(Build.T("Kulübe taşı", "Move to club") + " · " + who, Build.T("Uygula", "Apply"))
	{
		_data = data;
		_planner = planner;
		_move = new RadioButton { Content = moveText, IsChecked = true, GroupName = "mv", Margin = new Thickness(0, 0, 0, 4) };
		_release = new RadioButton { Content = releaseText ?? "", GroupName = "mv", Visibility = releaseText == null ? Visibility.Collapsed : Visibility.Visible };
		Body.Children.Add(_move);
		Body.Children.Add(_release);
		if (showFee)
		{
			var feeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
			feeRow.Children.Add(new TextBlock { Text = Build.T($"Bonservis ({Money.Current.Code}, 0 = bedelsiz): ", $"Transfer fee ({Money.Current.Code}, 0 = free): "), Foreground = Build.Res("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
			feeRow.Children.Add(_fee);
			Body.Children.Add(feeRow);
			_fee.TextChanged += (_, _) => Check();
		}
		Body.Children.Add(Label(Build.T("Kulüp ara", "Search club")));
		Body.Children.Add(_search);
		_clubs.Margin = new Thickness(0, 6, 0, 0);
		_clubs.DisplayMemberPath = nameof(ClubRow.Name);
		Body.Children.Add(_clubs);
		var hint = Build.Text(hintText, 12, Build.Res("Muted"));
		hint.TextWrapping = TextWrapping.Wrap;
		hint.Margin = new Thickness(0, 8, 0, 0);
		Body.Children.Add(hint);
		_search.TextChanged += (_, _) => Filter();
		_clubs.SelectionChanged += (_, _) => Check();
		_move.Checked += (_, _) => { _clubs.IsEnabled = _search.IsEnabled = _fee.IsEnabled = true; Check(); };
		_release.Checked += (_, _) => { _clubs.IsEnabled = _search.IsEnabled = _fee.IsEnabled = false; Check(); };
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
			long feeGbp = 0;
			if (_release.IsChecked != true && double.TryParse(_fee.Text.Trim().Replace(" ", "").Replace(".", "").Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double feeShown) && feeShown > 0)
			{
				feeGbp = (long)Math.Round(Money.FromDisplay(feeShown, false));
			}
			MovePlan plan = await System.Threading.Tasks.Task.Run(() => _planner(club, feeGbp));
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
			GameWrite.Apply(_data, _plan.Summary, _plan.Writes);
			Done = _plan;
			DialogResult = true;
		}
		catch (Exception e)
		{
			ShowError(e);
		}
	}
}

/// <summary>Shows a plan and asks before writing it (loan recall, loan made permanent).</summary>
public static class PlanConfirm
{
	public static bool Run(Window owner, GameData data, Func<MovePlan> planner, INavigator nav)
	{
		MovePlan plan;
		try
		{
			plan = planner();
		}
		catch (Exception e)
		{
			MessageBox.Show(owner, e.Message, "SquadEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
		string text = plan.Summary + "\n\n" + string.Join("\n", plan.Steps.Select(s => "•  " + s)) + "\n\n" +
			Build.T("Kaydın yedeği alındı mı? Yazılsın mı?", "Is your save backed up? Write it?");
		if (MessageBox.Show(owner, text, "SquadEditor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return false;
		}
		try
		{
			GameWrite.Apply(data, plan.Summary, plan.Writes);
			nav.SetStatus(plan.Summary + Build.T(" Ctrl+Z geri alır.", " Ctrl+Z undoes it."));
			return true;
		}
		catch (Exception e)
		{
			MessageBox.Show(owner, e.Message, "SquadEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
	}
}
