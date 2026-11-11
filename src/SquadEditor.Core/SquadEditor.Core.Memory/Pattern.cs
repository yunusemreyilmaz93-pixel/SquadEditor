using System;
using System.Globalization;

namespace SquadEditor.Core.Memory;

public sealed class Pattern
{
	private readonly byte[] _bytes;

	private readonly bool[] _mask;

	public string Text { get; }

	public int Length => _bytes.Length;

	internal int AnchorIndex { get; }

	internal byte AnchorByte => _bytes[AnchorIndex];

	private Pattern(byte[] bytes, bool[] mask, string text)
	{
		_bytes = bytes;
		_mask = mask;
		Text = text;
		AnchorIndex = Array.IndexOf(mask, value: true);
	}

	public static Pattern Parse(string text)
	{
		string[] array = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			throw new FormatException("Signature is empty.");
		}
		byte[] array2 = new byte[array.Length];
		bool[] array3 = new bool[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i];
			if ((!(text2 == "?") && !(text2 == "??")) || 1 == 0)
			{
				if (text2.Length != 2 || !byte.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out array2[i]))
				{
					throw new FormatException($"Invalid signature byte '{text2}' in \"{text}\".");
				}
				array3[i] = true;
			}
		}
		if (!array3.Contains(value: true))
		{
			throw new FormatException("Signature needs at least one fixed byte: \"" + text + "\".");
		}
		return new Pattern(array2, array3, text);
	}

	internal bool IsMatchAt(ReadOnlySpan<byte> data, int index)
	{
		for (int i = 0; i < _bytes.Length; i++)
		{
			if (_mask[i] && data[index + i] != _bytes[i])
			{
				return false;
			}
		}
		return true;
	}

	public override string ToString()
	{
		return Text;
	}
}
