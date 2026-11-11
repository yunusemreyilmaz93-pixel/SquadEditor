using System;
using System.Globalization;
using SquadEditor.Core.Layout;

namespace SquadEditor.Core.Editing;

public static class DisplayFormat
{
	public static string Format(FieldDef field, string raw, string? language = null)
	{
		if (field.IsString || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return raw;
		}
		bool flag = (language ?? CoreText.Language) == "tr";
		CultureInfo cultureInfo = (flag ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.GetCultureInfo("en-GB"));
		return field.Format switch
		{
			FieldFormat.Money => Money(result, language, field.Currency), 
			FieldFormat.Percent => flag ? ("%" + result.ToString("0.#", cultureInfo)) : (result.ToString("0.#", cultureInfo) + "%"), 
			FieldFormat.Number => result.ToString("#,0.##", cultureInfo), 
			_ => raw, 
		};
	}

	public static string Money(double value, string? language = null, string currency = "€")
	{
		bool flag = (language ?? CoreText.Language) == "tr";
		CultureInfo cultureInfo = (flag ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.GetCultureInfo("en-GB"));
		string text = ((value < 0.0) ? "-" : "");
		double num = Math.Abs(value);
		string text2;
		if (num >= 1000000000.0)
		{
			text2 = (num / 1000000000.0).ToString("0.##", cultureInfo) + (flag ? " mr" : "B");
		}
		else if (num >= 1000000.0)
		{
			text2 = (num / 1000000.0).ToString("0.##", cultureInfo) + (flag ? " mn" : "M");
		}
		else
		{
			text2 = ((!(num >= 10000.0)) ? num.ToString("#,0", cultureInfo) : ((num / 1000.0).ToString("0.#", cultureInfo) + (flag ? " bin" : "K")));
		}
		string text3 = text2;
		return text + currency + text3;
	}
}
