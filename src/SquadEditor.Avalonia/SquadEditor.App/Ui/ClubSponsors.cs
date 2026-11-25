using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using SquadEditor.Core.Editing;
using SquadEditor.Core.Memory;

namespace SquadEditor.App.Ui;

/// <summary>
/// Reads and edits live FM27 club sponsorship deals at Club+0x130 -> (begin, end, cap) of 24-byte entries.
/// Every write goes through EditorSession.History so Undo cleanly reverts any sponsorship edit.
/// </summary>
public static class ClubSponsors
{
	public sealed class Deal
	{
		public ulong Address { get; init; }
		public byte Kind { get; init; }
		public uint StartRaw { get; init; }
		public uint EndRaw { get; init; }
		public DateTime? StartDate { get; init; }
		public DateTime? EndDate { get; init; }
		public long TotalGbp { get; init; }
		public long PerSeasonGbp { get; init; }
		public ushort RelegationCut { get; init; }
		public ushort ClubRep { get; init; }

		public string KindName => LabelForKind(Kind);
		public string PeriodText => $"{(StartDate.HasValue ? Dates.Show(StartDate.Value.ToString("dd.MM.yyyy")) : "—")} → {(EndDate.HasValue ? Dates.Show(EndDate.Value.ToString("dd.MM.yyyy")) : "—")}";
		public string PerSeasonText => Money.Format(PerSeasonGbp, false) + Build.T("/yıl", "/yr");
		public string TotalText => Money.Format(TotalGbp, false);
		public bool IsExpired(DateTime gameDate) => EndDate.HasValue && EndDate.Value < gameDate;
	}

	public static string LabelForKind(byte kind) => kind switch
	{
		1 => Build.T("Ana Forma Sponsoru", "Main Kit Sponsor"),
		2 => Build.T("İkincil Forma Sponsoru", "Secondary Kit Sponsor"),
		3 => Build.T("Stadyum İsim Hakkı", "Stadium Naming Rights"),
		4 => Build.T("Genel Sponsor", "General Sponsor"),
		7 => Build.T("Kulüp Üyeliği", "Club Membership"),
		8 => Build.T("Kol Sponsoru", "Sleeve Sponsor"),
		9 => Build.T("Antrenman Forması", "Training Kit Sponsor"),
		11 => Build.T("Saha Kenarı Reklam", "Perimeter Advertising"),
		12 => Build.T("Medya / Dijital Sponsor", "Media / Digital Sponsor"),
		13 => Build.T("Bölgesel Sponsor", "Regional Sponsor"),
		16 => Build.T("Yayın / Ticari Ortaklık", "Commercial Partner"),
		18 => Build.T("Ana Ticari Gelir", "Primary Commercial Deal"),
		19 => Build.T("Resmi Ortak", "Official Partner"),
		22 => Build.T("Forma Üreticisi", "Kit Manufacturer"),
		_ => Build.F("Sponsor (#{0})", "Sponsor (#{0})", kind)
	};

	private static DateTime? DecodeDate(uint raw)
	{
		int dayOfYear = (int)(raw & 0x1FF);
		int year = (int)(raw >> 16);
		if (year < 1901 || year > 2099 || dayOfYear < 1 || dayOfYear > 366) return null;
		try { return new DateTime(year, 1, 1).AddDays(dayOfYear - 1); }
		catch { return null; }
	}

	private static uint EncodeDate(DateTime dt, uint preserveHighBits)
	{
		uint day = (uint)Math.Clamp(dt.DayOfYear, 1, 366);
		uint highDayBits = preserveHighBits & 0xFE00u;
		return ((uint)dt.Year << 16) | highDayBits | day;
	}

	public static List<Deal> ReadAll(GameData data, ulong clubAddress)
	{
		var result = new List<Deal>();
		try
		{
			ulong vecRec = data.Memory.Read<ulong>(clubAddress + 0x130);
			if (!FmOffsets.LooksPtr(vecRec)) return result;
			ulong b = data.Memory.Read<ulong>(vecRec + 0x00);
			ulong e = data.Memory.Read<ulong>(vecRec + 0x08);
			if (!FmOffsets.LooksPtr(b) || e <= b || (e - b) > 0x400 || ((e - b) % 8) != 0) return result;
			int count = (int)((e - b) / 8);
			for (int i = 0; i < count; i++)
			{
				ulong dealAddr = data.Memory.Read<ulong>(b + (ulong)(i * 8));
				if (!FmOffsets.LooksPtr(dealAddr)) continue;
				uint startRaw = data.Memory.Read<uint>(dealAddr + 0x00);
				uint endRaw = data.Memory.Read<uint>(dealAddr + 0x04);
				uint total = data.Memory.Read<uint>(dealAddr + 0x08);
				uint perSeason = data.Memory.Read<uint>(dealAddr + 0x0C);
				ushort relCut = data.Memory.Read<ushort>(dealAddr + 0x10);
				ushort rep = data.Memory.Read<ushort>(dealAddr + 0x12);
				byte kind = data.Memory.Read<byte>(dealAddr + 0x14);
				result.Add(new Deal
				{
					Address = dealAddr,
					Kind = kind,
					StartRaw = startRaw,
					EndRaw = endRaw,
					StartDate = DecodeDate(startRaw),
					EndDate = DecodeDate(endRaw),
					TotalGbp = total,
					PerSeasonGbp = perSeason,
					RelegationCut = relCut,
					ClubRep = rep
				});
			}
		}
		catch
		{
		}
		return result;
	}

	private static void WriteBytes(GameData data, ulong addr, byte[] next, string entity, string field, string oldDesc, string newDesc)
	{
		data.Session.History.Apply(data.Memory, addr, next, new EditInfo(entity, field, oldDesc, newDesc), guarded: true);
	}

	public static void UpdateDeal(GameData data, Deal deal, long newPerSeasonGbp, DateTime newEndDate, ushort newRelCut, byte newKind, string clubName)
	{
		long clampedAnnual = Math.Clamp(newPerSeasonGbp, 0, 2_000_000_000L);
		DateTime start = deal.StartDate ?? data.GameDate;
		int years = Math.Max(1, newEndDate.Year - start.Year);
		long clampedTotal = Math.Clamp(clampedAnnual * years, clampedAnnual, 2_100_000_000L);
		uint endRaw = EncodeDate(newEndDate, deal.EndRaw);

		byte[] next = new byte[21];
		data.Memory.TryRead(deal.Address, next);
		BitConverter.TryWriteBytes(next.AsSpan(0x04, 4), endRaw);
		BitConverter.TryWriteBytes(next.AsSpan(0x08, 4), (uint)clampedTotal);
		BitConverter.TryWriteBytes(next.AsSpan(0x0C, 4), (uint)clampedAnnual);
		BitConverter.TryWriteBytes(next.AsSpan(0x10, 2), newRelCut);
		next[0x14] = newKind;

		WriteBytes(data, deal.Address, next,
			clubName,
			LabelForKind(newKind),
			deal.PerSeasonText,
			Money.Format(clampedAnnual, false) + Build.T("/yıl", "/yr"));
	}

	public static Control BuildCard(GameData data, ClubRow club, INavigator nav)
	{
		var host = new ContentControl();
		bool canWrite = !AppSettings.Current.ReadOnlyMode;

		void Fill()
		{
			var deals = ReadAll(data, club.Address);
			var stack = new StackPanel { Margin = new Thickness(12, 4, 12, 10) };

			long totalAnnual = deals.Where(d => !d.IsExpired(data.GameDate)).Sum(d => d.PerSeasonGbp);
			var activeCount = deals.Count(d => !d.IsExpired(data.GameDate));
			var summaryBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
			var countTxt = Build.Text(Build.F("{0} aktif anlaşma", "{0} active deals", activeCount), 12, Build.Res("Muted"));
			countTxt.VerticalAlignment = VerticalAlignment.Center;
			DockPanel.SetDock(countTxt, Dock.Right);
			summaryBar.Children.Add(countTxt);
			summaryBar.Children.Add(Build.Text(
				Build.F("Yıllık Sponsor Geliri: {0}", "Annual Sponsorship Income: {0}", Money.Format(totalAnnual, false)),
				13, Build.Res("Text"), FontWeight.SemiBold));
			stack.Children.Add(summaryBar);

			if (deals.Count > 0 && canWrite)
			{
				var btnRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

				var megaBtn = new Button
				{
					Content = Icons.Label(IconKind.PlusCircle, Build.T("Süper Sponsor Ekle (+50M/yıl)", "Add Mega Sponsor (+50M/yr)"), 14),
					Theme = Build.StyleOf("AccentButton"),
					Padding = new Thickness(11, 5, 11, 5),
					Margin = new Thickness(0, 0, 8, 4),
					FontSize = 12
				};
				ToolTip.SetTip(megaBtn, Build.T($"En küçük veya süresi dolmuş sponsorluk yuvasını 5 yıllık +50M/yıl Süper Sponsorluk anlaşmasına yükseltir ({PlatformPaths.UndoHint} geri alır)",
					$"Upgrades the smallest or expired sponsorship slot into a 5-year +50M/yr Mega Sponsorship deal ({PlatformPaths.UndoHint} undoes)"));
				megaBtn.Click += (_, _) =>
				{
					Deal target = deals.FirstOrDefault(d => d.IsExpired(data.GameDate))
						?? deals.OrderBy(d => d.PerSeasonGbp).First();
					long addGbp = (long)Math.Round(Money.FromDisplay(50_000_000, false));
					DateTime endDt = new DateTime(data.GameDate.Year + 5, 6, 30);
					using (data.Session.History.BeginBatch(Build.F("{0}: Süper Sponsor", "{0}: Mega Sponsor", club.Name)))
					{
						UpdateDeal(data, target, target.PerSeasonGbp + addGbp, endDt, 0, target.Kind == 0 ? (byte)4 : target.Kind, club.Name);
					}
					nav.SetStatus(Build.F("✓ {0}: Süper Sponsor eklendi (+50M/yıl, {1} sonu). {2} geri alır.", "✓ {0}: Mega Sponsor added (+50M/yr until {1}). {2} undoes it.", club.Name, endDt.Year, PlatformPaths.UndoHint));
					Fill();
				};

				var doubleBtn = new Button
				{
					Content = Icons.Label(IconKind.TrendingUp, Build.T("Tüm Sponsorları 2x Yap", "Double All Sponsors (2x)"), 14),
					Padding = new Thickness(11, 5, 11, 5),
					Margin = new Thickness(0, 0, 6, 4),
					FontSize = 12
				};
				ToolTip.SetTip(doubleBtn, Build.T($"Kulübün tüm aktif sponsorluk gelirlerini 2 katına çıkarır ({PlatformPaths.UndoHint} geri alır)",
					$"Doubles annual payout of all club sponsorship deals ({PlatformPaths.UndoHint} undoes)"));
				doubleBtn.Click += (_, _) =>
				{
					using (data.Session.History.BeginBatch(Build.F("{0}: Sponsorlar 2x", "{0}: Double sponsors", club.Name)))
					{
						foreach (Deal d in deals)
						{
							DateTime endDt = d.EndDate is { } ed && ed > data.GameDate ? ed : new DateTime(data.GameDate.Year + 3, 6, 30);
							UpdateDeal(data, d, Math.Max(d.PerSeasonGbp * 2L, 1_000_000L), endDt, d.RelegationCut, d.Kind, club.Name);
						}
					}
					nav.SetStatus(Build.F("✓ {0}: {1} sponsorluk anlaşması 2 katına çıkarıldı. {2} geri alır.", "✓ {0}: {1} sponsorship deals doubled. {2} undoes it.", club.Name, deals.Count, PlatformPaths.UndoHint));
					Fill();
				};

				btnRow.Children.Add(megaBtn);
				btnRow.Children.Add(doubleBtn);
				stack.Children.Add(btnRow);
			}

			if (deals.Count == 0)
			{
				stack.Children.Add(Build.Text(Build.T("Bu kulüp için kayıtlı sponsorluk anlaşması bulunamadı.", "No sponsorship deals found for this club."), 12, Build.Res("Muted")));
			}
			else
			{
				long maxAnnual = Math.Max(1, deals.Max(x => x.PerSeasonGbp));
				foreach (Deal d in deals.OrderBy(x => x.IsExpired(data.GameDate)).ThenByDescending(x => x.PerSeasonGbp))
				{
					Deal cur = d;
					bool expired = cur.IsExpired(data.GameDate);
					var itemStack = new StackPanel();
					var row = new DockPanel();
					if (canWrite)
					{
						var editBtn = new Button
						{
							Content = Icons.Glyph(IconKind.Edit, 13),
							Theme = Build.StyleOf("GhostButton"),
							Padding = new Thickness(6, 3, 6, 3),
							Margin = new Thickness(8, 0, 0, 0)
						};
						ToolTip.SetTip(editBtn, Build.T("Sponsor tutarını ve bitiş yılını düzenle", "Edit sponsor amount and end year"));
						editBtn.Click += async (_, _) =>
						{
							Window? owner = TopLevel.GetTopLevel(editBtn) as Window
								?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
							if (await OpenEditDialogAsync(owner, data, club, cur, nav)) Fill();
						};
						DockPanel.SetDock(editBtn, Dock.Right);
						row.Children.Add(editBtn);
					}
					var amt = Build.Text(cur.PerSeasonText, 12.5, expired ? Build.Res("Faint") : Build.Res("Positive"), FontWeight.Bold);
					amt.VerticalAlignment = VerticalAlignment.Center;
					DockPanel.SetDock(amt, Dock.Right);
					row.Children.Add(amt);

					var leftInfo = new StackPanel();
					var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
					titleLine.Children.Add(Build.Text(cur.KindName, 12.5, expired ? Build.Res("Muted") : Build.Res("Text"), FontWeight.SemiBold));
					if (expired)
					{
						var expTag = new Border
						{
							Background = Build.Res("Field"),
							CornerRadius = new CornerRadius(4),
							Padding = new Thickness(5, 1, 5, 1),
							Margin = new Thickness(6, 0, 0, 0),
							Child = Build.Text(Build.T("Bitti", "Expired"), 10, Build.Res("Faint"))
						};
						titleLine.Children.Add(expTag);
					}
					leftInfo.Children.Add(titleLine);
					leftInfo.Children.Add(Build.Text(cur.PeriodText + (cur.RelegationCut > 0 ? $" · -{cur.RelegationCut}%" : ""), 11, Build.Res("Faint")));
					row.Children.Add(leftInfo);
					itemStack.Children.Add(row);

					if (!expired && maxAnnual > 0)
					{
						double pct = Math.Clamp((double)cur.PerSeasonGbp / maxAnnual, 0.04, 1.0);
						var track = new Grid { Height = 3, Margin = new Thickness(0, 5, 0, 0), ClipToBounds = true };
						track.Children.Add(new Border { Background = Build.Res("Field"), CornerRadius = new CornerRadius(1.5) });
						var fill = new Border { Background = Build.Res("Accent"), CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
						track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * pct;
						track.Children.Add(fill);
						itemStack.Children.Add(track);
					}

					IBrush baseBg = Brushes.Transparent;
					var cardRow = new Border
					{
						Background = baseBg,
						BorderBrush = Build.Res("StrokeSoft"),
						BorderThickness = new Thickness(0, 0, 0, 1),
						CornerRadius = new CornerRadius(4),
						Padding = new Thickness(8, 6, 6, 6),
						Margin = new Thickness(0, 1, 0, 1),
						Child = itemStack
					};
					cardRow.PointerEntered += (_, _) => cardRow.Background = Build.Res("CardHover");
					cardRow.PointerExited += (_, _) => cardRow.Background = baseBg;
					stack.Children.Add(cardRow);
				}
			}

			host.Content = stack;
		}

		Fill();
		return Build.Card(Build.T("Sponsorluk Anlaşmaları", "Sponsorship Deals"), host);
	}

	private static async Task<bool> OpenEditDialogAsync(Window? owner, GameData data, ClubRow club, Deal deal, INavigator nav)
	{
		var dlg = new Window
		{
			Title = Build.F("Sponsor Düzenle — {0}", "Edit Sponsor — {0}", deal.KindName),
			Width = 400,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = Build.Font("UiFont")
		};
		WindowHelper.ApplyChrome(dlg);

		var stack = new StackPanel { Margin = new Thickness(18) };
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
		int r = 0;

		TextBox AddField(string lbl, string val)
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
			var t = Build.Text(lbl, 12.5, Build.Res("Text"));
			Grid.SetRow(t, r);
			grid.Children.Add(t);
			var tb = new TextBox { Text = val, Height = 26, MinHeight = 26, FontSize = 12.5, HorizontalContentAlignment = HorizontalAlignment.Right };
			Grid.SetRow(tb, r);
			Grid.SetColumn(tb, 1);
			grid.Children.Add(tb);
			r++;
			return tb;
		}

		double displayAnnual = Money.ToDisplay(deal.PerSeasonGbp, false);
		TextBox bAnnual = AddField(Build.F("Yıllık Tutar ({0})", "Annual Amount ({0})", Money.Current.Code), Math.Round(displayAnnual).ToString(CultureInfo.InvariantCulture));
		TextBox bEndYear = AddField(Build.T("Bitiş Yılı", "End Year"), (deal.EndDate?.Year ?? (data.GameDate.Year + 4)).ToString());
		TextBox bRelCut = AddField(Build.T("Düşme Kesintisi (%)", "Relegation Cut (%)"), deal.RelegationCut.ToString());
		stack.Children.Add(grid);

		var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
		var cancel = new Button { Content = Build.T("Vazgeç", "Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
		cancel.Click += (_, _) => dlg.Close(false);
		var save = new Button { Content = Build.T("Kaydet", "Save"), Theme = Build.StyleOf("AccentButton"), IsDefault = true };
		save.Click += (_, _) =>
		{
			if (!double.TryParse((bAnnual.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double amtDisp) || amtDisp < 0) return;
			if (!int.TryParse((bEndYear.Text ?? "").Trim(), out int endY) || endY < 2025 || endY > 2090) return;
			ushort rel = ushort.TryParse((bRelCut.Text ?? "").Trim(), out ushort rc) ? Math.Min(rc, (ushort)100) : (ushort)0;

			long gbp = (long)Math.Round(Money.FromDisplay(amtDisp, false));
			DateTime endDt = new DateTime(endY, deal.EndDate?.Month ?? 6, Math.Min(deal.EndDate?.Day ?? 30, DateTime.DaysInMonth(endY, deal.EndDate?.Month ?? 6)));
			UpdateDeal(data, deal, gbp, endDt, rel, deal.Kind, club.Name);
			nav.SetStatus(Build.F("✓ {0} sponsoru güncellendi ({1}/yıl, {2}). {3} geri alır.", "✓ Updated {0} sponsor ({1}/yr until {2}). {3} undoes it.", deal.KindName, Money.Format(gbp, false), endY, PlatformPaths.UndoHint));
			dlg.Close(true);
		};
		btns.Children.Add(cancel);
		btns.Children.Add(save);
		stack.Children.Add(btns);
		dlg.Content = stack;
		if (owner is { IsVisible: true })
		{
			return (await dlg.ShowDialog<bool?>(owner)) == true;
		}
		var tcs = new TaskCompletionSource<bool>();
		dlg.Closed += (_, _) => tcs.TrySetResult(false);
		dlg.Show();
		return await tcs.Task;
	}
}
