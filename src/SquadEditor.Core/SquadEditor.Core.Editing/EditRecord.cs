using System;

namespace SquadEditor.Core.Editing;

public sealed record EditRecord(ulong Address, byte[] OldBytes, byte[] NewBytes, EditInfo Info, int Batch, DateTime Time, bool Guarded = false);
