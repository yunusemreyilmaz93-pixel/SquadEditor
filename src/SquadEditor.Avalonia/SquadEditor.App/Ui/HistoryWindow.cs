using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using SquadEditor.Core.Editing;

namespace SquadEditor.App.Ui;

/// <summary>Change log dialog: lists every edit batch (old -> new) and lets the user undo any batch or all changes.</summary>
public sealed class HistoryWindow : Window
{
	private sealed record Row(int BatchId, string Time, string Entity, string Field, string Change, string Guard);

	private readonly GameData _data;
	private readonly Action _afterUndo;
	private readonly DataGrid _grid;
	private readonly TextBlock _summary;

	public HistoryWindow(GameData data, Action afterUndo)
	{
		_data = data;
		_afterUndo = afterUndo;
		Title = Build.T("Değişiklik Geçmişi", "Change History");
		Width = 780;
		Height = 500;
		MinWidth = 600;
		MinHeight = 360;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Build.Res("Bg");
		Foreground = Build.Res("Text");
		FontFamily = Build.Font("UiFont");
		WindowHelper.ApplyChrome(this);

		var root = new DockPanel { Margin = new Thickness(16) };
		var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
		_summary = Build.Text("", 13, Build.Res("Muted"));
		head.Children.Add(_summary);
		DockPanel.SetDock(head, Dock.Top);
		root.Children.Add(head);

		var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
		var close = new Button { Content = Build.T("Kapat", "Close"), IsCancel = true, MinWidth = 84 };
		close.Click += (_, _) => Close();
		var undoSel = new Button { Content = Build.T("Seçiliyi geri al", "Undo selected"), Margin = new Thickness(0, 0, 8, 0) };
		var undoAll = new Button { Content = Build.T("Tümünü geri al", "Undo all"), Margin = new Thickness(0, 0, 8, 0) };
		DockPanel.SetDock(close, Dock.Right);
		buttons.Children.Add(close);
		buttons.Children.Add(undoSel);
		buttons.Children.Add(undoAll);
		DockPanel.SetDock(buttons, Dock.Bottom);
		root.Children.Add(buttons);

		_grid = new DataGrid { RowHeight = 28, SelectionMode = DataGridSelectionMode.Single };
		_grid.Columns.Add(new DataGridTextColumn { Header = Build.T("Saat", "Time"), Binding = new Binding(nameof(Row.Time)), Width = new DataGridLength(72) });
		_grid.Columns.Add(new DataGridTextColumn { Header = Build.T("Kayıt", "Record"), Binding = new Binding(nameof(Row.Entity)), Width = new DataGridLength(180) });
		_grid.Columns.Add(new DataGridTextColumn { Header = Build.T("Alan", "Field"), Binding = new Binding(nameof(Row.Field)), Width = new DataGridLength(150) });
		_grid.Columns.Add(new DataGridTextColumn { Header = Build.T("Değişiklik", "Change"), Binding = new Binding(nameof(Row.Change)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
		_grid.Columns.Add(new DataGridTextColumn { Header = Build.T("Tür", "Type"), Binding = new Binding(nameof(Row.Guard)), Width = new DataGridLength(86) });
		root.Children.Add(new Border { Classes = { "CardBorder" }, Margin = new Thickness(0), Padding = new Thickness(3), Child = _grid });

		undoSel.Click += async (_, _) =>
		{
			if (_grid.SelectedItem is not Row row)
			{
				return;
			}
			try
			{
				_data.Session.History.UndoBatch(_data.Memory, row.BatchId);
				_afterUndo();
				Fill();
			}
			catch (GuardedUndoException ex)
			{
				await MsgBox.ShowAsync(this, Build.F("\"{0}\" geri alınamadı: oyun bu arada veriyi değiştirdi.", "\"{0}\" could not be undone: the game changed this data since.", ex.Batch.Label), Title ?? "SquadEditor");
				Fill();
			}
			catch (InvalidOperationException)
			{
				await MsgBox.ShowAsync(this, Build.T("Aynı alana daha sonra başka bir değişiklik yapılmış. Önce daha yeni değişikliği geri al.", "A newer change touched the same field. Undo the newer change first."), Title ?? "SquadEditor");
			}
		};

		undoAll.Click += async (_, _) =>
		{
			if (_data.Session.History.Batches.Count == 0)
			{
				return;
			}
			if (!await MsgBox.AskAsync(this, Build.T("Tüm değişiklikler geri alınsın mı?", "Undo all changes?"), Title ?? "SquadEditor"))
			{
				return;
			}
			try
			{
				_data.Session.History.UndoAll(_data.Memory);
			}
			catch (GuardedUndoException ex)
			{
				await MsgBox.ShowAsync(this, Build.F("\"{0}\" geri alınamadı (oyun değiştirdi); kalanlar durdu.", "\"{0}\" could not be undone (changed by game); stopped.", ex.Batch.Label), Title ?? "SquadEditor");
			}
			_afterUndo();
			Fill();
		};

		Content = root;
		Fill();
	}

	private void Fill()
	{
		string lang = Loc.Instance.Language;
		var batches = _data.Session.History.Batches.Reverse().ToList();
		var rows = new List<Row>(batches.Count);
		foreach (EditBatch b in batches)
		{
			EditRecord first = b.Entries[0];
			bool guarded = b.Entries.Any(e => e.Guarded);
			string entity = b.Label ?? first.Info.Entity;
			string field = b.Entries.Count == 1 ? _data.Layout.Text(lang, first.Info.Field) : Build.F("{0} yazma", "{0} writes", b.Entries.Count);
			string change = b.Entries.Count == 1
				? $"{first.Info.OldValue} → {first.Info.NewValue}"
				: string.Join(", ", b.Entries.Take(3).Select(e => $"{_data.Layout.Text(lang, e.Info.Field)}: {e.Info.OldValue}→{e.Info.NewValue}")) + (b.Entries.Count > 3 ? "…" : "");
			rows.Add(new Row(b.Id, b.Time.ToString("HH:mm:ss"), entity, field, change, guarded ? Build.T("Korumalı", "Guarded") : Build.T("Standart", "Standard")));
		}
		_grid.ItemsSource = rows;
		_summary.Text = Build.F("{0} işlem ({1} alan değişikliği).", "{0} batches ({1} field writes).", batches.Count, _data.Session.History.Entries.Count);
	}
}
