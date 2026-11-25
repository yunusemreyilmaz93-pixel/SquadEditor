using System;

namespace SquadEditor.App.Ui;

/// <summary>
/// Centralized C++ struct offsets verified against both Windows x64 (MSVC ABI, layouts/fm27.json)
/// and macOS Apple Silicon ARM64 (Clang Itanium ABI, layouts/fm27-mac.json).
/// </summary>
public static class FmOffsets
{
	private static bool Mac => OperatingSystem.IsMacOS();

	// ACTUAL_PLAYER fields before PersonBlock (-0x08 shift on Clang ARM64)
	public static ulong PlayerMedicalRecord => Mac ? 0xF0UL : 0xF8UL;
	public static ulong PlayerTacticalFamiliarity => Mac ? 0xF8UL : 0x100UL;
	public static ulong PlayerTeam => Mac ? 0x128UL : 0x130UL;
	public static ulong PlayerRaw69 => Mac ? 0x148UL : 0x150UL;
	public static ulong PlayerTraitsMask => Mac ? 0x1E4UL : 0x1ECUL;
	public static ulong PlayerCA => Mac ? 0x25CUL : 0x264UL;

	// PersonBlock start inside ACTUAL_PLAYER & fields inside PersonBlock (-0x30 shift on Clang ARM64, -0x40 total from Player)
	public static ulong PlayerPersonBlock => Mac ? 0x278UL : 0x288UL;
	public static ulong PlayerRowId => Mac ? 0x390UL : 0x290UL;
	public static ulong PlayerPrimaryNation => Mac ? 0x2B0UL : 0x2F0UL;
	public static ulong PlayerExtraData => Mac ? 0x2C0UL : 0x300UL;
	public static ulong PlayerContract => Mac ? 0x2F0UL : 0x330UL;
	public static ulong PlayerLoanRecord => Mac ? 0x2F8UL : 0x338UL;
	public static ulong PlayerLanguagesVector => Mac ? 0x338UL : 0x378UL;

	// Relative to PersonBlock (used for both Player + PlayerPersonBlock and Staff entity address)
	public static ulong PersonFullName => Mac ? 0x10UL : 0x40UL;
	public static ulong PersonFirstName => Mac ? 0x20UL : 0x50UL;
	public static ulong PersonLastName => Mac ? 0x28UL : 0x58UL;
	public static ulong PersonCommonName => Mac ? 0x30UL : 0x60UL;
	public static ulong PersonContract => Mac ? 0x78UL : 0xA8UL;

	// ACTUAL_NON_PLAYER object start before PersonBlock (colOffset 248 = 0xF8 on Mac vs 256 = 0x100 on Win)
	public static ulong StaffObjectStart => Mac ? 0xF8UL : 0x100UL;

	/// <summary>Validates user-space 64-bit aligned heap/image pointers on both Windows x64 and macOS ARM64 (where heap starts above 4 GB = 0x1_0000_0000).</summary>
	public static bool LooksPtr(ulong p) => p >= 0x0000_0001_0000_0000UL && p < 0x0000_8000_0000_0000UL && (p & 7) == 0;
}
