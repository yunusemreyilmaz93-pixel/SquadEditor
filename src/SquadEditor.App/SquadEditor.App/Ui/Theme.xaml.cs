using System;
using System.Windows;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

public partial class Theme : ResourceDictionary
{
	public Theme(bool light = false)
	{
		InitializeComponent();
		ApplyTo(this, light, AppSettings.Current.Accent);
	}

	/// <summary>Applies theme and accent colour immediately without restarting the application.</summary>
	public static void ApplyLive(bool light, string? accent)
	{
		ResourceDictionary dict = Application.Current.Resources.MergedDictionaries.Count > 0
			? Application.Current.Resources.MergedDictionaries[0]
			: Application.Current.Resources;
		ApplyTo(dict, light, accent);
	}

	private static void ApplyTo(ResourceDictionary target, bool light, string? accent)
	{
		if (light)
		{
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
		}
		else
		{
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
		}
		if (accent is { Length: 7 } hex && hex.StartsWith('#'))
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

	private static void Set(ResourceDictionary target, string key, string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		target[key] = brush;
	}
}
