using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadEditor.Core.Layout;

internal sealed class HexInt64Converter : JsonConverter<long>
{
	public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Number)
		{
			return reader.GetInt64();
		}
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException("Expected a number or a hex string like \"0x1A4\".");
		}
		string text = (reader.GetString() ?? "").Trim();
		bool flag = text.StartsWith('-');
		if (flag)
		{
			text = text.Substring(1);
		}
		if (!((!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) ? long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var result) : long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result)))
		{
			throw new JsonException("Invalid number \"" + reader.GetString() + "\".");
		}
		if (!flag)
		{
			return result;
		}
		return -result;
	}

	public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
	{
		writer.WriteStringValue((value < 0) ? $"-0x{-value:X}" : $"0x{value:X}");
	}
}
