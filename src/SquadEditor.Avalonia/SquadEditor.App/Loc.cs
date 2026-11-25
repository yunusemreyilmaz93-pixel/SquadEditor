using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SquadEditor.Core;

namespace SquadEditor.App;

public sealed class Loc : INotifyPropertyChanged
{
	public static readonly Loc Instance = new Loc();

	private Dictionary<string, string> _strings = new Dictionary<string, string>();

	private readonly Dictionary<string, string> _fallback;

	public static IReadOnlyList<(string Code, string Name)> Languages { get; } = new (string, string)[]
	{
		("en", "English"),
		("tr", "Türkçe"),
		("es", "Español"),
		("pt", "Português"),
		("fr", "Français"),
		("de", "Deutsch"),
		("it", "Italiano"),
		("nl", "Nederlands"),
		("pl", "Polski"),
		("ru", "Русский"),
		("ja", "日本語"),
		("ko", "한국어")
	};

	public string Language { get; private set; } = "en";

	public string this[string key] => _strings.TryGetValue(key, out string? value) ? value : _fallback.GetValueOrDefault(key, key);

	public string UiText(string en) => _strings.TryGetValue(en, out string? value) && value.Length > 0 ? value : en;

	public event PropertyChangedEventHandler? PropertyChanged;

	public static event EventHandler? LanguageChanged;

	private Loc()
	{
		_fallback = LoadFile("en");
		_strings = _fallback;
		CoreText.ExternalLookup = key => _strings.TryGetValue(key, out string? v) && v.Length > 0 ? v : null;
	}

	public static string T(string key) => Instance[key];

	public static string T(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Instance[key], args);

	public void SetLanguage(string code)
	{
		if (Languages.All(l => l.Code != code))
		{
			code = "en";
		}
		Language = code;
		_strings = code == "en" ? _fallback : LoadFile(code);
		CoreText.Language = code;
		CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CoreText.Culture;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
		LanguageChanged?.Invoke(this, EventArgs.Empty);
	}

	private static Dictionary<string, string> LoadFile(string code)
	{
		using Stream stream = typeof(Loc).Assembly.GetManifestResourceStream("SquadEditor.App.Lang." + code + ".json")
			?? throw new InvalidOperationException("Language file '" + code + "' is missing.");
		using var streamReader = new StreamReader(stream);
		return JsonSerializer.Deserialize<Dictionary<string, string>>(streamReader.ReadToEnd()) ?? new Dictionary<string, string>();
	}
}
