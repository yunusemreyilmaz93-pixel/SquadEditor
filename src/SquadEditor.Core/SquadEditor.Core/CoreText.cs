using System.Collections.Generic;
using System.Globalization;

namespace SquadEditor.Core;

public static class CoreText
{
	private static readonly Dictionary<string, (string En, string Tr)> Messages = new Dictionary<string, (string, string)>
	{
		["ReadFailed"] = ("Could not access memory at 0x{0:X} ({1} bytes).", "Bellek okunamadı/yazılamadı: 0x{0:X} ({1} bayt)."),
		["NullPointer"] = ("Null pointer at 0x{0:X}.", "0x{0:X} adresindeki işaretçi boş."),
		["BadString"] = ("0x{0:X} is not a valid IL2CPP string (length {1}).", "0x{0:X} geçerli bir IL2CPP string değil (uzunluk {1})."),
		["Need64Bit"] = ("The editor must run as 64-bit (x64).", "Editör 64-bit (x64) çalışmalı."),
		["ProcessNotFound"] = ("Process '{0}' not found. Is the game running?", "'{0}' süreci bulunamadı. Oyun açık mı?"),
		["AttachFailed"] = ("Could not attach to '{0}' ({1}). Try running the editor as administrator.", "'{0}' sürecine bağlanılamadı ({1}). Editörü yönetici olarak çalıştırmayı dene."),
		["ReadOnly"] = ("'{0}' is read-only.", "'{0}' salt okunur."),
		["TextReadOnly"] = ("Text fields are read-only for now.", "Metin alanları şimdilik salt okunur."),
		["EnterBool"] = ("Enter true/false or 1/0.", "true/false veya 1/0 gir."),
		["EnterNumber"] = ("Enter a number.", "Sayı gir."),
		["EnterInteger"] = ("Enter a whole number.", "Tam sayı gir."),
		["EnterPositive"] = ("Enter a positive whole number.", "Pozitif tam sayı gir."),
		["TypeRange"] = ("Value must be within {1}..{2} for {0}.", "{0} için değer {1}..{2} aralığında olmalı."),
		["FieldRange"] = ("'{0}' must be within {1}..{2}.", "'{0}' {1}..{2} aralığında olmalı."),
		["InvalidOption"] = ("'{0}' is not a valid option for '{1}'.", "'{0}', '{1}' için geçerli bir seçenek değil."),
		["AnchorNotFound"] = ("Signature for '{0}' not found. The game may have been updated.", "'{0}' imzası bulunamadı. Oyun güncellenmiş olabilir."),
		["AnchorAmbiguous"] = ("Signature for '{0}' matched more than once. Make it longer.", "'{0}' imzası birden fazla yerde eşleşti. İmzayı uzat."),
		["ModuleNotFound"] = ("Module '{0}' not found.", "Modül bulunamadı: '{0}'."),
		["BadCount"] = ("Collection '{0}' reports {1} entries (expected 0..{2}). The offsets are probably wrong.", "'{0}' koleksiyonu {1} kayıt gösteriyor (beklenen 0..{2}). Offset'ler büyük ihtimalle yanlış.")
	};

	public static string Language { get; set; } = "en";

	public static System.Func<string, string?>? ExternalLookup { get; set; }

	public static CultureInfo Culture => Language switch
	{
		"tr" => CultureInfo.GetCultureInfo("tr-TR"),
		"es" => CultureInfo.GetCultureInfo("es-ES"),
		"pt" => CultureInfo.GetCultureInfo("pt-BR"),
		"fr" => CultureInfo.GetCultureInfo("fr-FR"),
		"de" => CultureInfo.GetCultureInfo("de-DE"),
		"it" => CultureInfo.GetCultureInfo("it-IT"),
		"nl" => CultureInfo.GetCultureInfo("nl-NL"),
		"pl" => CultureInfo.GetCultureInfo("pl-PL"),
		"ru" => CultureInfo.GetCultureInfo("ru-RU"),
		"ja" => CultureInfo.GetCultureInfo("ja-JP"),
		"ko" => CultureInfo.GetCultureInfo("ko-KR"),
		_ => CultureInfo.GetCultureInfo("en-GB")
	};

	public static string Get(string key, params object[] args)
	{
		var (en, tr) = Messages[key];
		string fmt = Language switch
		{
			"tr" => tr,
			"en" => en,
			_ => ExternalLookup?.Invoke("Core." + key) ?? en
		};
		return string.Format(CultureInfo.InvariantCulture, fmt, args);
	}
}
