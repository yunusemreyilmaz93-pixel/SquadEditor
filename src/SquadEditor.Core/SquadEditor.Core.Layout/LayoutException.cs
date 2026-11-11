using System;

namespace SquadEditor.Core.Layout;

public sealed class LayoutException : Exception
{
	public LayoutException(string message)
		: base(message)
	{
	}
}
