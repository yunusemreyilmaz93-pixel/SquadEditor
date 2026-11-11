using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Editing;

public static class BulkEdit
{
	public static BulkResult ApplyPreset(GameSession session, EntityDef entity, PresetDef preset, IReadOnlyList<BulkTarget> targets, string batchLabel)
	{
		return ApplyActions(session, entity, preset.Actions, targets, batchLabel);
	}

	public static BulkResult ApplyActions(GameSession session, EntityDef entity, IReadOnlyList<PresetAction> actions, IReadOnlyList<BulkTarget> targets, string batchLabel)
	{
		int num = 0;
		int num2 = 0;
		string text = null;
		using (session.History.BeginBatch(batchLabel))
		{
			foreach (BulkTarget target in targets)
			{
				foreach (PresetAction action in actions)
				{
					if (!PresetValue.TryParse(action.Value, out var value))
					{
						throw new FormatException(CoreText.Get("EnterNumber"));
					}
					foreach (FieldDef item in ((action.Field != null) ? entity.Fields.Where((FieldDef f) => f.Name == action.Field) : entity.Fields.Where((FieldDef f) => f.GroupKey == action.Group)).Where((FieldDef f) => f.IsWritable && f.IsNumeric))
					{
						try
						{
							double num3 = double.Parse(session.ReadField(target.Address, item), CultureInfo.InvariantCulture);
							double num4 = value.Apply(num3, item, action.Min, action.Max);
							if (!num4.Equals(num3))
							{
								session.WriteField(target.Address, item, num4.ToString(CultureInfo.InvariantCulture), target.Label);
								num++;
							}
						}
						catch (Exception ex) when ((ex is FormatException || ex is MemoryAccessException || ex is InvalidOperationException) ? true : false)
						{
							num2++;
							if (text == null)
							{
								text = $"{target.Label} · {item.Name}: {ex.Message}";
							}
						}
					}
				}
			}
		}
		return new BulkResult(num, num2, text);
	}

	public static BulkResult Clone(GameSession session, EntityDef entity, ulong source, IReadOnlyCollection<string> groups, IReadOnlyList<BulkTarget> targets, string batchLabel)
	{
		List<FieldDef> list = entity.Fields.Where((FieldDef f) => f.IsWritable && f.IsNumeric && groups.Contains(f.GroupKey)).ToList();
		List<(FieldDef, string)> list2 = new List<(FieldDef, string)>();
		foreach (FieldDef item in list)
		{
			try
			{
				list2.Add((item, session.ReadField(source, item)));
			}
			catch (MemoryAccessException)
			{
			}
		}
		int num = 0;
		int num2 = 0;
		string text = null;
		using (session.History.BeginBatch(batchLabel))
		{
			foreach (BulkTarget item2 in targets.Where((BulkTarget t) => t.Address != source))
			{
				foreach (var (fieldDef, text2) in list2)
				{
					try
					{
						if (!(session.ReadField(item2.Address, fieldDef) == text2))
						{
							session.WriteField(item2.Address, fieldDef, text2, item2.Label);
							num++;
						}
					}
					catch (Exception ex2) when ((ex2 is FormatException || ex2 is MemoryAccessException || ex2 is InvalidOperationException) ? true : false)
					{
						num2++;
						if (text == null)
						{
							text = $"{item2.Label} · {fieldDef.Name}: {ex2.Message}";
						}
					}
				}
			}
		}
		return new BulkResult(num, num2, text);
	}
}
