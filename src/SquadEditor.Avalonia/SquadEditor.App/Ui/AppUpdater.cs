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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

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

	private static readonly HttpClient Http = CreateHttpClient();

	public static Version CurrentVersion { get; } = ResolveCurrentVersion();

	public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"SquadEditor/{ResolveCurrentVersion()}");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return client;
	}

	private static Version ResolveCurrentVersion()
	{
		Version? v = typeof(AppUpdater).Assembly.GetName().Version;
		if (v == null) return new Version(1, 6, 0);
		return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
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
				bool isMac = OperatingSystem.IsMacOS();
				foreach (JsonElement a in assets.EnumerateArray())
				{
					string name = a.TryGetProperty("name", out JsonElement nEl) ? (nEl.GetString() ?? "") : "";
					string dl = a.TryGetProperty("browser_download_url", out JsonElement dEl) ? (dEl.GetString() ?? "") : "";
					long size = a.TryGetProperty("size", out JsonElement sEl) && sEl.TryGetInt64(out long sv) ? sv : 0;
					if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dl)) continue;

					bool match = isMac
						? name.Contains("macos", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
						: name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
					if (match)
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

	public static async Task<bool> DownloadAndApplyAsync(UpdateInfo info, IProgress<(double Ratio, long Done, long Total)>? progress, CancellationToken ct = default)
	{
		if (OperatingSystem.IsWindows())
			return await DownloadAndApplyWindowsAsync(info, progress, ct).ConfigureAwait(false);
		if (OperatingSystem.IsMacOS())
			return await DownloadAndApplyMacAsync(info, progress, ct).ConfigureAwait(false);

		OpenUrl(info.ReleasePageUrl);
		return false;
	}

	private static async Task DownloadFileAsync(string url, string destinationPath, long expectedSize, IProgress<(double Ratio, long Done, long Total)>? progress, CancellationToken ct)
	{
		using var dlClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		dlClient.DefaultRequestHeaders.UserAgent.ParseAdd($"SquadEditor/{CurrentVersionText}");

		using HttpResponseMessage resp = await dlClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
		resp.EnsureSuccessStatusCode();

		long total = resp.Content.Headers.ContentLength ?? expectedSize;
		await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		await using var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);

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
		if (done < 100_000 || (expectedSize > 0 && done != expectedSize))
			throw new IOException($"Incomplete download ({done} / {expectedSize} bytes).");
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
			// Prefer same directory for atomic rename when writable
			string probe = Path.Combine(exeDir, $"SquadEditor_{info.TagName}.tmp");
			using (File.Create(probe)) { }
			File.Delete(probe);
			tmpPath = probe;
		}
		catch
		{
			// Fall back to %TEMP%
		}

		await DownloadFileAsync(info.DownloadUrl, tmpPath, info.AssetSize, progress, ct).ConfigureAwait(false);

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

	private static string? FindMacAppBundlePath(string? processPath)
	{
		if (string.IsNullOrWhiteSpace(processPath)) return null;
		string? dir = Path.GetDirectoryName(processPath);
		while (!string.IsNullOrEmpty(dir))
		{
			if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
				return dir;
			dir = Path.GetDirectoryName(dir);
		}
		return null;
	}

	private static async Task<bool> DownloadAndApplyMacAsync(UpdateInfo info, IProgress<(double Ratio, long Done, long Total)>? progress, CancellationToken ct)
	{
		string? appBundle = FindMacAppBundlePath(Environment.ProcessPath);
		if (string.IsNullOrEmpty(appBundle))
		{
			OpenUrl(info.DownloadUrl);
			return false;
		}

		int pid = Environment.ProcessId;
		string zipPath = $"/tmp/squadeditor_update_{pid}.zip";
		string stageDir = $"/tmp/squadeditor_stage_{pid}";
		string scriptPath = $"/tmp/squadeditor_update_{pid}.sh";

		await DownloadFileAsync(info.DownloadUrl, zipPath, info.AssetSize, progress, ct).ConfigureAwait(false);

		string escApp = appBundle.Replace("'", "'\\''");
		string script =
			"#!/bin/sh\n" +
			$"PID={pid}\n" +
			$"ZIP='{zipPath}'\n" +
			$"STAGE='{stageDir}'\n" +
			$"APP='{escApp}'\n" +
			"while kill -0 \"$PID\" 2>/dev/null; do sleep 0.3; done\n" +
			"rm -rf \"$STAGE\" && mkdir -p \"$STAGE\"\n" +
			"if ditto -xk \"$ZIP\" \"$STAGE\"; then\n" +
			"  NEW_APP=$(find \"$STAGE\" -maxdepth 2 -name '*.app' -type d | head -n 1)\n" +
			"  if [ -n \"$NEW_APP\" ]; then\n" +
			"    BAK=\"${APP}.old\"\n" +
			"    rm -rf \"$BAK\"\n" +
			"    mv \"$APP\" \"$BAK\" && mv \"$NEW_APP\" \"$APP\" && rm -rf \"$BAK\"\n" +
			"    chmod +x \"$APP/Contents/MacOS/SquadEditor\" \"$APP/Contents/MacOS/SquadEditorCore\" 2>/dev/null || true\n" +
			"  fi\n" +
			"fi\n" +
			"rm -rf \"$STAGE\" \"$ZIP\"\n" +
			"open \"$APP\"\n" +
			"rm -f \"$0\"\n";

		await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), ct).ConfigureAwait(false);

		Process.Start(new ProcessStartInfo
		{
			FileName = "/bin/sh",
			Arguments = $"\"{scriptPath}\"",
			UseShellExecute = false,
			CreateNoWindow = true
		});

		return true;
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch
		{
		}
	}

	public static string BadgeText(UpdateInfo info) => $"↑ {info.TagName} {L("btn_short")}";

	public static string BadgeTooltip(UpdateInfo info) =>
		string.Format(CultureInfo.CurrentCulture, L("tooltip"), info.TagName, CurrentVersionText);

	private static IBrush Res(string key, IBrush fallback) =>
		Application.Current?.TryFindResource(key, out object? v) == true && v is IBrush b ? b : fallback;

	public static async Task<bool> ShowUpdateDialogAsync(Window owner, UpdateInfo info)
	{
		bool shouldShutdown = false;
		using var cts = new CancellationTokenSource();

		IBrush bg = Res("Bg", Brushes.Black);
		IBrush panel = Res("Panel", Brushes.DarkSlateGray);
		IBrush stroke = Res("Stroke", Brushes.Gray);
		IBrush text = Res("Text", Brushes.White);
		IBrush muted = Res("Muted", Brushes.LightGray);
		IBrush green = Res("Green", Brushes.SeaGreen);
		IBrush red = Res("Red", Brushes.IndianRed);

		var win = new Window
		{
			Title = L("title"),
			Width = 520,
			Height = 420,
			MinWidth = 440,
			MinHeight = 340,
			CanResize = true,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = bg,
			Foreground = text
		};

		var root = new Grid { Margin = new Thickness(20) };
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

		// Header
		var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 14) };
		head.Children.Add(new TextBlock
		{
			Text = string.Format(CultureInfo.CurrentCulture, L("heading"), info.TagName),
			FontSize = 17,
			FontWeight = FontWeight.Bold,
			Foreground = text
		});
		string sizeMb = info.AssetSize > 0
			? $"{info.AssetSize / (1024.0 * 1024.0):0.0} MB"
			: "";
		string subLine = string.Format(CultureInfo.CurrentCulture, L("sub"), CurrentVersionText, info.TagName)
			+ (sizeMb.Length > 0 ? $"  ·  {info.AssetName} ({sizeMb})" : "");
		head.Children.Add(new TextBlock
		{
			Text = subLine,
			FontSize = 12,
			Foreground = muted
		});
		Grid.SetRow(head, 0);
		root.Children.Add(head);

		// Release notes box
		var notesBorder = new Border
		{
			Background = panel,
			BorderBrush = stroke,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(8),
			Padding = new Thickness(14, 12)
		};
		var notesScroll = new ScrollViewer
		{
			Content = new TextBlock
			{
				Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? L("no_notes") : info.ReleaseNotes,
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.5,
				LineHeight = 19,
				Foreground = text
			}
		};
		notesBorder.Child = notesScroll;
		Grid.SetRow(notesBorder, 1);
		root.Children.Add(notesBorder);

		// Progress panel (shown during download)
		var progPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
		var progText = new TextBlock { FontSize = 12, Foreground = muted, Text = L("downloading") };
		var progBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Value = 0 };
		progPanel.Children.Add(progText);
		progPanel.Children.Add(progBar);
		Grid.SetRow(progPanel, 2);
		root.Children.Add(progPanel);

		// Footer buttons
		var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

		var btnGithub = new Button { Content = "GitHub", Padding = new Thickness(12, 7) };
		if (Application.Current?.TryFindResource("GhostButton", out object? ghostTheme) == true && ghostTheme is Avalonia.Styling.ControlTheme gt)
			btnGithub.Theme = gt;
		btnGithub.Click += (_, _) => OpenUrl(info.ReleasePageUrl);
		Grid.SetColumn(btnGithub, 0);
		footer.Children.Add(btnGithub);

		var btnLater = new Button { Content = L("later"), Padding = new Thickness(14, 7), Margin = new Thickness(0, 0, 8, 0) };
		if (Application.Current?.TryFindResource("GhostButton", out object? ghostTheme2) == true && ghostTheme2 is Avalonia.Styling.ControlTheme gt2)
			btnLater.Theme = gt2;
		btnLater.Click += (_, _) =>
		{
			cts.Cancel();
			win.Close();
		};
		Grid.SetColumn(btnLater, 2);
		footer.Children.Add(btnLater);

		var btnUpdate = new Button
		{
			Content = L("update_now"),
			Padding = new Thickness(16, 7),
			FontWeight = FontWeight.SemiBold,
			Background = green,
			Foreground = Brushes.White
		};
		btnUpdate.Click += async (_, _) =>
		{
			btnUpdate.IsEnabled = false;
			btnGithub.IsEnabled = false;
			progPanel.IsVisible = true;
			progText.Text = L("downloading");
			progBar.Value = 0;

			var progress = new Progress<(double Ratio, long Done, long Total)>(p =>
			{
				Dispatcher.UIThread.Post(() =>
				{
					int pct = (int)Math.Round(p.Ratio * 100);
					progBar.Value = pct;
					double doneMb = p.Done / (1024.0 * 1024.0);
					double totalMb = p.Total / (1024.0 * 1024.0);
					progText.Text = p.Total > 0
						? $"{L("downloading")} {pct}%  ({doneMb:0.1} / {totalMb:0.1} MB)"
						: $"{L("downloading")} {doneMb:0.1} MB";
				});
			});

			try
			{
				bool applied = await DownloadAndApplyAsync(info, progress, cts.Token);
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
				// User closed/cancelled
			}
			catch (Exception ex)
			{
				progPanel.IsVisible = true;
				progText.Foreground = red;
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
		win.Opened += (_, _) =>
		{
			WindowHelper.FitToScreen(win, isMain: false);
			WindowHelper.ApplyNativeTitleBar(win);
		};
		win.Closed += (_, _) => cts.Cancel();

		await win.ShowDialog(owner);
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
		["tr"] = new()
		{
			["btn_short"] = "Güncelle",
			["tooltip"] = "Yeni sürüm mevcut: {0} (Mevcut: v{1})",
			["title"] = "Güncelleme Mevcut",
			["heading"] = "SquadEditor {0} yayında!",
			["sub"] = "Mevcut sürüm: v{0} → Yeni sürüm: {1}",
			["no_notes"] = "Yeni sürüm indirilmeye hazır.",
			["update_now"] = "Şimdi Güncelle",
			["later"] = "Sonra",
			["downloading"] = "İndiriliyor…",
			["restarting"] = "Güncelleme uygulanıyor, yeniden başlatılıyor…",
			["failed"] = "Güncelleme başarısız:"
		},
		["en"] = new()
		{
			["btn_short"] = "Update",
			["tooltip"] = "New version available: {0} (Current: v{1})",
			["title"] = "Update Available",
			["heading"] = "SquadEditor {0} is available!",
			["sub"] = "Current: v{0} → New: {1}",
			["no_notes"] = "A new version is ready to install.",
			["update_now"] = "Update Now",
			["later"] = "Later",
			["downloading"] = "Downloading…",
			["restarting"] = "Applying update and restarting…",
			["failed"] = "Update failed:"
		},
		["es"] = new()
		{
			["btn_short"] = "Actualizar",
			["tooltip"] = "Nueva versión disponible: {0} (Actual: v{1})",
			["title"] = "Actualización disponible",
			["heading"] = "¡SquadEditor {0} disponible!",
			["sub"] = "Actual: v{0} → Nueva: {1}",
			["no_notes"] = "Hay una nueva versión lista para instalar.",
			["update_now"] = "Actualizar ahora",
			["later"] = "Más tarde",
			["downloading"] = "Descargando…",
			["restarting"] = "Aplicando actualización y reiniciando…",
			["failed"] = "Error al actualizar:"
		},
		["pt"] = new()
		{
			["btn_short"] = "Atualizar",
			["tooltip"] = "Nova versão disponível: {0} (Atual: v{1})",
			["title"] = "Atualização disponível",
			["heading"] = "SquadEditor {0} disponível!",
			["sub"] = "Atual: v{0} → Nova: {1}",
			["no_notes"] = "Uma nova versão está pronta para instalar.",
			["update_now"] = "Atualizar agora",
			["later"] = "Depois",
			["downloading"] = "Baixando…",
			["restarting"] = "Aplicando atualização e reiniciando…",
			["failed"] = "Falha ao atualizar:"
		},
		["fr"] = new()
		{
			["btn_short"] = "Mettre à jour",
			["tooltip"] = "Nouvelle version disponible : {0} (Actuelle : v{1})",
			["title"] = "Mise à jour disponible",
			["heading"] = "SquadEditor {0} est disponible !",
			["sub"] = "Actuelle : v{0} → Nouvelle : {1}",
			["no_notes"] = "Une nouvelle version est prête à être installée.",
			["update_now"] = "Mettre à jour",
			["later"] = "Plus tard",
			["downloading"] = "Téléchargement…",
			["restarting"] = "Application et redémarrage…",
			["failed"] = "Échec de la mise à jour :"
		},
		["de"] = new()
		{
			["btn_short"] = "Update",
			["tooltip"] = "Neue Version verfügbar: {0} (Aktuell: v{1})",
			["title"] = "Update verfügbar",
			["heading"] = "SquadEditor {0} ist verfügbar!",
			["sub"] = "Aktuell: v{0} → Neu: {1}",
			["no_notes"] = "Eine neue Version ist bereit.",
			["update_now"] = "Jetzt aktualisieren",
			["later"] = "Später",
			["downloading"] = "Herunterladen…",
			["restarting"] = "Update wird installiert, Neustart…",
			["failed"] = "Update fehlgeschlagen:"
		},
		["it"] = new()
		{
			["btn_short"] = "Aggiorna",
			["tooltip"] = "Nuova versione disponibile: {0} (Attuale: v{1})",
			["title"] = "Aggiornamento disponibile",
			["heading"] = "SquadEditor {0} è disponibile!",
			["sub"] = "Attuale: v{0} → Nuova: {1}",
			["no_notes"] = "Una nuova versione è pronta.",
			["update_now"] = "Aggiorna ora",
			["later"] = "Più tardi",
			["downloading"] = "Download…",
			["restarting"] = "Installazione e riavvio…",
			["failed"] = "Aggiornamento fallito:"
		},
		["nl"] = new()
		{
			["btn_short"] = "Updaten",
			["tooltip"] = "Nieuwe versie beschikbaar: {0} (Huidig: v{1})",
			["title"] = "Update beschikbaar",
			["heading"] = "SquadEditor {0} is beschikbaar!",
			["sub"] = "Huidig: v{0} → Nieuw: {1}",
			["no_notes"] = "Er staat een nieuwe versie klaar.",
			["update_now"] = "Nu updaten",
			["later"] = "Later",
			["downloading"] = "Downloaden…",
			["restarting"] = "Update toepassen en herstarten…",
			["failed"] = "Update mislukt:"
		},
		["pl"] = new()
		{
			["btn_short"] = "Aktualizuj",
			["tooltip"] = "Dostępna nowa wersja: {0} (Obecna: v{1})",
			["title"] = "Dostępna aktualizacja",
			["heading"] = "SquadEditor {0} jest dostępny!",
			["sub"] = "Obecna: v{0} → Nowa: {1}",
			["no_notes"] = "Nowa wersja jest gotowa do instalacji.",
			["update_now"] = "Aktualizuj teraz",
			["later"] = "Później",
			["downloading"] = "Pobieranie…",
			["restarting"] = "Instalowanie i ponowne uruchamianie…",
			["failed"] = "Błąd aktualizacji:"
		},
		["ru"] = new()
		{
			["btn_short"] = "Обновить",
			["tooltip"] = "Доступна новая версия: {0} (Текущая: v{1})",
			["title"] = "Доступно обновление",
			["heading"] = "Вышла версия SquadEditor {0}!",
			["sub"] = "Текущая: v{0} → Новая: {1}",
			["no_notes"] = "Новая версия готова к установке.",
			["update_now"] = "Обновить сейчас",
			["later"] = "Позже",
			["downloading"] = "Загрузка…",
			["restarting"] = "Установка и перезапуск…",
			["failed"] = "Ошибка обновления:"
		},
		["ja"] = new()
		{
			["btn_short"] = "更新",
			["tooltip"] = "新しいバージョンがあります: {0} (現在: v{1})",
			["title"] = "アップデートがあります",
			["heading"] = "SquadEditor {0} が公開されました！",
			["sub"] = "現在: v{0} → 最新: {1}",
			["no_notes"] = "新しいバージョンをインストールできます。",
			["update_now"] = "今すぐ更新",
			["later"] = "後で",
			["downloading"] = "ダウンロード中…",
			["restarting"] = "更新を適用して再起動中…",
			["failed"] = "更新に失敗しました:"
		},
		["ko"] = new()
		{
			["btn_short"] = "업데이트",
			["tooltip"] = "새 버전 사용 가능: {0} (현재: v{1})",
			["title"] = "업데이트 가능",
			["heading"] = "SquadEditor {0} 출시!",
			["sub"] = "현재: v{0} → 새 버전: {1}",
			["no_notes"] = "새 버전을 설치할 준비가 되었습니다.",
			["update_now"] = "지금 업데이트",
			["later"] = "나중에",
			["downloading"] = "다운로드 중…",
			["restarting"] = "업데이트 적용 및 재시작 중…",
			["failed"] = "업데이트 실패:"
		}
	};
}
