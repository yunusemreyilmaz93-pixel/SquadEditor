using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Editing;

public static class FieldCodec
{
	private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

	public static ulong ResolveAddress(IMemory memory, ulong entity, IReadOnlyList<long> path)
	{
		ulong address = entity;
		for (int i = 0; i < path.Count - 1; i++)
		{
			address = memory.ReadNonNullPointer(MemoryAccess.AddOffset(address, path[i]));
		}
		return MemoryAccess.AddOffset(address, path[path.Count - 1]);
	}

	public static int SizeOf(FieldType type)
	{
		switch (type)
		{
		case FieldType.U8:
		case FieldType.I8:
		case FieldType.Bool:
			return 1;
		case FieldType.U16:
		case FieldType.I16:
			return 2;
		case FieldType.U32:
		case FieldType.I32:
		case FieldType.F32:
		case FieldType.DayYear:
			return 4;
		default:
			return 8;
		}
	}

	public static string Read(IMemory memory, ulong entity, FieldDef field)
	{
		ulong address = ResolveAddress(memory, entity, field.Path);
		return field.Type switch
		{
			FieldType.Utf16Ptr => ReadUtf16(memory, memory.ReadPointer(address), field.MaxLength), 
			FieldType.Utf8Ptr => ReadUtf8(memory, memory.ReadPointer(address), field.MaxLength), 
			FieldType.Il2CppString => ReadIl2CppString(memory, memory.ReadPointer(address), field.MaxLength), 
			FieldType.LenUtf8Ptr => ReadLenUtf8(memory, memory.ReadPointer(address), field.MaxLength), 
			_ => Display(field, memory.ReadBytes(address, SizeOf(field.Type))), 
		};
	}

	/// <summary>Formats raw bytes as the user sees them (applies <see cref="FieldDef.Scale"/>).</summary>
	public static string Display(FieldDef field, ReadOnlySpan<byte> raw)
	{
		string text = Format(field.Type, raw);
		if (!field.Scale.HasValue)
		{
			return text;
		}
		double num = double.Parse(text, Inv) / field.Scale.Value;
		return (field.IsIntegerType ? Math.Round(num, MidpointRounding.AwayFromZero) : num).ToString(Inv);
	}

	public static string Format(FieldType type, ReadOnlySpan<byte> raw)
	{
		return type switch
		{
			FieldType.U8 => raw[0].ToString(Inv), 
			FieldType.I8 => ((sbyte)raw[0]).ToString(Inv), 
			FieldType.Bool => (raw[0] != 0) ? "true" : "false", 
			FieldType.U16 => BinaryPrimitives.ReadUInt16LittleEndian(raw).ToString(Inv), 
			FieldType.I16 => BinaryPrimitives.ReadInt16LittleEndian(raw).ToString(Inv), 
			FieldType.U32 => BinaryPrimitives.ReadUInt32LittleEndian(raw).ToString(Inv), 
			FieldType.I32 => BinaryPrimitives.ReadInt32LittleEndian(raw).ToString(Inv), 
			FieldType.U64 => BinaryPrimitives.ReadUInt64LittleEndian(raw).ToString(Inv), 
			FieldType.I64 => BinaryPrimitives.ReadInt64LittleEndian(raw).ToString(Inv), 
			FieldType.F32 => BinaryPrimitives.ReadSingleLittleEndian(raw).ToString(Inv), 
			FieldType.F64 => BinaryPrimitives.ReadDoubleLittleEndian(raw).ToString(Inv), 
			FieldType.DayYear => FormatDayYear(raw), 
			_ => throw new NotSupportedException($"{type} cannot be formatted from raw bytes."), 
		};
	}

	public static byte[] Encode(FieldDef field, string input)
	{
		if (field.IsString)
		{
			throw new NotSupportedException(CoreText.Get("TextReadOnly"));
		}
		string text = input.Trim();
		if (field.Scale.HasValue)
		{
			double num0 = ParseDouble(text);
			CheckRange(field, num0);
			double num1 = num0 * field.Scale.Value;
			text = (field.IsIntegerType ? Math.Round(num1, MidpointRounding.AwayFromZero) : num1).ToString(Inv);
			field = field.WithoutScale();
		}
		byte[] array = new byte[SizeOf(field.Type)];
		if (field.Type == FieldType.DayYear)
		{
			DateTime date = ParseDate(text);
			BinaryPrimitives.WriteUInt16LittleEndian(array, (ushort)date.DayOfYear);
			BinaryPrimitives.WriteUInt16LittleEndian(array.AsSpan(2), (ushort)date.Year);
			return array;
		}
		switch (field.Type)
		{
		case FieldType.Bool:
		{
			byte[] array2 = array;
			byte b;
			switch (text.ToLowerInvariant())
			{
			case "true":
			case "1":
				b = 1;
				break;
			case "false":
			case "0":
				b = 0;
				break;
			default:
				throw new FormatException(CoreText.Get("EnterBool"));
			}
			array2[0] = b;
			return array;
		}
		case FieldType.F32:
		case FieldType.F64:
		{
			double num3 = ParseDouble(text);
			CheckRange(field, num3);
			if (field.Type == FieldType.F32)
			{
				if ((num3 > 3.4028234663852886E+38 || num3 < -3.4028234663852886E+38) ? true : false)
				{
					throw new FormatException(CoreText.Get("TypeRange", "F32", float.MinValue, float.MaxValue));
				}
				BinaryPrimitives.WriteSingleLittleEndian(array, (float)num3);
			}
			else
			{
				BinaryPrimitives.WriteDoubleLittleEndian(array, num3);
			}
			return array;
		}
		case FieldType.U64:
		{
			if (!ulong.TryParse(text, NumberStyles.None, Inv, out var result2))
			{
				throw new FormatException(CoreText.Get("EnterPositive"));
			}
			CheckRange(field, result2);
			BinaryPrimitives.WriteUInt64LittleEndian(array, result2);
			return array;
		}
		default:
		{
			if (!long.TryParse(text, NumberStyles.AllowLeadingSign, Inv, out var result))
			{
				throw new FormatException(CoreText.Get("EnterInteger"));
			}
			var (num, num2) = IntegerBounds(field.Type);
			if (result < num || result > num2)
			{
				throw new FormatException(CoreText.Get("TypeRange", field.Type, num, num2));
			}
			CheckRange(field, result);
			if (field.Options.Count > 0 && !field.Options.ContainsKey(result.ToString(Inv)))
			{
				throw new FormatException(CoreText.Get("InvalidOption", result, field.Name));
			}
			Span<byte> destination = stackalloc byte[8];
			BinaryPrimitives.WriteInt64LittleEndian(destination, result);
			destination.Slice(0, array.Length).CopyTo(array);
			return array;
		}
		}
	}

	/// <summary>Game dates: 1-based day of year in the low 9 bits (upper bits are flags), then the year as u16. Year 1900 means empty.</summary>
	private static string FormatDayYear(ReadOnlySpan<byte> raw)
	{
		int day = BinaryPrimitives.ReadUInt16LittleEndian(raw) & 0x1FF;
		int year = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(2));
		if (year <= 1900 || year > 9999)
		{
			return "";
		}
		return new DateTime(year, 1, 1).AddDays(Math.Max(day, 1) - 1).ToString("dd.MM.yyyy", Inv);
	}

	private static DateTime ParseDate(string text)
	{
		string[] formats = new string[4] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy" };
		if (!DateTime.TryParseExact(text, formats, Inv, DateTimeStyles.None, out var result))
		{
			throw new FormatException("dd.MM.yyyy");
		}
		return result;
	}

	private static (long Min, long Max) IntegerBounds(FieldType type)
	{
		return type switch
		{
			FieldType.U8 => (Min: 0L, Max: 255L), 
			FieldType.I8 => (Min: -128L, Max: 127L), 
			FieldType.U16 => (Min: 0L, Max: 65535L), 
			FieldType.I16 => (Min: -32768L, Max: 32767L), 
			FieldType.U32 => (Min: 0L, Max: 4294967295L), 
			FieldType.I32 => (Min: -2147483648L, Max: 2147483647L), 
			FieldType.I64 => (Min: long.MinValue, Max: long.MaxValue), 
			_ => throw new NotSupportedException(type.ToString()), 
		};
	}

	private static double ParseDouble(string text)
	{
		if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, Inv, out var result) || !double.IsFinite(result))
		{
			throw new FormatException(CoreText.Get("EnterNumber"));
		}
		return result;
	}

	private static void CheckRange(FieldDef field, double value)
	{
		if (value < field.Min || value > field.Max)
		{
			throw new FormatException(CoreText.Get("FieldRange", Describe(field.Min), Describe(field.Max)));
		}
	}

	private static string Describe(double? bound)
	{
		return bound?.ToString(Inv) ?? "…";
	}

	private static string ReadUtf16(IMemory memory, ulong pointer, int maxLength)
	{
		if (pointer == 0L)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		byte[] array = new byte[128];
		byte[] array2 = new byte[2];
		while (stringBuilder.Length < maxLength)
		{
			ulong address = pointer + (ulong)(stringBuilder.Length * 2);
			byte[] array3 = array;
			if (!memory.TryRead(address, array3))
			{
				array3 = array2;
				if (!memory.TryRead(address, array3))
				{
					throw new MemoryAccessException(address, array3.Length);
				}
			}
			for (int i = 0; i < array3.Length; i += 2)
			{
				if (stringBuilder.Length >= maxLength)
				{
					break;
				}
				char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(array3.AsSpan(i));
				if (c == '\0')
				{
					return stringBuilder.ToString();
				}
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static string ReadUtf8(IMemory memory, ulong pointer, int maxLength)
	{
		if (pointer == 0L)
		{
			return "";
		}
		List<byte> list = new List<byte>();
		byte[] array = new byte[64];
		while (list.Count < maxLength * 4)
		{
			if (!memory.TryRead(pointer + (ulong)list.Count, array))
			{
				throw new MemoryAccessException(pointer, array.Length);
			}
			int num = Array.IndexOf(array, (byte)0);
			if (num >= 0)
			{
				list.AddRange(array.AsSpan(0, num).ToArray());
				break;
			}
			list.AddRange(array);
		}
		string text = Encoding.UTF8.GetString(list.ToArray());
		if (text.Length <= maxLength)
		{
			return text;
		}
		return text.Substring(0, maxLength);
	}

	private static string ReadLenUtf8(IMemory memory, ulong pointer, int maxLength)
	{
		if (pointer == 0L)
		{
			return "";
		}
		int num = memory.Read<int>(pointer);
		if (num < 0 || num > 4096)
		{
			throw new MemoryAccessException(pointer, 4, CoreText.Get("BadString", pointer, num));
		}
		string text = Encoding.UTF8.GetString(memory.ReadBytes(pointer + 4, num));
		return (text.Length <= maxLength) ? text : text.Substring(0, maxLength);
	}

	private static string ReadIl2CppString(IMemory memory, ulong pointer, int maxLength)
	{
		if (pointer == 0L)
		{
			return "";
		}
		int num = memory.Read<int>(pointer + 16);
		if (num < 0)
		{
			throw new MemoryAccessException(pointer, 4, CoreText.Get("BadString", pointer, num));
		}
		int num2 = Math.Min(num, maxLength);
		byte[] bytes = memory.ReadBytes(pointer + 20, num2 * 2);
		return Encoding.Unicode.GetString(bytes);
	}
}
