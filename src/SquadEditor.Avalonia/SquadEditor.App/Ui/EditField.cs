using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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
		Def = AppSettings.Current.Attributes100 && def.Scale.HasValue ? def.Unscaled() : def;
		Label = label;
		_labels = key => data.Layout.Text(Loc.Instance.Language, key);
		Refresh();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public static event EventHandler<EditField>? Written;

	public ulong Entity { get; }
	public FieldDef Def { get; }
	public FieldDef Original { get; }
	public string Label { get; }
	public string Key => Def.Name;
	public bool IsReadOnly => !Def.IsWritable || AppSettings.Current.ReadOnlyMode;
	public bool IsAttribute => Original.Min == 1 && Original.Max == 20 && Original.Options.Count == 0;
	public bool IsDate => Def.Type == FieldType.DayYear;
	public bool IsHeight => Def.Name == "Height";

	public int Rating => Def != Original && Original.Scale is double s ? (int)Math.Round(Number / s) : Number;
	public bool IsInverseAttribute => Def.Name is "Injury Proneness" or "Dirtiness" or "Controversy";
	public bool IsNumeric => Def.IsNumeric;

	public string Value
	{
		get => _value;
		private set
		{
			if (_value == value) return;
			_value = value;
			OnChanged(nameof(Value));
			OnChanged(nameof(Display));
			OnChanged(nameof(Number));
			OnChanged(nameof(ValueBrush));
		}
	}

	public bool IsMoney => Def.Format == FieldFormat.Money && Def.Currency == "£";

	public bool IsWage => IsMoney && Money.IsWage(Def.Name);

	public string Display
	{
		get
		{
			if (IsMoney && double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gbp))
			{
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

	public string EditText => IsMoney && double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gbp)
		? Math.Round(Money.ToDisplay(gbp, IsWage)).ToString(CultureInfo.InvariantCulture)
		: IsDate ? Dates.Show(_value) : _value;

	private readonly Func<string, string> _labels;

	public int Number => int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

	public IBrush ValueBrush => IsAttribute ? Colors.Attribute(IsInverseAttribute ? 21 - Math.Clamp(Rating, 1, 20) : Rating) : Colors.Text;

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
		catch (Exception ex) when (ex is FormatException or InvalidOperationException or SquadEditor.Core.Memory.MemoryAccessException)
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
	public static IBrush Text { get; private set; } = Freeze(Color.FromRgb(0xE8, 0xEC, 0xF4));
	private static IBrush Low = Freeze(Color.FromRgb(0xF2, 0x6D, 0x6D));
	private static IBrush Weak = Freeze(Color.FromRgb(0xF5, 0x9E, 0x4B));
	private static IBrush Mid = Freeze(Color.FromRgb(0xE9, 0xD2, 0x5A));
	private static IBrush Good = Freeze(Color.FromRgb(0x7F, 0xDB, 0x6A));
	private static IBrush Elite = Freeze(Color.FromRgb(0x3D, 0xD6, 0xC0));

	public static void UseLight(bool light)
	{
		if (light)
		{
			Text = Freeze(Color.FromRgb(0x17, 0x20, 0x33));
			Low = Freeze(Color.FromRgb(0xD0, 0x2F, 0x2F));
			Weak = Freeze(Color.FromRgb(0xC9, 0x5F, 0x0A));
			Mid = Freeze(Color.FromRgb(0xA1, 0x82, 0x00));
			Good = Freeze(Color.FromRgb(0x2E, 0x8B, 0x1F));
			Elite = Freeze(Color.FromRgb(0x0B, 0x8A, 0x78));
		}
		else
		{
			Text = Freeze(Color.FromRgb(0xE8, 0xEC, 0xF4));
			Low = Freeze(Color.FromRgb(0xF2, 0x6D, 0x6D));
			Weak = Freeze(Color.FromRgb(0xF5, 0x9E, 0x4B));
			Mid = Freeze(Color.FromRgb(0xE9, 0xD2, 0x5A));
			Good = Freeze(Color.FromRgb(0x7F, 0xDB, 0x6A));
			Elite = Freeze(Color.FromRgb(0x3D, 0xD6, 0xC0));
		}
	}

	public static IBrush Attribute(int value) => value switch
	{
		<= 5 => Low,
		<= 10 => Weak,
		<= 14 => Mid,
		<= 17 => Good,
		_ => Elite
	};

	private static IBrush Freeze(Color c) => new SolidColorBrush(c).ToImmutable();
}

/// <summary>
/// Attached behaviour for value boxes: shows the formatted value, edits the raw one,
/// writes on Enter / focus loss, Esc cancels, arrow keys change numbers by 1.
/// </summary>
public static class FieldBox
{
	public static readonly AttachedProperty<EditField?> FieldProperty =
		AvaloniaProperty.RegisterAttached<TextBox, EditField?>("Field", typeof(FieldBox));

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, PropertyChangedEventHandler> Handlers = new();

	public static string EntityLabel { get; set; } = "";

	static FieldBox()
	{
		FieldProperty.Changed.AddClassHandler<TextBox>(OnFieldChanged);
	}

	public static EditField? GetField(AvaloniaObject o) => o.GetValue(FieldProperty);

	public static void SetField(AvaloniaObject o, EditField? value) => o.SetValue(FieldProperty, value);

	private static void OnFieldChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.OldValue is EditField old && Handlers.TryGetValue(box, out PropertyChangedEventHandler? previous))
		{
			old.PropertyChanged -= previous;
			Handlers.Remove(box);
		}
		box.GotFocus -= OnFocus;
		box.LostFocus -= OnBlur;
		box.RemoveHandler(InputElement.KeyDownEvent, OnKey);
		box.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
		if (e.NewValue is not EditField field)
		{
			return;
		}
		box.IsReadOnly = field.IsReadOnly;
		box.Text = field.Display;
		ToolTip.SetTip(box, null);
		PropertyChangedEventHandler handler = (_, args) =>
		{
			if (!box.IsFocused && args.PropertyName == nameof(EditField.Value))
			{
				box.Text = field.Display;
			}
			if (args.PropertyName == nameof(EditField.Error))
			{
				ToolTip.SetTip(box, field.Error);
				if (field.HasError)
				{
					box.BorderBrush = Build.Res("Danger");
				}
				else
				{
					box.ClearValue(TemplatedControl.BorderBrushProperty);
				}
			}
		};
		field.PropertyChanged += handler;
		Handlers.Add(box, handler);
		box.GotFocus += OnFocus;
		box.LostFocus += OnBlur;
		box.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
		box.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
	}

	private static void FlashSuccess(TextBox box)
	{
		box.BorderBrush = Build.Res("Positive");
		var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
		timer.Tick += (_, _) =>
		{
			timer.Stop();
			if (GetField(box) is not { HasError: true })
			{
				box.ClearValue(TemplatedControl.BorderBrushProperty);
			}
		};
		timer.Start();
	}

	private static void OnFocus(object? sender, GotFocusEventArgs e)
	{
		if (sender is TextBox box && GetField(box) is { } f && !f.IsReadOnly)
		{
			box.Text = f.EditText;
			box.SelectAll();
		}
	}

	private static void OnBlur(object? sender, RoutedEventArgs e)
	{
		if (sender is TextBox box && GetField(box) is { } f)
		{
			Commit(box, f);
			box.Text = f.Display;
		}
	}

	private static void Commit(TextBox box, EditField f)
	{
		string before = f.Value;
		if (!f.Commit(box.Text ?? "", EntityLabel))
		{
			box.Text = f.EditText;
		}
		else if (f.Value != before)
		{
			FlashSuccess(box);
		}
	}

	private static void OnWheel(object? sender, PointerWheelEventArgs e)
	{
		if (sender is not TextBox box || GetField(box) is not { } f || f.IsReadOnly || !f.IsNumeric || f.IsMoney)
		{
			return;
		}
		bool modHeld = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
		if (!box.IsFocused && !modHeld)
		{
			return;
		}
		string raw = box.IsFocused ? (box.Text ?? "") : f.EditText;
		if (double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
		{
			double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
			double next = v + (e.Delta.Y > 0 ? step : -step);
			string before = f.Value;
			if (f.Commit(next.ToString(CultureInfo.InvariantCulture), EntityLabel))
			{
				box.Text = box.IsFocused ? f.EditText : f.Display;
				if (f.Value != before) FlashSuccess(box);
			}
			e.Handled = true;
		}
	}

	private static void OnKey(object? sender, KeyEventArgs e)
	{
		if (sender is not TextBox box || GetField(box) is not { } f || f.IsReadOnly)
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
			if (f.IsNumeric && !f.IsMoney && double.TryParse((box.Text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
			{
				double step = f.IsAttribute ? 1 : (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1);
				Commit(box, f);
				string before = f.Value;
				if (f.Commit((v + (e.Key == Key.Up ? step : -step)).ToString(CultureInfo.InvariantCulture), EntityLabel))
				{
					box.Text = f.Value;
					if (f.Value != before) FlashSuccess(box);
				}
				box.SelectAll();
				e.Handled = true;
			}
			break;
		}
	}
}

/// <summary>Image lookups for bindings: ConverterParameter "club" or "person", value is the unique ID.</summary>
public sealed class ImageLookup : IValueConverter
{
	public static GraphicsIndex Graphics { get; set; } = GraphicsIndex.Build(null);

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		uint id = value switch { uint u => u, int i => (uint)i, _ => 0 };
		if (id == 0) return null;
		return (parameter as string) == "club" ? Graphics.Club(id) : Graphics.Person(id);
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Attribute colour for numbers 1-20 in grids.</summary>
public sealed class AttributeBrush : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		int v = value is int i ? i : 0;
		return Colors.Attribute((parameter as string) == "ability" ? (int)Math.Round(v / 10.0) : v);
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
