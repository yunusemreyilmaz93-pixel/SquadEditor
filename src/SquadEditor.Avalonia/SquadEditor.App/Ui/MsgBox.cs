using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SquadEditor.App.Ui;

/// <summary>Themed async confirmation &amp; alert dialog replacing Windows-only MessageBox.</summary>
public static class MsgBox
{
	public static async Task ShowAsync(Window? owner, string message, string title = "SquadEditor")
	{
		var dlg = CreateDialog(title, message, yesNo: false, out _);
		if (owner is { IsVisible: true })
		{
			await dlg.ShowDialog(owner);
		}
		else
		{
			var tcs = new TaskCompletionSource();
			dlg.Closed += (_, _) => tcs.TrySetResult();
			dlg.Show();
			await tcs.Task;
		}
	}

	public static async Task<bool> AskAsync(Window? owner, string message, string title = "SquadEditor")
	{
		var dlg = CreateDialog(title, message, yesNo: true, out var resultBox);
		if (owner is { IsVisible: true })
		{
			await dlg.ShowDialog(owner);
		}
		else
		{
			var tcs = new TaskCompletionSource();
			dlg.Closed += (_, _) => tcs.TrySetResult();
			dlg.Show();
			await tcs.Task;
		}
		return resultBox[0];
	}

	private static Window CreateDialog(string title, string message, bool yesNo, out bool[] resultHolder)
	{
		bool[] holder = { false };
		resultHolder = holder;
		var dlg = new Window
		{
			Title = title,
			Width = 440,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = Build.Font("UiFont")
		};
		WindowHelper.ApplyChrome(dlg);

		var txt = new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			FontSize = 13,
			Foreground = Build.Res("Text"),
			MaxWidth = 396
		};

		var btns = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 18, 0, 0)
		};

		if (yesNo)
		{
			var no = new Button
			{
				Content = Build.T("Vazgeç", "Cancel"),
				IsCancel = true,
				MinWidth = 88,
				Margin = new Thickness(0, 0, 8, 0)
			};
			no.Click += (_, _) => { holder[0] = false; dlg.Close(); };
			var yes = new Button
			{
				Content = Build.T("Evet", "Yes"),
				Theme = Build.StyleOf("AccentButton"),
				IsDefault = true,
				MinWidth = 88
			};
			yes.Click += (_, _) => { holder[0] = true; dlg.Close(); };
			btns.Children.Add(no);
			btns.Children.Add(yes);
		}
		else
		{
			var ok = new Button
			{
				Content = Build.T("Tamam", "OK"),
				Theme = Build.StyleOf("AccentButton"),
				IsDefault = true,
				IsCancel = true,
				MinWidth = 92
			};
			ok.Click += (_, _) => dlg.Close();
			btns.Children.Add(ok);
		}

		var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
		root.Children.Add(txt);
		root.Children.Add(btns);
		dlg.Content = root;
		return dlg;
	}
}
