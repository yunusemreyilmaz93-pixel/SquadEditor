# SquadEditor

**Football Manager 27 için ücretsiz ve açık kaynak gerçek zamanlı editör.** Kaydını aç, SquadEditor'ı başlat; oyun açıkken oyuncuları, personeli ve kulüpleri düzenle.

[English](README.md)

> Durum: **0.3 (önizleme)**. FM27 27.0.1 (Steam, Windows) için. Sadece tek oyunculu kayıtlar. Düzenlemeden önce kaydını mutlaka yedekle.

## Özellikler

- **Kurulum yok, ayar yok.** Oyunu aç, kaydını yükle: SquadEditor oyunu kendisi bulur ve bağlanır. Layout dosyası, klasör seçimi yok.
- **Logo ve yüzler.** `Belgeler\Sports Interactive\Football Manager 27\graphics` klasöründeki paketlerin (oyunun kullandığı klasör) kendiliğinden okunur. İstersen Ayarlar'dan başka bir klasör seçebilirsin.
- **Oyuncular:** bütün teknik, zihinsel, fiziksel, kalecilik ve gizli özellikler, CA / PA, sahada mevki puanları, kondisyon, moral, değer, itibar (güncel, yerel, dünya), boy, doğum tarihi, maaş ve sözleşme bitişi.
- **Personel:** menajer, antrenör, gözlemci, fizyoterapist ve diğerleri: antrenörlük, zihinsel, bilgi ve medikal özellikler, kişilik, CA / PA, itibar, görev, maaş ve sözleşme.
- **Kulüpler:** A takım ve genç takımlara ayrılmış kadro, personel listesi, bakiye, transfer ve maaş bütçesi, bilet fiyatı, tesisler, stadyum, itibar ve taraftarlar.
- **İsim değiştirme, transfer, serbest oyuncu imzalama, kiralık ve personel taşıma:** oyuncu ve personelin adını değiştir, oyuncuyu başka kulübe taşı ya da serbest bırak, serbest oyuncu imzala, kiralık oyuncuyu geri çağır veya kalıcı yap, personeli başka kulübe taşı/görevden al. Sadece oyunun kendi belleğinin izin verdiği yazılır (oyunun kendi havuzları ve korumalı geri alma ile).
- **Dondurma (❄):** oyuncu ve personel özelliklerini oyun motoru düşüremesin diye tek tek ya da topluca kilitle, üst çubuktaki **Dondurulanlar** sekmesinden yönet.
- **Gözlemci filtreleri ve hızlı arama:** veritabanında mevki, yaş, CA/PA, serbest oyuncular, sakat/cezalı veya wonderkid filtreleri; aksan fark etmeyen genel arama (**Ctrl+F**) ve **F5** ile tek tık kayıt yenileme.
- **Sakatlık ve cezalar:** oyuncunun sakatlıklarını iyileştir ve cezalarını kaldır (tekil ya da kadroda çoklu seçimle).
- **Ön ayarlar** (Tam kondisyon ve moral, Wonderkid yap, Dünya yıldızı, Zengin patron, özellikleri fulle…): tek kayda ya da sağ tıkla kadroda seçtiğin bütün oyunculara.
- **Geri alma:** her değişiklik Ctrl+Z ile geri alınır.
- **Para senin istediğin gibi:** £, €, $ ya da ₺ (kur değiştirilebilir); maaşlar haftalık, aylık ya da yıllık.
- **Koyu ve açık tema, Türkçe ve İngilizce.**

## İndirme

[Releases](../../releases) sayfasından `SquadEditor-<sürüm>-win-x64.zip` dosyasını indir, aç ve `SquadEditor.exe`'yi çalıştır. Kurulum ve .NET gerekmez.

Sürüm henüz kod imzalı olmadığı için Windows *"Windows kişisel bilgisayarınızı korudu"* diyebilir: **Ek bilgi → Yine de çalıştır**.

## Kullanım

1. Football Manager 27'yi aç ve kaydını yükle.
2. SquadEditor'ı aç. Kendiliğinden bağlanır (kaydı okumak birkaç saniye sürer).
3. Oyuncu, personel ya da kulüp ara, değeri değiştir ve **Enter**'a bas. Ok tuşları sayıyı ±1 değiştirir, **Esc** vazgeçer, **Ctrl+Z** geri alır.
4. Değişikliklerin kalıcı olması için oyunu Football Manager'da kaydet.

Oyunun kendi ekranı, açık duran bir profilde eski değeri göstermeye devam edebilir: profili yeniden aç ya da Devam'a bas.

## Nasıl çalışır

SquadEditor oyunun belleğini okur ve yazar (`ReadProcessMemory` / `WriteProcessMemory`). Sabit adres kullanmaz: oyun kayıtlarını oyunun kendi sınıfları üzerinden bulur (MSVC RTTI, ör. `db::ACTUAL_PLAYER`); oyun bellekte yer değiştirse de çalışmaya devam eder. Alanların yerleri exe'nin içine gömülü bir JSON layout dosyasındadır (`layouts/fm27.json`); exe'nin yanındaki `layouts` klasörüne aynı adla bir dosya koyarsan o kullanılır.

Tersine mühendislik notları: [docs/fm27-notes.md](docs/fm27-notes.md). Bazı alanların yerleri [FMLibreEditor](https://github.com/tozmoz/FMLibreEditor) ve [fm-editor](https://github.com/barak14/fm-editor) projelerinin yayımladığı FM27 araştırmasıyla karşılaştırıldı; emeği geçenlere teşekkürler. Bu projelerden kod kullanılmadı. `tools/FmProbe`, layout'u çıkarmak için kullanılan salt okunur bellek aracı.

## Derleme

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) gerekir. Komutlar için [README.md](README.md#building).

## Yol haritası

- Yarışmalar ve uluslar
- Kulüpsüz personel işe alma

Katkı her zaman açık: bir alan eklemenin ya da yamadan sonra düzeltmenin en kolay yolu `layouts/fm27.json` ile pull request açmak.

## Sorumluluk reddi

SquadEditor bağımsız bir projedir; Sports Interactive veya SEGA ile bağlantılı değildir, onlar tarafından onaylanmamıştır. *Football Manager*, Sports Interactive / SEGA'nın ticari markasıdır. Tek oyunculu kayıtlarda, riski sana ait olmak üzere kullan ve yedek al.

## Lisans

[MIT](LICENSE)
