using System;
using System.Globalization;
using System.Windows.Data;

namespace SquadEditor.App.Ui;

/// <summary>Dates come from the game as dd.MM.yyyy and are shown in the user's format.</summary>
public static class Dates
{
	public static readonly string[] Formats = { "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };

	public static string Show(string gameDate)
	{
		return DateTime.TryParseExact(gameDate, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d)
			? d.ToString(AppSettings.Current.DateFormat, CultureInfo.InvariantCulture)
			: gameDate;
	}

	/// <summary>User text in their format back to the game's dd.MM.yyyy.</summary>
	public static string? ToGame(string text)
	{
		return DateTime.TryParseExact(text, AppSettings.Current.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d)
			|| DateTime.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
			? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
			: null;
	}
}

/// <summary>Grid binding for game dates.</summary>
public sealed class DateText : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Dates.Show(value as string ?? "");

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
