/**
 * Hukuki metinler — TASLAK (avukat incelemesi öncesi; karar 2026-10-17). Her metnin sürümü ve son güncelleme tarihi sayfada görünür.
 * Kullanım koşulları ve Veri işleme sözleşmesi esaslı değişince sürümü artırın ve backend `LegalVersions` (Domain/Users/UserConsent.cs)
 * ile aynı yapın: işletme Sahibi bir sonraki girişte bir kez yeniden onaylar. KVKK aydınlatma sürümü backend `KvkkNotice.Version`.
 * Şirket bilgisi content/company.ts'ten gelir.
 */
import { company } from "./company";

export type LegalSection = { heading: string; paragraphs?: string[]; items?: string[] };

export type LegalDoc = {
  slug: string;
  /** Sayfa başlığı. */
  title: string;
  /** Menü ve bağlantı adı. */
  short: string;
  description: string;
  version: string;
  updated: string;
  intro?: string;
  sections: LegalSection[];
};

const updated = "20 Ekim 2026";
const controller = company.name === company.brand ? company.name : `${company.name} ("${company.brand}")`;

/** Alt işleyenler ve yurt dışı aktarım (KVKK m.9): gizlilik, KVKK ve veri işleme sözleşmesinde aynı liste. */
export const subProcessors: { name: string; purpose: string; location: string }[] = [
  { name: "Sunucu sağlayıcısı (VDS)", purpose: "Uygulamanın ve veritabanının barındırılması", location: "Türkiye" },
  { name: "Brevo (Sendinblue SAS)", purpose: "E-posta gönderimi (doğrulama, şifre, randevu bildirimleri)", location: "Fransa (AB)" },
  {
    name: "Cloudflare, Inc.",
    purpose: "Alan adı (DNS), saldırı koruması, e-posta yönlendirme, çerezsiz ziyaretçi istatistiği (Web Analytics), şifreli yedek deposu (R2)",
    location: "ABD ve küresel ağ",
  },
  { name: "Sentry (Functional Software, Inc.)", purpose: "Uygulama hata kayıtları (kişisel veri gönderilmez; kullanıcı yalnızca kimlik numarasıyla)", location: "ABD" },
];

const transferText =
  "Yurt dışındaki hizmet sağlayıcılara aktarım KVKK m.9 kapsamında, Kişisel Verileri Koruma Kurulu'nun ilan ettiği standart sözleşmeler imzalanarak ve Kurul'a bildirilerek yapılır. Yedekler sunucudan çıkmadan önce şifrelenir; hata kayıtlarına kişisel veri yazılmaz.";

const kvkk: LegalDoc = {
  slug: "kvkk",
  title: "Kişisel Verilerin İşlenmesine İlişkin Aydınlatma Metni",
  short: "KVKK aydınlatma metni",
  description: "KobiFabrika'nın site ziyaretçileri ve işletme kullanıcıları için KVKK aydınlatma metni.",
  version: "2026-10-20",
  updated,
  intro: `Bu metin, 6698 sayılı Kişisel Verilerin Korunması Kanunu ("KVKK") m.10 uyarınca ${controller} tarafından; kobifabrika.com sitesini ziyaret edenler ile KobiFabrika uygulamasına kayıt olan işletme sahipleri ve çalışanları için hazırlanmıştır. İşletmelerin kendi müşterilerine ait veriler için veri sorumlusu ilgili işletmedir; bu konu Veri işleme sözleşmesinde düzenlenir.`,
  sections: [
    {
      heading: "Veri sorumlusu",
      paragraphs: [
        [
          `Veri sorumlusu: ${controller}.`,
          `İletişim ve KVKK başvuruları: ${company.email}.`,
          `İnternet sitesi: ${company.website.replace("https://", "")}.`,
          company.address && `Adres: ${company.address}.`,
          company.mersis && `MERSİS no: ${company.mersis}.`,
        ]
          .filter(Boolean)
          .join(" "),
      ],
    },
    {
      heading: "İşlenen kişisel veriler",
      items: [
        "Kimlik ve iletişim: ad soyad, e-posta adresi, cep telefonu.",
        "Hesap güvenliği: şifrenin geri döndürülemez özeti, oturum kayıtları, son giriş zamanı, IP adresi.",
        "İşletme bilgileri: işletme adı, adresi, telefonu, logosu, çalışma saatleri.",
        "İşlem kayıtları: uygulamada yapılan işlemlerin kim tarafından ve ne zaman yapıldığı (aktivite günlüğü).",
        "Destek: geri bildirim mesajları ve isteğe bağlı ekran görüntüleri, destek yazışmaları.",
        "Site ziyareti: çerezsiz, kimliksiz sayfa görüntüleme istatistiği (hangi sayfa, ülke, cihaz türü).",
      ],
    },
    {
      heading: "İşleme amaçları",
      items: [
        "Hesabın oluşturulması, kimlik doğrulama ve hesap güvenliği.",
        "Hizmetin sunulması: kasa, veresiye, randevu, personel ve diğer modüllerin çalışması.",
        "Destek taleplerinin yanıtlanması ve hizmetin iyileştirilmesi.",
        "Hizmetle ilgili zorunlu bildirimler (doğrulama, şifre sıfırlama, deneme süresi).",
        "Yasal yükümlülüklerin yerine getirilmesi ve hukuki uyuşmazlıklarda delil.",
      ],
    },
    {
      heading: "Hukuki sebepler",
      paragraphs: [
        "KVKK m.5/2 (c) bir sözleşmenin kurulması veya ifasıyla doğrudan ilgili olması, (ç) hukuki yükümlülüğün yerine getirilmesi, (e) bir hakkın tesisi, kullanılması veya korunması ve (f) temel hak ve özgürlüklere zarar vermemek kaydıyla meşru menfaat.",
      ],
    },
    {
      heading: "Toplama yöntemi",
      paragraphs: ["Kişisel veriler kayıt, kurulum ve uygulama ekranlarından, e-posta ve WhatsApp destek yazışmalarından elektronik ortamda toplanır."],
    },
    {
      heading: "Aktarım",
      paragraphs: [
        "Veriler yalnızca hizmetin sunulması için gerekli ölçüde aşağıdaki hizmet sağlayıcılara ve talep hâlinde yetkili kamu kurumlarına aktarılır:",
        ...subProcessors.map((s) => `${s.name}: ${s.purpose} (${s.location}).`),
        transferText,
      ],
    },
    {
      heading: "Saklama süresi",
      paragraphs: [
        "Hesap verileri hesap açık olduğu sürece; hesap kapatılınca yasal saklama süreleri (ör. ticari kayıtlar için 10 yıl) dolana kadar saklanır, ardından silinir veya anonim hâle getirilir. Geri bildirim ekran görüntüleri 90 gün, e-posta gönderim kayıtlarının içerikleri 30 gün sonra silinir.",
      ],
    },
    {
      heading: "Haklarınız",
      paragraphs: [
        `KVKK m.11 uyarınca verilerinizin işlenip işlenmediğini öğrenme, bilgi talep etme, amacına uygun kullanılıp kullanılmadığını öğrenme, aktarıldığı üçüncü kişileri bilme, düzeltilmesini, silinmesini veya yok edilmesini isteme, otomatik analiz sonucu aleyhinize bir sonuca itiraz etme ve zararın giderilmesini talep etme haklarına sahipsiniz. Başvurularınızı ${company.email} adresine iletebilirsiniz; en geç 30 gün içinde ücretsiz yanıtlanır.`,
      ],
    },
  ],
};

const gizlilik: LegalDoc = {
  slug: "gizlilik",
  title: "Gizlilik Politikası",
  short: "Gizlilik politikası",
  description: "KobiFabrika'da verilerin nasıl korunduğu, kimlerle paylaşıldığı ve ziyaretçi istatistiği.",
  version: "2026-10-20",
  updated,
  intro: "KobiFabrika, işletmenizin verilerini sizin verileriniz olarak görür. Bu politika verileri nasıl koruduğumuzu ve kimlerle paylaştığımızı sade bir dille anlatır.",
  sections: [
    {
      heading: "Verileriniz sizindir",
      items: [
        "İşletmenizin müşteri, satış, randevu ve kasa verileri yalnızca sizin ve yetki verdiğiniz kullanıcıların erişimindedir.",
        "Verilerinizi satmayız, reklam için kullanmayız.",
        "KobiFabrika destek ekibi işletmenizin müşteri verilerini göremez; destek ekranında yalnızca sayılar (kullanıcı, müşteri, randevu sayısı) görünür.",
        "Verilerinizi istediğiniz zaman Excel'e aktarabilir, müşterilerinizin kişisel verilerini silebilirsiniz.",
      ],
    },
    {
      heading: "Güvenlik",
      items: [
        "Bağlantılar HTTPS ile şifrelenir; şifreler geri döndürülemez biçimde saklanır.",
        "Her işletmenin verisi ayrı tutulur; bir işletme başka işletmenin verisini göremez.",
        "Rol bazlı yetki (Sahip, Yönetici, Personel, Mali Müşavir); maaş bilgisini sadece Sahip görür.",
        "Her gece yedek alınır; yedekler şifrelenerek saklanır.",
      ],
    },
    {
      heading: "Hizmet sağlayıcılar",
      paragraphs: [...subProcessors.map((s) => `${s.name}: ${s.purpose} (${s.location}).`), transferText],
    },
    {
      heading: "Ziyaretçi istatistiği",
      paragraphs: [
        "Sitemizde Cloudflare Web Analytics kullanılır. Bu araç çerez kullanmaz, tarayıcınızda bilgi saklamaz ve sizi siteler arasında izlemez; yalnızca kimliksiz sayfa görüntüleme sayıları, ülke ve cihaz türü gibi toplu bilgiler üretir. Bu nedenle çerez onayı istenmez.",
      ],
    },
    { heading: "Değişiklikler", paragraphs: ["Bu politika güncellenirse sayfadaki tarih değişir; esaslı değişikliklerde uygulama içinden haber verilir."] },
    { heading: "İletişim", paragraphs: [`Sorularınız için: ${company.email}.`] },
  ],
};

const cerez: LegalDoc = {
  slug: "cerez-politikasi",
  title: "Çerez Politikası",
  short: "Çerez politikası",
  description: "KobiFabrika yalnızca oturum için zorunlu çerez kullanır; analitik ve reklam çerezi yoktur.",
  version: "2026-10-20",
  updated,
  sections: [
    {
      heading: "Kullandığımız çerez",
      paragraphs: [
        "Yalnızca zorunlu bir çerez kullanırız: kf.auth. Giriş yaptığınızda oturumunuzu tanımak için yazılır, httpOnly ve güvenli bağlantıyla korunur. \"Beni hatırla\" seçilmediyse tarayıcı kapanınca, seçildiyse 30 gün sonra silinir.",
      ],
    },
    {
      heading: "Kullanmadıklarımız",
      paragraphs: [
        "Reklam, pazarlama veya izleme çerezi kullanmayız. Ziyaretçi istatistiği çerezsizdir (Cloudflare Web Analytics). Zorunlu çerezler için onay gerekmediğinden çerez bannerı gösterilmez.",
      ],
    },
    {
      heading: "Tarayıcıda saklanan diğer bilgiler",
      paragraphs: ["Bazı ekran tercihleri (ör. son seçilen sekme) yalnızca tarayıcınızda tutulur ve sunucuya gönderilmez."],
    },
    { heading: "Çerezleri silme", paragraphs: ["Tarayıcı ayarlarından çerezleri silebilirsiniz; bu durumda yeniden giriş yapmanız gerekir."] },
  ],
};

const kosullar: LegalDoc = {
  slug: "kullanim-kosullari",
  title: "Kullanım Koşulları",
  short: "Kullanım koşulları",
  description: "KobiFabrika uygulamasının kullanım koşulları: pilot dönemi, deneme, paketler, veri sahipliği.",
  version: "2026-10-20",
  updated,
  intro: `Bu koşullar ${controller} ile KobiFabrika uygulamasını kullanan işletme ve kullanıcılar arasındaki ilişkiyi düzenler. Kayıt olarak bu koşulları kabul etmiş olursunuz.`,
  sections: [
    {
      heading: "Hizmet",
      paragraphs: [
        "KobiFabrika; cari, kasa, veresiye, stok, satış, teklif, randevu, müşteri ve personel işlerini yürütmek için web tabanlı bir yazılımdır. Hizmet internet tarayıcısından kullanılır; mobil uygulama yoktur.",
      ],
    },
    {
      heading: "Pilot dönemi ve ücret",
      paragraphs: [
        "Pilot döneminde KobiFabrika ücretsizdir. Pilot olarak işaretlenen işletmeler için süre ve limit yoktur. Ücretli paketlere geçiş başladığında en az 30 gün önce haber verilir; onayınız olmadan ücret alınmaz.",
      ],
    },
    {
      heading: "Deneme ve paketler",
      paragraphs: [
        "Yeni işletmeler 14 gün boyunca bütün özellikleri dener. Deneme bitince paket seçilmediyse ücretsiz Çırak paketine geçilir; verileriniz silinmez, pakette olmayan özellikler salt okunur kalır. Paket fiyatları ve limitleri fiyatlar sayfasında yazar; fiyatlara KDV dahil değildir.",
      ],
    },
    {
      heading: "Hesap ve güvenlik",
      items: [
        "Kayıtta verdiğiniz bilgilerin doğru olmasından ve şifrenizin gizliliğinden siz sorumlusunuz.",
        "İşletmeye kullanıcı eklediğinizde onların yetkisini (rolünü) siz belirlersiniz.",
        "Hesabınızda yetkisiz bir işlem fark ederseniz hemen bize haber verin.",
      ],
    },
    {
      heading: "Veri sahipliği",
      paragraphs: [
        "Uygulamaya girdiğiniz veriler işletmenize aittir. Müşterilerinizin kişisel verileri için veri sorumlusu işletmenizdir; KobiFabrika bu verileri yalnızca sizin adınıza ve Veri işleme sözleşmesine göre işler. Verilerinizi istediğiniz zaman dışa aktarabilirsiniz.",
      ],
    },
    {
      heading: "Kabul edilemez kullanım",
      items: [
        "İzni olmayan kişilere pazarlama mesajı göndermek (İYS ve KVKK kurallarına aykırı kullanım).",
        "Yanıltıcı, yasa dışı veya üçüncü kişilerin haklarını ihlal eden içerik.",
        "Sistemin güvenliğini aşmaya, başka işletmelerin verisine erişmeye veya hizmeti aşırı yüklemeye çalışmak.",
      ],
      paragraphs: ["Bu durumlarda hesap salt okunur moda alınabilir veya kapatılabilir."],
    },
    {
      heading: "Hizmet seviyesi",
      paragraphs: [
        "Hizmeti kesintisiz ve hatasız sunmak için en iyi gayreti gösteririz; ancak bakım, internet altyapısı veya hizmet sağlayıcı kaynaklı kesintiler olabilir. Planlı bakımlar önceden duyurulur. Pilot döneminde hizmet \"olduğu gibi\" sunulur.",
      ],
    },
    {
      heading: "Sorumluluğun sınırı",
      paragraphs: [
        "KobiFabrika; muhasebe, vergi veya hukuki danışmanlık vermez, resmî defter ve e-fatura yerine geçmez. Kayıtların doğruluğunu ve yasal bildirimlerinizi kontrol etmek işletmenin sorumluluğundadır. Kasıt ve ağır ihmal dışında dolaylı zararlardan sorumluluk kabul edilmez.",
      ],
    },
    {
      heading: "Hesabın kapatılması",
      paragraphs: [
        `Hesabınızı istediğiniz zaman kapatabilirsiniz (${company.email}). Kapatmadan önce verilerinizi dışa aktarabilirsiniz; kapanıştan sonra veriler yasal saklama süreleri dolunca silinir.`,
      ],
    },
    {
      heading: "Değişiklikler ve uygulanacak hukuk",
      paragraphs: [
        "Koşullar değişirse sayfadaki sürüm ve tarih güncellenir; esaslı değişikliklerde işletme sahibinden bir sonraki girişte yeniden onay istenir. Bu koşullara Türk hukuku uygulanır.",
      ],
    },
  ],
};

const dpa: LegalDoc = {
  slug: "veri-isleme-sozlesmesi",
  title: "Veri İşleme Sözleşmesi",
  short: "Veri işleme sözleşmesi",
  description: "İşletme (veri sorumlusu) ile KobiFabrika (veri işleyen) arasındaki veri işleme sözleşmesi.",
  version: "2026-10-20",
  updated,
  intro: `Bu sözleşme, KobiFabrika'yı kullanan işletme ("Veri Sorumlusu") ile ${controller} ("Veri İşleyen") arasında, işletmenin müşterilerine, tedarikçilerine ve çalışanlarına ait kişisel verilerin KobiFabrika'da işlenmesine ilişkindir. Kayıt sırasında kabul edilir.`,
  sections: [
    {
      heading: "Taraflar ve roller",
      paragraphs: [
        "İşletme, uygulamaya girdiği kişisel verilerin veri sorumlusudur: hangi veriyi hangi amaçla girdiğine, müşterilerini aydınlatmaya ve gerekli izinleri almaya kendisi karar verir. KobiFabrika bu verileri yalnızca işletmenin talimatıyla ve hizmetin sunulması için işler.",
      ],
    },
    {
      heading: "İşlenen veriler",
      items: [
        "Müşteri ve tedarikçi: ad, telefon, e-posta, adres, doğum tarihi (isteğe bağlı), notlar, cari hareketler, satışlar, randevular, iletişim izinleri.",
        "Çalışan: ad, telefon, unvan, çalışma saatleri, mesai ve izin kayıtları, prim, maaş (sadece Sahip görür).",
      ],
    },
    {
      heading: "KobiFabrika'nın yükümlülükleri",
      items: [
        "Verileri yalnızca hizmetin sunulması için işlemek; kendi amaçları için kullanmamak, satmamak.",
        "Uygun teknik ve idari tedbirleri almak: şifreli bağlantı, işletmeler arası ayrım, rol bazlı yetki, şifreli yedek, erişim kayıtları.",
        "Verilere erişen çalışanlarını gizlilikle yükümlü kılmak. Destek ekibi işletmenin müşteri verisini görmez.",
        "Veri ihlalini öğrendiğinde gecikmeksizin, en geç 72 saat içinde işletmeye bildirmek.",
        "İlgili kişi başvurularında ve Kurul taleplerinde işletmeye makul ölçüde yardım etmek (dışa aktarma ve silme araçları).",
      ],
    },
    {
      heading: "Alt işleyenler",
      paragraphs: [
        "İşletme, aşağıdaki alt işleyenlerin kullanılmasına onay verir. Yeni bir alt işleyen eklenmeden önce uygulama içinden haber verilir.",
        ...subProcessors.map((s) => `${s.name}: ${s.purpose} (${s.location}).`),
        transferText,
      ],
    },
    {
      heading: "Destek talepleri",
      paragraphs: [
        "İşletmenin gönderdiği geri bildirim ekran görüntülerinde kişisel veri bulunabilir. Bu görüntüler yalnızca destek talebinin çözümü için, yalnızca KobiFabrika destek ekibi tarafından görülür ve 90 gün sonra silinir.",
      ],
    },
    {
      heading: "İşletmenin yükümlülükleri",
      items: [
        "Müşterilerini aydınlatmak (örnek metin: Müşteri aydınlatma şablonu) ve pazarlama iletileri için açık izin almak.",
        "Kullanıcılarına yalnızca gerektiği kadar yetki vermek.",
      ],
    },
    {
      heading: "Süre ve sona erme",
      paragraphs: [
        "Sözleşme hesap açık olduğu sürece geçerlidir. Hesap kapanınca veriler, işletmeye dışa aktarma imkânı tanındıktan sonra yasal saklama süreleri dolunca silinir veya anonim hâle getirilir.",
      ],
    },
  ],
};

const sablon: LegalDoc = {
  slug: "kvkk/musteri-aydinlatma-sablonu",
  title: "Müşteri Aydınlatma Metni Şablonu",
  short: "Müşteri aydınlatma şablonu",
  description: "İşletmelerin kendi müşterileri için KVKK aydınlatma metni örneği.",
  version: "2026-10-20",
  updated,
  intro:
    "İşletmeler bu şablonu kendi müşterilerini aydınlatmak için kullanabilir. Köşeli parantezli yerleri doldurun. Online randevu sayfanızda bu metin işletme bilgilerinizle otomatik doldurulmuş olarak görünür.",
  sections: customerNoticeSections({ name: "[İşletme adı]", address: "[Adres]", contact: "[Telefon / e-posta]" }),
};

/** İşletmenin müşterileri için aydınlatma (şablon ve /r/[slug]/aydinlatma). */
export function customerNoticeSections(business: { name: string; address: string | null; contact: string | null }): LegalSection[] {
  const who = [business.name, business.address].filter(Boolean).join(", ");
  return [
    { heading: "Veri sorumlusu", paragraphs: [`${who}${business.contact ? ` (iletişim: ${business.contact})` : ""}.`] },
    {
      heading: "İşlenen veriler",
      paragraphs: ["Adınız, soyadınız, telefonunuz, e-postanız (verdiyseniz), randevu ve alışveriş bilgileriniz, ödeme ve borç-alacak kayıtlarınız."],
    },
    {
      heading: "Amaçlar ve hukuki sebep",
      paragraphs: [
        "Randevunuzun alınması, hatırlatılması ve değişikliklerin bildirilmesi; satış, tahsilat ve cari hesap kayıtlarının tutulması (KVKK m.5/2 (c) sözleşmenin ifası, (ç) hukuki yükümlülük). Kampanya ve fırsat mesajları yalnızca açık izninizle gönderilir; her mesajda çıkış bağlantısı bulunur.",
      ],
    },
    {
      heading: "Aktarım",
      paragraphs: [
        "Verileriniz işletmenin kullandığı KobiFabrika yazılımında saklanır; KobiFabrika veri işleyen olarak yalnızca işletmenin talimatıyla işler. Bildirimler için e-posta hizmet sağlayıcısı kullanılır. Yasal zorunluluk dışında üçüncü kişilerle paylaşılmaz.",
      ],
    },
    {
      heading: "Haklarınız",
      paragraphs: [
        `KVKK m.11'deki haklarınız (bilgi isteme, düzeltme, silme, itiraz) için ${business.contact ? business.contact : "işletmeye"} başvurabilirsiniz.`,
      ],
    },
  ];
}

export const legalDocs = { kvkk, gizlilik, cerez, kosullar, dpa, sablon } as const;

/** Site alt bilgisindeki hukuki bağlantılar. */
export const legalLinks = [kvkk, gizlilik, cerez, kosullar, dpa].map((d) => ({ href: `/${d.slug}`, label: d.short }));
