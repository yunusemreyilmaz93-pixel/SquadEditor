using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadEditor.App.Ui;

/// <summary>Layouts built into the exe, overridable by files in a 'layouts' folder next to it, filtered by OS platform.</summary>
public static class Layouts
{
	private const string Prefix = "SquadEditor.App.Layouts.";

	public static List<LayoutFile> All()
	{
		bool isMac = OperatingSystem.IsMacOS();
		bool MatchesOs(string layoutName)
		{
			bool macLayout = layoutName.EndsWith("-mac", StringComparison.OrdinalIgnoreCase);
			return isMac ? macLayout : !macLayout;
		}

		var byName = new Dictionary<string, LayoutFile>(StringComparer.OrdinalIgnoreCase);
		System.Reflection.Assembly assembly = typeof(Layouts).Assembly;
		foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
		{
			string name = resource.Substring(Prefix.Length, resource.Length - Prefix.Length - 5);
			if (!MatchesOs(name))
			{
				continue;
			}
			using Stream stream = assembly.GetManifestResourceStream(resource)!;
			using var reader = new StreamReader(stream);
			byName[name] = new LayoutFile(name, null, reader.ReadToEnd());
		}
		string folder = Path.Combine(AppContext.BaseDirectory, "layouts");
		if (Directory.Exists(folder))
		{
			foreach (string file in Directory.GetFiles(folder, "*.json"))
			{
				string name = Path.GetFileNameWithoutExtension(file);
				if (MatchesOs(name))
				{
					byName[name] = new LayoutFile(name, file);
				}
			}
		}
		return byName.Values.OrderByDescending(l => l.Name).ToList();
	}
}
