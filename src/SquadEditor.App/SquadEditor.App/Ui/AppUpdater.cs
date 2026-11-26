using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

public sealed record UpdateInfo(
	string TagName,
	Version Version,
	string ReleaseNotes,
	string AssetName,
	string DownloadUrl,
	long AssetSize,
	string ReleasePageUrl);

public static class AppUpdater
{
	private const string LatestReleaseApi = "https://api.github.com/repos/yunusemreyilmaz93-pixel/SquadEditor/releases/latest";

	public static Version CurrentVersion { get; } = new Version(1, 6, 2);

	public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

	private static readonly HttpClient Http = CreateHttpClient();

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"SquadEditor/{CurrentVersionText}");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return client;
	}

	private static Version? ParseTagVersion(string? tag)
	{
		if (string.IsNullOrWhiteSpace(tag)) return null;
		string clean = tag.Trim().TrimStart('v', 'V');
		int dash = clean.IndexOf('-');
		if (dash > 0) clean = clean[..dash];
		if (!Version.TryParse(clean, out Version? v)) return null;
		return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
	}

	public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
			using HttpResponseMessage resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode) return null;

			await using Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
			JsonElement root = doc.RootElement;

			string tag = root.TryGetProperty("tag_name", out JsonElement tagEl) ? (tagEl.GetString() ?? "") : "";
			Version? remoteVer = ParseTagVersion(tag);
			if (remoteVer == null || remoteVer <= CurrentVersion) return null;

			string body = root.TryGetProperty("body", out JsonElement bodyEl) ? (bodyEl.GetString() ?? "") : "";
			string pageUrl = root.TryGetProperty("html_url", out JsonElement urlEl)
				? (urlEl.GetString() ?? "https://github.com/yunusemreyilmaz93-pixel/SquadEditor/releases/latest")
				: "https://github.com/yunusemreyilmaz93-pixel/SquadEditor/releases/latest";

			string assetName = "";
			string downloadUrl = "";
			long assetSize = 0;

			if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement a in assets.EnumerateArray())
				{
					string name = a.TryGetProperty("name", out JsonElement nEl) ? (nEl.GetString() ?? "") : "";
					string dl = a.TryGetProperty("browser_download_url", out JsonElement dEl) ? (dEl.GetString() ?? "") : "";
					long size = a.TryGetProperty("size", out JsonElement sEl) && sEl.TryGetInt64(out long sv) ? sv : 0;
					if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dl) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
					{
						assetName = name;
						downloadUrl = dl;
						assetSize = size;
						break;
					}
				}
			}

			if (string.IsNullOrEmpty(downloadUrl)) return null;

			return new UpdateInfo(
				TagName: tag.StartsWith('v') || tag.StartsWith('V') ? tag : "v" + remoteVer,
				Version: remoteVer,
				ReleaseNotes: body.Trim(),
				AssetName: assetName,
				DownloadUrl: downloadUrl,
				AssetSize: assetSize,
				ReleasePageUrl: pageUrl);
		}
		catch
		{
			return null;
		}
	}

	private static async Task<bool> DownloadAndApplyWindowsAsync(UpdateInfo info, IProgress<(double Ratio, long Done, long Total)>? progress, CancellationToken ct)
	{
		string? currentExe = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			OpenUrl(info.DownloadUrl);
			return false;
		}

		string exeDir = Path.GetDirectoryName(currentExe) ?? Path.GetTempPath();
		string tmpPath = Path.Combine(Path.GetTempPath(), $"SquadEditor_{info.TagName}.tmp");
		try
		{
			string probe = Path.Combine(exeDir, $"SquadEditor_{info.TagName}.tmp");
			using (File.Create(probe)) { }
			File.Delete(probe);
			tmpPath = probe;
		}
		catch
		{
		}

		using var dlClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		dlClient.DefaultRequestHeaders.UserAgent.ParseAdd($"SquadEditor/{CurrentVersionText}");

		using HttpResponseMessage resp = await dlClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
		resp.EnsureSuccessStatusCode();

		long total = resp.Content.Headers.ContentLength ?? info.AssetSize;
		await using (Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
		await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
		{
			byte[] buffer = new byte[65536];
			long done = 0;
			int read;
			while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
			{
				await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
				done += read;
				double ratio = total > 0 ? Math.Clamp((double)done / total, 0.0, 1.0) : 0.0;
				progress?.Report((ratio, done, total));
			}
			await dst.FlushAsync(ct).ConfigureAwait(false);
			if (done < 100_000 || (info.AssetSize > 0 && done != info.AssetSize))
				throw new IOException($"Incomplete download ({done} / {info.AssetSize} bytes).");
		}

		int pid = Environment.ProcessId;
		string scriptPath = Path.Combine(Path.GetTempPath(), $"squadeditor_update_{pid}.cmd");
		string script =
			"@echo off\r\n" +
			"setlocal\r\n" +
			$"set \"PID={pid}\"\r\n" +
			$"set \"SRC={tmpPath}\"\r\n" +
			$"set \"DST={currentExe}\"\r\n" +
			":wait\r\n" +
			"tasklist /FI \"PID eq %PID%\" 2>NUL | find /I \"%PID%\" >NUL\r\n" +
			"if not errorlevel 1 (\r\n" +
			"  timeout /t 1 /nobreak >NUL\r\n" +
			"  goto wait\r\n" +
			")\r\n" +
			"for /L %%i in (1,1,20) do (\r\n" +
			"  move /Y \"%SRC%\" \"%DST%\" >NUL 2>&1 && goto launch\r\n" +
			"  copy /Y \"%SRC%\" \"%DST%\" >NUL 2>&1 && del /F /Q \"%SRC%\" >NUL 2>&1 && goto launch\r\n" +
			"  timeout /t 1 /nobreak >NUL\r\n" +
			")\r\n" +
			":launch\r\n" +
			"start \"\" \"%DST%\"\r\n" +
			"del /F /Q \"%~f0\" >NUL 2>&1\r\n";

		await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, ct).ConfigureAwait(false);

		Process.Start(new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = $"/c \"\"{scriptPath}\"\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		});

		return true;
	}

	private static void OpenUrl(string url)
	{
		try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
	}

	public static string BadgeText(UpdateInfo info) => $"↑ {info.TagName} {L("btn_short")}";

	public static string BadgeTooltip(UpdateInfo info) =>
		string.Format(CultureInfo.CurrentCulture, L("tooltip"), info.TagName, CurrentVersionText);

	public static bool ShowUpdateDialog(Window owner, UpdateInfo info)
	{
		bool shouldShutdown = false;
		using var cts = new CancellationTokenSource();

		var win = new Window
		{
			Title = L("title"),
			Owner = owner,
			Width = 520,
			Height = 420,
			MinWidth = 440,
			MinHeight = 340,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = (FontFamily)Application.Current.Resources["UiFont"]
		};

		var root = new Grid { Margin = new Thickness(20) };
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
		head.Children.Add(Build.Text(string.Format(CultureInfo.CurrentCulture, L("heading"), info.TagName), 17, Build.Res("Text"), FontWeights.Bold));
		string sizeMb = info.AssetSize > 0 ? $"{info.AssetSize / (1024.0 * 1024.0):0.0} MB" : "";
		string subLine = string.Format(CultureInfo.CurrentCulture, L("sub"), CurrentVersionText, info.TagName)
			+ (sizeMb.Length > 0 ? $"  ·  {info.AssetName} ({sizeMb})" : "");
		var subTb = Build.Text(subLine, 12, Build.Res("Muted"));
		subTb.Margin = new Thickness(0, 4, 0, 0);
		head.Children.Add(subTb);
		Grid.SetRow(head, 0);
		root.Children.Add(head);

		var notesBorder = new Border
		{
			Background = Build.Res("Panel"),
			BorderBrush = Build.Res("Stroke"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(8),
			Padding = new Thickness(14, 12, 14, 12)
		};
		var notesScroll = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = new TextBlock
			{
				Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? L("no_notes") : info.ReleaseNotes,
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.5,
				LineHeight = 19,
				Foreground = Build.Res("Text")
			}
		};
		notesBorder.Child = notesScroll;
		Grid.SetRow(notesBorder, 1);
		root.Children.Add(notesBorder);

		var progPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
		var progText = Build.Text(L("downloading"), 12, Build.Res("Muted"));
		var progBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Value = 0, Margin = new Thickness(0, 6, 0, 0) };
		progPanel.Children.Add(progText);
		progPanel.Children.Add(progBar);
		Grid.SetRow(progPanel, 2);
		root.Children.Add(progPanel);

		var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		var btnGithub = new Button { Content = "GitHub", Padding = new Thickness(12, 7, 12, 7), Style = Build.StyleOf("GhostButton") };
		btnGithub.Click += (_, _) => OpenUrl(info.ReleasePageUrl);
		Grid.SetColumn(btnGithub, 0);
		footer.Children.Add(btnGithub);

		var btnLater = new Button { Content = L("later"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), Style = Build.StyleOf("GhostButton") };
		btnLater.Click += (_, _) => { cts.Cancel(); win.Close(); };
		Grid.SetColumn(btnLater, 2);
		footer.Children.Add(btnLater);

		var btnUpdate = new Button
		{
			Content = L("update_now"),
			Padding = new Thickness(16, 7, 16, 7),
			FontWeight = FontWeights.SemiBold,
			Background = Build.Res("Green"),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0)
		};
		btnUpdate.Click += async (_, _) =>
		{
			btnUpdate.IsEnabled = false;
			btnGithub.IsEnabled = false;
			progPanel.Visibility = Visibility.Visible;
			progText.Foreground = Build.Res("Muted");
			progText.Text = L("downloading");
			progBar.Value = 0;

			var progress = new Progress<(double Ratio, long Done, long Total)>(p =>
			{
				int pct = (int)Math.Round(p.Ratio * 100);
				progBar.Value = pct;
				double doneMb = p.Done / (1024.0 * 1024.0);
				double totalMb = p.Total / (1024.0 * 1024.0);
				progText.Text = p.Total > 0
					? $"{L("downloading")} {pct}%  ({doneMb:0.1} / {totalMb:0.1} MB)"
					: $"{L("downloading")} {doneMb:0.1} MB";
			});

			try
			{
				bool applied = await DownloadAndApplyWindowsAsync(info, progress, cts.Token);
				if (applied)
				{
					progBar.Value = 100;
					progText.Text = L("restarting");
					shouldShutdown = true;
					await Task.Delay(350);
					win.Close();
				}
				else
				{
					win.Close();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				progPanel.Visibility = Visibility.Visible;
				progText.Foreground = Build.Res("Red");
				progText.Text = $"{L("failed")} {ex.Message}";
				btnUpdate.IsEnabled = true;
				btnGithub.IsEnabled = true;
			}
		};
		Grid.SetColumn(btnUpdate, 3);
		footer.Children.Add(btnUpdate);

		Grid.SetRow(footer, 3);
		root.Children.Add(footer);

		win.Content = root;
		win.Closed += (_, _) => cts.Cancel();
		win.ShowDialog();
		return shouldShutdown;
	}

	private static string L(string key)
	{
		string lang = Loc.Instance.Language;
		if (Strings.TryGetValue(lang, out Dictionary<string, string>? dict) && dict.TryGetValue(key, out string? val))
			return val;
		return Strings["en"].GetValueOrDefault(key, key);
	}

	private static readonly Dictionary<string, Dictionary<string, string>> Strings = new(StringComparer.OrdinalIgnoreCase)
	{
		["tr"] = new() { ["btn_short"] = "Güncelle", ["tooltip"] = "Yeni sürüm mevcut: {0} (Mevcut: v{1})", ["title"] = "Güncelleme Mevcut", ["heading"] = "SquadEditor {0} yayında!", ["sub"] = "Mevcut sürüm: v{0} → Yeni sürüm: {1}", ["no_notes"] = "Yeni sürüm indirilmeye hazır.", ["update_now"] = "Şimdi Güncelle", ["later"] = "Sonra", ["downloading"] = "İndiriliyor…", ["restarting"] = "Güncelleme uygulanıyor, yeniden başlatılıyor…", ["failed"] = "Güncelleme başarısız:" },
		["en"] = new() { ["btn_short"] = "Update", ["tooltip"] = "New version available: {0} (Current: v{1})", ["title"] = "Update Available", ["heading"] = "SquadEditor {0} is available!", ["sub"] = "Current: v{0} → New: {1}", ["no_notes"] = "A new version is ready to install.", ["update_now"] = "Update Now", ["later"] = "Later", ["downloading"] = "Downloading…", ["restarting"] = "Applying update and restarting…", ["failed"] = "Update failed:" },
		["es"] = new() { ["btn_short"] = "Actualizar", ["tooltip"] = "Nueva versión disponible: {0} (Actual: v{1})", ["title"] = "Actualización disponible", ["heading"] = "¡SquadEditor {0} disponible!", ["sub"] = "Actual: v{0} → Nueva: {1}", ["no_notes"] = "Hay una nueva versión lista.", ["update_now"] = "Actualizar ahora", ["later"] = "Más tarde", ["downloading"] = "Descargando…", ["restarting"] = "Aplicando y reiniciando…", ["failed"] = "Error al actualizar:" },
		["pt"] = new() { ["btn_short"] = "Atualizar", ["tooltip"] = "Nova versão disponível: {0} (Atual: v{1})", ["title"] = "Atualização disponível", ["heading"] = "SquadEditor {0} disponível!", ["sub"] = "Atual: v{0} → Nova: {1}", ["no_notes"] = "Nova versão pronta para instalar.", ["update_now"] = "Atualizar agora", ["later"] = "Depois", ["downloading"] = "Baixando…", ["restarting"] = "Aplicando e reiniciando…", ["failed"] = "Falha ao atualizar:" },
		["fr"] = new() { ["btn_short"] = "Mettre à jour", ["tooltip"] = "Nouvelle version disponible : {0} (Actuelle : v{1})", ["title"] = "Mise à jour disponible", ["heading"] = "SquadEditor {0} est disponible !", ["sub"] = "Actuelle : v{0} → Nouvelle : {1}", ["no_notes"] = "Une nouvelle version est prête.", ["update_now"] = "Mettre à jour", ["later"] = "Plus tard", ["downloading"] = "Téléchargement…", ["restarting"] = "Application et redémarrage…", ["failed"] = "Échec :" },
		["de"] = new() { ["btn_short"] = "Update", ["tooltip"] = "Neue Version verfügbar: {0} (Aktuell: v{1})", ["title"] = "Update verfügbar", ["heading"] = "SquadEditor {0} ist verfügbar!", ["sub"] = "Aktuell: v{0} → Neu: {1}", ["no_notes"] = "Eine neue Version ist bereit.", ["update_now"] = "Jetzt aktualisieren", ["later"] = "Später", ["downloading"] = "Herunterladen…", ["restarting"] = "Update wird installiert…", ["failed"] = "Fehlgeschlagen:" },
		["it"] = new() { ["btn_short"] = "Aggiorna", ["tooltip"] = "Nuova versione disponibile: {0} (Attuale: v{1})", ["title"] = "Aggiornamento disponibile", ["heading"] = "SquadEditor {0} è disponibile!", ["sub"] = "Attuale: v{0} → Nuova: {1}", ["no_notes"] = "Una nuova versione è pronta.", ["update_now"] = "Aggiorna ora", ["later"] = "Più tardi", ["downloading"] = "Download…", ["restarting"] = "Installazione e riavvio…", ["failed"] = "Fallito:" },
		["nl"] = new() { ["btn_short"] = "Updaten", ["tooltip"] = "Nieuwe versie beschikbaar: {0} (Huidig: v{1})", ["title"] = "Update beschikbaar", ["heading"] = "SquadEditor {0} is beschikbaar!", ["sub"] = "Huidig: v{0} → Nieuw: {1}", ["no_notes"] = "Nieuwe versie staat klaar.", ["update_now"] = "Nu updaten", ["later"] = "Later", ["downloading"] = "Downloaden…", ["restarting"] = "Herstarten…", ["failed"] = "Mislukt:" },
		["pl"] = new() { ["btn_short"] = "Aktualizuj", ["tooltip"] = "Dostępna nowa wersja: {0} (Obecna: v{1})", ["title"] = "Dostępna aktualizacja", ["heading"] = "SquadEditor {0} jest dostępny!", ["sub"] = "Obecna: v{0} → Nowa: {1}", ["no_notes"] = "Nowa wersja jest gotowa.", ["update_now"] = "Aktualizuj teraz", ["later"] = "Później", ["downloading"] = "Pobieranie…", ["restarting"] = "Instalowanie i restart…", ["failed"] = "Błąd:" },
		["ru"] = new() { ["btn_short"] = "Обновить", ["tooltip"] = "Доступна новая версия: {0} (Текущая: v{1})", ["title"] = "Доступно обновление", ["heading"] = "Вышла версия SquadEditor {0}!", ["sub"] = "Текущая: v{0} → Новая: {1}", ["no_notes"] = "Новая версия готова.", ["update_now"] = "Обновить сейчас", ["later"] = "Позже", ["downloading"] = "Загрузка…", ["restarting"] = "Установка и перезапуск…", ["failed"] = "Ошибка:" },
		["ja"] = new() { ["btn_short"] = "更新", ["tooltip"] = "新しいバージョンがあります: {0} (現在: v{1})", ["title"] = "アップデートがあります", ["heading"] = "SquadEditor {0} が公開されました！", ["sub"] = "現在: v{0} → 最新: {1}", ["no_notes"] = "新しいバージョンをインストールできます。", ["update_now"] = "今すぐ更新", ["later"] = "後で", ["downloading"] = "ダウンロード中…", ["restarting"] = "更新して再起動中…", ["failed"] = "失敗:" },
		["ko"] = new() { ["btn_short"] = "업데이트", ["tooltip"] = "새 버전 사용 가능: {0} (현재: v{1})", ["title"] = "업데이트 가능", ["heading"] = "SquadEditor {0} 출시!", ["sub"] = "현재: v{0} → 새 버전: {1}", ["no_notes"] = "새 버전을 설치할 준비가 되었습니다.", ["update_now"] = "지금 업데이트", ["later"] = "나중에", ["downloading"] = "다운로드 중…", ["restarting"] = "업데이트 적용 및 재시작 중…", ["failed"] = "실패:" }
	};
}
