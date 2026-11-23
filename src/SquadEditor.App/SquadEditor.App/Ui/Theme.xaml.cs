using System;
using System.Windows;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

public partial class Theme : ResourceDictionary
{
	public Theme(string themeName = "dark")
	{
		InitializeComponent();
		ApplyTo(this, themeName, AppSettings.Current.Accent);
	}

	/// <summary>Applies theme ("dark", "oled", "charcoal", "light") and accent colour immediately without restarting.</summary>
	public static void ApplyLive(string themeName, string? accent)
	{
		ResourceDictionary dict = Application.Current.Resources.MergedDictionaries.Count > 0
			? Application.Current.Resources.MergedDictionaries[0]
			: Application.Current.Resources;
		ApplyTo(dict, themeName, accent);
	}

	private static void ApplyTo(ResourceDictionary target, string themeName, string? accent)
	{
		bool light = themeName == "light";
		switch (themeName)
		{
		case "light":
			Set(target, "Bg", "#F2F4F8");
			Set(target, "Chrome", "#FFFFFF");
			Set(target, "Panel", "#FFFFFF");
			Set(target, "Card", "#FFFFFF");
			Set(target, "CardHover", "#EDF1F8");
			Set(target, "Field", "#F5F7FB");
			Set(target, "Stroke", "#CFD7E4");
			Set(target, "StrokeSoft", "#E1E6EE");
			Set(target, "Text", "#172033");
			Set(target, "Muted", "#566179");
			Set(target, "Faint", "#8A94A8");
			Set(target, "Accent", "#2F6FEB");
			Set(target, "AccentSoft", "#D6E3FF");
			Set(target, "Selection", "#DCE7FB");
			SetHeader(target, "#1E448A", "#19356B", "#15284E");
			break;
		case "oled":
			Set(target, "Bg", "#04060A");
			Set(target, "Chrome", "#080B12");
			Set(target, "Panel", "#0C1018");
			Set(target, "Card", "#101520");
			Set(target, "CardHover", "#182032");
			Set(target, "Field", "#070A10");
			Set(target, "Stroke", "#243049");
			Set(target, "StrokeSoft", "#171F30");
			Set(target, "Text", "#F0F4FA");
			Set(target, "Muted", "#8E9BB3");
			Set(target, "Faint", "#55607A");
			Set(target, "Accent", "#4F8CFF");
			Set(target, "AccentSoft", "#162B54");
			Set(target, "Selection", "#1B2C4D");
			SetHeader(target, "#102348", "#0A1429", "#05070C");
			break;
		case "charcoal":
			Set(target, "Bg", "#111215");
			Set(target, "Chrome", "#16181C");
			Set(target, "Panel", "#1A1C21");
			Set(target, "Card", "#1E2127");
			Set(target, "CardHover", "#272B33");
			Set(target, "Field", "#131519");
			Set(target, "Stroke", "#323742");
			Set(target, "StrokeSoft", "#252931");
			Set(target, "Text", "#EFECE6");
			Set(target, "Muted", "#9C9A94");
			Set(target, "Faint", "#666561");
			Set(target, "Accent", "#E0A82E");
			Set(target, "AccentSoft", "#3D3117");
			Set(target, "Selection", "#342C19");
			SetHeader(target, "#3A2E14", "#241E12", "#16181C");
			break;
		default: // "dark" — FM Gece Laciverti
			Set(target, "Bg", "#0D1119");
			Set(target, "Chrome", "#111723");
			Set(target, "Panel", "#141B28");
			Set(target, "Card", "#182030");
			Set(target, "CardHover", "#1E2840");
			Set(target, "Field", "#0F1521");
			Set(target, "Stroke", "#243049");
			Set(target, "StrokeSoft", "#1C2538");
			Set(target, "Text", "#E8ECF4");
			Set(target, "Muted", "#8C97AD");
			Set(target, "Faint", "#5A6580");
			Set(target, "Accent", "#4F8CFF");
			Set(target, "AccentSoft", "#1F3A6E");
			Set(target, "Selection", "#22345A");
			SetHeader(target, "#1C3F86", "#16284F", "#121A2B");
			break;
		}
		bool useCustomAccent = accent is { Length: 7 } && accent.StartsWith('#')
			&& !(themeName == "charcoal" && accent.Equals("#4F8CFF", StringComparison.OrdinalIgnoreCase));
		if (useCustomAccent && accent is { } hex)
		{
			try
			{
				var c = (Color)ColorConverter.ConvertFromString(hex);
				Set(target, "Accent", hex);
				var soft = light
					? Color.FromRgb((byte)(c.R + (255 - c.R) * 0.78), (byte)(c.G + (255 - c.G) * 0.78), (byte)(c.B + (255 - c.B) * 0.78))
					: Color.FromRgb((byte)(c.R * 0.35), (byte)(c.G * 0.35), (byte)(c.B * 0.4));
				var brush = new SolidColorBrush(soft);
				brush.Freeze();
				target["AccentSoft"] = brush;
			}
			catch (FormatException)
			{
			}
		}
		Colors.UseLight(light);
	}

	private static void SetHeader(ResourceDictionary target, string c0, string c1, string c2)
	{
		var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
		grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c0), 0));
		grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c1), 0.55));
		grad.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c2), 1));
		grad.Freeze();
		target["HeaderBrush"] = grad;
	}

	private static void Set(ResourceDictionary target, string key, string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		target[key] = brush;
	}
}
