using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SquadEditor.App.Ui;

public static class FeedbackDialog
{
	private const string FeedbackEndpoint = "https://squadeditor.pages.dev/api/feedback";
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

	public static string TooltipText => L("tooltip");

	private static IBrush Res(string key, IBrush fallback) =>
		Application.Current?.TryFindResource(key, out object? v) == true && v is IBrush b ? b : fallback;

	public static async Task ShowAsync(Window owner, bool fmConnected)
	{
		IBrush bg = Res("Bg", Brushes.Black);
		IBrush panel = Res("Panel", Brushes.DarkSlateGray);
		IBrush field = Res("Field", Brushes.Black);
		IBrush stroke = Res("Stroke", Brushes.Gray);
		IBrush text = Res("Text", Brushes.White);
		IBrush muted = Res("Muted", Brushes.LightGray);
		IBrush accent = Res("Accent", Brushes.DodgerBlue);
		IBrush accentSoft = Res("AccentSoft", Brushes.MidnightBlue);
		IBrush green = Res("Green", Brushes.SeaGreen);
		IBrush red = Res("Red", Brushes.IndianRed);

		string selectedKind = "bug";
		string platform = OperatingSystem.IsMacOS() ? "mac" : OperatingSystem.IsWindows() ? "win" : "other";
		string metaSummary = $"v{AppUpdater.CurrentVersionText} · {(platform == "mac" ? "macOS" : "Windows")} {RuntimeInformation.ProcessArchitecture} · {Loc.Instance.Language} · {(fmConnected ? "FM27+" : "FM27-")}";

		var win = new Window
		{
			Title = L("title"),
			Width = 490,
			Height = 415,
			MinWidth = 420,
			MinHeight = 360,
			CanResize = true,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = bg,
			Foreground = text
		};

		var root = new Grid { Margin = new Thickness(20) };
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

		// Header
		var head = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 12) };
		head.Children.Add(new TextBlock { Text = L("title"), FontSize = 16.5, FontWeight = FontWeight.Bold, Foreground = text });
		head.Children.Add(new TextBlock { Text = L("sub"), FontSize = 12, Foreground = muted });
		Grid.SetRow(head, 0);
		root.Children.Add(head);

		// Kind selector pills
		var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
		var kindButtons = new List<(string Kind, Button Btn)>();
		void RefreshKinds()
		{
			foreach (var (k, b) in kindButtons)
			{
				bool on = k == selectedKind;
				b.Background = on ? accentSoft : panel;
				b.BorderBrush = on ? accent : stroke;
				b.Foreground = text;
				b.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
			}
		}
		foreach (var (k, label) in new[] { ("bug", $"🐞 {L("bug")}"), ("feature", $"💡 {L("feature")}"), ("other", $"💬 {L("other")}") })
		{
			var btn = new Button
			{
				Content = label,
				Padding = new Thickness(12, 6),
				CornerRadius = new CornerRadius(7),
				BorderThickness = new Thickness(1)
			};
			btn.Click += (_, _) => { selectedKind = k; RefreshKinds(); };
			kindButtons.Add((k, btn));
			kindRow.Children.Add(btn);
		}
		RefreshKinds();
		Grid.SetRow(kindRow, 1);
		root.Children.Add(kindRow);

		// Message box
		var msgBox = new TextBox
		{
			Watermark = L("msg_ph"),
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			MaxLength = 2000,
			Background = field,
			Foreground = text,
			BorderBrush = stroke,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(7),
			Padding = new Thickness(10, 8),
			VerticalContentAlignment = VerticalAlignment.Top
		};
		Grid.SetRow(msgBox, 2);
		root.Children.Add(msgBox);

		// Optional contact box
		var contactBox = new TextBox
		{
			Watermark = L("contact_ph"),
			MaxLength = 120,
			Margin = new Thickness(0, 10, 0, 0),
			Background = field,
			Foreground = text,
			BorderBrush = stroke,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(7),
			Padding = new Thickness(10, 7)
		};
		Grid.SetRow(contactBox, 3);
		root.Children.Add(contactBox);

		// Auto-attached technical info note
		var metaText = new TextBlock
		{
			Text = $"{L("auto_info")} {metaSummary}",
			FontSize = 11,
			Foreground = muted,
			Margin = new Thickness(0, 8, 0, 0),
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Grid.SetRow(metaText, 4);
		root.Children.Add(metaText);

		// Footer
		var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

		var statusText = new TextBlock
		{
			FontSize = 12,
			Foreground = muted,
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 0, 10, 0)
		};
		Grid.SetColumn(statusText, 0);
		footer.Children.Add(statusText);

		var btnClose = new Button { Content = L("close"), Padding = new Thickness(14, 7), Margin = new Thickness(0, 0, 8, 0) };
		if (Application.Current?.TryFindResource("GhostButton", out object? ghostTheme) == true && ghostTheme is Avalonia.Styling.ControlTheme gt)
			btnClose.Theme = gt;
		btnClose.Click += (_, _) => win.Close();
		Grid.SetColumn(btnClose, 1);
		footer.Children.Add(btnClose);

		var btnSend = new Button
		{
			Content = L("send"),
			Padding = new Thickness(16, 7),
			FontWeight = FontWeight.SemiBold,
			Background = green,
			Foreground = Brushes.White
		};
		btnSend.Click += async (_, _) =>
		{
			string msg = (msgBox.Text ?? "").Trim();
			if (msg.Length < 5)
			{
				statusText.Foreground = red;
				statusText.Text = L("err_short");
				return;
			}

			btnSend.IsEnabled = false;
			btnSend.Content = L("sending");
			statusText.Foreground = muted;
			statusText.Text = "";

			try
			{
				var payload = new
				{
					source = "app",
					kind = selectedKind,
					platform,
					version = AppUpdater.CurrentVersionText,
					lang = Loc.Instance.Language,
					contact = (contactBox.Text ?? "").Trim(),
					message = msg,
					meta = $"{RuntimeInformation.OSDescription} | {metaSummary}"
				};
				string json = JsonSerializer.Serialize(payload);
				using var content = new StringContent(json, Encoding.UTF8, "application/json");
				using HttpResponseMessage resp = await Http.PostAsync(FeedbackEndpoint, content);
				if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");

				statusText.Foreground = green;
				statusText.Text = L("ok");
				await Task.Delay(1100);
				win.Close();
			}
			catch
			{
				statusText.Foreground = red;
				statusText.Text = L("err_fail");
				btnSend.IsEnabled = true;
				btnSend.Content = L("send");
			}
		};
		Grid.SetColumn(btnSend, 2);
		footer.Children.Add(btnSend);

		Grid.SetRow(footer, 5);
		root.Children.Add(footer);

		win.Content = root;
		win.Opened += (_, _) =>
		{
			WindowHelper.FitToScreen(win, isMain: false);
			WindowHelper.ApplyNativeTitleBar(win);
			msgBox.Focus();
		};

		await win.ShowDialog(owner);
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
			["tooltip"] = "Hata bildir veya öneri gönder",
			["title"] = "Hata Bildir / Öneri Gönder",
			["sub"] = "Hesap gerekmez — doğrudan geliştiriciye ulaşır.",
			["bug"] = "Hata",
			["feature"] = "Özellik",
			["other"] = "Yorum",
			["msg_ph"] = "Yaşadığınız sorunu, önerinizi veya düşüncenizi kısaca yazın…",
			["contact_ph"] = "İsteğe bağlı: X (@kullanici) veya e-posta",
			["auto_info"] = "Otomatik eklenen:",
			["send"] = "Gönder",
			["sending"] = "Gönderiliyor…",
			["close"] = "Kapat",
			["ok"] = "Teşekkürler! Bildiriminiz ulaştı.",
			["err_short"] = "Lütfen en az 5 karakter yazın.",
			["err_fail"] = "Gönderilemedi. İnternet bağlantınızı kontrol edin."
		},
		["en"] = new()
		{
			["tooltip"] = "Report a bug or send feedback",
			["title"] = "Report Bug / Send Feedback",
			["sub"] = "No account needed — goes straight to the developer.",
			["bug"] = "Bug",
			["feature"] = "Feature",
			["other"] = "Comment",
			["msg_ph"] = "Briefly describe the issue, suggestion, or feedback…",
			["contact_ph"] = "Optional: X (@handle) or email",
			["auto_info"] = "Auto-attached:",
			["send"] = "Send",
			["sending"] = "Sending…",
			["close"] = "Close",
			["ok"] = "Thanks! Your message has been received.",
			["err_short"] = "Please enter at least 5 characters.",
			["err_fail"] = "Could not send. Check your internet connection."
		},
		["es"] = new()
		{
			["tooltip"] = "Reportar error o enviar sugerencia",
			["title"] = "Reportar error / Sugerencia",
			["sub"] = "Sin cuenta — llega directo al desarrollador.",
			["bug"] = "Error", ["feature"] = "Sugerencia", ["other"] = "Comentario",
			["msg_ph"] = "Describe brevemente el problema o sugerencia…",
			["contact_ph"] = "Opcional: X (@usuario) o correo",
			["auto_info"] = "Adjunto automático:",
			["send"] = "Enviar", ["sending"] = "Enviando…", ["close"] = "Cerrar",
			["ok"] = "¡Gracias! Tu mensaje ha sido recibido.",
			["err_short"] = "Escribe al menos 5 caracteres.",
			["err_fail"] = "No se pudo enviar. Revisa tu conexión."
		},
		["pt"] = new()
		{
			["tooltip"] = "Relatar erro ou enviar sugestão",
			["title"] = "Relatar erro / Sugestão",
			["sub"] = "Sem conta — vai direto para o desenvolvedor.",
			["bug"] = "Erro", ["feature"] = "Sugestão", ["other"] = "Comentário",
			["msg_ph"] = "Descreva brevemente o problema ou sugestão…",
			["contact_ph"] = "Opcional: X (@usuario) ou e-mail",
			["auto_info"] = "Anexado automaticamente:",
			["send"] = "Enviar", ["sending"] = "Enviando…", ["close"] = "Fechar",
			["ok"] = "Obrigado! Sua mensagem foi recebida.",
			["err_short"] = "Digite pelo menos 5 caracteres.",
			["err_fail"] = "Falha ao enviar. Verifique sua conexão."
		},
		["fr"] = new()
		{
			["tooltip"] = "Signaler un bug ou envoyer un avis",
			["title"] = "Signaler un bug / Avis",
			["sub"] = "Sans compte — envoyé directement au développeur.",
			["bug"] = "Bug", ["feature"] = "Idée", ["other"] = "Avis",
			["msg_ph"] = "Décrivez brièvement le problème ou votre idée…",
			["contact_ph"] = "Optionnel : X (@pseudo) ou e-mail",
			["auto_info"] = "Joint automatiquement :",
			["send"] = "Envoyer", ["sending"] = "Envoi…", ["close"] = "Fermer",
			["ok"] = "Merci ! Votre message a bien été reçu.",
			["err_short"] = "Veuillez saisir au moins 5 caractères.",
			["err_fail"] = "Échec de l'envoi. Vérifiez votre connexion."
		},
		["de"] = new()
		{
			["tooltip"] = "Fehler melden oder Feedback senden",
			["title"] = "Fehler melden / Feedback",
			["sub"] = "Kein Konto nötig — geht direkt an den Entwickler.",
			["bug"] = "Fehler", ["feature"] = "Idee", ["other"] = "Kommentar",
			["msg_ph"] = "Beschreibe kurz das Problem oder deinen Vorschlag…",
			["contact_ph"] = "Optional: X (@name) oder E-Mail",
			["auto_info"] = "Automatisch angehängt:",
			["send"] = "Senden", ["sending"] = "Wird gesendet…", ["close"] = "Schließen",
			["ok"] = "Danke! Deine Nachricht ist angekommen.",
			["err_short"] = "Bitte mindestens 5 Zeichen eingeben.",
			["err_fail"] = "Senden fehlgeschlagen. Verbindung prüfen."
		},
		["it"] = new()
		{
			["tooltip"] = "Segnala un bug o invia feedback",
			["title"] = "Segnala bug / Feedback",
			["sub"] = "Nessun account — arriva diretto allo sviluppatore.",
			["bug"] = "Bug", ["feature"] = "Idea", ["other"] = "Commento",
			["msg_ph"] = "Descrivi brevemente il problema o la tua idea…",
			["contact_ph"] = "Opzionale: X (@utente) o email",
			["auto_info"] = "Allegato automaticamente:",
			["send"] = "Invia", ["sending"] = "Invio…", ["close"] = "Chiudi",
			["ok"] = "Grazie! Il tuo messaggio è stato ricevuto.",
			["err_short"] = "Inserisci almeno 5 caratteri.",
			["err_fail"] = "Invio fallito. Controlla la connessione."
		},
		["nl"] = new()
		{
			["tooltip"] = "Bug melden of feedback sturen",
			["title"] = "Bug melden / Feedback",
			["sub"] = "Geen account nodig — gaat direct naar de ontwikkelaar.",
			["bug"] = "Bug", ["feature"] = "Idee", ["other"] = "Opmerking",
			["msg_ph"] = "Beschrijf kort het probleem of je suggestie…",
			["contact_ph"] = "Optioneel: X (@naam) of e-mail",
			["auto_info"] = "Automatisch toegevoegd:",
			["send"] = "Versturen", ["sending"] = "Verzenden…", ["close"] = "Sluiten",
			["ok"] = "Bedankt! Je bericht is ontvangen.",
			["err_short"] = "Vul minimaal 5 tekens in.",
			["err_fail"] = "Verzenden mislukt. Controleer je verbinding."
		},
		["pl"] = new()
		{
			["tooltip"] = "Zgłoś błąd lub wyślij opinię",
			["title"] = "Zgłoś błąd / Opinia",
			["sub"] = "Bez konta — trafia prosto do autora.",
			["bug"] = "Błąd", ["feature"] = "Pomysł", ["other"] = "Komentarz",
			["msg_ph"] = "Opisz krótko problem lub swoją propozycję…",
			["contact_ph"] = "Opcjonalnie: X (@nick) lub e-mail",
			["auto_info"] = "Dołączono automatycznie:",
			["send"] = "Wyślij", ["sending"] = "Wysyłanie…", ["close"] = "Zamknij",
			["ok"] = "Dzięki! Wiadomość została odebrana.",
			["err_short"] = "Wpisz co najmniej 5 znaków.",
			["err_fail"] = "Nie udało się wysłać. Sprawdź połączenie."
		},
		["ru"] = new()
		{
			["tooltip"] = "Сообщить об ошибке или предложить идею",
			["title"] = "Сообщить об ошибке / Идея",
			["sub"] = "Аккаунт не нужен — сообщение придёт напрямую автору.",
			["bug"] = "Ошибка", ["feature"] = "Идея", ["other"] = "Отзыв",
			["msg_ph"] = "Кратко опишите проблему или предложение…",
			["contact_ph"] = "Необязательно: X (@ник) или email",
			["auto_info"] = "Прикреплено автоматически:",
			["send"] = "Отправить", ["sending"] = "Отправка…", ["close"] = "Закрыть",
			["ok"] = "Спасибо! Сообщение получено.",
			["err_short"] = "Введите минимум 5 символов.",
			["err_fail"] = "Не удалось отправить. Проверьте интернет."
		},
		["ja"] = new()
		{
			["tooltip"] = "バグ報告・ご要望を送る",
			["title"] = "バグ報告・ご要望",
			["sub"] = "アカウント不要・開発者へ直接届きます。",
			["bug"] = "バグ", ["feature"] = "要望", ["other"] = "コメント",
			["msg_ph"] = "不具合の内容やご要望を簡潔にご記入ください…",
			["contact_ph"] = "任意：X (@ID) またはメール",
			["auto_info"] = "自動添付:",
			["send"] = "送信", ["sending"] = "送信中…", ["close"] = "閉じる",
			["ok"] = "ありがとうございます！送信されました。",
			["err_short"] = "5文字以上入力してください。",
			["err_fail"] = "送信に失敗しました。接続を確認してください。"
		},
		["ko"] = new()
		{
			["tooltip"] = "버그 신고 또는 건의 보내기",
			["title"] = "버그 신고 / 건의",
			["sub"] = "로그인 없이 개발자에게 바로 전달됩니다.",
			["bug"] = "버그", ["feature"] = "제안", ["other"] = "의견",
			["msg_ph"] = "문제 상황이나 건의 사항을 간단히 적어주세요…",
			["contact_ph"] = "선택: X (@아이디) 또는 이메일",
			["auto_info"] = "자동 첨부:",
			["send"] = "보내기", ["sending"] = "전송 중…", ["close"] = "닫기",
			["ok"] = "감사합니다! 정상적으로 접수되었습니다.",
			["err_short"] = "최소 5자 이상 입력해 주세요.",
			["err_fail"] = "전송에 실패했습니다. 인터넷 연결을 확인하세요."
		}
	};
}
