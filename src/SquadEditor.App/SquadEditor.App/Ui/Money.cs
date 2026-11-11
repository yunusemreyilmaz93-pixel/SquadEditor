using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace SquadEditor.App.Ui;

/// <summary>
/// The game keeps money in pounds and wages per week. The user picks how to see them:
/// currency (with an exchange rate) and wage period (week, month, year).
/// </summary>
public static class Money
{
	public sealed record CurrencyInfo(string Code, string Symbol, double DefaultRate, bool SymbolAfter);

	/// <summary>Rates per 1 GBP. EUR matches FM27's own rate; others are sensible defaults the user can change.</summary>
	public static readonly IReadOnlyList<CurrencyInfo> Currencies = new[]
	{
		new CurrencyInfo("GBP", "£", 1.0, false),
		new CurrencyInfo("EUR", "€", 1.1641, false),
		new CurrencyInfo("USD", "$", 1.35, false),
		new CurrencyInfo("TRY", "₺", 64.8, true),
	};

	public static CurrencyInfo Current => Currencies.FirstOrDefaultCode(AppSettings.Current.Currency) ?? Currencies[1];

	public static double Rate
	{
		get
		{
			CurrencyInfo c = Current;
			return AppSettings.Current.Rates != null && AppSettings.Current.Rates.TryGetValue(c.Code, out double r) && r > 0 ? r : c.DefaultRate;
		}
	}

	/// <summary>Weeks per chosen wage period.</summary>
	public static double WageFactor => AppSettings.Current.WagePeriod switch
	{
		"month" => 52.0 / 12.0,
		"year" => 52.0,
		_ => 1.0
	};

	public static string WageSuffix => AppSettings.Current.WagePeriod switch
	{
		"month" => Build.T("/ay", "/month"),
		"year" => Build.T("/yıl", "/year"),
		_ => Build.T("/hf", "/week")
	};

	public static bool IsWage(string fieldName) => fieldName.Contains("Wage", StringComparison.Ordinal);

	/// <summary>Game value (GBP, weekly for wages) to the shown number.</summary>
	public static double ToDisplay(double gbp, bool wage) => gbp * Rate * (wage ? WageFactor : 1.0);

	public static double FromDisplay(double shown, bool wage) => shown / Rate / (wage ? WageFactor : 1.0);

	public static string Format(double gbp, bool wage)
	{
		double v = ToDisplay(gbp, wage);
		bool tr = Loc.Instance.Language == "tr";
		CultureInfo culture = CultureInfo.GetCultureInfo(tr ? "tr-TR" : "en-GB");
		double a = Math.Abs(v);
		string number = a >= 1e9 ? (v / 1e9).ToString("0.##", culture) + (tr ? " mr" : "bn")
			: a >= 1e6 ? (v / 1e6).ToString("0.##", culture) + (tr ? " mn" : "m")
			: a >= 1e4 ? (v / 1e3).ToString("0.#", culture) + (tr ? " bin" : "k")
			: v.ToString("#,0", culture);
		CurrencyInfo c = Current;
		string text = c.SymbolAfter ? number + " " + c.Symbol : c.Symbol + number;
		return wage ? text + WageSuffix : text;
	}

	private static CurrencyInfo? FirstOrDefaultCode(this IReadOnlyList<CurrencyInfo> list, string? code)
	{
		foreach (CurrencyInfo c in list)
		{
			if (c.Code == code)
			{
				return c;
			}
		}
		return null;
	}
}

/// <summary>Grid binding: GBP number to the user's currency. ConverterParameter "wage" for weekly wages.</summary>
public sealed class MoneyText : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		double v = value switch { long l => l, int i => i, double d => d, uint u => u, _ => 0 };
		return v == 0 ? "" : Money.Format(v, (parameter as string) == "wage");
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
