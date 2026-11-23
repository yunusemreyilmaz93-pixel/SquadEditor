using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Layout;

namespace SquadEditor.App.Ui;

/// <summary>Flexible mass edit dialog: pick any writable numeric field and set, add, or scale it across all selected records.</summary>
public sealed class MassEditWindow : Window
{
	public MassEditWindow(GameData data, EntityDef entity, IReadOnlyList<BulkTarget> targets, INavigator nav, Action? after = null)
	{
		Title = Build.T($"Toplu Düzenle ({targets.Count} kayıt)", $"Mass Edit ({targets.Count} records)");
		Width = 440;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"];
		SourceInitialized += (_, _) => ApplyTitleBar();

		string lang = Loc.Instance.Language;
		var fields = entity.Fields
			.Where(f => f.IsWritable && f.IsNumeric && f.GroupKey != "Internal")
			.Select(f => new KeyValuePair<FieldDef, string>(f, $"{data.Layout.Text(lang, f.Name)}  ({data.Layout.Text(lang, f.GroupKey)})"))
			.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

		var stack = new StackPanel { Margin = new Thickness(18) };
		stack.Children.Add(Build.Text(Build.T($"{targets.Count} seçili kaydın tek bir alanını topluca değiştir (Ctrl+Z ile geri alınır).",
			$"Edit one field across all {targets.Count} selected records (Ctrl+Z undoes it)."), 12.5, Build.Res("Muted")));

		stack.Children.Add(new TextBlock { Text = Build.T("Alan", "Field"), Foreground = Build.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 12, 0, 4) });
		var fieldBox = new ComboBox { Height = 30, ItemsSource = fields, DisplayMemberPath = "Value", SelectedIndex = 0 };
		stack.Children.Add(fieldBox);

		stack.Children.Add(new TextBlock { Text = Build.T("İşlem", "Operation"), Foreground = Build.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 10, 0, 4) });
		var ops = new[]
		{
			new KeyValuePair<string, string>("set", Build.T("Sabit değer ata (=)", "Set exact value (=)")),
			new KeyValuePair<string, string>("add", Build.T("Ekle / Çıkar (+/-)", "Add / Subtract (+/-)")),
			new KeyValuePair<string, string>("pct", Build.T("Yüzde değiştir (+% / -%)", "Change by percent (+% / -%)")),
			new KeyValuePair<string, string>("max", Build.T("Maksimum yap (max)", "Set to maximum (max)"))
		};
		var opBox = new ComboBox { Height = 30, ItemsSource = ops, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = 0 };
		stack.Children.Add(opBox);

		stack.Children.Add(new TextBlock { Text = Build.T("Değer", "Value"), Foreground = Build.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 10, 0, 4) });
		var valueBox = new TextBox { Height = 30, FontSize = 13 };
		var rangeHint = Build.Text("", 11.5, Build.Res("Faint"));
		rangeHint.Margin = new Thickness(0, 4, 0, 0);
		void UpdateHint()
		{
			if (fieldBox.SelectedItem is KeyValuePair<FieldDef, string> kv)
			{
				FieldDef f = kv.Key;
				rangeHint.Text = f.Min.HasValue && f.Max.HasValue ? Build.T($"Aralık: {f.Min} – {f.Max}", $"Range: {f.Min} – {f.Max}") : "";
			}
			valueBox.IsEnabled = (opBox.SelectedValue as string) != "max";
		}
		fieldBox.SelectionChanged += (_, _) => UpdateHint();
		opBox.SelectionChanged += (_, _) => UpdateHint();
		UpdateHint();
		stack.Children.Add(valueBox);
		stack.Children.Add(rangeHint);

		var err = Build.Text("", 12, Build.Res("Danger"));
		err.Margin = new Thickness(0, 8, 0, 0);
		stack.Children.Add(err);

		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
		var apply = new Button { Content = Build.T("Uygula", "Apply"), Style = Build.StyleOf("AccentButton"), IsDefault = true };
		apply.Click += (_, _) =>
		{
			if (fieldBox.SelectedItem is not KeyValuePair<FieldDef, string> kv)
			{
				return;
			}
			string op = opBox.SelectedValue as string ?? "set";
			string raw = valueBox.Text.Trim().Replace(',', '.');
			string expr;
			if (op == "max")
			{
				expr = "max";
			}
			else if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
			{
				err.Text = Build.T("Geçerli bir sayı gir.", "Enter a valid number.");
				return;
			}
			else
			{
				expr = op switch
				{
					"add" => num >= 0 ? "+" + num.ToString(CultureInfo.InvariantCulture) : num.ToString(CultureInfo.InvariantCulture),
					"pct" => (num >= 0 ? "+" : "") + num.ToString(CultureInfo.InvariantCulture) + "%",
					_ => num.ToString(CultureInfo.InvariantCulture)
				};
			}
			var action = new PresetAction { Field = kv.Key.Name, Value = expr };
			string label = $"{data.Layout.Text(lang, kv.Key.Name)} ({expr})";
			BulkResult result = BulkEdit.ApplyActions(data.Session, entity, new[] { action }, targets, label);
			nav.SetStatus(result.Failed == 0
				? Build.T($"{label}: {targets.Count} kayıt, {result.Written} değer yazıldı. Ctrl+Z geri alır.", $"{label}: {targets.Count} records, {result.Written} values written. Ctrl+Z undoes it.")
				: Build.T($"{result.Written} yazıldı, {result.Failed} hata: {result.FirstError}", $"{result.Written} written, {result.Failed} failed: {result.FirstError}"));
			after?.Invoke();
			DialogResult = true;
		};
		buttons.Children.Add(cancel);
		buttons.Children.Add(apply);
		stack.Children.Add(buttons);

		Content = stack;
		Loaded += (_, _) => valueBox.Focus();
	}

	private void ApplyTitleBar()
	{
		if (AppSettings.Current.UiTheme == "light")
		{
			return;
		}
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		int on = 1;
		DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
