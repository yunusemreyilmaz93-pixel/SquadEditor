namespace SquadEditor.App;

/// <summary>A layout the editor can connect with: built into the exe (<see cref="Json"/>) or a file next to it (<see cref="Path"/>).</summary>
public sealed record LayoutFile(string Name, string? Path, string? Json = null)
{
	public override string ToString()
	{
		return Name;
	}
}
