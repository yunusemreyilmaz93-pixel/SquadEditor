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

/// <summary>Flexible mass edit dialog: supports numeric, money (user currency/period), and date (set or +/- years) fields with confirmation.</summary>
public sealed class MassEditWindow : Window
{
	public MassEditWindow(GameData data, EntityDef entity, IReadOnlyList<BulkTarget> targets, INavigator nav, Action? after = null)
	{
		Title = Build.F("Toplu Düzenle ({0} kayıt)", "Mass Edit ({0} records)", targets.Count);
		Width = 460;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"];
		SourceInitialized += (_, _) => ApplyTitleBar();

		string lang = Loc.Instance.Language;
		var fields = entity.Fields
			.Where(f => f.IsWritable && (f.IsNumeric || f.Type == FieldType.DayYear) && f.GroupKey != "Internal")
			.Select(f => new KeyValuePair<FieldDef, string>(f, $"{data.Layout.Text(lang, f.Name)}  ({data.Layout.Text(lang, f.GroupKey)})"))
			.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

		var stack = new StackPanel { Margin = new Thickness(18) };
		stack.Children.Add(Build.Text(Build.F("{0} seçili kaydın tek bir alanını topluca değiştir (Ctrl+Z ile geri alınır).", "Edit one field across all {0} selected records (Ctrl+Z undoes it).", targets.Count), 12.5, Build.Res("Muted")));

		stack.Children.Add(new TextBlock { Text = Build.T("Alan", "Field"), Foreground = Build.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 12, 0, 4) });
		var fieldBox = new ComboBox { Height = 30, ItemsSource = fields, DisplayMemberPath = "Value", SelectedIndex = 0 };
		stack.Children.Add(fieldBox);

		stack.Children.Add(new TextBlock { Text = Build.T("İşlem", "Operation"), Foreground = Build.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 10, 0, 4) });
		var numOps = new[]
		{
			new KeyValuePair<string, string>("set", Build.T("Sabit değer ata (=)", "Set exact value (=)")),
			new KeyValuePair<string, string>("add", Build.T("Ekle / Çıkar (+/-)", "Add / Subtract (+/-)")),
			new KeyValuePair<string, string>("pct", Build.T("Yüzde değiştir (+% / -%)", "Change by percent (+% / -%)")),
			new KeyValuePair<string, string>("max", Build.T("Maksimum yap (max)", "Set to maximum (max)"))
		};
		var dateOps = new[]
		{
			new KeyValuePair<string, string>("date_add_years", Build.T("Yıl ekle / çıkar (+/- yıl)", "Add / subtract years (+/- years)")),
			new KeyValuePair<string, string>("date_set", Build.T("Sabit tarih ata (gg.aa.yyyy)", "Set exact date (dd.MM.yyyy)"))
		};
		var opBox = new ComboBox { Height = 30, DisplayMemberPath = "Value", SelectedValuePath = "Key" };
		stack.Children.Add(opBox);

		stack.Children.Add(new TextBlock { Text = Build.T("Değer", "Value"), Foreground = Build.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 10, 0, 4) });
		var valueBox = new TextBox { Height = 30, FontSize = 13 };
		var rangeHint = Build.Text("", 11.5, Build.Res("Faint"));
		rangeHint.Margin = new Thickness(0, 4, 0, 0);

		bool IsMoney(FieldDef f) => f.Format == FieldFormat.Money && f.Currency == "£";
		bool IsWage(FieldDef f) => IsMoney(f) && Money.IsWage(f.Name);

		void UpdateOps()
		{
			if (fieldBox.SelectedItem is not KeyValuePair<FieldDef, string> kv) return;
			FieldDef f = kv.Key;
			bool isDate = f.Type == FieldType.DayYear;
			var desired = isDate ? dateOps : numOps;
			if (!ReferenceEquals(opBox.ItemsSource, desired))
			{
				opBox.ItemsSource = desired;
				opBox.SelectedIndex = 0;
			}
			UpdateHint();
		}

		void UpdateHint()
		{
			if (fieldBox.SelectedItem is KeyValuePair<FieldDef, string> kv)
			{
				FieldDef f = kv.Key;
				if (f.Type == FieldType.DayYear)
				{
					string op = opBox.SelectedValue as string ?? "";
					rangeHint.Text = op == "date_add_years"
						? Build.T("Örn: 2 (sözleşmeyi 2 yıl uzatır) veya -1", "e.g. 2 (extends by 2 years) or -1")
						: Build.T("Biçim: gg.aa.yyyy (örn. 30.06.2030)", "Format: dd.MM.yyyy (e.g. 30.06.2030)");
				}
				else if (IsMoney(f))
				{
					string period = IsWage(f) ? Money.WageSuffix : "";
					rangeHint.Text = Build.F("Para birimi: {0}{1} (otomatik dönüştürülür)", "Currency: {0}{1} (converted automatically)", Money.Current.Code, period);
				}
				else
				{
					rangeHint.Text = f.Min.HasValue && f.Max.HasValue ? Build.F("Aralık: {0} – {1}", "Range: {0} – {1}", f.Min, f.Max) : "";
				}
			}
			valueBox.IsEnabled = (opBox.SelectedValue as string) != "max";
		}

		fieldBox.SelectionChanged += (_, _) => UpdateOps();
		opBox.SelectionChanged += (_, _) => UpdateHint();
		UpdateOps();
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
			if (fieldBox.SelectedItem is not KeyValuePair<FieldDef, string> kv) return;
			FieldDef f = kv.Key;
			string op = opBox.SelectedValue as string ?? "set";
			string fieldLabel = data.Layout.Text(lang, f.Name);

			if (targets.Count > 1 && AppSettings.Current.ConfirmBulkEdits && MessageBox.Show(this,
				Build.F("\"{0}\" alanı {1} kayıt için topluca değiştirilsin mi? Ctrl+Z geri alır.", "Mass edit \"{0}\" across {1} records? Ctrl+Z undoes it.", fieldLabel, targets.Count),
				Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}

			if (f.Type == FieldType.DayYear)
			{
				int written = 0, failed = 0;
				string raw = valueBox.Text.Trim();
				if (op == "date_add_years")
				{
					if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deltaYears) || deltaYears == 0)
					{
						err.Text = Build.T("Geçerli bir yıl sayısı gir (örn. 2 veya -1).", "Enter a valid number of years (e.g. 2 or -1).");
						return;
					}
					using (data.Session.History.BeginBatch($"{fieldLabel} ({(deltaYears > 0 ? "+" : "")}{deltaYears}y)"))
					{
						foreach (BulkTarget t in targets)
						{
							try
							{
								DateTime? cur = GameData.ParseDate(data.Session.ReadField(t.Address, f));
								if (cur == null) continue;
								DateTime next = cur.Value.AddYears(deltaYears);
								data.Session.WriteField(t.Address, f, next.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), t.Label);
								written++;
							}
							catch (Exception) { failed++; }
						}
					}
				}
				else
				{
					string? gameDate = Dates.ToGame(raw);
					if (gameDate == null || GameData.ParseDate(gameDate) == null)
					{
						err.Text = Build.T("Geçerli bir tarih gir (örn. 30.06.2030).", "Enter a valid date (e.g. 30.06.2030).");
						return;
					}
					using (data.Session.History.BeginBatch($"{fieldLabel} = {raw}"))
					{
						foreach (BulkTarget t in targets)
						{
							try
							{
								data.Session.WriteField(t.Address, f, gameDate, t.Label);
								written++;
							}
							catch (Exception) { failed++; }
						}
					}
				}
				nav.SetStatus(Build.T($"✓ {fieldLabel}: {written} kayıt güncellendi" + (failed > 0 ? $", {failed} hata" : "") + ". Ctrl+Z geri alır.",
					$"✓ {fieldLabel}: {written} records updated" + (failed > 0 ? $", {failed} failed" : "") + ". Ctrl+Z undoes it."));
				after?.Invoke();
				DialogResult = true;
				return;
			}

			string rawNum = valueBox.Text.Trim().Replace(" ", "").Replace("£", "").Replace("€", "").Replace("$", "").Replace("₺", "").Replace(',', '.');
			string expr;
			if (op == "max")
			{
				expr = "max";
			}
			else if (!double.TryParse(rawNum, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
			{
				err.Text = Build.T("Geçerli bir sayı gir.", "Enter a valid number.");
				return;
			}
			else
			{
				if (IsMoney(f) && op is "set" or "add")
				{
					num = Math.Round(Money.FromDisplay(num, IsWage(f)));
				}
				expr = op switch
				{
					"add" => num >= 0 ? "+" + num.ToString(CultureInfo.InvariantCulture) : num.ToString(CultureInfo.InvariantCulture),
					"pct" => (num >= 0 ? "+" : "") + num.ToString(CultureInfo.InvariantCulture) + "%",
					_ => num.ToString(CultureInfo.InvariantCulture)
				};
			}
			var action = new PresetAction { Field = f.Name, Value = expr };
			string label = $"{fieldLabel} ({expr})";
			BulkResult result = BulkEdit.ApplyActions(data.Session, entity, new[] { action }, targets, label);
			nav.SetStatus(result.Failed == 0
				? Build.F("✓ {0}: {1} kayıt, {2} değer yazıldı. Ctrl+Z geri alır.", "✓ {0}: {1} records, {2} values written. Ctrl+Z undoes it.", label, targets.Count, result.Written)
				: Build.F("{0} yazıldı, {1} hata: {2}", "{0} written, {1} failed: {2}", result.Written, result.Failed, result.FirstError));
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
		if (AppSettings.Current.UiTheme == "light") return;
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		int on = 1;
		DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
