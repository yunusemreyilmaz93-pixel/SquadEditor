using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Layout;

namespace SquadEditor.App.Ui;

/// <summary>One editable value of a game record, read live from memory.</summary>
public sealed class EditField : INotifyPropertyChanged
{
	private readonly GameData _data;
	private string _value = "";
	private string? _error;

	public EditField(GameData data, ulong entity, FieldDef def, string label)
	{
		_data = data;
		Entity = entity;
		Original = def;
		// "Attributes 1-100": edit the value as the game stores it (5-100) instead of 1-20.
		Def = AppSettings.Current.Attributes100 && def.Scale.HasValue ? def.Unscaled() : def;
		Label = label;
		_labels = key => data.Layout.Text(Loc.Instance.Language, key);
		Refresh();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Raised after a successful write, so pages can refresh derived values (header, lists).</summary>
	public static event EventHandler<EditField>? Written;

	public ulong Entity { get; }
	public FieldDef Def { get; }

	/// <summary>The field as the layout defines it (before display preferences).</summary>
	public FieldDef Original { get; }
	public string Label { get; }
	public string Key => Def.Name;
	public bool IsReadOnly => !Def.IsWritable || AppSettings.Current.ReadOnlyMode;
	public bool IsAttribute => Original.Min == 1 && Original.Max == 20 && Original.Options.Count == 0;
	public bool IsDate => Def.Type == FieldType.DayYear;
	public bool IsHeight => Def.Name == "Height";

	/// <summary>Attribute on the 1-20 scale whatever the display preference (for colours).</summary>
	public int Rating => Def != Original && Original.Scale is double s ? (int)Math.Round(Number / s) : Number;
	public bool IsInverseAttribute => Def.Name is "Injury Proneness" or "Dirtiness" or "Controversy";
	public bool IsNumeric => Def.IsNumeric;

	public string Value
	{
		get => _value;
		private set
		{
			if (_value == value)
			{
				return;
			}
			_value = value;
			OnChanged(nameof(Value));
			OnChanged(nameof(Display));
			OnChanged(nameof(Number));
			OnChanged(nameof(ValueBrush));
		}
	}

	/// <summary>Money kept in GBP by the game, shown in the user's currency (and wage period).</summary>
	public bool IsMoney => Def.Format == FieldFormat.Money && Def.Currency == "£";

	public bool IsWage => IsMoney && Money.IsWage(Def.Name);

	/// <summary>Formatted for reading (money, separators); the box switches to <see cref="EditText"/> while editing.</summary>
	public string Display
	{
		get
		{
			if (IsMoney && double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gbp))
			{
				// The game uses -1 for "not set" (e.g. no asking price).
				return gbp < 0 && Def.Min < 0 && gbp == -1 ? Build.T("Belirlenmedi", "Not set") : Money.Format(gbp, IsWage);
			}
			if (Def.Options.TryGetValue(_value, out string? option))
			{
				return _labels(option);
			}
			if (IsDate)
			{
				return Dates.Show(_value);
			}
			if (IsHeight && AppSettings.Current.HeightUnit == "ftin" && Number > 0)
			{
				int inches = (int)Math.Round(Number / 2.54);
				return $"{inches / 12}' {inches % 12}\"";
			}
			return DisplayFormat.Format(Def, _value, Loc.Instance.Language);
		}
	}

	/// <summary>The plain number the user edits (in their currency for money).</summary>
	public string EditText => IsMoney && double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gbp)
		? Math.Round(Money.ToDisplay(gbp, IsWage)).ToString(CultureInfo.InvariantCulture)
		: IsDate ? Dates.Show(_value) : _value;

	private readonly Func<string, string> _labels;

	public int Number => int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

	public Brush ValueBrush => IsAttribute ? Colors.Attribute(IsInverseAttribute ? 21 - Math.Clamp(Rating, 1, 20) : Rating) : Colors.Text;

	public string? Error
	{
		get => _error;
		private set
		{
			_error = value;
			OnChanged(nameof(Error));
			OnChanged(nameof(HasError));
		}
	}

	public bool HasError => _error != null;

	public void Refresh()
	{
		try
		{
			Value = _data.Session.ReadField(Entity, Def);
		}
		catch (Exception)
		{
			Value = "";
		}
	}

	public bool Commit(string text, string entityLabel)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			return false;
		}
		if (IsDate && text.Trim() != EditText)
		{
			text = Dates.ToGame(text.Trim()) ?? text;
		}
		else if (IsDate)
		{
			Error = null;
			return true;
		}
		if (IsMoney && text.Trim() != EditText)
		{
			string cleaned = text.Trim().Replace(" ", "").Replace("£", "").Replace("€", "").Replace("$", "").Replace("₺", "");
			if (double.TryParse(cleaned.Replace(".", "").Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double shown))
			{
				text = Math.Round(Money.FromDisplay(shown, IsWage)).ToString(CultureInfo.InvariantCulture);
			}
		}
		else if (IsMoney)
		{
			Error = null;
			return true;
		}
		if (IsReadOnly || text.Trim() == _value)
		{
			Error = null;
			return true;
		}
		try
		{
			_data.Session.WriteField(Entity, Def, text, entityLabel);
			Error = null;
			Refresh();
			Written?.Invoke(this, this);
			return true;
		}
		catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException || ex is SquadEditor.Core.Memory.MemoryAccessException)
		{
			Error = ex.Message;
			return false;
		}
	}

	private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Shared colours used from code.</summary>
public static class Colors
{
	public static Brush Text { get; private set; } = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF4)));
	private static Brush Low = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0x6D, 0x6D)));
	private static Brush Weak = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x4B)));
	private static Brush Mid = Freeze(new SolidColorBrush(Color.FromRgb(0xE9, 0xD2, 0x5A)));
	private static Brush Good = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xDB, 0x6A)));
	private static Brush Elite = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0xC0)));

	/// <summary>Darker tones that stay readable on the light theme.</summary>
	public static void UseLight(bool light)
	{
		if (!light)
		{
			return;
		}
		Text = Freeze(new SolidColorBrush(Color.FromRgb(0x17, 0x20, 0x33)));
		Low = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0x2F, 0x2F)));
		Weak = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0x5F, 0x0A)));
		Mid = Freeze(new SolidColorBrush(Color.FromRgb(0xA1, 0x82, 0x00)));
		Good = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x1F)));
		Elite = Freeze(new SolidColorBrush(Color.FromRgb(0x0B, 0x8A, 0x78)));
	}

	/// <summary>FM-like attribute colours: red, orange, yellow, green, teal.</summary>
	public static Brush Attribute(int value) => value switch
	{
		<= 5 => Low,
		<= 10 => Weak,
		<= 14 => Mid,
		<= 17 => Good,
		_ => Elite
	};

	private static Brush Freeze(SolidColorBrush b)
	{
		b.Freeze();
		return b;
	}
}

/// <summary>
/// Attached behaviour for value boxes: shows the formatted value, edits the raw one,
/// writes on Enter / focus loss, Esc cancels, arrow keys change numbers by 1.
/// </summary>
public static class FieldBox
{
	public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
		"Field", typeof(EditField), typeof(FieldBox), new PropertyMetadata(null, OnFieldChanged));

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, PropertyChangedEventHandler> Handlers = new();

	public static string EntityLabel { get; set; } = "";

	public static EditField? GetField(DependencyObject o) => (EditField?)o.GetValue(FieldProperty);

	public static void SetField(DependencyObject o, EditField? value) => o.SetValue(FieldProperty, value);

	private static void OnFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not TextBox box)
		{
			return;
		}
		if (e.OldValue is EditField old && Handlers.TryGetValue(box, out PropertyChangedEventHandler? previous))
		{
			old.PropertyChanged -= previous;
			Handlers.Remove(box);
		}
		box.GotKeyboardFocus -= OnFocus;
		box.LostKeyboardFocus -= OnBlur;
		box.PreviewKeyDown -= OnKey;
		if (e.NewValue is not EditField field)
		{
			return;
		}
		box.IsReadOnly = field.IsReadOnly;
		box.Text = field.Display;
		box.ToolTip = null;
		PropertyChangedEventHandler handler = (_, args) =>
		{
			if (!box.IsKeyboardFocused && (args.PropertyName == nameof(EditField.Value)))
			{
				box.Text = field.Display;
			}
			if (args.PropertyName == nameof(EditField.Error))
			{
				box.ToolTip = field.Error;
				box.BorderBrush = field.HasError ? (Brush)Application.Current.Resources["Danger"] : null;
				if (!field.HasError)
				{
					box.ClearValue(Control.BorderBrushProperty);
				}
			}
		};
		field.PropertyChanged += handler;
		Handlers.Add(box, handler);
		box.GotKeyboardFocus += OnFocus;
		box.LostKeyboardFocus += OnBlur;
		box.PreviewKeyDown += OnKey;
	}

	private static void OnFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		var box = (TextBox)sender;
		if (GetField(box) is { } f && !f.IsReadOnly)
		{
			box.Text = f.EditText;
			box.SelectAll();
		}
	}

	private static void OnBlur(object sender, KeyboardFocusChangedEventArgs e)
	{
		var box = (TextBox)sender;
		if (GetField(box) is { } f)
		{
			Commit(box, f);
			box.Text = f.Display;
		}
	}

	private static void Commit(TextBox box, EditField f)
	{
		if (!f.Commit(box.Text, EntityLabel))
		{
			box.Text = f.EditText;
		}
	}

	private static void OnKey(object sender, KeyEventArgs e)
	{
		var box = (TextBox)sender;
		if (GetField(box) is not { } f || f.IsReadOnly)
		{
			return;
		}
		switch (e.Key)
		{
		case Key.Enter:
			Commit(box, f);
			box.Text = f.EditText;
			box.SelectAll();
			e.Handled = true;
			break;
		case Key.Escape:
			box.Text = f.EditText;
			box.SelectAll();
			e.Handled = true;
			break;
		case Key.Up:
		case Key.Down:
			if (f.IsNumeric && !f.IsMoney && double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
			{
				double step = f.IsAttribute ? 1 : (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1);
				Commit(box, f);
				if (f.Commit((v + (e.Key == Key.Up ? step : -step)).ToString(CultureInfo.InvariantCulture), EntityLabel))
				{
					box.Text = f.Value;
				}
				box.SelectAll();
				e.Handled = true;
			}
			break;
		}
	}
}

/// <summary>Image lookups for bindings: ConverterParameter "club" or "person", value is the unique ID.</summary>
public sealed class ImageLookup : System.Windows.Data.IValueConverter
{
	public static GraphicsIndex Graphics { get; set; } = GraphicsIndex.Build(null);

	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		uint id = value switch { uint u => u, int i => (uint)i, _ => 0 };
		if (id == 0)
		{
			return null;
		}
		return (parameter as string) == "club" ? Graphics.Club(id) : Graphics.Person(id);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Attribute colour for numbers 1-20 in grids.</summary>
public sealed class AttributeBrush : System.Windows.Data.IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		int v = value is int i ? i : 0;
		// CA / PA (1-200) are mapped onto the 1-20 colour scale.
		return Colors.Attribute((parameter as string) == "ability" ? (int)Math.Round(v / 10.0) : v);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
