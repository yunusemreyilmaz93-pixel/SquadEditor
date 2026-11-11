using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>One write an action plans: where, the bytes it expects there now, the bytes it leaves.</summary>
public sealed record PlannedWrite(ulong Address, byte[] Expected, byte[] Bytes, string Field, string OldText = "", string NewText = "");

/// <summary>
/// Writes for actions that change several places at once (renames, moves). A plan is read
/// from the game first; before anything is written every place is read again and must still
/// hold what the plan saw, so a plan never lands on memory the game has changed since.
/// All writes go through the edit history as one batch, so Ctrl+Z undoes the whole action.
/// </summary>
public static class GameWrite
{
	public static void Apply(GameData data, string label, IReadOnlyList<PlannedWrite> writes)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		foreach (PlannedWrite w in writes)
		{
			byte[] now = data.Memory.ReadBytes(w.Address, w.Expected.Length);
			if (!now.AsSpan().SequenceEqual(w.Expected))
			{
				throw new InvalidOperationException(Build.T(
					"Oyun bu arada veriyi değiştirdi; hiçbir şey yazılmadı. Tekrar dene.",
					"The game changed the data in the meantime; nothing was written. Try again."));
			}
		}
		EditHistory history = data.Session.History;
		using (history.InBatch ? null : history.BeginBatch(label))
		{
			foreach (PlannedWrite w in writes)
			{
				history.Apply(data.Memory, w.Address, w.Bytes, new EditInfo(label, w.Field, w.OldText, w.NewText));
				byte[] back = data.Memory.ReadBytes(w.Address, w.Bytes.Length);
				if (!back.AsSpan().SequenceEqual(w.Bytes))
				{
					throw new InvalidOperationException(Build.T("Yazma doğrulanamadı.", "A write could not be verified."));
				}
			}
		}
	}

	public static byte[] U64(ulong value) => BitConverter.GetBytes(value);

	public static byte[] U32(uint value) => BitConverter.GetBytes(value);
}
