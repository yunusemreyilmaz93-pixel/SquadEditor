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

	private Dictionary<string, string> _fallback = new Dictionary<string, string>();

	public static IReadOnlyList<(string Code, string Name)> Languages { get; } = new (string, string)[2]
	{
		("en", "English"),
		("tr", "Türkçe")
	};

	public string Language { get; private set; } = "en";

	public string this[string key]
	{
		get
		{
			if (!_strings.TryGetValue(key, out string value))
			{
				return _fallback.GetValueOrDefault(key, key);
			}
			return value;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public static event EventHandler? LanguageChanged;

	private Loc()
	{
		_fallback = LoadFile("en");
		_strings = _fallback;
	}

	public static string T(string key)
	{
		return Instance[key];
	}

	public static string T(string key, params object[] args)
	{
		return string.Format(CultureInfo.CurrentCulture, Instance[key], args);
	}

	public void SetLanguage(string code)
	{
		if (Languages.All(((string Code, string Name) l) => l.Code != code))
		{
			code = "en";
		}
		Language = code;
		_strings = ((code == "en") ? _fallback : LoadFile(code));
		CoreText.Language = code;
		CultureInfo.CurrentCulture = (CultureInfo.CurrentUICulture = CoreText.Culture);
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
		LanguageChanged?.Invoke(this, EventArgs.Empty);
	}

	private static Dictionary<string, string> LoadFile(string code)
	{
		using Stream stream = typeof(Loc).Assembly.GetManifestResourceStream("SquadEditor.App.Lang." + code + ".json") ?? throw new InvalidOperationException("Language file '" + code + "' is missing.");
		using StreamReader streamReader = new StreamReader(stream);
		return JsonSerializer.Deserialize<Dictionary<string, string>>(streamReader.ReadToEnd()) ?? new Dictionary<string, string>();
	}
}
