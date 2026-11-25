using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>
/// Live FM27 Team Cohesion (player + FmOffsets.PlayerExtraData -> club relation vector, byte+5 = 0..100)
/// and Tactical Familiarity (player + FmOffsets.PlayerTacticalFamiliarity -> 7 vectors at +0x00..+0xC0, bits 0..13 = 0..10000).
/// Every write is recorded in EditorSession.History so Undo cleanly restores previous values.
/// </summary>
public static class TeamCohesion
{
	/// <summary>Reads a player's current Team Cohesion (0..100) for their club, or -1 if unavailable.</summary>
	public static int ReadCohesion(GameData data, ulong playerAddr, ulong clubAddr)
	{
		if (clubAddr == 0) return -1;
		try
		{
			uint clubId = data.Memory.Read<uint>(clubAddr + 0x08);
			ulong relObj = data.Memory.Read<ulong>(playerAddr + FmOffsets.PlayerExtraData);
			if (!FmOffsets.LooksPtr(relObj)) return -1;
			ulong vb = data.Memory.Read<ulong>(relObj + 0x18);
			ulong ve = data.Memory.Read<ulong>(relObj + 0x20);
			if (!FmOffsets.LooksPtr(vb) || ve <= vb || (ve - vb) > 0x400 || ((ve - vb) % 8) != 0) return -1;
			int cnt = (int)((ve - vb) / 8);
			for (int i = 0; i < cnt; i++)
			{
				ulong entry = vb + (ulong)(i * 8);
				uint eid = data.Memory.Read<uint>(entry);
				byte typ = data.Memory.Read<byte>(entry + 4);
				byte val = data.Memory.Read<byte>(entry + 5);
				byte flag = data.Memory.Read<byte>(entry + 6);
				if (eid == clubId && (typ == 1 || typ == 2) && flag == 0)
					return val;
			}
		}
		catch
		{
		}
		return -1;
	}

	/// <summary>Sets a player's Team Cohesion for their club to 100 (0x64) with Undo support.</summary>
	public static bool MaxPlayerCohesion(GameData data, ulong playerAddr, ulong clubAddr, string playerName)
	{
		if (clubAddr == 0) return false;
		try
		{
			uint clubId = data.Memory.Read<uint>(clubAddr + 0x08);
			ulong relObj = data.Memory.Read<ulong>(playerAddr + FmOffsets.PlayerExtraData);
			if (!FmOffsets.LooksPtr(relObj)) return false;
			ulong vb = data.Memory.Read<ulong>(relObj + 0x18);
			ulong ve = data.Memory.Read<ulong>(relObj + 0x20);
			if (!FmOffsets.LooksPtr(vb) || ve <= vb || (ve - vb) > 0x400 || ((ve - vb) % 8) != 0) return false;
			int cnt = (int)((ve - vb) / 8);
			bool wrote = false;
			for (int i = 0; i < cnt; i++)
			{
				ulong entry = vb + (ulong)(i * 8);
				uint eid = data.Memory.Read<uint>(entry);
				byte typ = data.Memory.Read<byte>(entry + 4);
				byte val = data.Memory.Read<byte>(entry + 5);
				byte flag = data.Memory.Read<byte>(entry + 6);
				if (eid == clubId && (typ == 1 || typ == 2) && flag == 0 && val < 100)
				{
					data.Session.History.Apply(data.Memory, entry + 5, new byte[] { 100 },
						new EditInfo(playerName, Build.T("Takım Uyumu", "Team Cohesion"), val.ToString(), "100"), guarded: true);
					wrote = true;
				}
			}
			return wrote;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Sets all 6 tactical area familiarity lists + formations to 100% (10000) with Undo support.</summary>
	public static int MaxPlayerTacticalFamiliarity(GameData data, ulong playerAddr, string playerName)
	{
		int updatedEntries = 0;
		try
		{
			ulong tacObj = data.Memory.Read<ulong>(playerAddr + FmOffsets.PlayerTacticalFamiliarity);
			if (!FmOffsets.LooksPtr(tacObj)) return 0;
			// 6 tactical areas (+0x00..+0xA0, stride 0x20) + formation shapes (+0xC0)
			for (int vecOff = 0; vecOff <= 0xC0; vecOff += 0x20)
			{
				ulong vb = data.Memory.Read<ulong>(tacObj + (ulong)vecOff);
				ulong ve = data.Memory.Read<ulong>(tacObj + (ulong)vecOff + 8);
				ulong vc = data.Memory.Read<ulong>(tacObj + (ulong)vecOff + 16);
				if (!FmOffsets.LooksPtr(vb) || ve <= vb || vc < ve || (ve - vb) > 0x200 || ((ve - vb) % 4) != 0) continue;
				int count = (int)((ve - vb) / 4);
				byte[] raw = new byte[count * 4];
				if (!data.Memory.TryRead(vb, raw)) continue;
				byte[] next = (byte[])raw.Clone();
				bool changed = false;
				for (int i = 0; i < count; i++)
				{
					uint u = BitConverter.ToUInt32(raw, i * 4);
					uint fam = u & 0x3FFFu;
					if (fam <= 10000 && fam < 10000)
					{
						uint maxed = (u & ~0x3FFFu) | 10000u;
						BitConverter.TryWriteBytes(next.AsSpan(i * 4, 4), maxed);
						changed = true;
						updatedEntries++;
					}
				}
				if (changed)
				{
					data.Session.History.Apply(data.Memory, vb, next,
						new EditInfo(playerName, Build.T("Taktik Yatkınlığı", "Tactical Familiarity"), "", "100%"), guarded: true);
				}
			}
		}
		catch
		{
		}
		return updatedEntries;
	}

	/// <summary>Maxes both Team Cohesion and Tactical Familiarity for an entire squad in one Undo batch.</summary>
	public static (int CohesionPlayers, int TacticalPlayers) MaxSquad(GameData data, ClubRow club, IEnumerable<PlayerRow> rows)
	{
		int cohCount = 0, tacCount = 0;
		var list = rows.ToList();
		using (data.Session.History.BeginBatch(Build.F("{0}: Takım Uyumu & Taktik %100", "{0}: Max Cohesion & Tactics", club.Name)))
		{
			foreach (PlayerRow p in list)
			{
				if (MaxPlayerCohesion(data, p.Address, club.Address, p.Name)) cohCount++;
				if (MaxPlayerTacticalFamiliarity(data, p.Address, p.Name) > 0) tacCount++;
			}
		}
		return (cohCount, tacCount);
	}
}
