using System;
using System.Collections.Generic;

namespace SquadEditor.Core.Editing;

public sealed record EditBatch(int Id, string? Label, DateTime Time, IReadOnlyList<EditRecord> Entries);
