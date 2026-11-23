using System;
using System.Collections.Generic;
using System.Linq;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Editing;

public sealed class EditHistory
{
	private sealed class BatchScope(EditHistory owner) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				owner.CloseBatch();
			}
		}
	}

	private readonly List<EditRecord> _entries = new List<EditRecord>();

	private int _nextBatch = 1;

	private int? _openBatch;

	private readonly Dictionary<int, string?> _batchLabels = new Dictionary<int, string?>();

	private bool _changedWhileOpen;

	public IReadOnlyList<EditRecord> Entries => _entries;

	public Action<ulong, byte[]>? Written { get; set; }

	public IReadOnlyList<EditBatch> Batches => (from e in _entries
		group e by e.Batch into g
		select new EditBatch(g.Key, _batchLabels.GetValueOrDefault(g.Key), g.Last().Time, g.ToList())).ToList();

	public event EventHandler? Changed;

	public bool InBatch => _openBatch.HasValue;

	public IDisposable BeginBatch(string label)
	{
		int? openBatch = _openBatch;
		if (openBatch.HasValue)
		{
			throw new InvalidOperationException("A batch is already open.");
		}
		_openBatch = _nextBatch++;
		_batchLabels[_openBatch.Value] = label;
		_changedWhileOpen = false;
		return new BatchScope(this);
	}

	/// <param name="guarded">The write is part of a change to the game's own structures (lists, pools): it is only
	/// undone while the game still holds what was written, otherwise the undo is refused.</param>
	public void Apply(IMemory memory, ulong address, byte[] newBytes, EditInfo info, bool guarded = false)
	{
		byte[] array = memory.ReadBytes(address, newBytes.Length);
		if (!((ReadOnlySpan<byte>)array.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)newBytes))
		{
			memory.WriteBytes(address, newBytes);
			Written?.Invoke(address, newBytes);
			_entries.Add(new EditRecord(address, array, newBytes, info, _openBatch ?? _nextBatch++, DateTime.Now, guarded));
			int? openBatch = _openBatch;
			if (!openBatch.HasValue)
			{
				Changed?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				_changedWhileOpen = true;
			}
		}
	}

	public EditBatch? UndoLast(IMemory memory)
	{
		if (_entries.Count == 0)
		{
			return null;
		}
		IReadOnlyList<EditBatch> batches = Batches;
		return UndoBatch(memory, batches[batches.Count - 1].Id);
	}

	public EditBatch? UndoBatch(IMemory memory, int batchId)
	{
		EditBatch? editBatch = Batches.FirstOrDefault(b => b.Id == batchId);
		if (editBatch == null)
		{
			return null;
		}
		var addresses = new HashSet<ulong>(editBatch.Entries.Select(e => e.Address));
		if (_entries.Any(e => e.Batch > batchId && addresses.Contains(e.Address)))
		{
			throw new InvalidOperationException("A newer change touched the same field; undo the newer change first.");
		}
		foreach (EditRecord guarded in editBatch.Entries.Where(r => r.Guarded))
		{
			if (!((ReadOnlySpan<byte>)memory.ReadBytes(guarded.Address, guarded.NewBytes.Length)).SequenceEqual(guarded.NewBytes))
			{
				foreach (EditRecord r in editBatch.Entries)
				{
					_entries.Remove(r);
				}
				Changed?.Invoke(this, EventArgs.Empty);
				throw new GuardedUndoException(editBatch);
			}
		}
		for (int num = editBatch.Entries.Count - 1; num >= 0; num--)
		{
			EditRecord editRecord = editBatch.Entries[num];
			memory.WriteBytes(editRecord.Address, editRecord.OldBytes);
			Written?.Invoke(editRecord.Address, editRecord.OldBytes);
			_entries.Remove(editRecord);
		}
		Changed?.Invoke(this, EventArgs.Empty);
		return editBatch;
	}

	public int UndoAll(IMemory memory)
	{
		int num = 0;
		while (UndoLast(memory) != null)
		{
			num++;
		}
		return num;
	}

	public void Clear()
	{
		_entries.Clear();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void CloseBatch()
	{
		_openBatch = null;
		if (_changedWhileOpen)
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}

/// <summary>The game changed what a guarded batch wrote: the batch is dropped from the history instead of undone.</summary>
public sealed class GuardedUndoException(EditBatch batch) : InvalidOperationException("The game has changed this data since; it was not undone.")
{
	public EditBatch Batch { get; } = batch;
}
