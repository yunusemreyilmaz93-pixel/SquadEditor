using System;
using Avalonia;

namespace SquadEditor.App;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		var builder = AppBuilder.Configure<App>();
		builder = OperatingSystem.IsMacOS()
			? builder.UseAvaloniaNative()
			: builder.UseWin32();
		return builder
			.UseSkia()
			.With(new MacOSPlatformOptions { ShowInDock = true })
			.LogToTrace();
	}
}
