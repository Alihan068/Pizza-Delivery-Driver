# Rol, Kod Stili & Proje Durumu — Pizza-Delivery-Driver

## Güncel görev ve belge önceliği — 2026-09-10

**Aktif iş: Secondary map composition correction tamamlandı.** Tilemap üreticisinin yanlışlıkla yeniden oluşturduğu primary `GameScene` haritası son committen yalnızca harita olarak geri alındı; güncel gameplay sistemleri ve `PizzaPlace`/`PizzaCar` collection-point child hiyerarşisi korundu. NarrowDistrict ve Expressway şimdi daha büyük, daha yoğun, altı katmanlı tilemap düzeniyle ve kaynak-prefab adları görünen tutarlı dekor aileleriyle yeniden üretildi. Tilemap authoring standardı secondary mapler için geçerlidir ve `GameScene` bulk generation tarafından varsayılan olarak korunur. Claude Code/GPT-5.6 Luna dahil devralan uygulayıcı önce [dokümantasyon dizinini](memory-bank/README.md), [harita authoring sözleşmesini](memory-bank/pizza_map_authoring.md), [active context](memory-bank/activeContext.md), [handoff'u](memory-bank/drift_handoff.md) ve [doğrulama defterini](memory-bank/drift_verification.md) okur. Güncel uygulama sonucu `memory-bank/progress.md` içindedir; sonraki iş owner’ın Play Mode ve rota kontrolleridir. Gelecekteki şehir içi araç loop trafiği kaydedilmiştir, bu pass'te uygulanmamıştır.

**Kalıcı kullanıcı kararı:** Harita düzenleme, üretme veya yeniden oluşturma işlemleri `GameScene`'e dokunmayacak. Yeni haritalar ayrı scene/blueprint olarak oluşturulacak; mevcut secondary mapler kendi dosyalarında düzenlenecek. `GameScene` yalnızca kullanıcının açıkça istediği ayrı bir işlemle değiştirilebilir.

Aşağıdaki tarihli tarama/faz/ekonomi/MCP kayıtları geçmiş bağlamdır. Eski “şu an aşama 2”, “prompt yaz”, “iki araç”, “asmdef yok”, “sonraki iş finalize” cümleleri bugünkü iş sırasını belirlemez. Kalıcı commit, İngilizce kod/yorum, serialization ve guidelines kuralları geçerlidir. En yeni kullanıcı kararı ve aktif plan geçmiş tasarım kararlarının önündedir; kaldırılmış boost/turbo veya kapasiteye bağlı sipariş sistemi yeniden eklenmez.

Uygulama yetkisi geldiğinde her alt adım sonunda plan + handoff + progress/activeContext + test kaydı birlikte güncellenir. Owner checklist yalnız kullanıcının doğrulamasıdır; ajan onu kendi testleriyle tamamlayamaz. Çalıştırılmayan doğrulama Not Run/Not Measured olarak kalır. Kaynak uygulaması tamamlanmış maddeler canlı Unity kanıtı yoksa `[~]` kalır; bu, kodun yarım olduğu değil canlı kanıtın açık olduğu anlamına gelir.

Bu dosya kişisel, `.gitignore`'da — asla commit/push edilmez. Farklı bir session'dan bu projeye girersen, aşağıdakiler seninle konuşulmuş gibi geçerli — baştan sorma.

> ## ⚠️ ÖNCE BUNU OKU — `UNITY_AI_GUIDELINES.md`
>
> Proje kökünde **`UNITY_AI_GUIDELINES.md`** var (2026-09-03'te eklendi, `.gitignore`'da). Kod yazmadan önce **zorunlu okuma.** Unity/C# geliştirme için 22 bölümlük bağlayıcı kural seti: API doğruluğu, serialization güvenliği, SRP, kapsam izolasyonu, GC/hot-path kuralları, doğrulama raporu formatı.
>
> **Jenerik kuralların proje kararlarıyla çakıştığı yerler kılavuzun 0. bölümünde çözüldü** — oradaki kararlar bağlayıcıdır, yeniden tartışılmaz:
> - **Type-based `Find`**: tek örnekli tiplerde serbest, çok örnekli olabilenlerde `[SerializeField]`. İsim tabanlı `Find` yasak.
> - **Alanlar**: `[SerializeField]`, açık `private` yok; dışarısı kullanıyorsa `public`.
> - **XML doc**: yeni **ve değiştirilen** her public üye, ≥ 8/10.
> - **Görev sonu**: kılavuzun 13. bölümündeki **Verification Report** formatı zorunlu; ölçülmeyen şey "Not Measured" yazılır, uydurulmaz.
>
> ### Bilgi tabanı yapısı (2026-09-03'te kuruldu, hepsi `.gitignore`'da)
>
> | Dosya | İçerik |
> |---|---|
> | `memory-bank/projectbrief.md` | Oyun ne, hedef ne, kabul kriteri |
> | `memory-bank/productContext.md` | Tasarım kararları ve **gerekçeleri** |
> | `memory-bank/systemPatterns.md` | Fiilen kullanılan mimari desenler |
> | `memory-bank/techContext.md` | Sürümler, MCP kurulumu, **ortam kısıtları** |
> | `memory-bank/activeContext.md` | Şu an ne üzerinde çalışılıyor |
> | `memory-bank/progress.md` | Faz/hotfix durumu, doğrulanmamış kalanlar |
> | `knowledge/history/bug_fixes.md` | **Append-only** olay kaydı (BF-001…) — kök neden, çözüm, önleme |
> | `knowledge/history/engine_features.md` | Mevcut sistemlerin ne yaptığı |
>
> **Kural 4.1 gereği bu dosyalar kod değişikliğiyle birlikte, aynı görevde güncellenir** — sonraya bırakılmaz. Bug düzeltince `bug_fixes.md`'ye yeni bir BF kaydı eklenir (eskiler düzenlenmez).
>
> Bu AGENTS.md dosyası rol tanımı, commit kuralı ve tarihsel karar günlüğü olarak kalıyor; teknik detay yukarıdaki dosyalara taşındı.

## Rolüm: Tasarımcı + Yazılımcı (2026-09-01'de güncellendi)
**Artık kodu da ben yazıyorum.** Önceki "prompt yazıp yan bir Codex session'ına teslim etme" düzeni kaldırıldı — kullanıcı iki client arasında kopyala-yapıştır yapmak yerine tek oturumda devam etmeyi seçti.

**Çalışma şekli:** Taktik işler (bugfix, ayar düzeltmesi, küçük mekanik ekleme) için **plan mode** kullan — araştır, planı sun, onay al, uygula. Büyük tasarım kararları (yeni sistem, denge revizyonu) için önce uzman agent'lara danış (bkz. "Agent envanteri"), tasarımı kur, sonra uygula.

**Yetki:** Oyunun kendi dosyalarında (kod, sahne, prefab, asset, ScriptableObject) doğrudan değişiklik yapabilirim. `.md` dosyaları zaten serbestti.

**Hâlâ geçerli olan sınırlar:**
- **Commit kuralı** — aşağıdaki bölüm aynen geçerli, asla `git commit` çalıştırma. Bu kod yazma yetkisinden bağımsız, ayrı bir gerekçesi var.
- **Play Mode** — oturum hafızasındaki `feedback_playmode_focus` kuralı geçerli (kullanıcı aksini söylemedikçe test serbest).
- Geri döndürülmesi zor ya da yıkıcı işlemlerde (dosya silme, sahne yeniden düzenleme) önce ne yapacağımı söyle.

**Not — eski prompt düzeninin kalıntıları:** `oldUsedPromts/` klasöründeki promptlar ve bu dosyadaki bazı "prompt yazılacak" ifadeleri o dönemden. Arşiv olarak duruyorlar, iş akışı olarak geçersizler.

**AI izlerinin gizliliği:** Bu proje GitHub'da paylaşılacak ama yapay zeka kullanıldığı bariz görünmesin isteniyor — bu yüzden AGENTS.md gibi AI-sürecine özel `.md` dosyaları `.gitignore`'da kalmalı, asla commit edilmemeli.

## Commit kuralı — asla ihlal etme
**Bu repoda asla `git commit` çalıştırma.** Commit'ler GitHub'da senin (AI) adına görünür, kullanıcı bunu istemiyor — tüm commit'leri kendisi yapıyor. Büyük/önemli bir değişiklik tamamlandığında commit'i sen yapma, sadece kullanıcıya hatırlat. Kullanıcı konuşma içinde açıkça "commit et" derse bile, önce bu kuralı hatırlatıp teyit iste.

## Proje geçmişi — neden diğer 8 projeden farklı
Kullanıcı bu projede daha önce **MCP'siz, manuel ve "bilinçsiz" biçimde yoğun AI kullandığını** kendi ifadesiyle belirtti. Bu yüzden bu proje diğer 8 Unity projesinin ("Kod İmzası" raporu — bkz. altta) orijinal stil analizine dahil edilmedi. Kod tabanının mevcut hali muhtemelen kullanıcının doğal stilini tutarlı yansıtmıyor — düzeltmek/temizlemek üzerine çalışılıyor olabilir, varsayım yapmadan önce kodu gerçekten oku.

## Güncel uygulama planı ve kaynak önceliği (2026-09-07)

MCP kurulumu ve proje taraması tamamlandı; eski “Aşama 2'deyiz, tarama yapılacak” metni geçersizdir. Güncel 8 yüksek seviyeli faz, durum ve uygulama sırası `memory-bank/roadmap_1_0.md` içindedir. Ayrıntılı aktif handoff `memory-bank/plan_finalize.md` dosyasının en üstündeki courier rating migration bölümüdür. `memory-bank/activeContext.md` son oturum durumunu, `memory-bank/progress.md` uygulama durumunu, `memory-bank/owner_checklist.md` ise yalnızca sahibinin manuel kontrollerini tutar.

### Güncel courier rating kararı

`GameManager.totalReputation` geçiş döneminde courier rating save alanıdır. Rating vardiya performansına göre artabilir veya azalabilir, ancak 0'ın altına inemez. `CurrentRank` yalnızca görsel kademedir; rank veya `requiredRank` harita, araç, modifier ya da bölge açamaz. Kira ve tamir rating'i değiştiremez. Eski rank-gated bölümler tarihsel kayıttır ve yeni kod için kullanılmaz.

Sonraki kod işi önce **courier rating migration**; ardından D/4.9 map selection ve map ownership akışıdır. Workshop/Map Editor ve server-authoritative leaderboard/anti-cheat 1.0 sonrasıdır. Kullanıcı manuel testleri yalnızca `owner_checklist.md` üzerinden takip edilir; Codex bu maddeleri kendi doğrulaması gibi işaretlemez.

## MCP durumu (2026-08-25 itibarıyla güncel)
- `Packages/manifest.json`'a `com.coplaydev.unity-mcp` eklendi. Başlangıçta `v10.0.0`'a sabitlenmişti (main branch'i takip etmiyor, kasıtlı — kararlılık için); kullanıcı temel bağlantının kararlı çalıştığını gördükten sonra **v10.1.2'ye elle güncelledi**. `debug_request_context` ile doğrulandı: server v10.1.2 çalışıyor.
- Antigravity IDE'nin kendi "Manage MCPs" paneli ve Codex extension'ı (bu oturum) aynı UnityMCP session'ına bağlanıyor — ayrı ayrı doğrulama gerekmiyor, ikisi de aynı bağlantıyı paylaşıyor.
- Codex client kaydı `~/.Codex.json` içinde, bu projenin tam yoluna (`C:/Users/PcVIP/Documents/Unity Projects/Pizza-Delivery-Driver`) bağlı: `"UnityMCP": { "type": "http", "url": "http://127.0.0.1:8080/mcp" }`.
- **Kritik/tekrar unutulmasın:** Bu MCP aracı sadece Codex'un workspace kökü **tam olarak bu klasör** olduğunda görünür. Kök "Unity Projects" klasöründen açılan bir oturumda görünmez — ayrı bir pencere/oturum bu klasöre özel açılmalı.
- Bağlantı bir kez uçtan uca doğrulandı: Unity Editor bağlandı, 34 araç kaydoldu (`animation, asset_gen, core, docs, probuilder, profiling, scripting_ext, testing, ui, vfx`).
- **Bilinen kırılganlık:** Sunucuyu ben (Codex) manuel `uvx` komutuyla arka planda başlattığımda, o terminal/oturum kapanınca sunucu da sessizce durdu. **Bundan sonra sunucuyu Unity Editor'ün kendi "MCP for Unity" penceresinden ("Start Server" butonu) başlat** — Unity Editor'ün process ömrüne bağlı olduğu için daha kalıcı. Manuel `uvx` komutu (pencerede "Manual Server Launch" altında) sadece hızlı test için, günlük kullanım için değil.
- Sunucu başladıktan sonra ayrıca **"Connect"** butonuna basmak gerekiyor — "Start Server" tek başına yeterli değil, "No Session" kırmızı kalıyor, "Connect" ile oturum gerçekten kuruluyor.
- Doğrulama: yeni oturumda `/mcp` yaz ya da bir Unity aracı çağırmayı dene (örn. sahnedeki GameObject'leri listele).

### ÇÖZÜLDÜ (2026-08-25): Antigravity içindeki Codex extension'ında MCP görünmüyordu
**Sebep:** `~/.Codex.json` içinde bu proje için **iki ayrı kayıt** vardı — biri path'i büyük `C:/...` ile, diğeri küçük `c:/...` ile. Sadece büyük-C kaydında `mcpServers.UnityMCP` tanımlıydı, küçük-c kaydında `mcpServers: {}` boştu. Standalone Codex CLI büyük-C path'i kullanan bir oturumda "Configure" edilmişti; ama **Antigravity IDE içindeki Codex extension'ı** bu projeyi küçük-c working directory ile açıyor — Windows'ta dosya sistemi case-insensitive ama Codex'un proje-config lookup'ı string bazlı ve case-sensitive olduğu için extension hep boş kayda düşüyordu. "MCP for Unity" panelinde hem "Codex" hem "Antigravity IDE" client'ları "Configured" (yeşil) görünse bile, bu session'da UnityMCP aracı hiç yoktu.
**Çözüm:** `~/.Codex.json`'daki küçük-c kaydına da aynı `mcpServers.UnityMCP` bloğu manuel eklendi (dosya/kod değişikliği değil, tooling config — Codex'un doğrudan düzenleyebileceği kapsamda).
**Kalıcı ders:** Antigravity'nin Codex extension'ı bu projeyi hep küçük-c (`c:/Users/PcVIP/...`) path'iyle açıyor gibi görünüyor. İleride MCP (ya da başka path'e bağlı) bir ayar tekrar "görünmüyor" derse, önce `~/.Codex.json`'da aynı path'in birden fazla case-varyasyonuyla kayıtlı olup olmadığını kontrol et.

## Kod stili — bu projede doğrulandı (2026-08-25 tarama, Assets/Scripts altındaki 23 dosya)
Diğer 8 projede tespit edilen kişisel stil (bkz. [Kod İmzası raporu](https://Codex.ai/code/artifact/d3b44ac8-5c4e-44d8-a54e-10b8118e09d9)) taban alındı, bu projede aşağıdakiler fiilen kontrol edildi:
- **Düz alan/auto-property camelCase (public dahil):** doğrulandı, tutarlı (örn. `VehicleData.cs`, `GameManager.cs`).
- **`FindFirstObjectByType` sahne-içi referans bağlama için varsayılan:** doğrulandı — proje genelinde tutarlı kullanılmış, `FindObjectOfType` (eski/deprecated API) hiç yok.
- **Dosya adı = sınıf adı:** doğrulandı, 23 dosyanın tamamında birebir eşleşiyor, kırılma yok.
- **"Never hardcode anything":** büyük ölçüde uyulmuş — dengeleme sabitleri (hız, sağlık, kapasite seviyeleri vb.) `VehicleData` ScriptableObject'i üzerinden `[SerializeField]`/public alanlarla Inspector'a açılmış, koda gömülü değil.
- **K&R brace, henüz TUTARSIZ:** 23 dosyanın 17'si K&R (`class X {` aynı satırda), 6'sı Allman (brace alt satırda) kullanıyor — `DriverLights.cs`, `FollowCamera.cs`, `CustomerTarget.cs`, `SpritePool.cs`, `CollisionDetector.cs`, `DestroyOnTime.cs`. Yeni kod K&R ile yazılmalı; bu 6 dosya eski/vibe-code kalıntısı, düzeltme talep gelirse hatırla.

## Güncel sonraki adım

MCP kurulumu ve proje taraması tamamlandı; eski tarama raporu ve “kendi agent'ına prompt teslim et” iş akışı tarihsel kayıttır. Drift kaynak uygulaması ve kaynak kalite turu tamamlandı. Unity MCP instance bağlandığında `memory-bank/drift_handoff.md` içindeki D7.2 canlı import/Play Mode doğrulamasından devam edilir; ardından D7.3–D7.6 kanıtları kapatılır. `owner_checklist.md` yalnızca sahibinin manuel kontrolleridir; Codex bu maddeleri kendi doğrulaması gibi işaretlemez.

## Hedef ve tasarım kararları (2026-08-26)

> **TARİHSEL TASARIM KAYDI:** Bu bölümün rank/region/unlock ifadeleri 2026-09-07 courier rating kararı öncesine aittir. Yeni kodda kullanılmayacaklar. Güncel karar için `memory-bank/plan_finalize.md` üstündeki courier rating handoff'u kullan.
**Şu anki hedef: oyunu stable oynanabilir sürüme getirmek.** Kabul kriteri: *garajdan çıkıp bir seansı sonuna kadar oynayıp, kazanılan parayla garaja dönüp yükseltme yapıp tekrar çıkabilmek — üst üste, çökmeden.*

Kullanıcının verdiği ve **artık sabit olan** tasarım kararları:
1. **Tamir bedeli mekaniği eklenecek** (kullanıcının kendi fikri, mevcut kodda yok). Bölüm bitince kalan cana ve aracın ne kadar pahalı/yükseltilmiş olduğuna göre tamir bedeli ödenir. **Ölünürse tamir bedeli maksimum**, ayrıca **seans kazancının yarısı gider.** Bu, canı ekonomik bir kaynağa çeviriyor: dikkatli sürmek para kazandırıyor, pahalı/yükseltilmiş araçları işletmek riskli hale geliyor.
2. Seans **üç yolla** bitebilir: süre dolması + ölüm + ExtractionZone (erken çıkış, kazancı garantiler). Yani `ExtractionZone.cs` silinmeyecek, sahneye yerleştirilecek.
3. Seans süresi **5 dk → 3 dk** (`ScoreHandler.levelDurationInMinutes`).
4. Para **0'ın altına düşemez** — `AddMoneyToBank`'e clamp gelecek.

5. **Müşterilerin değişken sipariş adedi olacak.** Her müşteri spawn olurken, o seviye için tanımlı bir aralıktan (örn. 1-3) rastgele sipariş adedi ister. **Bu aralık seviye başına ayarlanabilir olmalı** — şu an tek harita var ama ileride başka haritalar/seviyeler gelecek, her birinin kendi sipariş aralığı olacak.
6. **Kısmi teslimat serbest.** Müşteri 2 pizza istiyorsa ve oyuncu 1 taşıyabiliyorsa, iki tur git-gel yaparak siparişi tamamlayabilir. Teslimat "hep ya da hiç" değil, biriktirmeli.
7. **Hız daha ölümcül olacak** ki zırh ve can gerçekten satın alınmaya değsin.

**5-7'nin gerekçesi (Economy Designer analizi, 2026-08-26):** 43 yükseltme seviyesinin 29'u ölü ya da zararlıydı. Health ve Armor işe yaramıyordu çünkü mevcut hasar formülüyle (`5 + hız*0.5`) ölüm olasılığı **%0.02** — 6.500 para hiç gerçekleşmeyen bir riske sigorta alıyordu. Capacity ise 3. seviyeden sonra **tuzaktı**: müşteriler pizza alımında 1:1 spawn olduğu için 10 pizza taşımak 10 paralel 40s sayacı demekti, çoğu dolmadan yetişilemiyordu (capacity 8'de net gelir **−127**, capacity 0'da **+180**). Sahibi bu sorunu Economy'nin önerdiği "waitTime'ı kapasiteyle ölçekle" çözümü yerine sipariş-adedi mekaniğiyle çözmeyi seçti — kapasiteyi "kaç turdan kurtulurum"a çeviriyor.

Bu kararların sayısal tarafı (tamir bedeli formülü, hız/hasar dengesi, sipariş aralığı, ilerleme hızı) ve `LevelData` veri yapısı için Game Designer, Economy Designer ve Unity Architect'e danışıldı. **Birleşik spesifikasyon: [Pizza Delivery Driver Dengesi](https://Codex.ai/code/artifact/41728b18-67ca-497a-af68-9f95da326392)** — tüm formüller, sabit listesi ve simülasyon sonuçları orada. Faz 1/3 promptları bu spec'ten yazılacak.

**Mevcut değerler placeholder sayılır (2026-08-26, kullanıcı beyanı):** "Bu yarım kalmış bir proje. Öncesinde koyduğum value'lar tamamen dengesiz zaten. Ekonomiyi, dengeyi baştan hazırlayabilirsin." Yani `VehicleData`'daki hiçbir sayı (`base`/`step`/`maxLevel`, 6 statın tamamı) ve `GetUpgradeCost` formülü kutsal değil — hepsi yeniden tasarlanabilir. **Şema aynı kalıyor, içindeki sayılar yeni.** Stat sayısının kendisi bile sorgulanabilir (6 stat × ~8 seviye = 43 seviye, 3 dakikalık arcade için fazla olabilir).

**Bunun sonucu:** aşağıdaki "üs ≤ 1.11" çıkmazı **evrensel bir kural değil, mevcut stat oranlarının bir sonucuydu.** Savunmanın hızdan hızlı ölçeklendiği yeni bir tablo kurulursa gerçek süperlineer hasar (p = 1.5–2.0) mümkün hale gelir. Sıfırdan tam denge tablosu için Economy Designer'a (sayılar) ve Game Designer'a (stat kimlikleri, stat sayısı) yeni brief verildi.

**Spec'ten çıkan en kritik yapısal kararlar:**
- **Hız aralığı daraltıldı: 3.0→10.5 (3.5×) yerine 4.0→6.5 (1.63×).** Kilidi açan hamle bu. Savunma 2.37× ölçeklendiği için artık `damageExponent = 2.0` mutlak çarpma hızına uygulanabiliyor — gerçek süperlineer hasar. (Eski tabloda hız 3.5× ölçeklendiği için üs ≤ 1.11 olmak zorundaydı ve hız yükseltmesi downgrade'e dönüşüyordu.) Kalan küçük açık, tam yükseltilmiş aracın hâlâ ölebilmesini sağlıyor — kasıtlı.
- **Toplam seviye 43 → 24.** `maxLevel` 3-5 bandında; her tek alım hemen sonraki seansta hissedilmeli. **Asimetrik derinlik en güçlü kaldıraç:** simetrik 5'er seviyede ölü seviye 11 iken, derinlikler kimliğe göre ayrılınca 4'e düştü.
- **"Sıfır ölü seviye" yapısal olarak imkânsız — ispatlandı.** Bir seansta TÜM çarpışma maliyeti 271 para; Handling+Chassis+Armor+Stabilizer'ın paylaştığı havuzun tamamı bu, ve statları aldıkça *küçülüyor*. Storage ise seviye başına +180-204 hareket ettiriyor çünkü gelir havuzu yatırımla *büyüyor*. **Kural: bir mitigasyon statının ömür boyu değeri, azalttığı kaybın üst sınırıyla kilitlidir.**
- **Sabit sipariş aralığı (1-3) capacity tuzağını ÇÖZMÜYOR.** Kapasite ancak `kapasite ≈ 2 × ortalama sipariş + 1` noktasına kadar amorti ediyor. Çözüm: `orderMax = max(orderMin+1, round(capacity * orderScale))`, `orderScale = 0.6`.
- **Turbo düzeltmesi bedavaya geliyor.** `turboBoost = 1.5` çarpanı `lastImpactSpeed`'e girdiği ve hasar karesel olduğu için turbo halinde çarpışma otomatik 2.25× hasar veriyor. Sadece `if (!turboMode)` guard'ı kaldırılacak — ekstra çarpan gerekmiyor. Bu aynı zamanda "maks araç ölebilmeli" kısıtını sağlıyor (%2.9 → %8.6).
- **Şema değişikliği gerekiyor:** `VehicleData`'ya stat başına `costMult` alanı + `GetUpgradeCost(statName, level)` imzası. Şu an tek global formül var, tek farklılaştırma kaldıracı `maxLevel`.

**Stat kimlikleri (Game Designer, bağlayıcı):** Her stat "bunu alırsam neyi YAPABİLİRİM"e cevap vermeli, "hangi kötü şey azalır"a değil. Ayrım cümlesi: **Chassis KOŞU satın alır, Armor PARA satın alır, Stabilizer SİPARİŞ satın alır.** Roller: Storage = temel/ilk alım çıpası (herkes alır), Speed+Handling = agresif build, Chassis+Armor = istikrarlı build, Stabilizer = wildcard/dürtüsel. **İlk alım Storage olmalı** (anında görünür, rota planlamayı öğretir, kendini geri öder); **Speed erken cazip olmamalı** (süperlineer hasar yeni oyuncuyu cezalandırır).

**Ön koşullar (bunlar olmadan hiçbir denge katsayısı tutmaz):** çarpışma dokunulmazlık penceresi (0.7s, yoksa zincirleme tetiklenme tek çarpmayı 2-4 katına çıkarıyor); `ObjectSpawner.maxObjectLimit`'in uygulanması; speedboost tavanı (1.5×, tavansız halde stok araçta bir boost + tam gaz = canın %174'ü); `sessionEarnings` havuzu; `Customer.waitTime` mutasyonunun düzeltilmesi; müşteri doğurmanın adet bazlı değil **talep bazlı** olması (`carryPizzaAmount > activeCustomers` sipariş adedini bilmiyor — 3 pizza alınca 3 müşteri spawn ediyor, `outstandingDemand`'a çevrilmeli).

**Doğrulanmış ekonomi kararları (Economy Designer, değiştirilmemeli):**
- **Tamir bedeli LİNEER olmalı** (`hpLost` ile doğru orantılı). Karesel ya da kademeli yapılırsa "az hasarla çık, tam canla dön" sömürüsü açılır — çünkü can seanslar arası bedava sıfırlanıyor (`Driver.InitializeStats` her seansta `GetHealth()` atıyor).
- **Süre tamamlama bonusuna gerek yok.** Marjinal analizle devam etmek zaten her zaman +EV; başabaş ölüm olasılığına (%17) ancak canın %25'inin altında iniliyor. Büyük bonus aktif zararlı — düşük canda akılcı olan çıkışı cezalandırıp oyuncuyu ölüme zorlar.
- **Banka koruma clamp'i:** `repair ≤ sessionKept + bankBefore*0.5`. Oyuncu daima bankasının en az yarısıyla çıkar; zenginde hiç devreye girmez, fakirde otomatik. Borç asla sonraki seansa taşınmaz, seansa giriş asla paraya bağlanmaz.
- **Ölüm cezası ağırlığı doğru:** her aşamada ~1 seans geri gidiş (0.9 / 1.06 / 1.15). Değiştirmeye gerek yok.
- **`ObjectSpawner.maxObjectLimit` hiç okunmuyor** → 3 dakikada ~126 engel doğuyor, çarpışma oranı seans sonuna doğru tırmanıyor. Bloklayıcı: bu düzeltilmeden hiçbir hasar/tamir dengesi tutmaz.

## Sahne akışı ve navigasyon (2026-08-26 kullanıcı kararı)
**Ana akış:** `MainMenu` → **Başla** → `GarageScene` → **StartJob** → `GameScene` → seans biter → `GarageScene` → `GameScene` → ... (garaj-oyun döngüsü sürer).

**Ana menüye dönüş yalnızca iki yoldan:**
1. Oyun içi **Pause ekranı** → Ana Menü
2. Garajdaki **"Ana Menüye Çık" butonu** — *henüz yok*. `GarageManager.OnClickMainMenu()` metodu var ama onu çağıran hiçbir buton sahnede bağlı değil (ölü kod). Buton eklenip bağlanacak.

**Pause ekranı (GameScene):** Devam / Garaja Dön / Ana Menü. **Projede şu an hiçbir pause kodu yok** — `timeScale`, `Pause`, `Resume` aramaları sıfır sonuç veriyor, sıfırdan yazılacak.

### ⚠️ Pause → Garaja Dön bir sömürü deliği açıyor — çözülmesi şart
Pause'dan garaja dönmek, seansı bitirmenin **üçüncü yolu**. Kazanç korunursa extraction mekaniği tamamen ölür: oyuncu extraction bölgesine gitmek zorunda kalmadan, üstelik ölmek üzereyken duraklatıp kaçarak ölüm cezasından da sıyrılır.

**Karar (Codex önerisi, kullanıcı aksini söylemedikçe geçerli):** Pause'dan çıkmak **seansı terk etmektir** — `sessionEarnings` tamamen yanar, ama ölüm cezası uygulanmaz (tamir bedeli gerçek kalan cana göre normal hesaplanır). Yani "beni buradan çıkar" kaçış kapısı, ekonomik bir seçenek değil. Extraction, kazancı bankaya yazmanın tek yolu olarak kalır.

Seansı bitirmenin dört yolu ve sonuçları:
| Yol | sessionEarnings | Tamir |
|---|---|---|
| Süre doldu | Tamamı bankaya | Kalan cana göre |
| Extraction | Tamamı bankaya | Kalan cana göre |
| Ölüm | **Yarısı** bankaya | **Maksimum** (0 HP ucu) |
| Pause → Garaj/Menü | **Tamamı yanar** | Kalan cana göre |

## Araç satın alma (2026-08-26 kullanıcı kararı) — stable kapsamında
İlk araç bedava, sonraki araçlar **bir kez parayla açılır ve sonra kalıcı olarak kullanılabilir.**

**İyi haber: altyapı zaten kodda var, sadece hiç okunmuyor.** `VehicleSaveData.isUnlocked` alanı mevcut; `GameManager.InitializeVehicles()` ilk aracı `isDefaultUnlocked = (vehicle == allVehicles[0])` ile zaten açık işaretliyor. Eksik olanlar: `ChangeVehicle()` kilit durumuna bakmıyor, `VehicleData.price` hiçbir yerde okunmuyor, garajda satın alma butonu/kilitli görünüm yok.

**Uyarı:** yükseltmeler araç başına tutuluyor (`vehicleSaveList` araç adına bağlı) — ikinci aracı almak, onun yükseltmelerine sıfırdan başlamak demek. Satın alma arayüzü bunu açıkça söylemeli, yoksa oyuncu kandırılmış hisseder.

## Faz planı (stable sürüme giden yol)
**✅ Faz 0 · Build sağlığı** — TAMAMLANDI (commit `3ca5d2c3`). NUnit `using`'leri silindi, Build Settings sırası MainMenu(0) → GarageScene(1) → GameScene(2).

**✅ Faz 1 · Döngüyü kapat + seans ekonomisi** — TAMAMLANDI (commit `3ca5d2c3`). Yeni dosyalar: `EndReason.cs`, `SessionResult.cs`, `SessionResultPanel.cs`. `sessionEarnings` havuzu, `SettleSession` + lineer tamir formülü + banka koruma clamp'i, `moneySpent` birikimi, ölümde `Destroy` yerine `isDisabled`, `Time.timeScale = 0` ile seans donması, sonuç paneli (ResultCanvas), ExtractionZone canlandırıldı (yeni Input System + 1.5s basılı tut + GameScene'e yerleştirildi), `GarageManager`'da `timeScale = 1f`.
- **Doğrulama (2026-08-27, MCP ile):** konsol temiz, ExtractionZone ve SessionResultPanel gerçekten GameScene'de bağlı, `Destroy(gameObject)` kaldırılmış.
- **⚠️ Atlanan:** `GameScene`'deki `ScoreHandler` bileşeninde `levelDurationInMinutes` hâlâ **5** (script varsayılanı 3f yapılmış ama Unity sahnedeki serialize değeri kullanıyor). Faz 2 promptuna düzeltme maddesi olarak eklendi.

→ **Sonra Unity 6.3 LTS yükseltmesi** *(Faz 0 bittiğine göre artık yapılabilir; ne kadar geç kalınırsa o kadar çok yeni kod eski API'ye karşı yazılmış olur).*

**✅ Faz 2 · Sürüş fiziği + hasar + YENİ STAT TABLOSU** — TAMAMLANDI (2026-08-27, **henüz commit edilmedi** — çalışma ağacında bekliyor).
- Stat tablosu: `GreenSedanData.asset`/`ScooterData.asset` yeni değerlerle güncellendi (bu fazda ikisi özdeş). Yan bulgu: `GreenSedanData.asset` **eski şemada kilitliydi** (`minSpeed/maxSpeed` gibi artık var olmayan alanlar) — script hep varsayılanları kullanıyordu, fark edilmeden. Düzeldi. `protectionStep` range `[0, 0.25f]`'e genişletildi. `NewVehicleData.asset` (prefabsız, listede yok, eski şema) silindi.
- Fizik: `Driver.cs` → `rb.MoveRotation` + `rb.linearVelocity` (`Time.fixedDeltaTime`). Her iki prefabda `Rigidbody2D`: Continuous + Interpolate, `linearDamping = 2.5` (his ayarı, oynayarak değiştirilebilir).
- Hasar: `damageBase(3) + damageFactor(0.85) × impactSpeed^damageExponent(2)`, gerçek `rb.linearVelocity.magnitude`'a bağlı. 0.7s çarpışma dokunulmazlık penceresi eklendi.
- Speedboost tavanı (`Min(moveSpeed+5, baseMoveSpeed×1.5)`) + turbo'nun `if (!turboMode)` dokunulmazlık zırhı tamamen kaldırıldı (dört ayrı iş `ApplyCollisionDamage`/`PlayCrashFlash`/`HandleDeath`'e ayrıştırıldı).
- Kamera zoom yazımı + `mainCam`/`baseCamSize` kaldırıldı; `CollisionDetector.cs` silindi (her iki prefabdan komponenti de); `GameUIManager.UpdateStatPanel` artık `maxHp` alıp `healthbar.maxValue`'yu güncelliyor.
- **Doğrulama (MCP ile, Play Mode canlı obje/method çağrılarıyla):** gaz %25/%50/%100 hasarı (3.85/6.4/16.6, stok 150 can) spesifikasyonla birebir eşleşti; speedboost 3× üst üste toplanınca `moveSpeed` 6.0'da (taban×1.5) kilitli kaldı; ölüm senaryosunda araç yok edilmedi, `timeScale=0`, sonuç paneli "Hurdaya Çıktın" ile açıldı; konsol temiz, `velocity` deprecation uyarısı yok.
- **⚠️ Atlanan:** gerçek klavyeyle "arabayı sürüp duvara çarpma" testi yapılmadı — editör penceresi bu oturumlarda odakta olmadığında Unity'nin frame döngüsü ilerlemiyor, bu yüzden mantık reflection ile canlı çağrılarak doğrulandı, fiziksel his/tünelleme testi kullanıcının kendisi tarafından yapılmalı.
- **Yeni kural (2026-08-27):** Play Mode'a girmeden önce (test amacıyla bile) **her seferinde kullanıcıdan onay al** — arka planda fullscreen bir şey çalışırken pencere fokusu kayıp ekranı değiştirebiliyor. Bkz. oturum hafızası `feedback_playmode_focus`.

**✅ Faz 3 · Kayıt sistemi** — TAMAMLANDI (2026-08-27, henüz commit edilmedi). Yeni dosya: `GameSaveData.cs` (`totalMoney` + `currentVehicleName` + `vehicleSaveList`). `GameManager`'a `LoadGame()`/`SaveGame()` eklendi — `Application.persistentDataPath/save.json`, `JsonUtility`, bozuk/eksik dosyaya karşı try/catch. `Awake()`'te `InitializeVehicles()`'ten önce `LoadGame()` çağrılıyor; mevcut "kayıtsız aracı varsayılanla doldur" mantığı hiç değişmeden bununla birlikte çalışıyor (yüklenen liste zaten doluysa `InitializeVehicles` bir şey eklemiyor). Kayıt üç noktada tetikleniyor: `SettleSession` (seans kapanışı), `TryUpgradeStat` (yükseltme alınca), `ChangeVehicle` (araç değiştirilince).
- **Doğrulama (Edit Mode, Play Mode'a girmeden):** `GameSaveData`+`VehicleSaveData` JSON round-trip'i (nested `List<>` dahil) doğru çalıştığı `execute_code` ile teyit edildi; gerçek `persistentDataPath` konumuna yazma/okuma/silme sorunsuz.
- **⚠️ Atlanan (kullanıcı kararıyla ertelendi):** Gerçek `GameManager.Awake()→LoadGame()` uçtan uca akışı (para kazan → yükselt → oyunu kapat → tekrar aç → yüklendiğini gör) Play Mode'da henüz test edilmedi — kullanıcı testi sonraya bıraktı.
- **Yeni kural (2026-08-27):** Bir faz bitince onay beklemeden direkt sıradaki faza geçiliyor; Play Mode'a girme onayı kuralı (bkz. Faz 2 notu) ayrı ve hâlâ geçerli.

**✅ Faz 4 · Garaj: ekonomi + satın alma + sunum** — TAMAMLANDI (2026-08-27, henüz commit edilmedi). Balance spec artifact'ini ([Pizza Delivery Driver Dengesi](https://Codex.ai/code/artifact/41728b18-67ca-497a-af68-9f95da326392)) tam okuyup sayıları oradan aldım.
- `VehicleData.cs`: her stat'a `costMult` alanı eklendi (Storage 0.7, Speed 1.5, Handling 0.9, Chassis 1.0, Armor 1.2, Stabilizer 0.5 — GreenSedan/Scooter'da aynı).
- `GameManager.cs`: `GetUpgradeCost(string statName, int level)` yeni imza, `upgradeCostBase=460`/`upgradeCostStep=380`; `TryUpgradeStat`'ın switch'ine `default: return false;` eklendi (para yiyen tuzak kapandı — reflection ile canlı doğrulandı: bilinmeyen stat adı artık para almıyor); `GetStatValueAtLevel(statName, level)` yeni yardımcı (current→next gösterimi için); `TryPurchaseVehicle()` eklendi.
- **Araç farklılaşması + satın alma uygulandı:** `allVehicles` sırası her üç sahnede de (`MainMenu`, `GarageScene`, `GameScene`) `[Scooter, GreenSedan]` olacak şekilde birleştirildi (MainMenu zaten böyleydi, diğer ikisi ters sıradaydı — düzeltildi). Scooter = ücretsiz/çevik-kırılgan başlangıç aracı (price 0, speed.base 4.4, handling.base 210, chassis.base 120, storage.base 2, armor.base 0). GreenSedan = paralı/ağır-dayanıklı (price 4500, speed.base 3.7, handling.base 160, chassis.base 195, storage.base 3, armor.base 0.05).
- `GarageManager.cs`: 6 upgrade butonu artık `Start()`'ta runtime `AddListener` ile bağlanıyor (Faz1'deki SessionResultPanel deseniyle aynı — persistent UnityEvent yerine kod içi bağlama, MCP üzerinden çok daha güvenilir). Kilitli araç için `LockedPanel` (mesaj + fiyat + "Satın Al" butonu) eklendi, `statsPanel`/`startButton` kilide göre gizleniyor/pasifleşiyor. "Ana Menüye Çık" butonu eklendi ve bağlandı.
- `StatDisplay.cs`: yeni `valueText` alanı eklendi. **Panel alanı yetmediği için** (`DescriptionText`'in altında boşluk yoktu) yeni bir UI elemanı eklemek yerine `valueText` her 6 kartta da AYNI `DescriptionText` objesine işaret ediyor — `Setup()` çağrı sırasında value string, statik açıklama metninin üzerine yazıyor. Kasıtlı bir kısayol, ama ileride kafa karıştırabilir: description metni artık hiç görünmüyor.
- 5 fazla (boş, hiçbir alana bağlı olmayan) `StatDisplay` komponenti (kart 1-5'te ikişer tane vardı) silindi — sadece GarageManager'ın referans verdiği komponent kaldı.
- **Doğrulama (Edit Mode, `execute_code` ile, Play Mode'a hiç girmeden):** 6 stat için üretilen tüm 24 seviyenin maliyeti spec tablosuyla **birebir** eşleşti (toplam 26.012); satın alma akışı (yetersiz para → red, yeterli para → başarı + `isUnlocked=true`, tekrar satın almaya çalışma → red) doğru çalıştı; bilinmeyen stat adıyla yükseltme denemesi parayı yemedi; `GetStatValueAtLevel` current→next doğru hesaplıyor. Test sırasında gerçek `save.json`'a yazılan test verisi temizlendi.
- **⚠️ Atlanan:** Kilitli-araç panelinin ve yeni butonların gerçek görsel yerleşimi hiç ekrana bakılarak kontrol edilmedi (Edit Mode'da uGUI canvas screenshot güvenilir değil, Play Mode onay gerektiriyor) — pozisyonlar mantıken hesaplandı ama ince ayar gerekebilir.

**✅ Faz 5A · Sipariş mekaniği (veri + mekanik)** — TAMAMLANDI (2026-08-28, henüz commit edilmedi).
- Yeni dosyalar: `LevelData.cs` (+ `Level_01.asset`, `orderMin/orderScale/waitBase/waitPerOrderPizza/partialExtension/partialExtensionCapMult/pizzaBaseReward/tipPerPizza/completionBonusBase/bonusExponent/failPenaltyPerPizza` — hepsi balance spec artifact'inden), `CustomerOrder.cs`.
- **`readonly required` yerine klasik `readonly` + constructor kullandım** — C# 11'in `required`'ı bu Unity sürümünde derlenir mi diye risk almak istemedim; `totalPizzas`/`waitTime` yine de constructor'dan sonra hiç mutasyona uğramıyor, aynı yapısal garanti (eski "waitTime seanslar arası birikiyor" hatası hâlâ imkânsız), sadece farklı bir dil mekanizmasıyla.
- `Customer.cs` tamamen yeniden yazıldı: `Awake()` (bir kereye mahsus referans bulma) / `OnEnable()` (`ResetState` + `LeaveAfterTime` başlatma) ayrıldı; `ReceivePizza(int offeredCount)` artık kabul edilen miktarı `int` olarak döndürüyor (kısmi teslimat sözleşmesi); `bodyCollider` sipariş tamamlanana kadar açık kalıyor; parça teslimatta `partialExtension` ile bekleme süresi uzuyor (`partialExtensionCapMult` ile orijinal süreyi aşmayacak şekilde clamp'li).
- `Delivery.cs`: `carryPizzaAmount`/`maxCarryPizzaAmount` `float`→`int`; müşteri teslimatı `OnTriggerEnter2D`'den `OnTriggerStay2D`'ye taşındı (pizza alımı hâlâ `OnTriggerEnter2D`'de, tek seferlik) — araç birden fazla ziyarette parça parça teslimat yapabiliyor artık.
- `CustomerManager.cs`: **talep bazlı doğurma** — `outstandingDemand` (aktif siparişlerin toplam kalan miktarı) artık `Delivery.PickupPizza`'nın yeni müşteri doğurma kararını yönetiyor (eskiden `carryPizzaAmount > activeCustomers` idi — 3 pizza alınca 3 müşteri spawn oluyordu, artık tek bir 3'lük sipariş 3 pizzayı karşılıyor). `CustomerRespawnRoutine`'in kullanılmayan `respawnTime` parametresi düzeltildi (hardcoded `1f` yerine gerçekten parametreyi kullanıyor — pool slotu artık gerçekten 10-15s cooldown'da).
- **Test sırasında bulunan gerçek hata:** İlk tasarımda `outstandingDemand` yalnızca müşteri işi bitince (`CustomerRoutine`) düşürülüyordu ve "kalan" miktarla — tamamlanan siparişlerde kalan=0 olduğu için talep havuzu **hiç boşalmıyordu**. Düzeltme: `CustomerManager.RegisterDelivery(accepted)` eklendi, her kısmi/tam teslimatta anlık düşüyor; `CustomerRoutine`'e geçen "kalan" artık sadece zaman aşımıyla terk edilen (hiç teslim edilmeyen) miktar için kullanılıyor.
- `ObjectSpawner.cs`: `maxObjectLimit` `float`→`int`, artık gerçekten uygulanıyor (`spawnedInstances` listesi ile sayılıyor, limite ulaşınca yeni spawn atlanıyor). Sahne taraması bunun sadece engeller için değil, **iki sabit pizza doğurma noktası için de** geçerli olduğunu ortaya çıkardı (haritada `pizza .prefab` iki farklı `ObstacleGroup`'tan sürekli yeniden doğuyor — Faz1'deki tekil "pizza " objesi sadece bir kalıntıymış).
- **Doğrulama (Edit Mode, `execute_code` ile):** Sipariş boyutu formülü kapasiteye göre doğru ölçekleniyor (kapasite 2 → [1,2], kapasite 7 → [1,4]); `CustomerOrder` teslim/kalan/tamamlandı mantığı ve taşma clamp'i doğru; gerçek `Customer` prefabı instantiate edilip kısmi→tam teslimat senaryosu uçtan uca koşturuldu — `outstandingDemand` 3→2→0 doğru düştü, `partialExtension` bekleme süresini doğru uzatıp tavanda doğru kırptı (`min(14+12,54)=26`, `min(54+12,54)=54`).
- **Not (metodolojik):** Edit Mode'da `Instantiate()` + `SetActive(true)`, Play Mode'un aksine `Awake()`/`OnEnable()`'ı senkron çağırmıyor — test için reflection ile elle çağırmak gerekti. Gerçek Play Mode'da bu sorun yok, sadece bu oturumun test yöntemiyle ilgili.
- **⚠️ Atlanan:** Zaman aşımı (müşteri sabrı bitip siparişi hiç/kısmen teslim edilmeden terk etmesi) yolu coroutine tabanlı olduğu için Edit Mode'da sürülemedi, sadece kod incelemesiyle doğrulandı — mantığı `CompleteOrder` ile birebir simetrik (`failPenaltyPerPizza` × kalan miktar). Gerçek sürüş/çarpışma/tünelleme testi gibi bu da kullanıcının Play Mode'da doğrulaması gereken kısımlardan.

**✅ Faz 5B · Okunabilirlik ve geri bildirim** — TAMAMLANDI (2026-08-28, henüz commit edilmedi).
- **Müşteri üstü sipariş göstergesi:** `Customer.prefab`'a dünya-uzayı `OrderIndicator` eklendi (Timer'ın üstünde) — 5 pizza ikonu (`pizza.png` sprite'ı, sayı değil) kalan sipariş kadarı aktif, arkasındaki `Background` `carry >= remaining` ise yeşil, değilse kırmızı ("yetişir mi" sinyali aynı elemanda birleşti). `Customer.Update()` her frame `Delivery.carryPizzaAmount`'a bakıp güncelliyor.
- **HUD taşınan/kapasite:** `GameUIManager`'a `carryText` eklendi (InGameCanvas/ScoreTexts altında yeni `CarryText`, "carried/capacity" formatında), `Delivery` her `carryPizzaAmount` değişiminde `UpdateCarryUI()` çağırıyor.
- **`SmartIndicator.cs`:** `maxTimeCache` tamamen kaldırıldı — artık `targetCustomer.currentOrder.waitTime`'ı canlı okuyor (bayatlama imkânsız). Yok etme koşulu `timeLeft<=0` yerine `currentOrder.IsComplete` oldu (eskiden sipariş vaktinden önce başarıyla bitince gösterge müşteri "cooldown"da aktif kaldığı sürece ekranda asılı kalıyordu). Bilgi metnine kalan sipariş adedi eklendi.
- **`DriverTarget.cs`:** "Customer" hedefi ararken artık en yakın **karşılanabilir** siparişi (`remaining <= carried`) önceliklendiriyor, hiçbiri karşılanamıyorsa en yakına düşüyor — `Delivery`'den `carryPizzaAmount` okuyor (`GetComponentInParent<Delivery>()`).
- **Stabilizer görünür geri bildirim:** `Delivery.AttemptDropPizza`'daki `Debug.Log("Pizza is saved")` kaldırıldı, yerine `Driver.PlayProtectionFlash()` (mavi/camgöbeği kısa flaş, hasar flaşıyla aynı mekanizma farklı renkte).
- **Hasar geri bildirimi:** Cinemachine Impulse sistemi entegre edildi — `Driver`/`GreenSedan` prefablarına `CinemachineImpulseSource`, `Main Camera`'ya `CinemachineImpulseListener` eklendi; çarpışma şiddetine göre (`impactSpeed / (baseMoveSpeed*speedBoostMaxMult)`, 0-1) `GenerateImpulseWithForce` çağrılıyor — ekran sarsıntısı artık gerçek hıza orantılı. `GameUIManager.FlashHealthBar()` eklendi (can barı dolgusu kısa kırmızı flaş). Çarpışma sesi de aynı şiddet değeriyle ölçekleniyor (`AudioSource.PlayOneShot(clip, volume)`, 0.5-1.0 arası).
- **Doğrulama:** Konsol temiz, kod mantığı incelendi. **Bu fazın tamamı görsel/his meselesi olduğu için** (ikon boyutu/pozisyonu, ekran sarsıntısının gücü, renk seçimleri, flaş süreleri) Play Mode'da gerçekten oynanarak kullanıcı tarafından değerlendirilmesi gerekiyor — sadece derlendiğini ve mantığın doğru olduğunu doğrulayabildim.

**✅ Faz 6 · MainMenu + Pause** — TAMAMLANDI (2026-08-28, henüz commit edilmedi). Bu, Faz planındaki **son fazdı**.
- **`MainMenu.unity` sıfırdan kuruldu:** EventSystem + `MainMenuCanvas` (Title + StartButton "Başla" + QuitButton "Çıkış"). Yeni `MainMenuManager.cs`: `Awake()`'te iki butonu `AddListener` ile bağlıyor (Faz1/4'teki runtime-wiring deseniyle aynı). `OnClickStart()` → `SceneManager.LoadScene("GarageScene")`; `OnClickQuit()` → `Application.Quit()` (+ `#if UNITY_EDITOR` ile `EditorApplication.isPlaying=false`, Editor'de Quit'in bir şey yapmaması sorununu çözüyor).
- **`GameScene`'e Pause sistemi eklendi:** Yeni `PauseManager.cs` — `Update()`'te yeni Input System'in `Keyboard.current.escapeKey.wasPressedThisFrame` ile ESC dinliyor (ama `ScoreHandler.IsGameActive` false ise, yani seans zaten bittiyse, dinlemiyor — sonuç paneli açıkken pause'a girilemiyor). `TogglePause()` → `Time.timeScale` 0/1 + `pauseCanvas.SetActive`. Üç buton: Devam (`Resume()`), Garaja Dön (`scoreHandler.EndLevel(EndReason.Abandoned, "GarageScene")`), Ana Menü (aynı ama `"MainMenu"` hedefiyle).
- **Terk-etme ekonomisi kodlandı:** `EndReason` enum'ına zaten olan `Abandoned` değeri kullanıldı; `SessionResultPanel.GetReasonLabel`'a `Abandoned → "Seans Terk Edildi"` eklendi. `ScoreHandler.EndLevel`/`SessionResultPanel.Show` zaten Faz1'den beri `destinationScene` parametresi taşıyordu (Faz1'de sabit `"GarageScene"` geçiliyordu) — Pause artık bunu `"MainMenu"` ile de çağırabiliyor, `SessionResultPanel.OnClickReturnToGarage()` hangi sahneye döneceğini bu parametreden okuyor. **Önemli:** `EndLevel` içindeki `SettleSession` çağrısı terk etme dahil her yolda aynı — yani Pause'dan çıkışta da `sessionEarnings` tamamen yanıyor ama tamir bedeli gerçek kalan cana göre (ölüm-cezası YOK) hesaplanıyor, tasarım kararındaki tabloyla birebir eşleşiyor.
- **`GameScene`'e eklenen UI:** `PauseCanvas` (DimBackground + Panel + TitleText "Duraklatıldı" + 3 buton + 3 etiket), `CinemachineImpulseListener` (Faz5B'nin impulse source'larıyla eşleşmesi için Main Camera'ya — bu Faz5B'de unutulmuştu, burada tamamlandı), `GameUIManager`'a `CarryText` (Faz5B'nin kod tarafı zaten hazırdı, sahne objesi burada eklendi).
- **Doğrulama (Edit Mode + resource read, Play Mode'a hiç girilmeden):** her iki sahne de `refresh_unity` sonrası konsol temiz (0 error/warning) derlendi; `PauseManager` component'inin 4 alanı (`pauseCanvas`/`resumeButton`/`garageButton`/`mainMenuButton`) `mcpforunity://scene/gameobject/.../component/PauseManager` resource'u üzerinden doğru GameObject'lere bağlı olduğu ismen doğrulandı; `MainMenuManager`'ın iki butonu aynı şekilde doğrulandı; `git status --short` beklenen dosya kümesiyle eşleşti.
- **⚠️ Atlanan (Play Mode gerektirir, kullanıcının kendi onayı/testi lazım):** ESC ile pause'a girip-çıkmanın gerçekten çalıştığı, `Time.timeScale=0` iken UI'ın hâlâ tıklanabilir olduğu, üç butonun gerçekten doğru sahneye geçtiği, ve MainMenu'nün görsel yerleşimi hiç test edilmedi. Ayrıca **Faz planının tamamı** (Faz 2'nin sürüş hissi, Faz 4'ün garaj UI yerleşimi, Faz 5B'nin görsel/his cilası, ve şimdi Faz 6'nın pause/menü akışı) tek bir uçtan uca Play Mode oturumuyla birlikte doğrulanmayı bekliyor — bu oturumda hiç Play Mode'a girilmedi (kullanıcının "testi sonraya bırak" talimatı gereği).

**Faz planı tamamlandı.** Kalan iş: kullanıcının kendi Play Mode testi (garajdan çık → seans oyna → pause/extraction/ölüm/süre yollarının her biri → garaja dön → yükselt → tekrar çık, üst üste) ve ardından commit (hâlâ hiçbir şey commit edilmedi, hepsi çalışma ağacında bekliyor).

## Hotfix'ler (Faz planı sonrası, 2026-08-28)

**Play Mode izin kuralı güncellendi:** Kullanıcı artık Play Mode testi için her seferinde onay istememi istemiyor — "Test yapabilirsin. Bundan sonrakilerde de ben aksini söylemedikçe test yapabilirsin." Bkz. oturum hafızası `feedback_playmode_focus` (güncellendi). Aksini söylerse eski kurala (her seferinde sor) dönülecek.

**Hotfix 1 — MainMenu çalışmıyor + periyodik donma:** MainMenu sahnesinde `MainMenuManager` komponenti hiçbir sahneye eklenmemişti (script yazılmış ama sahneye hiç eklenmemiş) — **UnityMCP o an bağlı değildi**, `MainMenu.unity`'nin text/YAML serialization'ı olduğu için elle YAML düzenlemesiyle eklendi (`UI` GameObject'ine yeni `MonoBehaviour` bloğu, `startButton`/`quitButton` Button komponent fileID'leriyle referanslandı) — sonradan MCP bağlanınca `mcpforunity://scene/gameobject/.../component/MainMenuManager` ile doğru bağlandığı doğrulandı. Ayrıca `MainMenuManager.Awake()`'e `Time.timeScale=1f` eklendi (Pause'dan çıkışta donuk gelme sorunu). Periyodik donma (GC): `GameUIManager.UpdateTimerText`, `SmartIndicator.UpdateVisuals`, `Customer.Update` her karede gereksiz TMP text/mesh rebuild yapıyordu — hepsine "değer değişmediyse dokunma" cache eklendi; `Customer.LeaveAfterTime`/`ObjectSpawner.SpawnObstacleRoutine`'deki tekrar tekrar `new WaitForSeconds` de önbelleklendi; `CustomerManager.GetCustomer`'daki yerel liste sınıf seviyesine taşındı. **Bilinçli sapma:** `CustomerManager.CustomerRespawnRoutine`'e dokunulmadı — döngüde değil, tek seferlik + rastgele süreli, önbellekleme mantıksız olurdu.

**Hotfix 2 — Kamera titremesi (yalnızca Cinemachine ayarları):** `CinemachineBrain.BlendUpdateMethod`: FixedUpdate(0) → **LateUpdate(1)** — asıl düzeltme, kameranın nihai pozisyonu artık render'la aynı hızda güncelleniyor. `CinemachineFollow.TrackerSettings.BindingMode`: LockToTargetOnAssign(0) → **WorldSpace(4)** — kamera artık aracın rotasyonuna kilitlenmiyor, eksen hizalı kalıyor; `PositionDamping` (0.4,0.4,0.4) dokunulmadan korundu. Ölü `FollowCamera.cs` komponenti Main Camera'dan kaldırıldı ve script dosyası silindi (GUID'i yalnızca `GameScene.unity`'de referanslıydı, doğrulanıp silindi).
- **Değerler Cinemachine paket kaynağından doğrulandı** (`Library/PackageCache/com.unity.cinemachine@.../Runtime/...`), tahmin edilmedi: `BrainUpdateMethods{FixedUpdate=0,LateUpdate=1}`, `TargetTracking.BindingMode{...,WorldSpace=4,...}`.
- **⚠️ Önemli teknik bulgu — otomatik kare-bazlı doğrulama bu ortamda imkânsız:** Play Mode'a MCP üzerinden girildiğinde, editör penceresi gerçek OS focus'u almadığı için Unity'nin player loop'u ilerlemiyor (`Time.frameCount` dakikalarca aynı sayıda donuk kalıyor, `EditorApplication.update` bile neredeyse hiç tetiklenmiyor) — Faz 2'de daha önce belgelenen bulgunun aynısı, bu sefer kamera titremesini kare-kare ölçmeye çalışırken tekrar doğrulandı. Bunun sonucunda "titreme ne kadar azaldı" sorusunu sayısal olarak ölçemedim; düzeltmeler yalnızca Cinemachine kaynak kodundan doğrulanan doğru enum değerleriyle ve konsol/sahne bütünlüğü kontrolleriyle doğrulandı. **Gerçek "titreme geçti mi" hissi kullanıcının kendi elle test etmesini gerektiriyor.**
- Ölçüm denemesi sırasında geçici olarak `EditorApplication.update`'in tüm subscriber'ları reflection ile temizlendi (dangling test handler'ı durdurmak için) — hemen ardından `refresh_unity` (force compile) ile domain reload tetiklenip tüm iç editör sistemlerinin normal subscription'ları geri geldi, konsol temiz doğrulandı. Kalıcı bir iz bırakmadı ama ileride benzer bir otomatik kare-ölçüm denemesi yapılırsa bu riski bilerek yapılmalı.
- Adım 2a (Brain → SmartUpdate) ve 2b (ImpulseSource Decay 0.7→0.35) **uygulanmadı** — hotfix promptu bunları yalnızca Adım 1-3 yetmezse dene diyordu, yetip yetmediği kullanıcının kendi testine bağlı.
- Her iki sahne `refresh_unity` sonrası konsol temiz, `manage_scene validate` sıfır sorun.

**Hotfix 3 — Boost'ları kaldır, obstacle'ları canlandır, müşteri döngüsünü düzelt:**
- **Boost sistemi tamamen kaldırıldı:** `ObjectSpawner.obstacleGroups`'ta boost'ları içeren Grup 0 (SpeedBoost/TurboBoost/TurnBoost, spawnInterval 5) silindi, kalan 3 grup (Obstacle, iki pizza grubu) dokunulmadan korundu. `SpeedBoost.prefab`/`TurboBoost.prefab`/`TurnBoost.prefab` silinmeden önce GUID'leri tüm `.unity`/`.prefab` dosyalarında arandı, yalnızca Grup 0'da referanslı oldukları doğrulandı. `Driver.cs`'ten `turboBoost`/`turboDuration`/`speedBoostAmount`/`speedBoostMaxMult` ve `TurboTimer()` tamamen kaldırıldı; `FixedUpdate`'teki hız çarpanı ve `ApplyCollisionDamage`'daki `severity` paydası (`baseMoveSpeed * speedBoostMaxMult` → yalnızca `baseMoveSpeed`) buna göre güncellendi.
- **Obstacle'lar artık gerçek bir tehdit:** `Obstacle.prefab` zaten `Debuff` tag'li + trigger'dı ama `Driver` onu hiç dinlemiyordu (Faz 2'de silinen `CollisionDetector.cs` de yanlış tag'e (`Obstacle`) bakıyordu — hiç eşleşmemiş, yeni bir regresyon değil). `Driver.OnTriggerEnter2D` artık `Debuff` tag'ini yakalayıp `HandleObstacleHit()` çağırıyor: zırhla ölçeklenen hasar (`obstacleDamage=6`, çarpışmanın ~üçte biri), `delivery.AttemptDropPizza` ile pizza düşürme (Stabilizer koruma şansı zaten o metodun içinde), ve `SpeedDebuffRoutine` ile 2 saniye `moveSpeed × 0.6` yavaşlama (üst üste girilirse süre yenileniyor, birikmiyor). **Çarpışma ve obstacle aynı `lastDamageTime`'ı paylaşıyor** — biri diğerini de dokunulmazlık penceresine sokuyor, çifte hasar imkânsız. `ApplyCollisionDamage` ve `HandleDeath` artık çalışan debuff coroutine'ini düzgün durduruyor.
- **Müşteri döngüsü düzeltildi:** `CustomerManager`'daki `minRespawnTime`/`maxRespawnTime` (15-30sn, "respawn" adı yanıltıcı — aslında "Thank You!" sonrası ekranda kalma süresiydi) tamamen kaldırıldı. `CustomerRoutine` artık açık bir `despawnDelay` parametresi alıyor; `Customer.CompleteOrder()` → `levelData.completedDespawnDelay` (3sn), `Customer.LeaveAfterTime()` → `levelData.timedOutDespawnDelay` (2sn) — ikisi de `LevelData`'da yeni `[SerializeField]` alanlar, hardcode değil. Ödül/ceza mantığına dokunulmadı.
- **`DriverTarget` navigasyon oku düzeltildi:** `FindBestCustomerTarget` artık `customer.currentOrder.IsComplete` olan (siparişi bitmiş ama despawn gecikmesi boyunca hâlâ aktif) müşterileri tamamen atlıyor — işi bitmiş müşteriye yönlendirme sorunu çözüldü.
- **`Collectable.cs` null kontrolü:** `driver` alanı `OnTriggerEnter2D` anında null ise (obje oyuncu spawn olmadan aktifleşirse) artık yeniden `FindFirstObjectByType` deniyor, yine bulamazsa sessizce çıkıyor — `NullReferenceException` riski kapandı.
- **Doğrulama (Play Mode + Edit Mode, reflection ile canlı çağrılar):** Play Mode'da gerçek spawn edilen `Driver` üzerinde `HandleObstacleHit()` doğrudan çağrıldı — hasar (6, zırh 0), hız düşüşü (4.4→2.64, ×0.6) birebir doğrulandı; hemen ardından tekrar çağrıldığında dokunulmazlık penceresi doğru bloke etti; `ApplyCollisionDamage()` çağrılıp hemen ardından `HandleObstacleHit()` çağrılınca **paylaşılan `lastDamageTime` sayesinde ikinci darbe bloklandı** (çifte hasar yok, kritik kabul kriteri doğrulandı); can 4'e düşürülüp obstacle'la öldürüldüğünde `isDisabled=true`, `timeScale=0`, `SessionResultPanel` açıldı, konsol temiz. Edit Mode'da sentetik `Customer`+`DriverTarget` nesneleriyle `FindBestCustomerTarget` test edildi: tamamlanmış siparişli yakın müşteri değil, açık siparişli uzak müşteri seçildi (kod okumasıyla da teyitli). Her iki sahne `refresh_unity` sonrası konsol temiz, `manage_scene validate` sıfır sorun.
- **⚠️ Atlanan:** Gerçek klavyeyle sürüp obstacle'a çarpma "his" testi (görsel debuff/flaş/pizza düşürme animasyonu) yapılmadı — aynı bilinen "editör penceresi OS focus'u almadan Unity'nin player loop'u ilerlemiyor" kısıtı nedeniyle gerçek çok-karelik oynanışı otomatikleştiremedim; mantık reflection ile doğrulandı, gerçek "hissi" kullanıcının kendi oynayışı gerektiriyor.
- **Yan not:** `git status` bu hotfix'ten bağımsız olarak üç `StoreAssets/SimplePixelUI` health bar prefabında küçük diff gösteriyor (`m_RootOrder`→`m_ConstrainProportionsScale`, `m_RaycastPadding`/`m_Maskable` eklenmesi gibi) — bunlar `refresh_unity mode=force` çağrılarımın tetiklediği otomatik Unity/UGUI serialization format yükseltmesi, işlevsel bir değişiklik değil, ben kasıtlı dokunmadım.

**Hotfix 4 — `ResultCanvas` yanlış canvas türünde:** `GameScene` → `ResultCanvas`'ın `Canvas.renderMode`'u **World Space(2) → Screen Space - Overlay(0)** yapıldı — asıl ve tek düzeltme. `RectTransform` (`100×100`) dünya biriminde yorumlandığı için içindeki `Panel` (`560×640`) ekranda aracın ~400 katı büyüklükte kalıyordu, oyuncu yalnızca minik bir parçasını görüyordu. `sortingOrder=20` dokunulmadan korundu (Pause'un 15'i ve HUD'ın 0'ının üstünde kalmaya devam ediyor), `CanvasScaler`/`GraphicRaycaster`'a dokunulmadı.
- **Çocuk nesneler zaten doğru kurulmuştu** — `DimBackground` (anchor stretch `(0,0)-(1,1)`, `sizeDelta=(0,0)`) ve `Panel` (anchor merkez `(0.5,0.5)`, `anchoredPosition=(0,0)`) render mode değişikliğinden önce de sonra da aynı ayarlarla kaldı; World Space'te bu ayarlar "560 dünya birimi" olarak yorumlanıyordu, Overlay'e geçince otomatik olarak "560 piksel, ekran merkezinde" oldu — ekstra bir anchor/pozisyon düzeltmesi gerekmedi.
- **Doğrulama (Play Mode + ekran görüntüsü):** Play Mode'a girilip `ScoreHandler.EndLevel(EndReason.TimeUp)` reflection ile tetiklendi, `manage_camera screenshot` ile gerçek render görüntülendi — panel ekranın tam ortasında, doğru boyutta, karartma tüm ekranı kaplıyor, "Garaja Dön" butonu ve tüm metinler taşmadan okunaklı göründü (kabul kriterleri 1-4 görsel olarak doğrulandı). Test ekran görüntüsü iş bitince silindi. Konsol temiz, `manage_scene validate` sıfır sorun. Diğer canvas'lara (`InGameCanvas`, `PauseCanvas`) hiç dokunulmadı.

**Hotfix 5 — Pizza toplama noktaları + müşteri talep değişmezi:** Haritadaki tekil, sürekli doğup yok olan `pizza .prefab` nesneleri tamamen kaldırıldı; yerine **sabit toplama noktaları** geldi.
- **Yeni dosya `PizzaCollectPoint.cs`:** oyuncu (`Player` tag) trigger içinde durdukça `collectInterval` (0.3sn, bileşen üzerinde, nokta başına farklılaşabilir) saniyede bir `Delivery.CollectPizza()` çağırıyor; envanter doluyken "dolu" sesi bir kez çalıyor, trigger'dan çıkınca sayaç sıfırlanıyor. Yeni tag **`CollectPoint`** eklendi (`Pizza` tag'i eski tekil-toplama koduna aitti, karıştırılmadı).
- **`Delivery.cs`:** Eski `OnTriggerEnter2D`'deki tekil pizza toplama (`PickupPizza`, `Destroy`, `pizzaFailClip`) tamamen silindi. Yeni `IsFull` property + `CollectPizza(PizzaCollectPoint)`: **harcanabilir para = banka (`GameManager.totalMoney`) + seans kazancı (`ScoreHandler.sessionEarnings`)**, ücret bu toplamla `Clamp`leniyor — para hiç yoksa pizza yine verilir (bilinçli: kilitlenmeyi imkânsız kılıyor). Navigasyon hedefi `"Pizza"` → `"CollectPoint"` (iki çağrı noktası: `Start` ve `havePizzaStatus`). `LevelData`'ya erişim `CustomerManager.LevelData` (yeni public getter) üzerinden, yeni bir çözümleme yolu icat edilmedi.
- **`CustomerManager.cs` — görevin kalbi, gerçek bir deadlock'u kapatıyor:** Müşteri doğurma kararı artık "pizza alındığı an" olayına değil, 0.5 saniyede bir kontrol edilen **sürekli bir değişmeze** bağlı: *açık talep, oyuncunun taşıdığı pizzadan az olduğu sürece yeni müşteri doğar* (`EnsureDemandCoversCarry`, `DemandCheckRoutine`). Eski tasarımda envanter doluyken müşteri zaman aşımına uğrarsa oyuncu elinde pizzayla müşterisiz kalıp döngü kilitleniyordu — artık bu yapısal olarak imkânsız. `GetCustomer()` artık `bool` dönüyor (havuz tükendiğinde `false`, sonsuz döngüyü önlüyor). Yeni tavan `LevelData.maxActiveCustomers` (4) — değişmez olmasa aynı anda çok müşteri doğup hepsi birden zaman aşımına uğrayabilirdi.
- **`LevelData.cs`/`Level_01.asset`:** yeni alanlar `pizzaCost` (1), `maxActiveCustomers` (4).
- **Sahne:** `ObjectSpawner.obstacleGroups`'ta pizza doğuran 2 grup silindi, yalnızca Obstacle grubu kaldı. İki yeni `CollectPoint` GameObject'i eski gruplarla **aynı dünya konumlarında** eklendi ((17.8, 15.3) pizza dükkânı, (-17.7, 8.9) sokak) — `BoxCollider2D` (trigger, 2.5×2.5), `pizza.png` sprite'ı (×2 scale, görünür olsun diye), `AudioSource`, `PizzaCollectPoint`. `pizza .prefab` silinmedi, görseli burada yeniden kullanıldı.
- **Doğrulama (Play Mode, reflection ile canlı çağrılar):** `Delivery.CollectPizza()` çağrılıp `sessionEarnings`'in doğru düştüğü, envanter doluyken hiçbir şey yapmadığı, banka+seans kazancı derin negatifteyken pizzanın yine de bedava verildiği (para daha da düşmedi) doğrulandı. **Kritik kabul kriteri #7 (deadlock testi):** taşınan pizza 3'e, açık talep ve aktif müşteri 0'a ayarlanıp `EnsureDemandCoversCarry()` doğrudan çağrıldı — talep anında 3'e çıktı, 2 müşteri doğdu. Tavan testi: taşınan pizza 20'ye çıkarılıp aynı çağrı yapıldı — aktif müşteri tam `maxActiveCustomers=4`'te durdu (havuzda 11 müşteri olmasına rağmen, yani durduran şey tavandı, havuz tükenmesi değil). Havuz gerçekten tüketildiğinde `GetCustomer()`'ın `false` döndüğü ayrıca doğrulandı. Ekran görüntüsüyle toplama noktasının haritada net göründüğü teyit edildi. Konsol temiz, `manage_scene validate` sıfır sorun.
- **⚠️ Atlanan:** Gerçek klavyeyle toplama noktasının üstünde durup 0.3sn'lik tik-tik toplamayı ve "dolu" sesinin gerçekten bir kez çaldığını dinlemek — bilinen "editör OS focus'u almadan player loop ilerlemiyor" kısıtı yüzünden otomatikleştirilemedi, mantık reflection ile doğrulandı.

**Hotfix 6 — Periyodik donma (ikinci tur, 2026-09-03):** Hotfix 1'in düzeltmeleri **duruyordu, regresyon yoktu** — donma yeni kod ve hiç ele alınmamış kaynaklardan geliyordu. İki Explore + bir Plan agent'ı çalıştırıldı; Plan agent'ı Explore'un birkaç bulgusunu haklı olarak çürüttü ve kaçırılan ikinci bir sebep buldu. **İki bağımsız sebep vardı:**

1. **`SmartIndicator.cs` — asıl çöp kaynağı.** Hotfix 1'de eklenen dirty check **işe yaramıyordu**: `d = RoundToInt(distance * 10)` desimetre çözünürlüğünde, araç hareket ettikçe neredeyse her kare değişiyor, yani 6 operandlı `infoText.text = d + "m\n" + ...` concat'i her kare çalışıyordu (object[6] + 3 int boxing + TMP mesh rebuild, en fazla 4 ekran-dışı gösterge için).
   **Düzeltme:** `infoText.SetText("{0}m\n{1}s\n{2}x", d, t, r)` — TMP'nin kendi format yolu, doğrudan iç char buffer'ına yazıyor. **Canlı ölçümle doğrulandı: eski concat 115.7 B/çağrı, StringBuilder 19.5 B/çağrı (Mono'da `Append(int)` içeride `int.ToString()` çağırıyor), `SetText(format,args)` tam 0 B.** Çıktı birebir aynı (`123m\n38s\n3x`, ondalık yok — gerçek TMP nesnesinde `GetParsedText()` ile teyit edildi). Önce StringBuilder yazıldı, ölçüm 0'a inmediği görülünce TMP yoluna geçildi.
   Ayrıca `canvasGroup.alpha` her kare yazılıyordu → sadece görünürlük **geçiş anında** yazacak şekilde kapılandı (`lastOffScreen` tri-state, ilk kare her zaman yazıyor).

2. **Göstergeler HUD ile aynı Canvas'taydı** — `IndicatorManager.uiCanvasParent` = `InGameCanvas`, ve `GameUIManager` de aynı nesnede. `IndicatorPrefab`'ın kendi Canvas'ı olmadığı için `SmartIndicator.UpdatePosition`'daki **her kare çalışan `rectTransform.position` Lerp'i tüm HUD'ın yeniden batch'lenmesine** yol açıyordu. Bu, 1. madde düzeltildikten sonra da devam edecekti.
   **Düzeltme:** yeni kök `IndicatorCanvas` (ScreenSpaceOverlay, `sortingOrder = 1`, `InGameCanvas`'ın `CanvasScaler` ayarları kopyalandı, **`GraphicRaycaster` bilerek eklenmedi** — göstergeler tıklanabilir değil). `uiCanvasParent` ona yönlendirildi. `sortingOrder = 1` seçimi kasıtlı: göstergeler eskiden `InGameCanvas`'ın son çocuğu olduğu için HUD'ın üstüne çiziliyordu, bu görsel davranış birebir korundu (Pause 15 ve Result 20'nin altında kalıyor).

**Yan düzeltmeler:** `Collectable.OnEnable`'daki `FindFirstObjectByType<Driver>()` kaldırıldı — `Obstacle.prefab` 2 saniyede bir doğduğu için her spawn'da tam sahne taraması yapılıyordu, üstelik o prefabda `effectClip` **null** ve `AudioSource` yok, yani tarama hiçbir işe yaramıyordu; artık ilk temasta tembel çözülüyor + `effectClip == null` erken çıkışı var. `DriverTarget.FindClosestTargetRoutine`'deki `while(true)` içindeki `new WaitForSeconds` `Awake()`'te bir kez oluşturuluyor (**`Start()` değil** — `Delivery.Start()` bizden önce `SearchSetNavigation` çağırabiliyor, o zaman alan null olur ve `yield return null` her kare dönen bir döngüye çevirirdi).

**Bilerek YAPILMAYANLAR (Plan agent'ı tarafından çürütüldü, tekrar önerilmemeli):**
- `SmartIndicator.cs`'teki `targetImage.color` ve `ExtractionZone`'daki `Image.fillAmount` — **yanlış alarm.** Unity'nin `Graphic.color`/`fillAmount` setter'ları eşit değerde erken çıkıyor; `timeLeft` saniyede bir değiştiği için renk de saniyede bir değişiyor. `fillAmount` gerçekten her kare değişiyor ama kasıtlı yumuşak bar, sıfır ayırma — yuvarlanırsa görsel bozulur.
- **`ObjectSpawner` object pooling** — kararlı durum matematiği: 2sn'de bir spawn × 5sn ömür = ortalama **~2.5 obje canlı** (20 limitinin çok altında), ≈1-2 KB/sn. `SmartIndicator`'ın ~28 KB/sn'sinin yanında 15-25× küçük. Büyük iş, kalıcı karmaşıklık, ölçülemez kazanç.
- `ObjectSpawner`'daki `RemoveAll(go => go == null)` — lambda hiçbir şey yakalamıyor, Roslyn delegate'i statik alanda cache'liyor, **zaten sıfır ayırma**.
- `Driver`'daki `Invoke(nameof(NormalizeColor), 0.5f)` — `nameof` derleme zamanı sabiti, string ayırmıyor; ayrıca 0.7s dokunulmazlık penceresiyle kapılı.
- `Delivery.cs`'teki `OnTriggerStay2D` `GetComponent` — `carryPizzaAmount <= 0` guard'ı ondan **önce** geliyor, normal akışta ilk tick'te carry sıfırlanıp sonraki tick'ler erken çıkıyor.
- `CustomerTarget.cs` — **hiçbir sahnede yok, çalışmıyor** (ölü kod, silinebilir ama performans kaynağı değil).
- `gcIncremental: 1` zaten açık.

**Doğrulama:** Konsol temiz (0 error/warning), `manage_scene validate` sıfır sorun, sahne kaydedildi. **⚠️ Atlanan:** "donma gerçekten geçti mi" hissi — bilinen "editör OS focus'u almadan Unity'nin player loop'u ilerlemiyor" kısıtı yüzünden kare-bazlı ölçüm yapılamadı; ayırma azalması izole mikro-benchmark ile ölçüldü, gerçek oyun içi doğrulama kullanıcının kendi testini gerektiriyor.

**Şimdilik kapsam dışı:** `CustomerTarget` ↔ `IndicatorManager` çakışması, `FollowCamera`'nın kaldırılması, brace stili temizliği, ses/görsel cila, seviye seçim ekranı (tek seviye var, `LevelData` yeterli). Ayrıca üç sahnedeki tüm `CanvasScaler`'lar `Constant Pixel Size` — UI çözünürlükle ölçeklenmiyor; `Scale With Screen Size` + 1920×1080'e geçilmesi gerekiyor ama mevcut yerleşimleri yeniden ölçekleyeceği için **yerleşimler son halini aldıktan sonra** yapılacak.

## Prompt arşivi (kapanmış iş akışı)
`oldUsedPromts/` klasöründe (proje kökünde) eski düzenin promptları duruyor: 7 faz + 3 hotfix, artı hangisinin ne yaptığını listeleyen bir `README.md`. **`.gitignore`'da** (`oldUsedPromts/`, satır 76) — AGENTS.md ile aynı gerekçe: AI süreç dosyaları repoya girmemeli.

**Bu iş akışı 2026-09-01'de kapandı** (bkz. "Rolüm"). Klasör yalnızca **arşiv** — bir işin neden öyle yapıldığını geriye dönük anlamak için. Yeni iş için oradan kopyalama, güncel durum bu dosyada.

Arşivdeki promptlar için bekleyen bir uygulama işi yoktur. Yeni işler yalnızca güncel `memory-bank/plan_finalize.md` handoff'u ve ilgili owner-only kontrol listesi üzerinden yürütülür.

## Proje yapısı — hızlı özet (2026-08-25 taraması)
- `Assets/Scripts/`: 23 oyun script'i (Driver, Customer, Delivery, GameManager, GarageManager, ScoreHandler, IndicatorManager/SmartIndicator, ObjectSpawner, ExtractionZone, VehicleData/VehicleSaveData vb.) — teslimat/müşteri/araç yükseltme döngüsü kurulu görünüyor.
- `Assets/Scenes/`: `MainMenu`, `GarageScene`, `GameScene`.
- `Assets/ScriptableObjects/`: `GreenSedanData`, `ScooterData`, `NewVehicleData` — araç dengeleme verisi `VehicleData` şemasına bağlı.
- `Assets/Prefabs/`: Vehicles, PowerUps (SpeedBoost/TurboBoost/TurnBoost/Obstacle), MapObjects (Car/House/Rocks/Tree varyasyonları), Customer, WastedPizza.
- `Assets/StoreAssets/`, `Assets/2D Casual UI/`, `Assets/Audio/`, `Assets/TileSets/`: satın alınmış/hazır asset paketleri (3. parti kod: `vFolders`, `Wingman` editor araçları — proje kod stiline dahil değil).
- Proje içinde `.Codex/` klasörü **yok** — kod stili/kurallar yalnızca bu AGENTS.md içinde tutuluyor.

## Agent envanteri (2026-08-26 taraması)
10 özel agent var, hepsi **kullanıcı seviyesinde** (`~/.Codex/agents/`), yani tüm Unity projelerinde ortak. Hiçbirinde model override yok, hepsi tam araç erişimli.

Bu projede işe yarayanlar: **Game Designer** (gameplay loop, ölüm/seans sonu tasarımı), **Economy Designer** (yükseltme maliyet eğrisi, ödül/ceza dengesi, sink/source), **Unity Architect** (ScriptableObject/decoupling kararları), **Level Designer** (GameScene dünyası, pacing), **Unity Editor Tool Developer** (çift komponent / bağlanmamış buton gibi hataları yakalayan validation aracı yazabilir).

Şimdilik ilgisiz: Narrative Designer, Unity Multiplayer Engineer (tek oyuncu), Unity Shader Graph Artist, Technical Artist, Game Audio Engineer — son ikisi cila fazında devreye girer.

Skill'ler: `unity-mcp-skill` (kullanıcı seviyesi, Unity MCP iş akışı kalıpları). `~/.Codex/skills/learned` klasörü var ama **boş**.
