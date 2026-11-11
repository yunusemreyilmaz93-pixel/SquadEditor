using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Layout;

public sealed class GameLayout
{
	internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Converters = 
		{
			(JsonConverter)new HexInt64Converter(),
			(JsonConverter)new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
		}
	};

	public string Game { get; init; } = "";

	public string GameVersion { get; init; } = "";

	public string ProcessName { get; init; } = "";

	public string? Module { get; init; }

	public List<AnchorDef> Anchors { get; init; } = new List<AnchorDef>();

	public List<CollectionDef> Collections { get; init; } = new List<CollectionDef>();

	public List<EntityDef> Entities { get; init; } = new List<EntityDef>();

	public List<PresetDef> Presets { get; init; } = new List<PresetDef>();

	public Dictionary<string, Dictionary<string, string>> Strings { get; init; } = new Dictionary<string, Dictionary<string, string>>();

	public string Text(string language, string key)
	{
		if (!Strings.TryGetValue(language, out Dictionary<string, string> value) || !value.TryGetValue(key, out var value2))
		{
			return key;
		}
		return value2;
	}

	public AnchorDef GetAnchor(string name)
	{
		return Anchors.FirstOrDefault((AnchorDef a) => a.Name == name) ?? throw new LayoutException("Unknown anchor '" + name + "'.");
	}

	public CollectionDef GetCollection(string name)
	{
		return Collections.FirstOrDefault((CollectionDef c) => c.Name == name) ?? throw new LayoutException("Unknown collection '" + name + "'.");
	}

	public EntityDef GetEntity(string name)
	{
		return Entities.FirstOrDefault((EntityDef e) => e.Name == name) ?? throw new LayoutException("Unknown entity '" + name + "'.");
	}

	public static GameLayout Load(string path)
	{
		try
		{
			return Parse(File.ReadAllText(path));
		}
		catch (LayoutException ex)
		{
			throw new LayoutException(Path.GetFileName(path) + ": " + ex.Message);
		}
	}

	public static GameLayout Parse(string json)
	{
		GameLayout gameLayout;
		try
		{
			gameLayout = JsonSerializer.Deserialize<GameLayout>(json, JsonOptions);
		}
		catch (JsonException ex)
		{
			throw new LayoutException("Invalid JSON: " + ex.Message);
		}
		if (gameLayout == null)
		{
			throw new LayoutException("Layout is empty.");
		}
		gameLayout.Validate();
		return gameLayout;
	}

	private void Validate()
	{
		List<string> list = new List<string>();
		if (string.IsNullOrWhiteSpace(ProcessName))
		{
			list.Add("'processName' is required.");
		}
		AddDuplicates(list, "anchor", Anchors.Select((AnchorDef a) => a.Name));
		AddDuplicates(list, "collection", Collections.Select((CollectionDef collectionDef) => collectionDef.Name));
		AddDuplicates(list, "entity", Entities.Select((EntityDef e) => e.Name));
		AddDuplicates(list, "preset", Presets.Select((PresetDef p) => p.Name));
		foreach (AnchorDef anchor in Anchors)
		{
			if (string.IsNullOrWhiteSpace(anchor.Name))
			{
				list.Add("Anchor name is empty.");
			}
			if (anchor.Resolve == AnchorResolve.RttiVtable)
			{
				if (string.IsNullOrWhiteSpace(anchor.TypeName))
				{
					list.Add("Anchor '" + anchor.Name + "': 'rttiVtable' needs 'typeName'.");
				}
				if (string.IsNullOrWhiteSpace(Module))
				{
					list.Add("Anchor '" + anchor.Name + "': 'rttiVtable' needs a layout 'module'.");
				}
			}
			else
			{
				try
				{
					Pattern.Parse(anchor.Pattern);
				}
				catch (FormatException ex)
				{
					list.Add("Anchor '" + anchor.Name + "': " + ex.Message);
				}
			}
			if (anchor.Scope == AnchorScope.Module && string.IsNullOrWhiteSpace(Module))
			{
				list.Add("Anchor '" + anchor.Name + "' has scope 'module' but the layout defines no 'module'.");
			}
			if (anchor.Resolve == AnchorResolve.RipRelative && anchor.InstructionEnd <= 0)
			{
				list.Add("Anchor '" + anchor.Name + "': 'ripRelative' needs 'instructionEnd' > 0.");
			}
		}
		foreach (CollectionDef c in Collections)
		{
			if (Anchors.All((AnchorDef a) => a.Name != c.Anchor))
			{
				list.Add($"Collection '{c.Name}': unknown anchor '{c.Anchor}'.");
			}
			if (Entities.All((EntityDef e) => e.Name != c.Entity))
			{
				list.Add($"Collection '{c.Name}': unknown entity '{c.Entity}'.");
			}
			if (c.Stride <= 0)
			{
				list.Add("Collection '" + c.Name + "': 'stride' must be > 0.");
			}
			if (c.ItemIsPointer && c.Stride < 8)
			{
				list.Add("Collection '" + c.Name + "': 'stride' must be at least 8 when 'itemIsPointer' is true.");
			}
			if (c.MaxCount <= 0)
			{
				list.Add("Collection '" + c.Name + "': 'maxCount' must be > 0.");
			}
		}
		foreach (EntityDef entity in Entities)
		{
			ValidateEntity(entity, list);
		}
		foreach (PresetDef preset in Presets)
		{
			ValidatePreset(preset, list);
		}
		if (list.Count > 0)
		{
			throw new LayoutException(string.Join(Environment.NewLine, list));
		}
	}

	private static void ValidateEntity(EntityDef entity, List<string> errors)
	{
		string name = entity.Name;
		AddDuplicates(errors, "'" + name + "' field", entity.Fields.Select((FieldDef f) => f.Name));
		if (!Has(entity.DisplayField))
		{
			errors.Add($"Entity '{name}': displayField '{entity.DisplayField}' is not a field.");
		}
		foreach (string item in entity.HeaderFields.Where((string n) => !Has(n)))
		{
			errors.Add($"Entity '{name}': headerFields contains unknown field '{item}'.");
		}
		foreach (string item2 in entity.ListFields.Where((string n) => !Has(n)))
		{
			errors.Add($"Entity '{name}': listFields contains unknown field '{item2}'.");
		}
		foreach (string item3 in entity.TableFields.Where((string n) => !Has(n)))
		{
			errors.Add($"Entity '{name}': tableFields contains unknown field '{item3}'.");
		}
		foreach (ProfileAxis item4 in entity.Profile)
		{
			if (item4.Fields.Count == 0)
			{
				errors.Add($"Entity '{name}': profile axis '{item4.Name}' is empty.");
			}
			foreach (string name2 in item4.Fields)
			{
				FieldDef fieldDef = entity.Fields.FirstOrDefault((FieldDef f) => f.Name == name2);
				if (fieldDef == null)
				{
					errors.Add($"Entity '{name}': profile axis '{item4.Name}' contains unknown field '{name2}'.");
				}
				else if (fieldDef.IsString || !fieldDef.Max.HasValue)
				{
					errors.Add($"Entity '{name}': profile field '{name2}' must be numeric with a 'max'.");
				}
			}
		}
		HashSet<string> groups = entity.Fields.Select((FieldDef f) => f.GroupKey).ToHashSet();
		AddDuplicates(errors, "'" + name + "' tab", entity.Tabs.Select((TabDef t) => t.Name));
		foreach (TabDef tab in entity.Tabs)
		{
			if (tab.Groups.Count == 0)
			{
				errors.Add($"Entity '{name}': tab '{tab.Name}' has no groups.");
			}
			foreach (string item5 in tab.Groups.Where((string g) => !groups.Contains(g)))
			{
				errors.Add($"Entity '{name}': tab '{tab.Name}' references unknown group '{item5}'.");
			}
		}
		foreach (FieldDef field in entity.Fields)
		{
			string text = $"Field '{name}.{field.Name}'";
			foreach (string item7 in field.Concat.Where((string n) => !Has(n) || n == field.Name))
			{
				errors.Add(text + ": concat contains unknown field '" + item7 + "'.");
			}
			if (field.Scale.HasValue && (field.Scale <= 0 || !field.IsNumeric))
			{
				errors.Add(text + ": 'scale' must be > 0 on a numeric field.");
			}
			if (field.Path.Count == 0 && field.Concat.Count == 0)
			{
				errors.Add(text + ": 'path' must not be empty.");
			}
			if (field.Min > field.Max)
			{
				errors.Add(text + ": 'min' > 'max'.");
			}
			if (field.Options.Count > 0 && !field.IsIntegerType)
			{
				errors.Add(text + ": 'options' are only allowed on integer types.");
			}
			foreach (string item6 in field.Options.Keys.Where((string k) => !long.TryParse(k, out var _)))
			{
				errors.Add(text + ": option key '" + item6 + "' is not an integer.");
			}
		}
		bool Has(string text2)
		{
			return entity.Fields.Any((FieldDef f) => f.Name == text2);
		}
	}

	private void ValidatePreset(PresetDef preset, List<string> errors)
	{
		string text = "Preset '" + preset.Name + "'";
		EntityDef entityDef = Entities.FirstOrDefault((EntityDef e) => e.Name == preset.Entity);
		if (entityDef == null)
		{
			errors.Add(text + ": unknown entity '" + preset.Entity + "'.");
			return;
		}
		if (preset.Actions.Count == 0)
		{
			errors.Add(text + ": no actions.");
		}
		foreach (PresetAction action in preset.Actions)
		{
			if (action.Field == null == (action.Group == null))
			{
				errors.Add(text + ": each action needs exactly one of 'field' or 'group'.");
				continue;
			}
			List<FieldDef> list = ((action.Field != null) ? entityDef.Fields.Where((FieldDef f) => f.Name == action.Field).ToList() : entityDef.Fields.Where((FieldDef f) => f.GroupKey == action.Group).ToList());
			if (list.Count == 0)
			{
				errors.Add(text + ": unknown " + ((action.Field != null) ? ("field '" + action.Field + "'") : ("group '" + action.Group + "'")) + ".");
			}
			foreach (FieldDef item in list.Where((FieldDef t) => !t.IsWritable || t.IsString))
			{
				errors.Add(text + ": field '" + item.Name + "' is not writable.");
			}
			if (!PresetValue.TryParse(action.Value, out var _))
			{
				errors.Add(text + ": invalid value '" + action.Value + "' (use 20, +5, -3, min or max).");
			}
		}
	}

	private static void AddDuplicates(List<string> errors, string kind, IEnumerable<string> names)
	{
		foreach (string item in from n in names
			group n by n into g
			where g.Count() > 1
			select g.Key)
		{
			errors.Add($"Duplicate {kind} name '{item}'.");
		}
	}
}
