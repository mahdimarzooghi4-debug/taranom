# TRN-SP-01 — Identity, Privacy & Audit Foundation

- **وضعیت: SPRINT CANDIDATE / PLANNING ONLY — Code Authorization صادر نشده**
- مبدأ: [Technical Baseline](../technical/00-architecture-baseline.md) و [Product Backlog](../product/04-product-backlog.md)
- محدوده: تأسیس مسیر امن توسعه داخلی برای معماری جامع، بدون ادعای خدمات سلامت، بیمه یا عملیات مالی واقعی.

## هدف

ساخت درون‌سازمانی آینده را از ابتدا با **مرز actor/role، جداسازی داده سلامت، رضایت تفکیک‌شده و ثبت ممیزی** طراحی کنیم؛ هیچ مرحله‌ای وارد تشخیص بالینی، محاسبه اعتبار کیف پول، قرارداد بیرونی، سرویس AI یا Production نمی‌شود.

## Stories پیشنهادی و پذیرش

| Story | قرارداد مورد نیاز پیش از Code | ملاک پذیرش آینده |
|---|---|---|
| SP01-01 Architecture skeleton | تصمیم آگاهانه درباره Stack و structure repo | ماژول‌های مستقل و تست build/lint سبز |
| SP01-02 Actor & authorization boundary | تعریف role/actor و سیاست deny-by-default | تست عدم دسترسی، جداسازی tenant و نبود نقش فرضی |
| SP01-03 Consent boundary | تفکیک رضایت درمان/اشتراک داده/پژوهش AI | ممنوعیت رضایت ضمنی و آزمون رد دسترسی |
| SP01-04 Append-only audit contract | حداقل actor/action/evidence بدون ذخیره بی‌مورد PII | ردپای قابل بررسی، عدم تغییر رویداد ثبت‌شده |
| SP01-05 Synthetic smoke tests | داده کامل ساختگی و محیط غیرProduction | موفقیت unit/contract/security؛ هیچ endpoint بیرونی واقعی |

## خارج از محدوده

- پرداخت، ایجاد مانده، فرمول تخصیص، پرداخت بانک یا بیمه، هویت واقعی، تماس اورژانس، تشخیص/AI، پرونده بیمار واقعی، خدمت درمان، Stage واقعی، Production.
- هیچ provider، API، secret، model، threshold، server، bucket یا تاریخ release فرض نشود.

## Gate قبل از شروع Code

1. قرارداد انتخاب stack و نحوه اجرای CI/تست توسط مسئول فنی/محصول پذیرفته شود.
2. boundary و آزمون قرارداد Stories فوق به شکل دقیق قابل اجرا مستند شوند.
3. PR Technical مستقل در وضعیت Draft بررسی شود؛ Code فقط پس از عبور از گیت Technical و دستور Code Authorization.
4. پیاده‌سازی در شاخه Sprint و Draft PR مستقل، تست→Code→Code Review→CI؛ بدون ادعای Stage یا انتشار.

**تا گیت‌ها احراز نشوند، این سند Sprint Planning است نه انجام Sprint/Code.**
