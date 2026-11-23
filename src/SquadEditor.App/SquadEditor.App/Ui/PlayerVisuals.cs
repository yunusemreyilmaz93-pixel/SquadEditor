using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadEditor.App.Ui;

/// <summary>
/// Bespoke FM-native visual components: 8-axis attribute radar polygon (single or 2-player overlay),
/// CA/PA dual progress bar, and two-player side-by-side comparison window.
/// </summary>
public static class PlayerVisuals
{
	public static readonly (string Tr, string En)[] RadarAxes =
	{
		("Hücum", "Attack"),
		("Teknik", "Tech"),
		("Zihinsel", "Mental"),
		("Savunma", "Defend"),
		("Fizik", "Physical"),
		("Hız", "Speed"),
		("Hava", "Aerial"),
		("Vizyon", "Vision")
	};

	/// <summary>Computes 8 normalized 0..20 axis scores from player raw69 bytes (+0x150..+0x194).</summary>
	public static double[] ComputeRadar8(ReadOnlySpan<byte> raw69)
	{
		if (raw69.Length < 69) return new double[8];
		ReadOnlySpan<byte> a = raw69.Slice(15, 54);
		static double Avg(ReadOnlySpan<byte> s, params int[] idx)
		{
			double sum = 0;
			foreach (int i in idx) sum += s[i] / 5.0;
			return Math.Clamp(sum / idx.Length, 1, 20);
		}
		return new[]
		{
			Avg(a, 2, 6, 4, 52),       // Attack: Fin, Otb, Lon, Cmp
			Avg(a, 1, 22, 23, 7),      // Tech: Dri, Fir, Tec, Pas
			Avg(a, 17, 18, 28, 29, 53),// Mental: Ant, Dec, Tea, Wor, Cnt
			Avg(a, 5, 9, 20),          // Defend: Mar, Tac, Pos
			Avg(a, 36, 37, 42, 46),    // Physical: Str, Sta, Bal, Agi
			Avg(a, 34, 38),            // Speed: Acc, Pac
			Avg(a, 3, 39, 43),         // Aerial: Hea, Jum, Bra
			Avg(a, 7, 10, 26, 0)       // Vision: Pas, Vis, Fla, Cro
		};
	}

	/// <summary>Renders an 8-axis octagonal radar chart. Optional second series overlays Player B in emerald.</summary>
	public static FrameworkElement RadarChart(double size, double[] valuesA, double[]? valuesB = null)
	{
		var canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
		double cx = size / 2.0, cy = size / 2.0;
		double radius = size * 0.34;
		var ringStroke = new SolidColorBrush(Color.FromArgb(0x38, 0x8C, 0x97, 0xAD));

		// 4 concentric guide octagons (5, 10, 15, 20)
		for (int ring = 1; ring <= 4; ring++)
		{
			double r = radius * (ring / 4.0);
			var pts = new PointCollection();
			for (int i = 0; i < 8; i++)
			{
				double ang = -Math.PI / 2 + i * Math.PI / 4;
				pts.Add(new Point(cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r));
			}
			canvas.Children.Add(new Polygon
			{
				Points = pts,
				Stroke = ringStroke,
				StrokeThickness = ring == 4 ? 1.1 : 0.7,
				Fill = ring == 4 ? new SolidColorBrush(Color.FromArgb(0x10, 0x8C, 0x97, 0xAD)) : Brushes.Transparent
			});
		}

		// Axis spokes + labels
		for (int i = 0; i < 8; i++)
		{
			double ang = -Math.PI / 2 + i * Math.PI / 4;
			double ex = cx + Math.Cos(ang) * radius;
			double ey = cy + Math.Sin(ang) * radius;
			canvas.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = ex, Y2 = ey, Stroke = ringStroke, StrokeThickness = 0.7 });

			var lbl = Build.Text(Build.T(RadarAxes[i].Tr, RadarAxes[i].En), size < 200 ? 9.5 : 10.5, Build.Res("Muted"), FontWeights.SemiBold);
			lbl.Measure(new Size(100, 30));
			double lx = cx + Math.Cos(ang) * (radius + 17) - lbl.DesiredSize.Width / 2;
			double ly = cy + Math.Sin(ang) * (radius + 14) - lbl.DesiredSize.Height / 2;
			Canvas.SetLeft(lbl, Math.Clamp(lx, 2, size - lbl.DesiredSize.Width - 2));
			Canvas.SetTop(lbl, Math.Clamp(ly, 1, size - lbl.DesiredSize.Height - 1));
			canvas.Children.Add(lbl);
		}

		void DrawSeries(double[] vals, Color strokeColor, Color fillColor)
		{
			var pts = new PointCollection();
			for (int i = 0; i < 8; i++)
			{
				double v = Math.Clamp(vals[i], 0, 20) / 20.0;
				double ang = -Math.PI / 2 + i * Math.PI / 4;
				pts.Add(new Point(cx + Math.Cos(ang) * radius * v, cy + Math.Sin(ang) * radius * v));
			}
			canvas.Children.Add(new Polygon
			{
				Points = pts,
				Fill = new SolidColorBrush(fillColor),
				Stroke = new SolidColorBrush(strokeColor),
				StrokeThickness = 1.8
			});
			foreach (Point pt in pts)
			{
				var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(strokeColor) };
				Canvas.SetLeft(dot, pt.X - 2.5);
				Canvas.SetTop(dot, pt.Y - 2.5);
				canvas.Children.Add(dot);
			}
		}

		Color accentColor = (Build.Res("Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0x4F, 0x8C, 0xFF);
		DrawSeries(valuesA, accentColor, Color.FromArgb(0x48, accentColor.R, accentColor.G, accentColor.B));
		if (valuesB != null)
		{
			Color bColor = Color.FromRgb(0x3D, 0xD6, 0x8C);
			DrawSeries(valuesB, bColor, Color.FromArgb(0x3C, bColor.R, bColor.G, bColor.B));
		}

		return canvas;
	}

	/// <summary>Dual-layer CA/PA progress bar showing current ability and remaining potential headroom.</summary>
	public static FrameworkElement AbilityBar(int ca, int pa)
	{
		var stack = new StackPanel { Margin = new Thickness(12, 2, 12, 10) };
		int growth = Math.Max(0, pa - ca);
		var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
		var right = Build.Text(growth > 0 ? Build.T($"+{growth} potansiyel", $"+{growth} growth") : Build.T("Zirvede", "At peak"),
			11, growth > 0 ? Build.Res("Positive") : Build.Res("Faint"), FontWeights.SemiBold);
		DockPanel.SetDock(right, Dock.Right);
		top.Children.Add(right);
		top.Children.Add(Build.Text($"CA {ca} / PA {pa}", 11.5, Build.Res("Muted")));
		stack.Children.Add(top);

		var track = new Grid { Height = 7, ClipToBounds = true };
		track.Children.Add(new Border { Background = Build.Res("Field"), CornerRadius = new CornerRadius(3.5) });
		var paBar = new Border
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = Build.Res("AccentSoft"),
			CornerRadius = new CornerRadius(3.5)
		};
		var caBar = new Border
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = Colors.Attribute((int)Math.Round(ca / 10.0)),
			CornerRadius = new CornerRadius(3.5)
		};
		track.Children.Add(paBar);
		track.Children.Add(caBar);
		track.SizeChanged += (_, e) =>
		{
			double w = e.NewSize.Width;
			paBar.Width = w * Math.Clamp(pa / 200.0, 0, 1);
			caBar.Width = w * Math.Clamp(ca / 200.0, 0, 1);
		};
		stack.Children.Add(track);
		return stack;
	}
}

/// <summary>Two-player side-by-side comparison window with overlaid 8-axis radar and attribute delta table.</summary>
public sealed class PlayerCompareWindow : Window
{
	public PlayerCompareWindow(GameData data, PlayerRow a, PlayerRow b, INavigator nav)
	{
		Title = Build.T($"Karşılaştır: {a.Name} vs {b.Name}", $"Compare: {a.Name} vs {b.Name}");
		Width = 960;
		Height = 720;
		MinWidth = 820;
		MinHeight = 600;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

		byte[] rawA = new byte[69], rawB = new byte[69];
		data.Memory.TryRead(a.Address + 0x150, rawA);
		data.Memory.TryRead(b.Address + 0x150, rawB);
		double[] radA = PlayerVisuals.ComputeRadar8(rawA);
		double[] radB = PlayerVisuals.ComputeRadar8(rawB);

		var root = new DockPanel { Margin = new Thickness(16) };

		// Top player banners
		var banner = new Grid { Margin = new Thickness(0, 0, 0, 12) };
		banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
		banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		Border PlayerBox(PlayerRow p, Brush color)
		{
			var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			var av = Build.Avatar(ImageLookup.Graphics.Person(p.Uid), p.Name, 54, round: true);
			av.Margin = new Thickness(0, 0, 12, 0);
			sp.Children.Add(av);
			var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			var nameBtn = new Button { Content = p.Name, Style = Build.StyleOf("LinkButton"), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = color, HorizontalAlignment = HorizontalAlignment.Left };
			nameBtn.Click += (_, _) => { nav.OpenPlayer(p.Address); Close(); };
			info.Children.Add(nameBtn);
			info.Children.Add(Build.Text($"{p.Positions} · {p.Age} · {p.Club}", 12, Build.Res("Muted")));
			info.Children.Add(Build.Text($"CA {p.CA} / PA {p.PA} · {p.RoleDisplay} · {Money.Format(p.Value, false)}", 12, Build.Res("Text"), FontWeights.SemiBold));
			sp.Children.Add(info);
			return new Border
			{
				Background = Build.Res("Card"),
				BorderBrush = color,
				BorderThickness = new Thickness(0, 2, 0, 0),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(12, 10, 12, 10),
				Child = sp
			};
		}

		var boxA = PlayerBox(a, Build.Res("Accent"));
		var boxB = PlayerBox(b, Build.Res("Positive"));
		var radarHost = new Border
		{
			Background = Build.Res("Card"),
			BorderBrush = Build.Res("StrokeSoft"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Margin = new Thickness(10, 0, 10, 0),
			Child = PlayerVisuals.RadarChart(210, radA, radB)
		};
		Grid.SetColumn(radarHost, 1);
		Grid.SetColumn(boxB, 2);
		banner.Children.Add(boxA);
		banner.Children.Add(radarHost);
		banner.Children.Add(boxB);
		DockPanel.SetDock(banner, Dock.Top);
		root.Children.Add(banner);

		// Side-by-side attribute delta comparison across Technical, Mental, Physical
		string lang = Loc.Instance.Language;
		var groups = new[] { ("Technical", Build.T("Teknik", "Technical")), ("Mental", Build.T("Zihinsel", "Mental")), ("Physical", Build.T("Fiziksel", "Physical")) };
		var cols = new Grid();
		for (int i = 0; i < 3; i++) cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		for (int gi = 0; gi < groups.Length; gi++)
		{
			var (gKey, gTitle) = groups[gi];
			var rows = new StackPanel { Margin = new Thickness(8, 2, 8, 8) };
			var fields = data.PlayerDef.Fields.Where(f => f.GroupKey == gKey && f.Concat.Count == 0)
				.OrderBy(f => data.Layout.Text(lang, f.Name));
			foreach (var f in fields)
			{
				int va = int.TryParse(data.Read(a.Address, data.PlayerDef, f.Name), out int x1) ? x1 : 0;
				int vb = int.TryParse(data.Read(b.Address, data.PlayerDef, f.Name), out int x2) ? x2 : 0;
				int diff = va - vb;
				var g = new Grid { Height = 24 };
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
				g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
				g.Children.Add(Build.Text(data.Layout.Text(lang, f.Name), 12, Build.Res("Muted")));
				var ta = Build.Text(va.ToString(), 12.5, Colors.Attribute(va), FontWeights.Bold);
				ta.HorizontalAlignment = HorizontalAlignment.Center;
				Grid.SetColumn(ta, 1);
				var td = Build.Text(diff == 0 ? "—" : (diff > 0 ? $"+{diff}" : diff.ToString()), 11.5,
					diff > 0 ? Build.Res("Accent") : (diff < 0 ? Build.Res("Positive") : Build.Res("Faint")), FontWeights.SemiBold);
				td.HorizontalAlignment = HorizontalAlignment.Center;
				Grid.SetColumn(td, 2);
				var tb = Build.Text(vb.ToString(), 12.5, Colors.Attribute(vb), FontWeights.Bold);
				tb.HorizontalAlignment = HorizontalAlignment.Center;
				Grid.SetColumn(tb, 3);
				g.Children.Add(ta);
				g.Children.Add(td);
				g.Children.Add(tb);
				rows.Children.Add(g);
			}
			var card = Build.Card(gTitle, rows);
			Grid.SetColumn(card, gi);
			cols.Children.Add(card);
		}
		root.Children.Add(Build.Scroll(cols));
		Content = root;
	}
}

public sealed class ShortlistStarBrushConverter : System.Windows.Data.IValueConverter
{
	private static readonly Brush Gold = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x2E)));
	private static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x80)));

	public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
		value is true ? Gold : Dim;

	public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
		throw new NotSupportedException();

	private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
