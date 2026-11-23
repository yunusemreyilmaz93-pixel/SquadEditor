using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

/// <summary>Small factory for the building blocks every page uses (cards, field rows, header, pills).</summary>
public static class Build
{
	public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

	public static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

	public static string T(string tr, string en) => Loc.Instance.Language == "tr" ? tr : en;

	public static TextBlock Text(string text, double size = 13, Brush? brush = null, FontWeight? weight = null)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = size,
			Foreground = brush ?? Res("Text"),
			FontWeight = weight ?? FontWeights.Normal,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	/// <summary>A titled card; <paramref name="extra"/> sits at the right of the title (e.g. a toggle).</summary>
	public static Border Card(string title, UIElement content, UIElement? extra = null)
	{
		var head = new DockPanel { LastChildFill = true };
		if (extra != null)
		{
			DockPanel.SetDock(extra, Dock.Right);
			if (extra is FrameworkElement fe)
			{
				fe.Margin = new Thickness(0, 5, 10, 4);
			}
			head.Children.Add(extra);
		}
		var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 8, 12, 6) };
		titleRow.Children.Add(new Border
		{
			Width = 3,
			Height = 11,
			CornerRadius = new CornerRadius(1.5),
			Background = Res("Accent"),
			Margin = new Thickness(0, 0, 7, 0),
			VerticalAlignment = VerticalAlignment.Center
		});
		var titleTxt = new TextBlock { Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture), Style = StyleOf("CardTitle"), Margin = new Thickness(0) };
		titleRow.Children.Add(titleTxt);
		head.Children.Add(titleRow);
		var headBorder = new Border
		{
			BorderBrush = Res("StrokeSoft"),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Margin = new Thickness(0, 0, 0, 4),
			Child = head
		};
		var panel = new DockPanel();
		DockPanel.SetDock(headBorder, Dock.Top);
		panel.Children.Add(headBorder);
		panel.Children.Add(content);
		return new Border { Style = StyleOf("CardBorder"), Child = panel };
	}

	/// <summary>Label on the left, editable value on the right. Optional role highlight: 2 = key (green), 1 = secondary (accent).</summary>
	public static FrameworkElement Row(EditField field, int highlightLevel = 0)
	{
		Brush baseBg = highlightLevel switch
		{
			2 => new SolidColorBrush(Color.FromArgb(0x26, 0x3D, 0xD6, 0x8C)),
			1 => new SolidColorBrush(Color.FromArgb(0x20, 0x4F, 0x8C, 0xFF)),
			_ => Brushes.Transparent
		};
		var grid = new Grid { Height = AppSettings.Current.Compact ? 25 : 30, Background = Brushes.Transparent };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var label = Text(field.Label, 13, highlightLevel > 0 ? Res("Text") : Res("Muted"), highlightLevel == 2 ? FontWeights.SemiBold : FontWeights.Normal);
		label.Margin = new Thickness(8, 0, 8, 0);
		grid.Children.Add(label);
		if (field.Def.Options.Count > 0 && !field.IsReadOnly)
		{
			var combo = new ComboBox { Width = 180, Height = 26, IsEditable = false };
			var items = field.Def.Options.Select(o => new KeyValuePair<string, string>(o.Key, Loc.Instance.Language == "tr" ? Translate(field, o.Value) : o.Value)).ToList();
			if (!items.Any(i => i.Key == field.Value) && field.Value.Length > 0)
			{
				items.Add(new KeyValuePair<string, string>(field.Value, "#" + field.Value));
			}
			combo.ItemsSource = items;
			combo.DisplayMemberPath = "Value";
			combo.SelectedValuePath = "Key";
			combo.SelectedValue = field.Value;
			combo.SelectionChanged += (_, _) =>
			{
				if (combo.SelectedValue is string key && key != field.Value)
				{
					field.Commit(key, FieldBox.EntityLabel);
				}
			};
			Grid.SetColumn(combo, 1);
			grid.Children.Add(combo);
			return new Border { Margin = new Thickness(6, 0, 6, 0), Child = grid };
		}
		double boxWidth = field.IsAttribute ? 44
			: (field.IsMoney || field.IsDate ? 132
			: (field.IsNumeric ? 88 : 180));
		var box = new TextBox
		{
			Width = boxWidth,
			Height = 25,
			HorizontalContentAlignment = field.IsAttribute ? HorizontalAlignment.Center : (field.IsNumeric || field.IsDate ? HorizontalAlignment.Right : HorizontalAlignment.Left),
			FontWeight = field.IsAttribute ? FontWeights.Bold : FontWeights.Normal,
			Padding = new Thickness(6, 1, 6, 1),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		if (field.IsAttribute)
		{
			box.SetBinding(Control.ForegroundProperty, new Binding(nameof(EditField.ValueBrush)) { Source = field });
		}
		FieldBox.SetField(box, field);
		if (field.IsDate && !field.IsReadOnly)
		{
			box.Width = 104;
			var calBtn = new Button
			{
				Content = "📅",
				Width = 26,
				Height = 25,
				Padding = new Thickness(0),
				Margin = new Thickness(2, 0, 0, 0),
				FontSize = 11.5,
				ToolTip = T("Takvimden seç veya +1/+3/+5 yıl ekle", "Pick from calendar or add +1/+3/+5 years")
			};
			var popup = new System.Windows.Controls.Primitives.Popup
			{
				PlacementTarget = calBtn,
				Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
				StaysOpen = false,
				AllowsTransparency = true
			};
			calBtn.Click += (_, _) =>
			{
				DateTime cur = GameData.ParseDate(field.Value) ?? DateTime.Today;
				var cal = new Calendar { SelectedDate = cur, DisplayDate = cur };
				cal.SelectedDatesChanged += (_, _) =>
				{
					if (cal.SelectedDate is DateTime picked)
					{
						string txt = picked.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
						if (field.Commit(txt, FieldBox.EntityLabel)) box.Text = field.Display;
						popup.IsOpen = false;
					}
				};
				var quick = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(4, 4, 4, 4) };
				void AddQuick(string lbl, Func<DateTime, DateTime> fn)
				{
					var b = new Button { Content = lbl, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(2), FontSize = 11 };
					b.Click += (_, _) =>
					{
						DateTime next = fn(GameData.ParseDate(field.Value) ?? DateTime.Today);
						string txt = next.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
						if (field.Commit(txt, FieldBox.EntityLabel)) box.Text = field.Display;
						popup.IsOpen = false;
					};
					quick.Children.Add(b);
				}
				AddQuick(T("+1 Yıl", "+1y"), d => d.AddYears(1));
				AddQuick(T("+3 Yıl", "+3y"), d => d.AddYears(3));
				AddQuick(T("+5 Yıl", "+5y"), d => d.AddYears(5));
				AddQuick("30.06", d => new DateTime(d.Year, 6, 30));
				var popStack = new StackPanel();
				popStack.Children.Add(cal);
				popStack.Children.Add(quick);
				popup.Child = new Border
				{
					Background = Res("Panel"),
					BorderBrush = Res("Stroke"),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(8),
					Padding = new Thickness(4),
					Child = popStack
				};
				popup.IsOpen = true;
			};
			var dateRow = new StackPanel { Orientation = Orientation.Horizontal };
			dateRow.Children.Add(box);
			dateRow.Children.Add(calBtn);
			Grid.SetColumn(dateRow, 1);
			grid.Children.Add(dateRow);
		}
		else
		{
			Grid.SetColumn(box, 1);
			grid.Children.Add(box);
		}
		var wrap = new Border
		{
			Margin = new Thickness(6, 0, 6, 0),
			CornerRadius = new CornerRadius(3),
			Background = baseBg,
			BorderBrush = highlightLevel == 2 ? Res("Positive") : (highlightLevel == 1 ? Res("Accent") : Brushes.Transparent),
			BorderThickness = highlightLevel > 0 ? new Thickness(2, 0, 0, 0) : new Thickness(0),
			Child = grid
		};
		wrap.MouseEnter += (_, _) => wrap.Background = Res("CardHover");
		wrap.MouseLeave += (_, _) => wrap.Background = baseBg;
		return wrap;
	}

	/// <summary>Option labels come from the layout's translations.</summary>
	public static Func<string, string> Translator { get; set; } = s => s;

	private static string Translate(EditField field, string text) => Translator(text);

	public static StackPanel Rows(IEnumerable<EditField> fields, Func<string, int>? highlight = null)
	{
		var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
		foreach (EditField f in fields)
		{
			panel.Children.Add(Row(f, highlight?.Invoke(f.Key) ?? 0));
		}
		return panel;
	}

	/// <summary>Round picture (face / logo) or initials when there is none.</summary>
	public static FrameworkElement Avatar(ImageSource? image, string name, double size, bool round)
	{
		var border = new Border
		{
			Width = size,
			Height = size,
			CornerRadius = new CornerRadius(round ? size / 2 : 12),
			Background = image == null ? Res("AccentSoft") : Brushes.Transparent,
			ClipToBounds = true
		};
		if (image != null)
		{
			border.Child = new Image { Source = image, Stretch = round ? Stretch.UniformToFill : Stretch.Uniform };
		}
		else
		{
			string initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
			border.Child = new TextBlock
			{
				Text = initials,
				FontSize = size * 0.34,
				FontWeight = FontWeights.Bold,
				Foreground = Res("Text"),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
		}
		return border;
	}

	/// <summary>Header stat box: big value, small caption.</summary>
	public static Border Pill(string value, string caption, Brush? valueBrush = null)
	{
		var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
		var v = Text(value, 16, valueBrush ?? Brushes.White, FontWeights.Bold);
		v.HorizontalAlignment = HorizontalAlignment.Center;
		var c = Text(caption, 10.5, new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
		c.HorizontalAlignment = HorizontalAlignment.Center;
		stack.Children.Add(v);
		stack.Children.Add(c);
		return new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(12, 6, 12, 6),
			Margin = new Thickness(6, 0, 0, 0),
			MinWidth = 96,
			Child = stack
		};
	}

	/// <summary>The coloured band at the top of a page.</summary>
	public static Border Header(FrameworkElement avatar, string title, UIElement subtitle, UIElement? subtitle2, IEnumerable<UIElement> pills)
	{
		var grid = new Grid { Margin = new Thickness(20, 12, 20, 12) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		avatar.Margin = new Thickness(0, 0, 14, 0);
		grid.Children.Add(avatar);
		var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		texts.Children.Add(new TextBlock { Text = title, Style = StyleOf("H1"), TextTrimming = TextTrimming.CharacterEllipsis });
		texts.Children.Add(subtitle);
		if (subtitle2 != null)
		{
			texts.Children.Add(subtitle2);
		}
		Grid.SetColumn(texts, 1);
		grid.Children.Add(texts);
		var pillPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		foreach (UIElement p in pills)
		{
			pillPanel.Children.Add(p);
		}
		Grid.SetColumn(pillPanel, 2);
		grid.Children.Add(pillPanel);
		return new Border { Background = Res("HeaderBrush"), Child = grid };
	}

	/// <summary>Left menu + content area. Returns the menu so pages can add sections.</summary>
	public static Grid PageBody(out StackPanel nav, out ContentControl content)
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		nav = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
		var navBorder = new Border { Background = Res("Chrome"), BorderBrush = Res("StrokeSoft"), BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };
		grid.Children.Add(navBorder);
		content = new ContentControl { Margin = new Thickness(12, 12, 4, 0) };
		Grid.SetColumn(content, 1);
		grid.Children.Add(content);
		return grid;
	}

	public static RadioButton NavItem(StackPanel nav, string text, Action select, bool isChecked = false)
	{
		var item = new RadioButton
		{
			Content = text,
			Style = StyleOf("NavItem"),
			GroupName = "nav" + nav.GetHashCode()
		};
		item.Checked += (_, _) => select();
		nav.Children.Add(item);
		if (isChecked)
		{
			item.IsChecked = true;
		}
		return item;
	}

	/// <summary>Buttons for the layout's presets of an entity in a neat 2-column grid.</summary>
	public static Border PresetsCard(GameData data, string entity, Func<IReadOnlyList<SquadEditor.Core.Editing.BulkTarget>> targets, Action after, INavigator nav)
	{
		var panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(10, 2, 10, 10) };
		foreach (var preset in data.Layout.Presets.Where(p => p.Entity == entity))
		{
			string lang = Loc.Instance.Language;
			var button = new Button
			{
				Content = data.Layout.Text(lang, preset.Name),
				ToolTip = preset.Description == null ? null : data.Layout.Text(lang, preset.Description),
				IsEnabled = !AppSettings.Current.ReadOnlyMode,
				Margin = new Thickness(3),
				Padding = new Thickness(8, 5, 8, 5),
				FontSize = 11.5
			};
			button.Click += (_, _) =>
			{
				var list = targets();
				if (list.Count == 0)
				{
					return;
				}
				if (preset.Name == "Wonderkid" && entity == "Player")
				{
					RollWonderkid(data, list, nav);
					after();
					return;
				}
				var result = SquadEditor.Core.Editing.BulkEdit.ApplyPreset(data.Session, data.Layout.GetEntity(entity), preset, list, data.Layout.Text(lang, preset.Name));
				nav.SetStatus(result.Failed == 0
					? T($"✓ {data.Layout.Text(lang, preset.Name)}: {list.Count} kayda uygulandı ({result.Written} değer). Ctrl+Z geri alır.", $"✓ {preset.Name}: applied to {list.Count} records ({result.Written} values). Ctrl+Z undoes it.")
					: T($"{result.Written} değer yazıldı, {result.Failed} hata: {result.FirstError}", $"{result.Written} values written, {result.Failed} failed: {result.FirstError}"));
				after();
			};
			panel.Children.Add(button);
		}
		return Card(T("Ön ayarlar", "Presets"), panel);
	}

	/// <summary>Generates a fresh, realistic, position-aware random Wonderkid build (CA 120-160, PA 158-198) on every click.</summary>
	private static void RollWonderkid(GameData data, IReadOnlyList<SquadEditor.Core.Editing.BulkTarget> targets, INavigator nav)
	{
		var rng = Random.Shared;
		var def = data.PlayerDef;
		var byName = def.Fields.Where(f => f.IsWritable && f.Concat.Count == 0).ToDictionary(f => f.Name);
		int lastCa = 0, lastPa = 0;
		using (data.Session.History.BeginBatch(T("🎲 Wonderkid oluştur", "🎲 Roll Wonderkid")))
		{
			foreach (var t in targets)
			{
				int ca = rng.Next(120, 161);
				int pa = rng.Next(Math.Max(ca + 15, 158), 199);
				lastCa = ca;
				lastPa = pa;
				byte[] raw69 = new byte[69];
				string role = data.Memory.TryRead(t.Address + 0x150, raw69) ? ScoutRating.Evaluate(raw69).Role : "";
				bool isGk = raw69[0] >= 15;
				double baseAttr = 8.5 + (ca - 110) / 13.0; // ~9.3 at CA 120, ~12.3 at CA 160

				void WriteVal(string fieldName, int v)
				{
					if (byName.TryGetValue(fieldName, out var fd))
					{
						try { data.Session.WriteField(t.Address, fd, v.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Label); } catch { }
					}
				}

				WriteVal("CA", ca);
				WriteVal("PA", pa);
				string primaryGroup = isGk ? "Goalkeeping" : "Technical";
				foreach (var fd in def.Fields.Where(f => f.IsWritable && f.GroupKey is "Mental" or "Physical" || f.GroupKey == primaryGroup))
				{
					int hl = ScoutRating.HighlightLevel(role, fd.Name);
					int val = hl switch
					{
						2 => Math.Clamp((int)Math.Round(baseAttr + rng.Next(3, 6)), 12, 19),
						1 => Math.Clamp((int)Math.Round(baseAttr + rng.Next(1, 4)), 10, 18),
						_ => Math.Clamp((int)Math.Round(baseAttr + rng.Next(-3, 3)), 6, 16)
					};
					WriteVal(fd.Name, val);
				}
				WriteVal("Determination", rng.Next(14, 19));
				WriteVal("Professionalism", rng.Next(13, 19));
				WriteVal("Ambition", rng.Next(14, 19));
				WriteVal("Consistency", rng.Next(12, 17));
				WriteVal("Important Matches", rng.Next(12, 17));
				WriteVal("Injury Proneness", rng.Next(3, 9));
			}
		}
		nav.SetStatus(targets.Count == 1
			? T($"✓ {targets[0].Label} → Wonderkid (CA {lastCa} / PA {lastPa}) · Beğenmezsen tekrar bas · Ctrl+Z geri alır.",
				$"✓ {targets[0].Label} → Wonderkid (CA {lastCa} / PA {lastPa}) · Click again to reroll · Ctrl+Z undoes.")
			: T($"✓ {targets.Count} oyuncu için rastgele Wonderkid profili oluşturuldu. Ctrl+Z geri alır.",
				$"✓ Rolled random Wonderkid builds for {targets.Count} players. Ctrl+Z undoes."));
	}

	public static ScrollViewer Scroll(UIElement content)
	{
		return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };
	}

	public static DataGridTemplateColumn TemplateColumn(string header, string template, string sortPath, double width)
	{
		return new DataGridTemplateColumn
		{
			Header = header,
			CellTemplate = (DataTemplate)Application.Current.Resources[template],
			SortMemberPath = sortPath,
			Width = new DataGridLength(width, width <= 0 ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel)
		};
	}

	public static DataGridTextColumn TextColumn(string header, string path, double width = 0, string? format = null)
	{
		var binding = new Binding(path) { StringFormat = format };
		if (path is "DateOfBirth" or "ContractEnd")
		{
			binding.Converter = (IValueConverter)Application.Current.Resources["DateText"];
		}
		return new DataGridTextColumn
		{
			Header = header,
			Binding = binding,
			SortMemberPath = path is "DateOfBirth" ? "Born" : path,
			Width = width <= 0 ? DataGridLength.Auto : new DataGridLength(width)
		};
	}
}
