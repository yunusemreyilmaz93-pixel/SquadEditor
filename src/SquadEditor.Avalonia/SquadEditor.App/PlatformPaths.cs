using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Input;

namespace SquadEditor.App;

/// <summary>
/// Cross-platform paths, folder/URL launchers, and keyboard shortcut helpers for Windows x64 and macOS ARM64.
/// Handles macOS elevated launch via osascript (preserving the real user's home directory via SQUADEDITOR_USER_HOME).
/// </summary>
public static class PlatformPaths
{
	public static bool IsMac => OperatingSystem.IsMacOS();

	public static KeyModifiers ActionModifier => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

	public static string ModLabel => IsMac ? "⌘" : "Ctrl+";

	public static string UndoHint => IsMac ? "⌘Z" : "Ctrl+Z";

	public static string SearchHint => IsMac ? "⌘F" : "Ctrl+F";

	public static string PaletteHint => IsMac ? "⌘K" : "Ctrl+K";

	public static string CloseTabHint => IsMac ? "⌘W" : "Ctrl+W";

	/// <summary>Returns the real user's home directory even when launched with administrator privileges on macOS.</summary>
	public static string UserHome
	{
		get
		{
			if (IsMac)
			{
				string? fromLauncher = Environment.GetEnvironmentVariable("SQUADEDITOR_USER_HOME");
				if (!string.IsNullOrWhiteSpace(fromLauncher) && Directory.Exists(fromLauncher))
				{
					return fromLauncher;
				}
				string? sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
				if (!string.IsNullOrWhiteSpace(sudoUser) && sudoUser != "root")
				{
					string candidate = Path.Combine("/Users", sudoUser);
					if (Directory.Exists(candidate))
					{
						return candidate;
					}
				}
			}
			return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
	}

	public static string AppDataDir
	{
		get
		{
			if (IsMac)
			{
				return Path.Combine(UserHome, "Library", "Application Support", "SquadEditor");
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SquadEditor");
		}
	}

	public static string LocalAppDataDir
	{
		get
		{
			if (IsMac)
			{
				return AppDataDir;
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquadEditor");
		}
	}

	/// <summary>Candidate Sports Interactive root directories on macOS and Windows.</summary>
	public static IEnumerable<string> SportsInteractiveRoots()
	{
		if (IsMac)
		{
			string appSupport = Path.Combine(UserHome, "Library", "Application Support", "Sports Interactive");
			if (Directory.Exists(appSupport)) yield return appSupport;
			string docs = Path.Combine(UserHome, "Documents", "Sports Interactive");
			if (Directory.Exists(docs)) yield return docs;
		}
		else
		{
			string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive");
			if (Directory.Exists(docs)) yield return docs;
		}
	}

	public static string PrimaryFm27Folder()
	{
		foreach (string si in SportsInteractiveRoots())
		{
			string? fm27 = Directory.GetDirectories(si, "Football Manager*")
				.OrderByDescending(d => d.Contains("27", StringComparison.Ordinal) ? 1 : 0)
				.ThenByDescending(d => d)
				.FirstOrDefault();
			if (fm27 != null) return fm27;
		}
		return IsMac
			? Path.Combine(UserHome, "Library", "Application Support", "Sports Interactive", "Football Manager 27")
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 27");
	}

	public static void OpenFolder(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			if (IsMac)
			{
				Process.Start(new ProcessStartInfo("/usr/bin/open", $"\"{path}\"") { UseShellExecute = false });
			}
			else
			{
				Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
			}
		}
		catch
		{
		}
	}

	public static void OpenUrl(string url)
	{
		try
		{
			if (IsMac)
			{
				Process.Start(new ProcessStartInfo("/usr/bin/open", $"\"{url}\"") { UseShellExecute = false });
			}
			else
			{
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			}
		}
		catch
		{
		}
	}
}
