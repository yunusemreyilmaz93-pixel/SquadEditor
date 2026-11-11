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

	private readonly Dictionary<int, string?> _batchLabels = new Dictionary<int, string>();

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

	public void Apply(IMemory memory, ulong address, byte[] newBytes, EditInfo info)
	{
		byte[] array = memory.ReadBytes(address, newBytes.Length);
		if (!((ReadOnlySpan<byte>)array.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)newBytes))
		{
			memory.WriteBytes(address, newBytes);
			Written?.Invoke(address, newBytes);
			_entries.Add(new EditRecord(address, array, newBytes, info, _openBatch ?? _nextBatch++, DateTime.Now));
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
		EditBatch editBatch = batches[batches.Count - 1];
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
		while ((object)UndoLast(memory) != null)
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
