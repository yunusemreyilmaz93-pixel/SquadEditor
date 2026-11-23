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
				fe.Margin = new Thickness(0, 6, 10, 0);
			}
			head.Children.Add(extra);
		}
		head.Children.Add(new TextBlock { Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture), Style = StyleOf("CardTitle") });
		var panel = new DockPanel();
		DockPanel.SetDock(head, Dock.Top);
		panel.Children.Add(head);
		panel.Children.Add(content);
		return new Border { Style = StyleOf("CardBorder"), Child = panel };
	}

	/// <summary>Label on the left, editable value on the right. Attributes get a compact coloured box.</summary>
	public static FrameworkElement Row(EditField field)
	{
		var grid = new Grid { Margin = new Thickness(6, 0, 6, 0), Height = AppSettings.Current.Compact ? 25 : 30, Background = Brushes.Transparent };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var label = Text(field.Label, 13, Res("Muted"));
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
			return grid;
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
		Grid.SetColumn(box, 1);
		grid.Children.Add(box);
		grid.MouseEnter += (_, _) => grid.Background = Res("CardHover");
		grid.MouseLeave += (_, _) => grid.Background = Brushes.Transparent;
		return grid;
	}

	/// <summary>Option labels come from the layout's translations.</summary>
	public static Func<string, string> Translator { get; set; } = s => s;

	private static string Translate(EditField field, string text) => Translator(text);

	public static StackPanel Rows(IEnumerable<EditField> fields)
	{
		var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
		foreach (EditField f in fields)
		{
			panel.Children.Add(Row(f));
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
				Foreground = Brushes.White,
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
				var result = SquadEditor.Core.Editing.BulkEdit.ApplyPreset(data.Session, data.Layout.GetEntity(entity), preset, list, data.Layout.Text(lang, preset.Name));
				nav.SetStatus(result.Failed == 0
					? T($"{data.Layout.Text(lang, preset.Name)}: {list.Count} kayda uygulandı ({result.Written} değer).", $"{preset.Name}: applied to {list.Count} records ({result.Written} values).")
					: T($"{result.Written} değer yazıldı, {result.Failed} hata: {result.FirstError}", $"{result.Written} values written, {result.Failed} failed: {result.FirstError}"));
				after();
			};
			panel.Children.Add(button);
		}
		return Card(T("Ön ayarlar", "Presets"), panel);
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
