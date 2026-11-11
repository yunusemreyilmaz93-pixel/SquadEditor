using System;
using System.Windows.Input;

namespace SquadEditor.App;

internal sealed class RelayCommand(Action execute) : ICommand
{
	public event EventHandler? CanExecuteChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public bool CanExecute(object? parameter)
	{
		return true;
	}

	public void Execute(object? parameter)
	{
		execute();
	}
}
