using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadEditor.App.Ui;

public static class FeedbackDialog
{
	private const string FeedbackEndpoint = "https://squadeditor.pages.dev/api/feedback";
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

	public static string TooltipText => L("tooltip");

	public static void Show(Window owner, bool fmConnected)
	{
		string selectedKind = "bug";
		string metaSummary = $"v{AppUpdater.CurrentVersionText} · Windows {RuntimeInformation.ProcessArchitecture} · {Loc.Instance.Language} · {(fmConnected ? "FM27+" : "FM27-")}";

		var win = new Window
		{
			Title = L("title"),
			Owner = owner,
			Width = 490,
			Height = 420,
			MinWidth = 420,
			MinHeight = 360,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Build.Res("Bg"),
			Foreground = Build.Res("Text"),
			FontFamily = (FontFamily)Application.Current.Resources["UiFont"]
		};

		var root = new Grid { Margin = new Thickness(20) };
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
		head.Children.Add(Build.Text(L("title"), 16.5, Build.Res("Text"), FontWeights.Bold));
		var subTb = Build.Text(L("sub"), 12, Build.Res("Muted"));
		subTb.Margin = new Thickness(0, 3, 0, 0);
		head.Children.Add(subTb);
		Grid.SetRow(head, 0);
		root.Children.Add(head);

		var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
		var kindButtons = new List<(string Kind, Button Btn)>();
		void RefreshKinds()
		{
			foreach (var (k, b) in kindButtons)
			{
				bool on = k == selectedKind;
				b.Background = on ? Build.Res("AccentSoft") : Build.Res("Panel");
				b.BorderBrush = on ? Build.Res("Accent") : Build.Res("Stroke");
				b.Foreground = Build.Res("Text");
				b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
			}
		}
		foreach (var (k, label) in new[] { ("bug", $"🐞 {L("bug")}"), ("feature", $"💡 {L("feature")}"), ("other", $"💬 {L("other")}") })
		{
			var btn = new Button
			{
				Content = label,
				Padding = new Thickness(12, 6, 12, 6),
				Margin = new Thickness(0, 0, 8, 0),
				BorderThickness = new Thickness(1)
			};
			btn.Click += (_, _) => { selectedKind = k; RefreshKinds(); };
			kindButtons.Add((k, btn));
			kindRow.Children.Add(btn);
		}
		RefreshKinds();
		Grid.SetRow(kindRow, 1);
		root.Children.Add(kindRow);

		var msgBox = new TextBox
		{
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			MaxLength = 2000,
			Background = Build.Res("Field"),
			Foreground = Build.Res("Text"),
			BorderBrush = Build.Res("Stroke"),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(10, 8, 10, 8),
			VerticalContentAlignment = VerticalAlignment.Top,
			ToolTip = L("msg_ph")
		};
		Grid.SetRow(msgBox, 2);
		root.Children.Add(msgBox);

		var contactPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
		contactPanel.Children.Add(Build.Text(L("contact_ph"), 11.5, Build.Res("Muted")));
		var contactBox = new TextBox
		{
			MaxLength = 120,
			Margin = new Thickness(0, 4, 0, 0),
			Background = Build.Res("Field"),
			Foreground = Build.Res("Text"),
			BorderBrush = Build.Res("Stroke"),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(10, 6, 10, 6)
		};
		contactPanel.Children.Add(contactBox);
		Grid.SetRow(contactPanel, 3);
		root.Children.Add(contactPanel);

		var metaText = Build.Text($"{L("auto_info")} {metaSummary}", 11, Build.Res("Muted"));
		metaText.Margin = new Thickness(0, 8, 0, 0);
		metaText.TextTrimming = TextTrimming.CharacterEllipsis;
		Grid.SetRow(metaText, 4);
		root.Children.Add(metaText);

		var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		var statusText = Build.Text("", 12, Build.Res("Muted"));
		statusText.VerticalAlignment = VerticalAlignment.Center;
		statusText.TextWrapping = TextWrapping.Wrap;
		statusText.Margin = new Thickness(0, 0, 10, 0);
		Grid.SetColumn(statusText, 0);
		footer.Children.Add(statusText);

		var btnClose = new Button { Content = L("close"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), Style = Build.StyleOf("GhostButton") };
		btnClose.Click += (_, _) => win.Close();
		Grid.SetColumn(btnClose, 1);
		footer.Children.Add(btnClose);

		var btnSend = new Button
		{
			Content = L("send"),
			Padding = new Thickness(16, 7, 16, 7),
			FontWeight = FontWeights.SemiBold,
			Background = Build.Res("Green"),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0)
		};
		btnSend.Click += async (_, _) =>
		{
			string msg = (msgBox.Text ?? "").Trim();
			if (msg.Length < 5)
			{
				statusText.Foreground = Build.Res("Red");
				statusText.Text = L("err_short");
				return;
			}

			btnSend.IsEnabled = false;
			btnSend.Content = L("sending");
			statusText.Foreground = Build.Res("Muted");
			statusText.Text = "";

			try
			{
				var payload = new
				{
					source = "app",
					kind = selectedKind,
					platform = "win",
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

				statusText.Foreground = Build.Res("Green");
				statusText.Text = L("ok");
				await Task.Delay(1100);
				win.Close();
			}
			catch
			{
				statusText.Foreground = Build.Res("Red");
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
		win.Loaded += (_, _) => msgBox.Focus();
		win.ShowDialog();
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
		["tr"] = new() { ["tooltip"] = "Hata bildir veya öneri gönder", ["title"] = "Hata Bildir / Öneri Gönder", ["sub"] = "Hesap gerekmez — doğrudan geliştiriciye ulaşır.", ["bug"] = "Hata", ["feature"] = "Özellik", ["other"] = "Yorum", ["msg_ph"] = "Yaşadığınız sorunu, önerinizi veya düşüncenizi kısaca yazın…", ["contact_ph"] = "İsteğe bağlı: X (@kullanici) veya e-posta", ["auto_info"] = "Otomatik eklenen:", ["send"] = "Gönder", ["sending"] = "Gönderiliyor…", ["close"] = "Kapat", ["ok"] = "Teşekkürler! Bildiriminiz ulaştı.", ["err_short"] = "Lütfen en az 5 karakter yazın.", ["err_fail"] = "Gönderilemedi. İnternet bağlantınızı kontrol edin." },
		["en"] = new() { ["tooltip"] = "Report a bug or send feedback", ["title"] = "Report Bug / Send Feedback", ["sub"] = "No account needed — goes straight to the developer.", ["bug"] = "Bug", ["feature"] = "Feature", ["other"] = "Comment", ["msg_ph"] = "Briefly describe the issue, suggestion, or feedback…", ["contact_ph"] = "Optional: X (@handle) or email", ["auto_info"] = "Auto-attached:", ["send"] = "Send", ["sending"] = "Sending…", ["close"] = "Close", ["ok"] = "Thanks! Your message has been received.", ["err_short"] = "Please enter at least 5 characters.", ["err_fail"] = "Could not send. Check your internet connection." },
		["es"] = new() { ["tooltip"] = "Reportar error o enviar sugerencia", ["title"] = "Reportar error / Sugerencia", ["sub"] = "Sin cuenta — llega directo al desarrollador.", ["bug"] = "Error", ["feature"] = "Sugerencia", ["other"] = "Comentario", ["msg_ph"] = "Describe el problema o sugerencia…", ["contact_ph"] = "Opcional: X (@usuario) o correo", ["auto_info"] = "Adjunto automático:", ["send"] = "Enviar", ["sending"] = "Enviando…", ["close"] = "Cerrar", ["ok"] = "¡Gracias! Recibido.", ["err_short"] = "Escribe al menos 5 caracteres.", ["err_fail"] = "No se pudo enviar." },
		["pt"] = new() { ["tooltip"] = "Relatar erro ou enviar sugestão", ["title"] = "Relatar erro / Sugestão", ["sub"] = "Sem conta — vai direto para o desenvolvedor.", ["bug"] = "Erro", ["feature"] = "Sugestão", ["other"] = "Comentário", ["msg_ph"] = "Descreva o problema ou sugestão…", ["contact_ph"] = "Opcional: X (@usuario) ou e-mail", ["auto_info"] = "Anexado:", ["send"] = "Enviar", ["sending"] = "Enviando…", ["close"] = "Fechar", ["ok"] = "Obrigado! Recebido.", ["err_short"] = "Digite pelo menos 5 caracteres.", ["err_fail"] = "Falha ao enviar." },
		["fr"] = new() { ["tooltip"] = "Signaler un bug ou envoyer un avis", ["title"] = "Signaler un bug / Avis", ["sub"] = "Sans compte — envoyé au développeur.", ["bug"] = "Bug", ["feature"] = "Idée", ["other"] = "Avis", ["msg_ph"] = "Décrivez le problème ou votre idée…", ["contact_ph"] = "Optionnel : X (@pseudo) ou e-mail", ["auto_info"] = "Joint :", ["send"] = "Envoyer", ["sending"] = "Envoi…", ["close"] = "Fermer", ["ok"] = "Merci ! Reçu.", ["err_short"] = "Minimum 5 caractères.", ["err_fail"] = "Échec de l'envoi." },
		["de"] = new() { ["tooltip"] = "Fehler melden oder Feedback senden", ["title"] = "Fehler melden / Feedback", ["sub"] = "Kein Konto nötig — direkt an den Entwickler.", ["bug"] = "Fehler", ["feature"] = "Idee", ["other"] = "Kommentar", ["msg_ph"] = "Problem oder Vorschlag beschreiben…", ["contact_ph"] = "Optional: X (@name) oder E-Mail", ["auto_info"] = "Angehängt:", ["send"] = "Senden", ["sending"] = "Wird gesendet…", ["close"] = "Schließen", ["ok"] = "Danke! Angekommen.", ["err_short"] = "Mindestens 5 Zeichen.", ["err_fail"] = "Senden fehlgeschlagen." },
		["it"] = new() { ["tooltip"] = "Segnala un bug o invia feedback", ["title"] = "Segnala bug / Feedback", ["sub"] = "Nessun account — arriva allo sviluppatore.", ["bug"] = "Bug", ["feature"] = "Idea", ["other"] = "Commento", ["msg_ph"] = "Descrivi il problema o l'idea…", ["contact_ph"] = "Opzionale: X (@utente) o email", ["auto_info"] = "Allegato:", ["send"] = "Invia", ["sending"] = "Invio…", ["close"] = "Chiudi", ["ok"] = "Grazie! Ricevuto.", ["err_short"] = "Almeno 5 caratteri.", ["err_fail"] = "Invio fallito." },
		["nl"] = new() { ["tooltip"] = "Bug melden of feedback sturen", ["title"] = "Bug melden / Feedback", ["sub"] = "Geen account nodig — naar de ontwikkelaar.", ["bug"] = "Bug", ["feature"] = "Idee", ["other"] = "Opmerking", ["msg_ph"] = "Beschrijf probleem of idee…", ["contact_ph"] = "Optioneel: X (@naam) of e-mail", ["auto_info"] = "Toegevoegd:", ["send"] = "Versturen", ["sending"] = "Verzenden…", ["close"] = "Sluiten", ["ok"] = "Bedankt! Ontvangen.", ["err_short"] = "Minimaal 5 tekens.", ["err_fail"] = "Verzenden mislukt." },
		["pl"] = new() { ["tooltip"] = "Zgłoś błąd lub wyślij opinię", ["title"] = "Zgłoś błąd / Opinia", ["sub"] = "Bez konta — trafia do autora.", ["bug"] = "Błąd", ["feature"] = "Pomysł", ["other"] = "Komentarz", ["msg_ph"] = "Opisz problem lub propozycję…", ["contact_ph"] = "Opcjonalnie: X (@nick) lub e-mail", ["auto_info"] = "Dołączono:", ["send"] = "Wyślij", ["sending"] = "Wysyłanie…", ["close"] = "Zamknij", ["ok"] = "Dzięki! Odebrano.", ["err_short"] = "Minimum 5 znaków.", ["err_fail"] = "Nie udało się wysłać." },
		["ru"] = new() { ["tooltip"] = "Сообщить об ошибке или предложить идею", ["title"] = "Сообщить об ошибке / Идея", ["sub"] = "Аккаунт не нужен — напрямую автору.", ["bug"] = "Ошибка", ["feature"] = "Идея", ["other"] = "Отзыв", ["msg_ph"] = "Опишите проблему или идею…", ["contact_ph"] = "Необязательно: X (@ник) или email", ["auto_info"] = "Прикреплено:", ["send"] = "Отправить", ["sending"] = "Отправка…", ["close"] = "Закрыть", ["ok"] = "Спасибо! Получено.", ["err_short"] = "Минимум 5 символов.", ["err_fail"] = "Не удалось отправить." },
		["ja"] = new() { ["tooltip"] = "バグ報告・ご要望を送る", ["title"] = "バグ報告・ご要望", ["sub"] = "アカウント不要・開発者へ届きます。", ["bug"] = "バグ", ["feature"] = "要望", ["other"] = "コメント", ["msg_ph"] = "不具合やご要望をご記入ください…", ["contact_ph"] = "任意：X (@ID) またはメール", ["auto_info"] = "自動添付:", ["send"] = "送信", ["sending"] = "送信中…", ["close"] = "閉じる", ["ok"] = "送信されました！", ["err_short"] = "5文字以上入力してください。", ["err_fail"] = "送信に失敗しました。" },
		["ko"] = new() { ["tooltip"] = "버그 신고 또는 건의 보내기", ["title"] = "버그 신고 / 건의", ["sub"] = "로그인 없이 개발자에게 전달됩니다.", ["bug"] = "버그", ["feature"] = "제안", ["other"] = "의견", ["msg_ph"] = "문제나 건의 사항을 적어주세요…", ["contact_ph"] = "선택: X (@아이디) 또는 이메일", ["auto_info"] = "자동 첨부:", ["send"] = "보내기", ["sending"] = "전송 중…", ["close"] = "닫기", ["ok"] = "접수되었습니다!", ["err_short"] = "최소 5자 이상 입력하세요.", ["err_fail"] = "전송에 실패했습니다." }
	};
}
