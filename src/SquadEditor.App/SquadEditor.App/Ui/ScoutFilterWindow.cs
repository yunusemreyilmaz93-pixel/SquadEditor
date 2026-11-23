using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SquadEditor.App.Ui;

/// <summary>Genie Scout-style deep nationality, role, attribute, hidden stat, growth, role rating and wage filter.</summary>
public sealed class ScoutFilter
{
	public string? Nation { get; set; }
	public string? BestRole { get; set; }
	public int? MinGrowth { get; set; }
	public double? MinRating { get; set; }
	public double? MaxWageDisplay { get; set; }
	public int? MinPro { get; set; }
	public int? MinCons { get; set; }
	public int? MinImp { get; set; }
	public int? MaxInj { get; set; }
	public int? MinDet { get; set; }
	public int? MinPace { get; set; }
	public int? MinFin { get; set; }
	public int? MinPas { get; set; }

	public bool IsActive => !string.IsNullOrEmpty(Nation) || !string.IsNullOrEmpty(BestRole)
		|| MinGrowth.HasValue || MinRating.HasValue || MaxWageDisplay.HasValue
		|| MinPro.HasValue || MinCons.HasValue || MinImp.HasValue || MaxInj.HasValue
		|| MinDet.HasValue || MinPace.HasValue || MinFin.HasValue || MinPas.HasValue;

	public void Clear()
	{
		Nation = BestRole = null;
		MinGrowth = MinPro = MinCons = MinImp = MaxInj = MinDet = MinPace = MinFin = MinPas = null;
		MinRating = MaxWageDisplay = null;
	}

	public bool Matches(PlayerRow p, long? maxWageGbp)
	{
		if (!string.IsNullOrEmpty(Nation) && !GameData.Normalize(p.Nation).Contains(GameData.Normalize(Nation))) return false;
		if (!string.IsNullOrEmpty(BestRole) && p.BestRole != BestRole) return false;
		if (MinGrowth.HasValue && p.Growth < MinGrowth.Value) return false;
		if (MinRating.HasValue && p.BestRating < MinRating.Value) return false;
		if (maxWageGbp.HasValue && p.Wage > maxWageGbp.Value) return false;
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
		Width = 460;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"];
		SourceInitialized += (_, _) => ApplyTitleBar();

		var stack = new StackPanel { Margin = new Thickness(18) };
		stack.Children.Add(Build.Text(Build.T("Boş bırakılan alanlar filtrelenmez. Özellik değerleri 1–20 ölçeğindedir.", "Leave any field blank to ignore it. Attribute values use the 1–20 scale."), 12, Build.Res("Muted")));

		var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
		int row = 0;

		ComboBox AddCombo(string label, IEnumerable<KeyValuePair<string, string>> items, string? current, bool editable)
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
			var lbl = Build.Text(label, 12.5, Build.Res("Text"));
			Grid.SetRow(lbl, row);
			grid.Children.Add(lbl);
			var cb = new ComboBox
			{
				Height = 26,
				FontSize = 12,
				IsEditable = editable,
				IsTextSearchEnabled = true,
				ItemsSource = items.ToList(),
				DisplayMemberPath = "Value",
				SelectedValuePath = "Key",
				SelectedValue = current ?? ""
			};
			if (editable && !string.IsNullOrEmpty(current)) cb.Text = current;
			Grid.SetRow(cb, row);
			Grid.SetColumn(cb, 1);
			grid.Children.Add(cb);
			row++;
			return cb;
		}

		TextBox AddRow(string label, string current)
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
			var lbl = Build.Text(label, 12.5, Build.Res("Text"));
			Grid.SetRow(lbl, row);
			grid.Children.Add(lbl);
			var box = new TextBox { Text = current, Width = 100, HorizontalAlignment = HorizontalAlignment.Right, Height = 25, FontSize = 12.5, HorizontalContentAlignment = HorizontalAlignment.Center };
			Grid.SetRow(box, row);
			Grid.SetColumn(box, 1);
			grid.Children.Add(box);
			row++;
			return box;
		}

		var nationItems = new List<KeyValuePair<string, string>> { new("", Build.T("Tüm Ülkeler", "All Nations")) };
		if (nations != null)
		{
			nationItems.AddRange(nations.Where(n => n.Length > 0).Distinct().OrderBy(n => n).Select(n => new KeyValuePair<string, string>(n, n)));
		}
		ComboBox cNation = AddCombo(Build.T("Uyruk (Ülke)", "Nationality"), nationItems, filter.Nation, editable: true);

		var roleItems = new List<KeyValuePair<string, string>> { new("", Build.T("Tüm Roller", "All Roles")) };
		roleItems.AddRange(ScoutRating.AllRoles().Select(r => new KeyValuePair<string, string>(r.Code, r.Label)));
		ComboBox cRole = AddCombo(Build.T("En İyi Rol", "Best Role"), roleItems, filter.BestRole, editable: false);

		TextBox bGrowth = AddRow(Build.T("Gelişim payı (PA − CA) ≥", "Potential gap (PA − CA) ≥"), filter.MinGrowth?.ToString() ?? "");
		TextBox bRating = AddRow(Build.T("En iyi rol reytingi (%) ≥", "Best role rating (%) ≥"), filter.MinRating?.ToString(CultureInfo.InvariantCulture) ?? "");
		TextBox bWage = AddRow(Build.T($"Maks. maaş ({Money.Current.Code}{Money.WageSuffix}) ≤", $"Max wage ({Money.Current.Code}{Money.WageSuffix}) ≤"), filter.MaxWageDisplay?.ToString(CultureInfo.InvariantCulture) ?? "");
		TextBox bPro = AddRow(Build.T("Profesyonellik ≥", "Professionalism ≥"), filter.MinPro?.ToString() ?? "");
		TextBox bCons = AddRow(Build.T("İstikrar ≥", "Consistency ≥"), filter.MinCons?.ToString() ?? "");
		TextBox bImp = AddRow(Build.T("Önemli Maçlar ≥", "Important Matches ≥"), filter.MinImp?.ToString() ?? "");
		TextBox bInj = AddRow(Build.T("Sakatlık Eğilimi ≤", "Injury Proneness ≤"), filter.MaxInj?.ToString() ?? "");
		TextBox bDet = AddRow(Build.T("Kararlılık ≥", "Determination ≥"), filter.MinDet?.ToString() ?? "");
		TextBox bPace = AddRow(Build.T("Hız ≥", "Pace ≥"), filter.MinPace?.ToString() ?? "");
		TextBox bFin = AddRow(Build.T("Bitiricilik ≥", "Finishing ≥"), filter.MinFin?.ToString() ?? "");
		TextBox bPas = AddRow(Build.T("Pas ≥", "Passing ≥"), filter.MinPas?.ToString() ?? "");
		stack.Children.Add(grid);

		var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
		var clear = new Button { Content = Build.T("Temizle", "Clear"), Style = Build.StyleOf("GhostButton") };
		clear.Click += (_, _) =>
		{
			filter.Clear();
			DialogResult = true;
		};
		var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
		var apply = new Button { Content = Build.T("Filtrele", "Apply"), Style = Build.StyleOf("AccentButton"), IsDefault = true };
		apply.Click += (_, _) =>
		{
			static int? PInt(TextBox t) => int.TryParse(t.Text.Trim(), out int v) ? v : null;
			static double? PDbl(TextBox t) => double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
			string natText = (cNation.SelectedValue as string) ?? cNation.Text?.Trim() ?? "";
			if (natText == Build.T("Tüm Ülkeler", "All Nations")) natText = "";
			filter.Nation = natText.Length > 0 ? natText : null;
			string roleCode = cRole.SelectedValue as string ?? "";
			filter.BestRole = roleCode.Length > 0 ? roleCode : null;
			filter.MinGrowth = PInt(bGrowth);
			filter.MinRating = PDbl(bRating);
			filter.MaxWageDisplay = PDbl(bWage);
			filter.MinPro = PInt(bPro);
			filter.MinCons = PInt(bCons);
			filter.MinImp = PInt(bImp);
			filter.MaxInj = PInt(bInj);
			filter.MinDet = PInt(bDet);
			filter.MinPace = PInt(bPace);
			filter.MinFin = PInt(bFin);
			filter.MinPas = PInt(bPas);
			DialogResult = true;
		};
		right.Children.Add(cancel);
		right.Children.Add(apply);
		DockPanel.SetDock(clear, Dock.Left);
		buttons.Children.Add(clear);
		buttons.Children.Add(right);
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
