# FM27 reverse engineering notes

Game: Football Manager 27, 27.0.1, Unity 6000.3.18f1 (IL2CPP metadata v39).

## Tools

- Il2CppDumper 6.7.46 does not support metadata v39.
- Cpp2IL 2022.1.0-pre-release.21 works (`--output-as diffable-cs`).
- `game_plugin.dll` code sits in protected sections (`.ecode`, `.xtext`): code signatures from the file on disk are not usable. RTTI in `.rodata` is intact.
- `tools/FmProbe`: read-only memory explorer.
  - `check <layout>`: validate a layout (game not needed)
  - `players <layout> [name] [field...]`: end-to-end read through `GameSession`
  - `regions`, `str`, `ptr`, `dump`, `bytes`, `fuzzy`, `objs`, `run`, `bigruns`, `roots`

## Managed / native bridge

- The Unity side (`FM.GamePlugin`) talks to `game_plugin.dll` only through data channels:
  `GamePlugin.PerformChannelActions`, `ChannelData`, `InteropReference`, `UiRecordID`.
- The database lives in native memory. Classes have MSVC RTTI (`db::ACTUAL_PLAYER`, `db::ACTUAL_NON_PLAYER`, `db::AGENT` ...).

## Finding players

No single global pointer array was found (only per-club / per-list vectors of a few hundred).
The layout scans writable memory for objects whose first qword is the primary
`db::ACTUAL_PLAYER` vtable. The vtable is located through RTTI (anchor `resolve: rttiVtable`),
so it survives ASLR and patches. About 31.5k players in the test save; a scan takes a few seconds.

## db::ACTUAL_PLAYER (offsets from object start S)

| Offset | Type | Meaning | Verified on |
|---|---|---|---|
| +0x000 | ptr | primary vtable (COL offset 0) | |
| +0x148 | ptr | attributes sub-object vtable | |
| +0x150 | u8[15] | position ratings 1-20 (order not mapped yet) | Oppong DC = 20 at index 3 |
| +0x15F | u8[54] | attributes, stored as shown value x 5 | Oppong, all visible ones |
| +0x22E | u16 | height (cm) | Oppong 185, Haaland 195 |
| +0x264 | u16 | current ability | Oppong 125, Haaland 187 |
| +0x266 | u16 | potential ability | Oppong 150, Haaland 195 |
| +0x288 | ptr | person part vtable (virtual base, COL offset 0x288) | |
| +0x294 | u32 | unique ID | Haaland 29179241 |
| +0x2C8 | ptr | full name (only some players), length-prefixed UTF-8 | Haaland |
| +0x2D8 | ptr | first name entry -> +0 string ptr | Oppong |
| +0x2E0 | ptr | last name entry -> +0 string ptr | Oppong |
| +0x310 | u16 day-of-year, u16 year | date of birth | both |

Position ratings (u8 1-20, from +0x150): GK, SW, DL, DC, DR, DM, ML, MC, MR, AML, AMC, AMR, ST, WBL, WBR
(checked on Ederson, Rodri, Trent, Hakimi, Salah, Vinícius, Saka, Bellingham, Ødegaard, Oppong).

Links from ACTUAL_PLAYER:

| Offset | Target |
|---|---|
| +0x120 | INTERNATIONAL_PLAYER_DATA |
| +0x130 | TEAM (team the player plays for); TEAM +0x30 CLUB, +0x50 COMP (league) |
| +0x2F0 | NATION (nationality) |
| +0x308 | CITY (city of birth) |
| +0x330 | FULL_CONTRACT |
| +0x390 | CLUB (second link, e.g. favourite club, to verify) |
| +0x398 | AGENT |

FULL_CONTRACT (0xC8 bytes): +0x08 person (player +0x288), +0x10 team, +0x20 i32 weekly wage in GBP,
+0x44 date (start?), +0x48 expiry date, +0x4C date.

CLUB (0x170 bytes, stored in arrays): +0xC0 full name, +0xC8 name, +0xD8 NATION, +0xF8 CITY,
+0x100 -> short name (3 letters), +0x150 CLUB_FINANCE_BASE.
NATION (0x200): +0x18/+0x20 name, +0x28 code, +0x30 nationality. COMP (0x1B0): +0x40 full, +0x48 short, +0x50 code.
STADIUM (0xC0): +0x40/+0x48 name. CITY (0x58): +0x18 name. Strings are already in the game's language.

Dates: u16 1-based day of year (low 9 bits; upper bits are flags) + u16 year. Game date seen as `B4 1A EA 07` (29.06.2026).

Object counts in the test save: ACTUAL_PLAYER 31487, ACTUAL_NON_PLAYER 21816, ACTUAL_PLAYER_AND_NON_PLAYER 161,
CLUB 30574, TEAM 64677, NATION 251, NATIONAL_TEAM 3586, COMP 8209, STADIUM 26388, CITY 58475, FULL_CONTRACT 38812.
`FmProbe vthist` lists every class by object count.

Attribute order (index from +0x15F): Crossing, Dribbling, Finishing, Heading, Long Shots, Marking,
Off the Ball, Passing, Penalty Taking, Tackling, Vision, Handling, Aerial Reach, Command of Area,
Communication, Kicking, Throwing, Anticipation, Decisions, One on Ones, Positioning, Reflexes,
First Touch, Technique, Left Foot, Right Foot, Flair, Corners, Teamwork, Work Rate, Long Throws,
Eccentricity, Rushing Out, Punching, Acceleration, Free Kicks, Strength, Stamina, Pace,
Jumping Reach, Leadership, Dirtiness, Balance, Bravery, Consistency, Aggression, Agility,
Important Matches, Injury Proneness, Versatility, Natural Fitness, Determination, Composure,
Concentration.

Goalkeeping and hidden attributes follow the classic FM order but are not checked against the
game yet; they are read-only in the layout until verified.

## Strings

Length-prefixed UTF-8: pointer -> `u32 length` -> bytes. Name pool entries carry a refcount.
Dates `01 00 6C 07` (day 1, year 1900) mean empty.

## Game screen refresh

Writes land in the database (match engine and save use them), but the game's own screen keeps showing
the old value for a profile that is already open. To check: reopen the player or press Continue.

## Cross-checks with other FM27 research

The FM27 notes of FMLibreEditor (https://github.com/tozmoz/FMLibreEditor, GPL-3.0) and fm-editor
(https://github.com/barak14/fm-editor, GPL-3.0) were read for facts and each offset used here was
read back on our own save before it went into the layout:

- Player attributes: the same 54-byte order as mapped here (goalkeeping and hidden ones now writable).
- Player: reputation is home +0x25E, current +0x260, world +0x262 (earlier notes here had the first
  two swapped); match sharpness +0x258, jadedness +0x25A (i16), morale +0x26C (1..20), transfer value
  +0x234, asking price +0x238, personality +0x2F8 (8 bytes), caps +0x3BC; contract squad number +0x5D,
  squad status +0x54, type +0xC3, loyalty bonus +0x38, signed +0x44, joined +0x4C.
- Staff (object start = person block - 0x100): attributes stored 0..100 at +0x1D..+0x43, working with
  youngsters +0x1C and authority +0x14 as 1..20, CA/PA +0xDA/+0xDC, reputation +0xD4..+0xD8.
  Haluk Kaplan's values match the bands the game shows (set pieces 1 "unsuited", attacking 7 "competent").
- Club: +0x18 team list (first = senior team; team +0xA8 reputation, +0x78 stadium), +0x100 info record
  (founded, season tickets, supporters, facilities +0x120/+0x12B/+0x12C/+0x12D), finance +0x808 wage
  budget, +0x814 spending wage budget, +0x864 ticket price, +0x8A9 corporate facilities.
  Fenerbahçe: capacity 47,430 as in the game, founded 1907.

## Open

- Injuries and bans (records described by FMLibreEditor, not mapped here yet), renaming people (needs string allocation).
- Staff (`ACTUAL_NON_PLAYER`, primary vtable has COL offset 0x100), competitions, nations as entities.
