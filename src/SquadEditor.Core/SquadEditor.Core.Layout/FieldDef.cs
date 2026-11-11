using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadEditor.Core.Layout;

public sealed class FieldDef
{
	public const string DefaultGroup = "General";

	public string Name { get; init; } = "";

	public List<long> Path { get; init; } = new List<long>();

	public FieldType Type { get; init; }

	public double? Min { get; init; }

	public double? Max { get; init; }

	public bool ReadOnly { get; init; }

	public string? Group { get; init; }

	public FieldFormat Format { get; init; }

	public Dictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

	public int MaxLength { get; init; } = 256;

	/// <summary>Stored value = shown value * Scale (e.g. 5 for 1-20 attributes kept as 5-100).</summary>
	public double? Scale { get; init; }

	/// <summary>Read-only text built from other fields of the entity, joined with a space.</summary>
	public List<string> Concat { get; init; } = new List<string>();

	/// <summary>Currency symbol for <see cref="FieldFormat.Money"/>.</summary>
	public string Currency { get; init; } = "€";

	/// <summary>Other copies of the same value in the game; written together with <see cref="Path"/>.</summary>
	public List<List<long>> Mirrors { get; init; } = new List<List<long>>();

	/// <summary>The same field on the stored scale (e.g. 5-100 instead of 1-20), with its limits scaled too.</summary>
	public FieldDef Unscaled()
	{
		double scale = Scale ?? 1.0;
		return new FieldDef
		{
			Name = Name,
			Path = Path,
			Mirrors = Mirrors,
			Type = Type,
			Min = Min * scale,
			Max = Max * scale,
			ReadOnly = ReadOnly,
			Group = Group,
			Format = Format,
			Currency = Currency,
			Options = Options,
			MaxLength = MaxLength
		};
	}

	internal FieldDef WithoutScale()
	{
		return new FieldDef
		{
			Name = Name,
			Path = Path,
			Type = Type,
			ReadOnly = ReadOnly,
			Group = Group,
			Format = Format,
			Currency = Currency,
			Options = Options,
			MaxLength = MaxLength
		};
	}

	[JsonIgnore]
	public string GroupKey => Group ?? "General";

	[JsonIgnore]
	public bool IsString
	{
		get
		{
			FieldType type = Type;
			if ((uint)(type - 11) <= 3u || Concat.Count > 0)
			{
				return true;
			}
			return false;
		}
	}

	[JsonIgnore]
	public bool IsIntegerType
	{
		get
		{
			FieldType type = Type;
			if ((uint)type <= 7u)
			{
				return true;
			}
			return false;
		}
	}

	[JsonIgnore]
	public bool IsNumeric
	{
		get
		{
			bool flag = IsIntegerType;
			if (!flag)
			{
				FieldType type = Type;
				bool flag2 = (uint)(type - 8) <= 1u;
				flag = flag2;
			}
			return flag;
		}
	}

	[JsonIgnore]
	public bool IsWritable
	{
		get
		{
			if (!ReadOnly)
			{
				return !IsString;
			}
			return false;
		}
	}

	[JsonIgnore]
	public bool IsAttribute
	{
		get
		{
			double? min = Min;
			if (min.HasValue && min.GetValueOrDefault() == 1.0)
			{
				min = Max;
				if (min.HasValue && min.GetValueOrDefault() == 20.0)
				{
					return Options.Count == 0;
				}
			}
			return false;
		}
	}
}
