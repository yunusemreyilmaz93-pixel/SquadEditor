using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace SquadEditor.App;

public partial class App : Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		Loc.Instance.SetLanguage(AppSettings.Current.EffectiveLanguage);
		Dispatcher.UIThread.UnhandledException += OnUnhandledException;

		var theme = new Ui.Theme(AppSettings.Current.UiTheme);
		Resources.MergedDictionaries.Clear();
		Resources.MergedDictionaries.Add(theme);
		RequestedThemeVariant = AppSettings.Current.UiTheme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
			desktop.MainWindow = new Ui.EditorWindow();
		}

		base.OnFrameworkInitializationCompleted();
	}

	private async void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		e.Handled = true;
		var owner = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
		await Ui.MsgBox.ShowAsync(owner, e.Exception.ToString(), "SquadEditor");
	}
}
