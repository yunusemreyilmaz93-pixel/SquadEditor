using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace SquadEditor.App.Ui;

public enum IconKind
{
	Plus,
	PlusCircle,
	Zap,
	Search,
	Edit,
	TrendingUp,
	Handshake,
	Coins,
	Wallet,
	Award,
	Shield,
	Users,
	Camera,
	Sliders,
	Star,
	Check,
	Layers,
	Briefcase,
	Percent,
	RefreshCw,
	Save,
	Trash,
	Trophy
}

/// <summary>
/// Unified Lucide-style 24x24 vector icon system (1.9px round stroke).
/// Replaces inconsistent OS emoji glyphs with crisp, theme-aware vector paths across Windows and macOS.
/// </summary>
public static class Icons
{
	private static string PathData(IconKind kind) => kind switch
	{
		IconKind.Plus => "M12 5v14 M5 12h14",
		IconKind.PlusCircle => "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 8v8 M8 12h8",
		IconKind.Zap => "M13 2 3 14h9l-1 8 10-12h-9l1-8z",
		IconKind.Search => "M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z M21 21l-4.35-4.35",
		IconKind.Edit => "M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z",
		IconKind.TrendingUp => "M22 7 13.5 15.5 8.5 10.5 2 17 M16 7h6v6",
		IconKind.Handshake => "m11 17 2 2a1 1 0 1 0 3-3 M14 14l2.5 2.5a1 1 0 1 0 3-3l-3.88-3.88a3 3 0 0 0-4.24 0l-.88.88a1 1 0 1 1-3-3l2.81-2.81a5.79 5.79 0 0 1 7.06-.87l.47.28a2 2 0 0 0 1.42.25L21 4 M21 3l1 11h-2 M3 3 2 14l6.5 6.5a1 1 0 1 0 3-3 M3 4h8",
		IconKind.Coins => "M8 14a6 6 0 1 0 0-12 6 6 0 0 0 0 12z M18.09 10.37A6 6 0 1 1 10.34 18 M7 6h1v4",
		IconKind.Wallet => "M19 7V4a1 1 0 0 0-1-1H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v4h-3a2 2 0 0 0 0 4h3a1 1 0 0 0 1-1v-2a1 1 0 0 0-1-1 M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1v-4",
		IconKind.Award => "M12 15a7 7 0 1 0 0-14 7 7 0 0 0 0 14z M8.21 13.89 7 23l5-3 5 3-1.21-9.12",
		IconKind.Shield => "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z",
		IconKind.Users => "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 7a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
		IconKind.Camera => "M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z M12 17a3 3 0 1 0 0-6 3 3 0 0 0 0 6z",
		IconKind.Sliders => "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M2 14h4 M10 8h4 M18 16h4",
		IconKind.Star => "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z",
		IconKind.Check => "M20 6 9 17l-5-5",
		IconKind.Layers => "M12 2 2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5",
		IconKind.Briefcase => "M16 20V4a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16 M4 6h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z",
		IconKind.Percent => "M19 5 5 19 M6.5 9a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z M17.5 20a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z",
		IconKind.RefreshCw => "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16 M16 16h5v5",
		IconKind.Save => "M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8",
		IconKind.Trash => "M3 6h18 M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6 M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2",
		IconKind.Trophy => "M6 9H4.5a2.5 2.5 0 0 1 0-5H6 M18 9h1.5a2.5 2.5 0 0 0 0-5H18 M4 22h16 M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22 M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22 M18 2H6v7a6 6 0 0 0 12 0V2Z",
		_ => "M12 5v14 M5 12h14"
	};

	public static Control Glyph(IconKind kind, double size = 14, IBrush? stroke = null)
	{
		var path = new Path
		{
			Data = Geometry.Parse(PathData(kind)),
			StrokeThickness = 1.9,
			StrokeLineCap = PenLineCap.Round,
			StrokeJoin = PenLineJoin.Round,
			Fill = Brushes.Transparent
		};
		if (stroke != null)
		{
			path.Stroke = stroke;
		}
		else
		{
			path.Bind(Shape.StrokeProperty, new Binding("Foreground")
			{
				RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(TemplatedControl) }
			});
		}
		return new Viewbox
		{
			Width = size,
			Height = size,
			VerticalAlignment = VerticalAlignment.Center,
			Child = new Canvas { Width = 24, Height = 24, Children = { path } }
		};
	}

	public static StackPanel Label(IconKind kind, string text, double iconSize = 14, IBrush? stroke = null)
	{
		var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
		var g = Glyph(kind, iconSize, stroke);
		g.Margin = new Thickness(0, 0, 6, 0);
		sp.Children.Add(g);
		sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
		return sp;
	}
}
