using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace SquadEditor.App.Ui;

/// <summary>Genie Scout-style deep nationality, role, attribute, hidden stat, growth, role rating and wage filter.</summary>
public sealed class ScoutFilter
{
	public string? Nation { get; set; }
	public string? BestRole { get; set; }
	public int? MinGrowth { get; set; }
	public double? MinRating { get; set; }
	public double? MinPotentialRating { get; set; }
	public double? MaxWageDisplay { get; set; }
	public double? MaxAskingPriceM { get; set; }
	public bool BothFeetOnly { get; set; }
	public int? MinPro { get; set; }
	public int? MinCons { get; set; }
	public int? MinImp { get; set; }
	public int? MaxInj { get; set; }
	public int? MinDet { get; set; }
	public int? MinPace { get; set; }
	public int? MinFin { get; set; }
	public int? MinPas { get; set; }

	public bool IsActive => !string.IsNullOrEmpty(Nation) || !string.IsNullOrEmpty(BestRole)
		|| MinGrowth.HasValue || MinRating.HasValue || MinPotentialRating.HasValue
		|| MaxWageDisplay.HasValue || MaxAskingPriceM.HasValue || BothFeetOnly
		|| MinPro.HasValue || MinCons.HasValue || MinImp.HasValue || MaxInj.HasValue
		|| MinDet.HasValue || MinPace.HasValue || MinFin.HasValue || MinPas.HasValue;

	public void Clear()
	{
		Nation = BestRole = null;
		MinGrowth = MinPro = MinCons = MinImp = MaxInj = MinDet = MinPace = MinFin = MinPas = null;
		MinRating = MinPotentialRating = MaxWageDisplay = MaxAskingPriceM = null;
		BothFeetOnly = false;
	}

	public bool Matches(PlayerRow p, long? maxWageGbp, long? maxAskingGbp = null)
	{
		if (!string.IsNullOrEmpty(Nation) && !GameData.Normalize(p.Nation).Contains(GameData.Normalize(Nation))) return false;
		if (!string.IsNullOrEmpty(BestRole) && p.BestRole != BestRole) return false;
		if (MinGrowth.HasValue && p.Growth < MinGrowth.Value) return false;
		if (MinRating.HasValue && p.BestRating < MinRating.Value) return false;
		if (MinPotentialRating.HasValue && p.PotentialRating < MinPotentialRating.Value) return false;
		if (maxWageGbp.HasValue && p.Wage > maxWageGbp.Value) return false;
		if (maxAskingGbp.HasValue && p.AskingPrice > 0 && p.AskingPrice > maxAskingGbp.Value) return false;
		if (BothFeetOnly && !p.BothFeet) return false;
		if (MinPro.HasValue && p.Professionalism < MinPro.Value) return false;
		if (MinCons.HasValue && p.Consistency < MinCons.Value) return false;
		if (MinImp.HasValue && p.ImportantMatches < MinImp.Value) return false;
		if (MaxInj.HasValue && p.InjuryProneness > MaxInj.Value) return false;
		if (MinDet.HasValue && p.Determination < MinDet.Value) return false;
		if (MinPace.HasValue && p.Pace < MinPace.Value) return false;
		if (MinFin.HasValue && p.Finishing < MinFin.Value) return false;
		if (MinPas.HasValue && p.Passing < MinPas.Value) return false;
		return true;
	}
}

public sealed class ScoutFilterWindow : Window
{
	public ScoutFilterWindow(ScoutFilter filter, IEnumerable<string>? nations = null)
	{
		Title = Build.T("Gelişmiş Gözlemci Filtresi (Genie Scout)", "Advanced Scout Filter (Genie Scout)");
		Width = 680;
		SizeToContent = SizeToContent.Height;
		CanResize = false;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = Build.Font("UiFont");
		WindowHelper.ApplyChrome(this);

		var root = new StackPanel { Margin = new Thickness(18) };
		root.Children.Add(Build.Text(Build.T("Boş bırakılan alanlar filtrelenmez. Özellik değerleri 1–20 ölçeğindedir.", "Leave any field blank to ignore it. Attribute values use the 1–20 scale."), 12, Build.Res("Muted")));

		var topGrid = new Grid { Margin = new Thickness(0, 12, 0, 10) };
		topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
		topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		var nationItems = new List<KeyValuePair<string, string>> { new("", Build.T("Tüm Ülkeler", "All Nations")) };
		if (nations != null)
		{
			nationItems.AddRange(nations.Where(n => n.Length > 0).Distinct().OrderBy(n => n).Select(n => new KeyValuePair<string, string>(n, n)));
		}
		var roleItems = new List<KeyValuePair<string, string>> { new("", Build.T("Tüm Roller", "All Roles")) };
		roleItems.AddRange(ScoutRating.AllRoles().Select(r => new KeyValuePair<string, string>(r.Code, r.Label)));

		var natCol = new StackPanel();
		var natLbl = Build.Text(Build.T("Uyruk (Ülke)", "Nationality"), 11.5, Build.Res("Muted"), FontWeight.SemiBold);
		natLbl.Margin = new Thickness(0, 0, 0, 4);
		natCol.Children.Add(natLbl);
		var cNation = new EditableCombo(312, 28, 12, filterOnType: true) { HorizontalAlignment = HorizontalAlignment.Stretch };
		cNation.SetKeyValues(nationItems);
		cNation.SelectedValue = filter.Nation ?? "";
		if (!string.IsNullOrEmpty(filter.Nation)) cNation.Text = filter.Nation;
		natCol.Children.Add(cNation);

		var roleCol = new StackPanel();
		var roleLbl = Build.Text(Build.T("En İyi Rol", "Best Role"), 11.5, Build.Res("Muted"), FontWeight.SemiBold);
		roleLbl.Margin = new Thickness(0, 0, 0, 4);
		roleCol.Children.Add(roleLbl);
		var cRole = new ComboBox
		{
			Height = 28,
			MinHeight = 28,
			FontSize = 12,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			ItemsSource = roleItems,
			DisplayMemberBinding = new Binding("Value"),
			SelectedValueBinding = new Binding("Key"),
			SelectedValue = filter.BestRole ?? ""
		};
		roleCol.Children.Add(cRole);

		Grid.SetColumn(roleCol, 2);
		topGrid.Children.Add(natCol);
		topGrid.Children.Add(roleCol);
		root.Children.Add(topGrid);

		var presetRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
		TextBox bGrowth = null!, bRating = null!, bPR = null!, bWage = null!, bAsking = null!;
		TextBox bPro = null!, bCons = null!, bImp = null!, bInj = null!, bDet = null!, bPace = null!, bFin = null!, bPas = null!;
		CheckBox chkBothFeet = null!;

		void ClearBoxes()
		{
			cNation.SelectedValue = "";
			cRole.SelectedValue = "";
			foreach (var tb in new[] { bGrowth, bRating, bPR, bWage, bAsking, bPro, bCons, bImp, bInj, bDet, bPace, bFin, bPas })
				tb.Text = "";
			chkBothFeet.IsChecked = false;
		}

		void AddPresetChip(string label, Action fill)
		{
			var btn = new Button
			{
				Content = label,
				Padding = new Thickness(9, 4, 9, 4),
				Margin = new Thickness(0, 0, 6, 4),
				FontSize = 11.5
			};
			btn.Click += (_, _) => { ClearBoxes(); fill(); };
			presetRow.Children.Add(btn);
		}
		AddPresetChip(Build.T("Moneyball", "Moneyball"), () => { bPR.Text = "78"; bPro.Text = "12"; bInj.Text = "10"; });
		AddPresetChip(Build.T("Gelişime Açık", "High Growth"), () => { bGrowth.Text = "25"; bPro.Text = "13"; bDet.Text = "13"; });
		AddPresetChip(Build.T("Büyük Maç Oyuncusu", "Big Game"), () => { bCons.Text = "14"; bImp.Text = "14"; bInj.Text = "10"; });
		AddPresetChip(Build.T("Hızlı Bitirici", "Fast Finisher"), () => { bPace.Text = "15"; bFin.Text = "14"; });
		AddPresetChip(Build.T("Oyun Kurucu", "Playmaker"), () => { bPas.Text = "15"; bRating.Text = "74"; });
		root.Children.Add(presetRow);

		var cols = new Grid();
		cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
		cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		TextBox AddFieldRow(StackPanel parent, string label, string current)
		{
			var g = new Grid { Height = 30, Margin = new Thickness(12, 1, 12, 1) };
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
			g.Children.Add(Build.Text(label, 12, Build.Res("Muted")));
			var box = new TextBox
			{
				Text = current,
				Width = 76,
				Height = 24,
				MinHeight = 24,
				FontSize = 12,
				HorizontalAlignment = HorizontalAlignment.Right,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(box, 1);
			g.Children.Add(box);
			parent.Children.Add(g);
			return box;
		}

		var leftBody = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
		bGrowth = AddFieldRow(leftBody, Build.T("Gelişim payı (PA − CA) ≥", "Potential gap (PA − CA) ≥"), filter.MinGrowth?.ToString() ?? "");
		bRating = AddFieldRow(leftBody, Build.T("En iyi rol reytingi (%) ≥", "Best role rating (%) ≥"), filter.MinRating?.ToString(CultureInfo.InvariantCulture) ?? "");
		bPR = AddFieldRow(leftBody, Build.T("Potansiyel Reyting (PR %) ≥", "Potential Rating (PR %) ≥"), filter.MinPotentialRating?.ToString(CultureInfo.InvariantCulture) ?? "");
		bWage = AddFieldRow(leftBody, Build.F("Maks. maaş ({0}{1}) ≤", "Max wage ({0}{1}) ≤", Money.Current.Code, Money.WageSuffix), filter.MaxWageDisplay?.ToString(CultureInfo.InvariantCulture) ?? "");
		bAsking = AddFieldRow(leftBody, Build.F("Maks. istenen bonservis (mn {0}) ≤", "Max asking price (M {0}) ≤", Money.Current.Symbol), filter.MaxAskingPriceM?.ToString(CultureInfo.InvariantCulture) ?? "");

		chkBothFeet = new CheckBox
		{
			Content = Build.T("Çift Ayaklı (Sol ≥ 15 ve Sağ ≥ 15)", "Both Feet (Left ≥ 15 & Right ≥ 15)"),
			IsChecked = filter.BothFeetOnly,
			FontSize = 12,
			Foreground = Build.Res("Text"),
			Margin = new Thickness(12, 8, 12, 4)
		};
		leftBody.Children.Add(chkBothFeet);

		var rightBody = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
		bPro = AddFieldRow(rightBody, Build.T("Profesyonellik ≥", "Professionalism ≥"), filter.MinPro?.ToString() ?? "");
		bCons = AddFieldRow(rightBody, Build.T("İstikrar ≥", "Consistency ≥"), filter.MinCons?.ToString() ?? "");
		bImp = AddFieldRow(rightBody, Build.T("Önemli Maçlar ≥", "Important Matches ≥"), filter.MinImp?.ToString() ?? "");
		bInj = AddFieldRow(rightBody, Build.T("Sakatlık Eğilimi ≤", "Injury Proneness ≤"), filter.MaxInj?.ToString() ?? "");
		bDet = AddFieldRow(rightBody, Build.T("Kararlılık ≥", "Determination ≥"), filter.MinDet?.ToString() ?? "");
		bPace = AddFieldRow(rightBody, Build.T("Hız ≥", "Pace ≥"), filter.MinPace?.ToString() ?? "");
		bFin = AddFieldRow(rightBody, Build.T("Bitiricilik ≥", "Finishing ≥"), filter.MinFin?.ToString() ?? "");
		bPas = AddFieldRow(rightBody, Build.T("Pas ≥", "Passing ≥"), filter.MinPas?.ToString() ?? "");

		var leftCard = Build.Card(Build.T("Potansiyel & Ekonomi", "Potential & Economy"), leftBody);
		var rightCard = Build.Card(Build.T("Gizli & Saha Özellikleri (1-20)", "Hidden & Key Attributes (1-20)"), rightBody);
		leftCard.Margin = new Thickness(0);
		rightCard.Margin = new Thickness(0);
		Grid.SetColumn(rightCard, 2);
		cols.Children.Add(leftCard);
		cols.Children.Add(rightCard);
		root.Children.Add(cols);

		var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
		var clear = new Button { Content = Build.T("Temizle", "Clear"), Theme = Build.StyleOf("GhostButton") };
		clear.Click += (_, _) =>
		{
			filter.Clear();
			Close(true);
		};
		var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
		cancel.Click += (_, _) => Close(false);
		var apply = new Button { Content = Build.T("Filtrele", "Apply"), Theme = Build.StyleOf("AccentButton"), IsDefault = true };
		apply.Click += (_, _) =>
		{
			static int? PInt(TextBox t) => int.TryParse((t.Text ?? "").Trim(), out int v) ? v : null;
			static double? PDbl(TextBox t) => double.TryParse((t.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
			string natText = (cNation.SelectedValue as string) ?? cNation.Text.Trim();
			if (natText == Build.T("Tüm Ülkeler", "All Nations")) natText = "";
			filter.Nation = natText.Length > 0 ? natText : null;
			string roleCode = cRole.SelectedValue as string ?? "";
			filter.BestRole = roleCode.Length > 0 ? roleCode : null;
			filter.MinGrowth = PInt(bGrowth);
			filter.MinRating = PDbl(bRating);
			filter.MinPotentialRating = PDbl(bPR);
			filter.MaxWageDisplay = PDbl(bWage);
			filter.MaxAskingPriceM = PDbl(bAsking);
			filter.BothFeetOnly = chkBothFeet.IsChecked == true;
			filter.MinPro = PInt(bPro);
			filter.MinCons = PInt(bCons);
			filter.MinImp = PInt(bImp);
			filter.MaxInj = PInt(bInj);
			filter.MinDet = PInt(bDet);
			filter.MinPace = PInt(bPace);
			filter.MinFin = PInt(bFin);
			filter.MinPas = PInt(bPas);
			Close(true);
		};
		rightBtns.Children.Add(cancel);
		rightBtns.Children.Add(apply);
		DockPanel.SetDock(clear, Dock.Left);
		buttons.Children.Add(clear);
		buttons.Children.Add(rightBtns);
		root.Children.Add(buttons);

		Content = root;
	}
}
