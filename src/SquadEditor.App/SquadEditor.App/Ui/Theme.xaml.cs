using System;
using System.Windows;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

public partial class Theme : ResourceDictionary
{
	public Theme(bool light = false)
	{
		InitializeComponent();
		if (light)
		{
			Set("Bg", "#F2F4F8");
			Set("Chrome", "#FFFFFF");
			Set("Panel", "#FFFFFF");
			Set("Card", "#FFFFFF");
			Set("CardHover", "#EDF1F8");
			Set("Field", "#F5F7FB");
			Set("Stroke", "#CFD7E4");
			Set("StrokeSoft", "#E1E6EE");
			Set("Text", "#172033");
			Set("Muted", "#566179");
			Set("Faint", "#8A94A8");
			Set("Accent", "#2F6FEB");
			Set("AccentSoft", "#D6E3FF");
			Set("Selection", "#DCE7FB");
		}
		if (AppSettings.Current.Accent is { Length: 7 } accent && accent.StartsWith('#'))
		{
			try
			{
				var c = (Color)ColorConverter.ConvertFromString(accent);
				Set("Accent", accent);
				var soft = light
					? Color.FromRgb((byte)(c.R + (255 - c.R) * 0.78), (byte)(c.G + (255 - c.G) * 0.78), (byte)(c.B + (255 - c.B) * 0.78))
					: Color.FromRgb((byte)(c.R * 0.35), (byte)(c.G * 0.35), (byte)(c.B * 0.4));
				var brush = new SolidColorBrush(soft);
				brush.Freeze();
				this["AccentSoft"] = brush;
			}
			catch (FormatException)
			{
			}
		}
		Colors.UseLight(light);
	}

	private void Set(string key, string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		this[key] = brush;
	}
}
