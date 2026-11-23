using System.Windows;
using System.Windows.Threading;

namespace SquadEditor.App;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		Loc.Instance.SetLanguage(AppSettings.Current.EffectiveLanguage);
		DispatcherUnhandledException += OnUnhandledException;
		base.OnStartup(e);
		// The editor uses its own theme; the resources of the old window are not needed.
		Resources = new Ui.Theme(AppSettings.Current.UiTheme);
		new Ui.EditorWindow().Show();
	}

	private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		MessageBox.Show(e.Exception.ToString(), "Beklenmeyen hata", MessageBoxButton.OK, MessageBoxImage.Hand);
		e.Handled = true;
	}
}
