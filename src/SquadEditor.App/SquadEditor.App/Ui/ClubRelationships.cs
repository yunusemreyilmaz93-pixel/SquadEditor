using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

public sealed record ClubRelation(int Index, ulong EntryAddress, ulong TargetAddress, byte Category, byte Kind, byte Strength, string TargetName)
{
	public string KindLabel => (Kind, Category) switch
	{
		(0x05, 0x01) => Build.T("🔥 Rakip Kulüp", "🔥 Rival Club"),
		(0x01, 0x03) => Build.T("⭐ Efsane / Favori Kişi", "⭐ Legend / Favoured Person"),
		(0x02, 0x03) => Build.T("⛔ Sevilmeyen Kişi", "⛔ Disliked Person"),
		(0x44, 0x03) => Build.T("👔 Eski / Mevcut Teknik Direktör", "👔 Former / Current Manager"),
		_ => Build.T($"İlişki (#{Kind:X2})", $"Relation (#{Kind:X2})")
	};
}

/// <summary>
/// Live-verified FM27 27.0.1 Club Relationships vector at db::CLUB +0x48 (std::vector of 16-byte entries:
/// +0x00 u64 target*, +0x08 u16 subtype, +0x0A u8 category (1=Club, 3=Person), +0x0B u8 kind (5=Rival, 1=Favoured, 2=Disliked), +0x0C u8 strength 1..100).
/// </summary>
public static class ClubRelationships
{
	private const long RelVector = 0x48;
	private const int EntrySize = 16;

	public static IReadOnlyList<ClubRelation> Read(GameData data, ulong clubAddress)
	{
		var list = new List<ClubRelation>();
		IMemory m = data.Memory;
		try
		{
			ulong begin = m.ReadPointer(clubAddress + RelVector);
			ulong end = m.ReadPointer(clubAddress + RelVector + 8);
			if (begin == 0 || end <= begin || end - begin > EntrySize * 128 || (end - begin) % EntrySize != 0)
			{
				return list;
			}
			int count = (int)((end - begin) / EntrySize);
			byte[] raw = m.ReadBytes(begin, count * EntrySize);
			for (int i = 0; i < count; i++)
			{
				int o = i * EntrySize;
				ulong target = BitConverter.ToUInt64(raw, o);
				byte cat = raw[o + 10];
				byte kind = raw[o + 11];
				byte str = raw[o + 12];
				if (kind != 0x05 && kind != 0x01 && kind != 0x02 && kind != 0x44) continue;
				string name = ResolveTargetName(data, target, cat);
				if (name.Length > 0)
				{
					list.Add(new ClubRelation(i, begin + (ulong)o, target, cat, kind, str, name));
				}
			}
		}
		catch (MemoryAccessException)
		{
		}
		return list;
	}

	public static void SetStrength(GameData data, ClubRelation rel, byte strength, string clubName)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		byte clamped = Math.Clamp(strength, (byte)1, (byte)100);
		data.Session.History.Apply(data.Memory, rel.EntryAddress + 12, new[] { clamped },
			new EditInfo(clubName, $"{rel.KindLabel}: {rel.TargetName}", rel.Strength.ToString(), clamped.ToString()), guarded: true);
	}

	public static void Remove(GameData data, ulong clubAddress, int index, string label, string clubName)
	{
		if (AppSettings.Current.ReadOnlyMode)
		{
			throw new InvalidOperationException(Build.T("Salt okunur mod açık.", "Read-only mode is on."));
		}
		IMemory m = data.Memory;
		ulong begin = m.ReadPointer(clubAddress + RelVector);
		ulong end = m.ReadPointer(clubAddress + RelVector + 8);
		int count = (int)((end - begin) / EntrySize);
		if (index < 0 || index >= count) return;
		byte[] raw = m.ReadBytes(begin, count * EntrySize);
		using (data.Session.History.BeginBatch(Build.T($"{clubName}: İlişki sil ({label})", $"{clubName}: Remove relation ({label})")))
		{
			for (int i = index; i < count - 1; i++)
			{
				byte[] next = raw.AsSpan((i + 1) * EntrySize, EntrySize).ToArray();
				data.Session.History.Apply(m, begin + (ulong)(i * EntrySize), next, new EditInfo(clubName, Build.T("Kulüp ilişkisi", "Club relation"), "", ""), guarded: true);
			}
			data.Session.History.Apply(m, clubAddress + RelVector + 8, BitConverter.GetBytes(end - EntrySize),
				new EditInfo(clubName, Build.T("Kulüp ilişkisi", "Club relation"), label, "—"), guarded: true);
		}
	}

	public static UIElement BuildTab(GameData data, ClubRow club, INavigator nav)
	{
		var host = new ContentControl();
		void Fill()
		{
			var rels = Read(data, club.Address);
			var grid = new Grid { MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });

			UIElement MakeList(IEnumerable<ClubRelation> items, string emptyTr, string emptyEn)
			{
				var stack = new StackPanel { Margin = new Thickness(12, 4, 12, 10) };
				var arr = items.ToList();
				if (arr.Count == 0)
				{
					stack.Children.Add(Build.Text(Build.T(emptyTr, emptyEn), 12, Build.Res("Muted")));
					return stack;
				}
				bool canWrite = !AppSettings.Current.ReadOnlyMode;
				foreach (ClubRelation rel in arr)
				{
					ClubRelation current = rel;
					var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
					var del = new Button { Content = "✕", Style = Build.StyleOf("GhostButton"), Padding = new Thickness(6, 1, 6, 1), FontSize = 11, Margin = new Thickness(6, 0, 0, 0), IsEnabled = canWrite, ToolTip = Build.T("Sil (Ctrl+Z geri alır)", "Remove (Ctrl+Z undoes)") };
					del.Click += (_, _) =>
					{
						Remove(data, club.Address, current.Index, current.TargetName, club.Name);
						nav.SetStatus(Build.T($"✓ {club.Name}: {current.TargetName} ilişkisi silindi. Ctrl+Z geri alır.", $"✓ {club.Name}: removed relation with {current.TargetName}. Ctrl+Z undoes it."));
						Fill();
					};
					var strBox = new TextBox
					{
						Text = current.Strength.ToString(),
						Width = 52,
						Height = 25,
						FontSize = 12,
						HorizontalContentAlignment = HorizontalAlignment.Center,
						VerticalContentAlignment = VerticalAlignment.Center,
						IsReadOnly = !canWrite,
						ToolTip = Build.T("İlişki gücü (1–100 · Enter ile kaydet)", "Relation strength (1–100 · Enter to save)")
					};
					strBox.KeyDown += (_, e) =>
					{
						if (e.Key == System.Windows.Input.Key.Enter && byte.TryParse(strBox.Text.Trim(), out byte val))
						{
							SetStrength(data, current, val, club.Name);
							nav.SetStatus(Build.T($"✓ {club.Name} · {current.TargetName} gücü {Math.Clamp(val, (byte)1, (byte)100)} yapıldı.", $"✓ {club.Name} · {current.TargetName} strength set to {Math.Clamp(val, (byte)1, (byte)100)}."));
							Fill();
						}
					};
					DockPanel.SetDock(del, Dock.Right);
					DockPanel.SetDock(strBox, Dock.Right);
					row.Children.Add(del);
					row.Children.Add(strBox);
					row.Children.Add(Build.Text(current.TargetName, 12.5, Build.Res("Text"), FontWeights.SemiBold));
					stack.Children.Add(row);
				}
				return stack;
			}

			var left = new StackPanel();
			left.Children.Add(Build.Card(Build.T("🔥 Rakip Kulüpler (1-100)", "🔥 Rival Clubs (1-100)"),
				MakeList(rels.Where(r => r.Kind == 0x05), "Kayıtlı rakip kulüp yok.", "No rival clubs recorded.")));
			left.Children.Add(Build.Card(Build.T("⛔ Sevilmeyen Kişiler (1-100)", "⛔ Disliked Personnel (1-100)"),
				MakeList(rels.Where(r => r.Kind == 0x02), "Kayıtlı sevilmeyen kişi yok.", "No disliked personnel recorded.")));

			var right = new StackPanel();
			right.Children.Add(Build.Card(Build.T("⭐ Kulüp Efsaneleri, İkonlar & Favori Kişiler (1-100)", "⭐ Club Legends, Icons & Favoured Personnel (1-100)"),
				MakeList(rels.Where(r => r.Kind == 0x01 || r.Kind == 0x44), "Kayıtlı efsane/favori kişi yok.", "No legends or favoured personnel recorded.")));

			Grid.SetColumn(right, 1);
			grid.Children.Add(left);
			grid.Children.Add(right);
			host.Content = Build.Scroll(grid);
		}
		Fill();
		return host;
	}

	private static string ResolveTargetName(GameData data, ulong target, byte category)
	{
		if (target == 0) return "";
		if (category == 0x01 && data.ClubsByAddress.TryGetValue(target, out ClubRow? c))
		{
			return c.Name;
		}
		IMemory m = data.Memory;
		try
		{
			string ReadLenUtf8(ulong ptrField)
			{
				ulong sp = m.ReadPointer(ptrField);
				if (sp == 0) return "";
				ulong s = m.ReadPointer(sp);
				if (s == 0) return "";
				int len = m.Read<int>(s);
				return len > 0 && len < 64 ? Encoding.UTF8.GetString(m.ReadBytes(s + 4, len)) : "";
			}
			string full = (ReadLenUtf8(target + 0x50) + " " + ReadLenUtf8(target + 0x58)).Trim();
			return full;
		}
		catch (MemoryAccessException)
		{
			return "";
		}
	}
}
