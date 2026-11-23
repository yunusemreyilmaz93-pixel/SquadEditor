using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SquadEditor.App.Ui;

/// <summary>FMRTE-style advanced staff filter for coaches, scouts, physios, and analysts.</summary>
public sealed class StaffFilter
{
	public string? Nation { get; set; }
	public int? MaxAge { get; set; }
	public double? MaxWageDisplay { get; set; }
	public int? MinAtt { get; set; }
	public int? MinDef { get; set; }
	public int? MinFit { get; set; }
	public int? MinGk { get; set; }
	public int? MinTac { get; set; }
	public int? MinTec { get; set; }
	public int? MinSet { get; set; }
	public int? MinWwy { get; set; }
	public int? MinDet { get; set; }
	public int? MinMot { get; set; }
	public int? MinMan { get; set; }
	public int? MinJpa { get; set; }
	public int? MinJpp { get; set; }
	public int? MinTk { get; set; }
	public int? MinPhy { get; set; }
	public int? MinSci { get; set; }

	public bool IsActive => !string.IsNullOrEmpty(Nation) || MaxAge.HasValue || MaxWageDisplay.HasValue
		|| MinAtt.HasValue || MinDef.HasValue || MinFit.HasValue || MinGk.HasValue
		|| MinTac.HasValue || MinTec.HasValue || MinSet.HasValue || MinWwy.HasValue
		|| MinDet.HasValue || MinMot.HasValue || MinMan.HasValue || MinJpa.HasValue
		|| MinJpp.HasValue || MinTk.HasValue || MinPhy.HasValue || MinSci.HasValue;

	public void Clear()
	{
		Nation = null;
		MaxAge = MinAtt = MinDef = MinFit = MinGk = MinTac = MinTec = MinSet = MinWwy = null;
		MinDet = MinMot = MinMan = MinJpa = MinJpp = MinTk = MinPhy = MinSci = null;
		MaxWageDisplay = null;
	}

	public bool Matches(StaffRow s, long? maxWageGbp)
	{
		if (!string.IsNullOrEmpty(Nation) && !GameData.Normalize(s.Nation).Contains(GameData.Normalize(Nation))) return false;
		if (MaxAge.HasValue && (s.Age <= 0 || s.Age > MaxAge.Value)) return false;
		if (maxWageGbp.HasValue && s.Wage > maxWageGbp.Value) return false;
		if (MinAtt.HasValue && s.Attacking < MinAtt.Value) return false;
		if (MinDef.HasValue && s.Defending < MinDef.Value) return false;
		if (MinFit.HasValue && s.Fitness < MinFit.Value) return false;
		if (MinGk.HasValue && s.Goalkeeping < MinGk.Value) return false;
		if (MinTac.HasValue && s.Tactical < MinTac.Value) return false;
		if (MinTec.HasValue && s.Technical < MinTec.Value) return false;
		if (MinSet.HasValue && s.SetPieces < MinSet.Value) return false;
		if (MinWwy.HasValue && s.WorkingWithYoungsters < MinWwy.Value) return false;
		if (MinDet.HasValue && s.Determination < MinDet.Value) return false;
		if (MinMot.HasValue && s.Motivating < MinMot.Value) return false;
		if (MinMan.HasValue && s.ManManagement < MinMan.Value) return false;
		if (MinJpa.HasValue && s.JudgingAbility < MinJpa.Value) return false;
		if (MinJpp.HasValue && s.JudgingPotential < MinJpp.Value) return false;
		if (MinTk.HasValue && s.TacticalKnowledge < MinTk.Value) return false;
		if (MinPhy.HasValue && s.Physiotherapy < MinPhy.Value) return false;
		if (MinSci.HasValue && s.SportsScience < MinSci.Value) return false;
		return true;
	}
}

public sealed class StaffFilterWindow : Window
{
	public StaffFilterWindow(StaffFilter filter, IEnumerable<string> nations)
	{
		Title = Build.T("Gelişmiş Personel Filtresi (Antrenör · Gözlemci · Fizyoterapist)", "Advanced Staff Filter (Coach · Scout · Physio)");
		Width = 680;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"];
		SourceInitialized += (_, _) => ApplyTitleBar();

		var stack = new StackPanel { Margin = new Thickness(18) };
		stack.Children.Add(Build.Text(Build.T("Antrenör, gözlemci ve fizyoterapistleri 1–20 özelliklerine göre doğrudan filtrele. Boş alanlar yoksayılır.",
			"Filter coaches, scouts and physios directly by 1–20 attributes. Blank fields are ignored."), 12, Build.Res("Muted")));

		// Top row: Nationality + Max Age + Max Wage
		var topGrid = new Grid { Margin = new Thickness(0, 10, 0, 8) };
		topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
		topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
		topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		var natItems = new List<KeyValuePair<string, string>> { new("", Build.T("Tüm Ülkeler", "All Nations")) };
		natItems.AddRange(nations.Where(n => n.Length > 0).Distinct().OrderBy(n => n).Select(n => new KeyValuePair<string, string>(n, n)));
		var cNation = new ComboBox
		{
			Height = 26,
			FontSize = 12,
			IsEditable = true,
			IsTextSearchEnabled = true,
			ItemsSource = natItems,
			DisplayMemberPath = "Value",
			SelectedValuePath = "Key",
			SelectedValue = filter.Nation ?? ""
		};
		if (!string.IsNullOrEmpty(filter.Nation)) cNation.Text = filter.Nation;
		var bAge = new TextBox { Text = filter.MaxAge?.ToString() ?? "", Height = 26, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Center };
		var bWage = new TextBox { Text = filter.MaxWageDisplay?.ToString(CultureInfo.InvariantCulture) ?? "", Height = 26, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Center };

		StackPanel Labeled(string l, UIElement c)
		{
			var p = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
			p.Children.Add(Build.Text(l, 11.5, Build.Res("Muted")));
			p.Children.Add(c);
			return p;
		}
		var pNat = Labeled(Build.T("Uyruk", "Nationality"), cNation);
		var pAge = Labeled(Build.T("Maks. Yaş", "Max Age"), bAge);
		var pWage = Labeled(Build.T($"Maks. Maaş ({Money.Current.Code}{Money.WageSuffix})", $"Max Wage ({Money.Current.Code}{Money.WageSuffix})"), bWage);
		Grid.SetColumn(pAge, 1);
		Grid.SetColumn(pWage, 2);
		topGrid.Children.Add(pNat);
		topGrid.Children.Add(pAge);
		topGrid.Children.Add(pWage);
		stack.Children.Add(topGrid);

		// Two columns of attribute inputs
		var cols = new Grid { Margin = new Thickness(0, 6, 0, 0) };
		cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
		cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		TextBox MakeField(StackPanel host, string label, int? current)
		{
			var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
			var tb = new TextBox { Text = current?.ToString() ?? "", Width = 64, Height = 25, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Center };
			DockPanel.SetDock(tb, Dock.Right);
			dp.Children.Add(tb);
			dp.Children.Add(Build.Text(label, 12.5, Build.Res("Text")));
			host.Children.Add(dp);
			return tb;
		}

		var left = new StackPanel { Margin = new Thickness(12, 4, 12, 10) };
		TextBox bAtt = MakeField(left, Build.T("Hücum ≥", "Attacking ≥"), filter.MinAtt);
		TextBox bDef = MakeField(left, Build.T("Savunma ≥", "Defending ≥"), filter.MinDef);
		TextBox bFit = MakeField(left, Build.T("Kondisyon ≥", "Fitness ≥"), filter.MinFit);
		TextBox bGk = MakeField(left, Build.T("Kaleci ≥", "Goalkeeping ≥"), filter.MinGk);
		TextBox bTac = MakeField(left, Build.T("Taktik ≥", "Tactical ≥"), filter.MinTac);
		TextBox bTec = MakeField(left, Build.T("Teknik ≥", "Technical ≥"), filter.MinTec);
		TextBox bSet = MakeField(left, Build.T("Duran Top ≥", "Set Pieces ≥"), filter.MinSet);
		TextBox bWwy = MakeField(left, Build.T("Gençlerle Çalışma ≥", "Working with Youngsters ≥"), filter.MinWwy);

		var right = new StackPanel { Margin = new Thickness(12, 4, 12, 10) };
		TextBox bJpa = MakeField(right, Build.T("Yetenek Değerlendirme ≥", "Judging Ability ≥"), filter.MinJpa);
		TextBox bJpp = MakeField(right, Build.T("Potansiyel Değerlendirme ≥", "Judging Potential ≥"), filter.MinJpp);
		TextBox bTk = MakeField(right, Build.T("Taktiksel Bilgi ≥", "Tactical Knowledge ≥"), filter.MinTk);
		TextBox bDet = MakeField(right, Build.T("Kararlılık ≥", "Determination ≥"), filter.MinDet);
		TextBox bMot = MakeField(right, Build.T("Motivasyon ≥", "Motivating ≥"), filter.MinMot);
		TextBox bMan = MakeField(right, Build.T("İnsan Yönetimi ≥", "Man Management ≥"), filter.MinMan);
		TextBox bPhy = MakeField(right, Build.T("Fizyoterapi ≥", "Physiotherapy ≥"), filter.MinPhy);
		TextBox bSci = MakeField(right, Build.T("Spor Bilimi ≥", "Sports Science ≥"), filter.MinSci);

		var leftCard = Build.Card(Build.T("Antrenörlük (1-20)", "Coaching (1-20)"), left);
		var rightCard = Build.Card(Build.T("Gözlemcilik · Zihinsel · Medikal", "Scouting · Mental · Medical"), right);
		Grid.SetColumn(rightCard, 2);
		cols.Children.Add(leftCard);
		cols.Children.Add(rightCard);

		// Quick 1-click role presets
		var presetRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
		void ClearBoxes()
		{
			foreach (var tb in new[] { bAtt, bDef, bFit, bGk, bTac, bTec, bSet, bWwy, bJpa, bJpp, bTk, bDet, bMot, bMan, bPhy, bSci }) tb.Text = "";
		}
		void PresetBtn(string label, Action fill)
		{
			var b = new Button { Content = label, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 4), FontSize = 11.5 };
			b.Click += (_, _) => { ClearBoxes(); fill(); };
			presetRow.Children.Add(b);
		}
		PresetBtn(Build.T("⭐ 5★ Antrenör", "⭐ 5★ Coach"), () => { bDet.Text = "15"; bMot.Text = "15"; bMan.Text = "14"; });
		PresetBtn(Build.T("🔍 Süper Gözlemci", "🔍 Elite Scout"), () => { bJpa.Text = "16"; bJpp.Text = "16"; });
		PresetBtn(Build.T("🏥 Süper Fizyoterapist", "🏥 Elite Physio"), () => { bPhy.Text = "17"; });
		PresetBtn(Build.T("🎓 Gençlik Antrenörü", "🎓 Youth Coach"), () => { bWwy.Text = "16"; bDet.Text = "14"; });
		PresetBtn(Build.T("🏋 Kondisyoner", "🏋 Fitness Coach"), () => { bFit.Text = "16"; bDet.Text = "14"; bMot.Text = "14"; });
		stack.Children.Add(presetRow);
		stack.Children.Add(cols);

		var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
		var clear = new Button { Content = Build.T("Temizle", "Clear"), Style = Build.StyleOf("GhostButton") };
		clear.Click += (_, _) => { filter.Clear(); DialogResult = true; };
		var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
		var apply = new Button { Content = Build.T("Filtrele", "Apply"), Style = Build.StyleOf("AccentButton"), IsDefault = true };
		apply.Click += (_, _) =>
		{
			static int? PInt(TextBox t) => int.TryParse(t.Text.Trim(), out int v) ? v : null;
			static double? PDbl(TextBox t) => double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
			string nat = (cNation.SelectedValue as string) ?? cNation.Text?.Trim() ?? "";
			if (nat == Build.T("Tüm Ülkeler", "All Nations")) nat = "";
			filter.Nation = nat.Length > 0 ? nat : null;
			filter.MaxAge = PInt(bAge);
			filter.MaxWageDisplay = PDbl(bWage);
			filter.MinAtt = PInt(bAtt); filter.MinDef = PInt(bDef); filter.MinFit = PInt(bFit); filter.MinGk = PInt(bGk);
			filter.MinTac = PInt(bTac); filter.MinTec = PInt(bTec); filter.MinSet = PInt(bSet); filter.MinWwy = PInt(bWwy);
			filter.MinDet = PInt(bDet); filter.MinMot = PInt(bMot); filter.MinMan = PInt(bMan);
			filter.MinJpa = PInt(bJpa); filter.MinJpp = PInt(bJpp); filter.MinTk = PInt(bTk);
			filter.MinPhy = PInt(bPhy); filter.MinSci = PInt(bSci);
			DialogResult = true;
		};
		rightBtns.Children.Add(cancel);
		rightBtns.Children.Add(apply);
		DockPanel.SetDock(clear, Dock.Left);
		buttons.Children.Add(clear);
		buttons.Children.Add(rightBtns);
		stack.Children.Add(buttons);

		Content = stack;
	}

	private void ApplyTitleBar()
	{
		if (AppSettings.Current.UiTheme == "light") return;
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		int on = 1;
		DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
