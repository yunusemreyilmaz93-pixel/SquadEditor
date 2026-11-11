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
		Colors.UseLight(light);
	}

	private void Set(string key, string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		this[key] = brush;
	}
}
