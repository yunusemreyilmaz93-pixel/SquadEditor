using System;
using System.Globalization;
using SquadEditor.Core.Layout;

namespace SquadEditor.Core.Editing;

public readonly record struct PresetValue(PresetValueKind Kind, double Amount)
{
	public static bool TryParse(string text, out PresetValue value)
	{
		string text2 = text.Trim().ToLowerInvariant();
		value = default;
		if (!(text2 == "min"))
		{
			if (text2 == "max")
			{
				value = new PresetValue(PresetValueKind.Max, 0.0);
				return true;
			}
			bool flag = text2.StartsWith('+') || (text2.StartsWith('-') && text2.Length > 1);
			if (!double.TryParse(text2.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result))
			{
				return false;
			}
			value = new PresetValue(flag ? PresetValueKind.Relative : PresetValueKind.Exact, result);
			return true;
		}
		value = new PresetValue(PresetValueKind.Min, 0.0);
		return true;
	}

	public double Apply(double current, FieldDef field, double? min, double? max)
	{
		double num = Kind switch
		{
			PresetValueKind.Exact => Amount, 
			PresetValueKind.Relative => current + Amount, 
			PresetValueKind.Min => field.Min ?? current, 
			_ => field.Max ?? current, 
		};
		if (min.HasValue)
		{
			double valueOrDefault = min.GetValueOrDefault();
			num = Math.Max(num, valueOrDefault);
		}
		if (max.HasValue)
		{
			double valueOrDefault2 = max.GetValueOrDefault();
			num = Math.Min(num, valueOrDefault2);
		}
		double? min2 = field.Min;
		if (min2.HasValue)
		{
			double valueOrDefault3 = min2.GetValueOrDefault();
			num = Math.Max(num, valueOrDefault3);
		}
		min2 = field.Max;
		if (min2.HasValue)
		{
			double valueOrDefault4 = min2.GetValueOrDefault();
			num = Math.Min(num, valueOrDefault4);
		}
		if (!field.IsIntegerType)
		{
			return num;
		}
		return Math.Round(num);
	}
}
