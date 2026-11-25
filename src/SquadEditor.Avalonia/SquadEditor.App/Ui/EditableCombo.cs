using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SquadEditor.App.Ui;

/// <summary>
/// Editable + searchable dropdown combo control matching SquadEditor's exact field/combo visual style.
/// Supports both free typing (Text / TextChanged) and picking from a filtered or full dropdown list.
/// </summary>
public sealed class EditableCombo : UserControl
{
	private readonly TextBox _box;
	private readonly Button _dropBtn;
	private readonly Popup _popup;
	private readonly ListBox _list;
	private List<(object? Raw, string Key, string Label)> _all = new();
	private bool _suppress;

	public EditableCombo(double width = 120, double height = 28, double fontSize = 12.5, bool filterOnType = false)
	{
		Width = width;
		Height = height;

		_box = new TextBox
		{
			Height = height,
			FontSize = fontSize,
			Padding = new Thickness(7, 1, 24, 1),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		_dropBtn = new Button
		{
			Content = new TextBlock { Text = "⌄", FontSize = 13, Foreground = Build.Res("Muted"), Margin = new Thickness(0, -5, 0, 0) },
			Theme = Build.StyleOf("GhostButton"),
			Width = 22,
			Height = height - 2,
			Padding = new Thickness(0),
			Margin = new Thickness(0, 0, 1, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Focusable = false
		};
		_list = new ListBox
		{
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			MaxHeight = 320
		};
		_popup = new Popup
		{
			PlacementTarget = this,
			Placement = PlacementMode.BottomEdgeAlignedLeft,
			IsLightDismissEnabled = true,
			Child = new Border
			{
				Background = Build.Res("Panel"),
				BorderBrush = Build.Res("Stroke"),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(8),
				MinWidth = Math.Max(width, 130),
				Padding = new Thickness(4),
				Margin = new Thickness(0, 3, 0, 0),
				Child = _list
			}
		};

		_dropBtn.Click += (_, _) =>
		{
			if (_popup.IsOpen)
			{
				_popup.IsOpen = false;
			}
			else
			{
				PopulatePopup(filterText: "");
				_popup.IsOpen = _all.Count > 0;
			}
		};

		_box.TextChanged += (_, _) =>
		{
			if (_suppress) return;
			SelectedItem = null;
			SelectedValue = null;
			var match = _all.FirstOrDefault(x => string.Equals(x.Label, Text, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Key, Text, StringComparison.OrdinalIgnoreCase));
			if (match.Label != null)
			{
				SelectedItem = match.Raw;
				SelectedValue = match.Key;
			}
			if (filterOnType && _box.IsFocused && Text.Length > 0)
			{
				PopulatePopup(Text);
				_popup.IsOpen = _list.ItemCount > 0;
			}
			TextChanged?.Invoke(this, EventArgs.Empty);
		};

		_box.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Down)
			{
				if (!_popup.IsOpen)
				{
					PopulatePopup("");
					_popup.IsOpen = _all.Count > 0;
				}
				else if (_list.SelectedIndex < _list.ItemCount - 1)
				{
					_list.SelectedIndex++;
				}
				e.Handled = true;
			}
			else if (e.Key == Key.Up && _popup.IsOpen && _list.SelectedIndex > 0)
			{
				_list.SelectedIndex--;
				e.Handled = true;
			}
			else if (e.Key == Key.Enter && _popup.IsOpen && _list.SelectedItem is ComboEntry ce)
			{
				PickEntry(ce);
				e.Handled = true;
			}
			else if (e.Key == Key.Escape && _popup.IsOpen)
			{
				_popup.IsOpen = false;
				e.Handled = true;
			}
		};

		_list.PointerReleased += (_, _) =>
		{
			if (_list.SelectedItem is ComboEntry ce)
			{
				PickEntry(ce);
			}
		};

		var grid = new Grid();
		grid.Children.Add(_box);
		grid.Children.Add(_dropBtn);
		grid.Children.Add(_popup);
		Content = grid;
	}

	private sealed record ComboEntry(object? Raw, string Key, string Label)
	{
		public override string ToString() => Label;
	}

	public event EventHandler? SelectionChanged;
	public event EventHandler? TextChanged;

	public string Text
	{
		get => _box.Text ?? "";
		set
		{
			_suppress = true;
			_box.Text = value;
			var match = _all.FirstOrDefault(x => string.Equals(x.Label, value, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Key, value, StringComparison.OrdinalIgnoreCase));
			SelectedItem = match.Raw;
			SelectedValue = match.Key;
			_suppress = false;
		}
	}

	public object? SelectedItem { get; private set; }

	public object? SelectedValue
	{
		get => _selectedValue;
		set
		{
			_selectedValue = value;
			string keyStr = value?.ToString() ?? "";
			var match = _all.FirstOrDefault(x => x.Key == keyStr);
			_suppress = true;
			if (match.Label != null && (keyStr.Length > 0 || match.Raw != null))
			{
				SelectedItem = match.Raw;
				_box.Text = keyStr.Length == 0 && match.Key.Length == 0 ? match.Label : match.Label;
			}
			else if (keyStr.Length == 0)
			{
				SelectedItem = null;
				_box.Text = "";
			}
			_suppress = false;
		}
	}
	private object? _selectedValue;

	public int SelectedIndex
	{
		get => SelectedItem == null ? -1 : _all.FindIndex(x => Equals(x.Raw, SelectedItem));
		set
		{
			if (value < 0 || value >= _all.Count)
			{
				_suppress = true;
				SelectedItem = null;
				_selectedValue = null;
				_box.Text = "";
				_suppress = false;
			}
			else
			{
				var e = _all[value];
				_suppress = true;
				SelectedItem = e.Raw;
				_selectedValue = e.Key;
				_box.Text = e.Label;
				_suppress = false;
			}
		}
	}

	public void SetStrings(IEnumerable<string> items)
	{
		_all = items.Select(s => ((object?)s, s, s)).ToList();
	}

	public void SetKeyValues(IEnumerable<KeyValuePair<string, string>> items)
	{
		_all = items.Select(kv => ((object?)kv, kv.Key, kv.Value)).ToList();
	}

	public void SetObjects<T>(IEnumerable<T> items, Func<T, string> keySelector, Func<T, string> labelSelector)
	{
		_all = items.Select(x => ((object?)x, keySelector(x), labelSelector(x))).ToList();
	}

	private void PopulatePopup(string filterText)
	{
		string norm = GameData.Normalize(filterText.Trim());
		var filtered = norm.Length == 0
			? _all
			: _all.Where(x => GameData.Normalize(x.Label).Contains(norm)).ToList();
		_list.ItemsSource = filtered.Select(x => new ComboEntry(x.Raw, x.Key, x.Label)).ToList();
	}

	private void PickEntry(ComboEntry ce)
	{
		_suppress = true;
		SelectedItem = ce.Raw;
		_selectedValue = ce.Key;
		_box.Text = ce.Key.Length == 0 ? "" : ce.Label;
		_suppress = false;
		_popup.IsOpen = false;
		SelectionChanged?.Invoke(this, EventArgs.Empty);
		TextChanged?.Invoke(this, EventArgs.Empty);
	}
}
