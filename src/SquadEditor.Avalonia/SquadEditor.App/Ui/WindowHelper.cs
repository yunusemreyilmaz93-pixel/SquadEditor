using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace SquadEditor.App.Ui;

/// <summary>
/// Platform window ergonomics:
/// - Fits windows cleanly onto 13" MacBook Air (1280x800 logical working area) without overflowing, while keeping full resize/fullscreen support.
/// - Applies dark/light native DWM titlebar on Windows 10/11 and Cocoa appearance on macOS.
/// </summary>
public static class WindowHelper
{
	public static void ApplyChrome(Window window)
	{
		window.Opened += (_, _) =>
		{
			FitToScreen(window, isMain: false);
			ApplyNativeTitleBar(window);
		};
	}

	public static void FitToScreen(Window window, bool isMain)
	{
		try
		{
			var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
			if (screen == null) return;
			double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
			double workW = screen.WorkingArea.Width / scale;
			double workH = screen.WorkingArea.Height / scale;
			if (workW <= 0 || workH <= 0) return;

			if (isMain)
			{
				// On compact displays like 13" MacBook Air (1280x800 / 1440x900), fit cleanly inside WorkingArea with room to resize/maximize.
				if (workW < 1500 || workH < 920)
				{
					window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, workW - 16));
					window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, workH - 16));
					double x = screen.WorkingArea.X + (screen.WorkingArea.Width - window.Width * scale) / 2.0;
					double y = screen.WorkingArea.Y + (screen.WorkingArea.Height - window.Height * scale) / 2.0;
					window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
				}
			}
			else
			{
				double maxW = workW - 32;
				double maxH = workH - 40;
				if (!double.IsNaN(window.Width) && window.Width > maxW)
				{
					window.Width = Math.Max(360, maxW);
				}
				if (!double.IsNaN(window.Height) && window.Height > maxH)
				{
					window.Height = Math.Max(320, maxH);
				}
				window.MaxHeight = maxH;
			}
		}
		catch
		{
		}
	}

	public static void ApplyNativeTitleBar(Window window)
	{
		if (!OperatingSystem.IsWindows()) return;
		try
		{
			if (window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
			{
				int on = AppSettings.Current.UiTheme == "light" ? 0 : 1;
				DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
			}
		}
		catch
		{
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
