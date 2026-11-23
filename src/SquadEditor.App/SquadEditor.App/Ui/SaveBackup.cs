using System;
using System.IO;
using System.Linq;

namespace SquadEditor.App.Ui;

/// <summary>One-click backup of the most recently modified FM save file into games\SquadEditor_Backups.</summary>
public static class SaveBackup
{
	public static string? FindGamesFolder()
	{
		string si = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive");
		if (!Directory.Exists(si))
		{
			return null;
		}
		return Directory.GetDirectories(si, "Football Manager*")
			.OrderByDescending(d => d.Contains("27") ? 1 : 0).ThenByDescending(d => d)
			.Select(d => Path.Combine(d, "games"))
			.FirstOrDefault(Directory.Exists);
	}

	public static FileInfo? FindLatestSave()
	{
		string? dir = FindGamesFolder();
		if (dir == null)
		{
			return null;
		}
		try
		{
			return new DirectoryInfo(dir)
				.EnumerateFiles("*.fm", SearchOption.TopDirectoryOnly)
				.OrderByDescending(f => f.LastWriteTimeUtc)
				.FirstOrDefault();
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static string Create(FileInfo save)
	{
		string backupDir = Path.Combine(save.DirectoryName!, "SquadEditor_Backups");
		Directory.CreateDirectory(backupDir);
		string stem = Path.GetFileNameWithoutExtension(save.Name);
		string dest = Path.Combine(backupDir, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}.fm");
		File.Copy(save.FullName, dest, overwrite: true);
		return dest;
	}
}
