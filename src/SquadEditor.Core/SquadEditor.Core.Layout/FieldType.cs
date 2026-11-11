namespace SquadEditor.Core.Layout;

public enum FieldType
{
	U8,
	I8,
	U16,
	I16,
	U32,
	I32,
	U64,
	I64,
	F32,
	F64,
	Bool,
	Utf16Ptr,
	Utf8Ptr,
	Il2CppString,
	LenUtf8Ptr,
	/// <summary>u16 1-based day of year (low 9 bits, upper bits are flags) + u16 year.</summary>
	DayYear
}
