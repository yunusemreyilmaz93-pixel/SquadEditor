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

	/// <summary>Interface zoom, 0.9 to 1.3.</summary>
	public double UiScale { get; set; } = 1.0;

	/// <summary>Denser rows in lists and cards.</summary>
	public bool Compact { get; set; }

	/// <summary>Accent colour as #RRGGBB.</summary>
	public string Accent { get; set; } = "#4F8CFF";

	/// <summary>Show attributes on the game's internal 1-100 scale instead of 1-20.</summary>
	public bool Attributes100 { get; set; }

	/// <summary>"cm" or "ftin".</summary>
	public string HeightUnit { get; set; } = "cm";

	/// <summary>.NET date format used everywhere.</summary>
	public string DateFormat { get; set; } = "dd.MM.yyyy";

	/// <summary>Role rating weight profile: "classic" (FM role weights) or "meta" (Match Engine meta weights).</summary>
	public string RoleWeightMode { get; set; } = "classic";

	/// <summary>Look only: nothing is written to the game.</summary>
	public bool ReadOnlyMode { get; set; }

	public bool ConfirmBulkEdits { get; set; } = true;

	public bool LiveRefresh { get; set; } = true;

	public int LiveRefreshMs { get; set; } = 1500;

	public bool AutoConnect { get; set; } = true;

	public bool RestoreTabs { get; set; } = true;

	/// <summary>Tabs open at the last exit, by unique ID ("player:29179241").</summary>
	public List<string> LastTabs { get; set; } = new List<string>();

	public string? LastActiveTab { get; set; }

	/// <summary>Frozen entities persisted across reloads and restarts ("player:29179241", "staff:1234").</summary>
	public List<string> FrozenUids { get; set; } = new List<string>();

	/// <summary>When true, frozen attributes may increase organically but never drop.</summary>
	public bool FreezeAllowIncrease { get; set; } = true;

	/// <summary>Most recently opened club UID for 1-click access from Home.</summary>
	public uint LastClubUid { get; set; }

	/// <summary>Optional Genie Scout columns enabled in PlayerGrid.</summary>
	public List<string> ExtraColumns { get; set; } = new List<string> { "Growth", "Role" };

	/// <summary>Starred / shortlisted player UIDs (legacy fallback).</summary>
	public List<uint> ShortlistUids { get; set; } = new List<uint>();

	/// <summary>Save-scoped frozen entities ("save:&lt;humanUid&gt;" -> ["player:123", ...]).</summary>
	public Dictionary<string, List<string>> FrozenBySave { get; set; } = new Dictionary<string, List<string>>();

	/// <summary>Save-scoped shortlisted player UIDs.</summary>
	public Dictionary<string, List<uint>> ShortlistBySave { get; set; } = new Dictionary<string, List<uint>>();

	/// <summary>User-saved custom scout filter presets.</summary>
	public List<SavedScoutPreset> CustomScoutPresets { get; set; } = new List<SavedScoutPreset>();

	public List<string> GetFrozen(string saveKey) => FrozenBySave.TryGetValue(saveKey, out var list) ? list : FrozenUids;

	public void SetFrozen(string saveKey, List<string> list)
	{
		FrozenBySave[saveKey] = list;
		FrozenUids = list;
	}

	public List<uint> GetShortlist(string saveKey) => ShortlistBySave.TryGetValue(saveKey, out var list) ? list : ShortlistUids;

	public void SetShortlist(string saveKey, List<uint> list)
	{
		ShortlistBySave[saveKey] = list;
		ShortlistUids = list;
	}

	private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SquadEditor", "settings.json");

	public static AppSettings Current { get; } = Load();

	public string EffectiveLanguage
	{
		get
		{
			string? text = Language;
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
			if (Path.GetDirectoryName(FilePath) is { } dir)
			{
				Directory.CreateDirectory(dir);
			}
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

public sealed class SavedScoutPreset
{
	public string Name { get; set; } = "";
	public string Pos { get; set; } = "";
	public string Region { get; set; } = "";
	public string Nation { get; set; } = "";
	public string AgeMin { get; set; } = "";
	public string AgeMax { get; set; } = "";
	public string CaMin { get; set; } = "";
	public string CaMax { get; set; } = "";
	public string PaMin { get; set; } = "";
	public string MaxValM { get; set; } = "";
	public string Query { get; set; } = "";
	public bool FreeOnly { get; set; }
	public bool ExpiringOnly { get; set; }
	public bool InjuredOnly { get; set; }
	public bool Wonderkid { get; set; }
	public bool RegenOnly { get; set; }
	public string? FilterNation { get; set; }
	public string? BestRole { get; set; }
	public int? MinGrowth { get; set; }
	public double? MinRating { get; set; }
	public double? MinPotentialRating { get; set; }
	public double? MaxWageDisplay { get; set; }
	public double? MaxAskingPriceM { get; set; }
	public bool BothFeetOnly { get; set; }
	public int? MinPro { get; set; }
	public int? MinCons { get; set; }
	public int? MinImp { get; set; }
	public int? MaxInj { get; set; }
	public int? MinDet { get; set; }
	public int? MinPace { get; set; }
	public int? MinFin { get; set; }
	public int? MinPas { get; set; }
}
