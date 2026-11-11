using System;
using System.Globalization;
using System.Linq;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Editing;

public sealed record FilterCondition(string Field, FilterOp Op, double Value)
{
	public bool Matches(GameSession session, ulong entity, EntityDef def)
	{
		FieldDef fieldDef = def.Fields.FirstOrDefault((FieldDef f) => f.Name == Field);
		if (fieldDef == null || !fieldDef.IsNumeric)
		{
			return false;
		}
		try
		{
			double num = double.Parse(session.ReadField(entity, fieldDef), CultureInfo.InvariantCulture);
			return Op switch
			{
				FilterOp.AtLeast => num >= Value, 
				FilterOp.AtMost => num <= Value, 
				_ => Math.Abs(num - Value) < 1E-09, 
			};
		}
		catch (MemoryAccessException)
		{
			return false;
		}
	}
}
