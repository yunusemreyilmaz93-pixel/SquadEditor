using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SquadEditor.App;

public sealed class AppSettings
{
	public string Theme { get; set; } = "light";

	public string? Language { get; set; }

	/// <summary>Graphics folder chosen by the user; empty means Documents\Sports Interactive\...\graphics.</summary>
	public string? GraphicsFolder { get; set; }

	/// <summary>"dark" or "light".</summary>
	public string UiTheme { get; set; } = "dark";

	/// <summary>GBP, EUR, USD or TRY.</summary>
	public string Currency { get; set; } = "EUR";

	/// <summary>Custom exchange rates per 1 GBP, by currency code.</summary>
	public Dictionary<string, double>? Rates { get; set; }

	/// <summary>"week", "month" or "year".</summary>
	public string WagePeriod { get; set; } = "year";

	private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SquadEditor", "settings.json");

	public static AppSettings Current { get; } = Load();

	public string EffectiveLanguage
	{
		get
		{
			string text = Language;
			if (text == null)
			{
				if (!(CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "tr"))
				{
					return "en";
				}
				text = "tr";
			}
			return text;
		}
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
		{
		}
	}

	private static AppSettings Load()
	{
		try
		{
			return File.Exists(FilePath) ? (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()) : new AppSettings();
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) ? true : false)
		{
			return new AppSettings();
		}
	}
}
