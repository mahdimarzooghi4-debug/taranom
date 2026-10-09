# TRN-TECH-003 — قرارداد داخلی قابل آزمون Sprint 01: هویت، مجوز، رضایت و ممیزی

- **وضعیت: TECHNICAL DESIGN / PROPOSED FOR REVIEW؛ نه Code Authorization**
- تاریخ: 2026-10-09
- مبنا: `TRN-BIZ-018…023` (احراز هویت واقعی، اصلاح و بازآزمایی)، `TRN-BIZ-029` (حامی حق دسترسی به درمان ندارد)، `TRN-BIZ-034/035/036` (فوریت، دسترسی پرونده، رضایت مستقل پژوهشی)، `TRN-BIZ-037…039` (AI داخلی و حاکمیت انسانی)، `TRN-BIZ-052` (اطلاع و انتخاب مالی بدون تعهد رسمی)، `TRN-TECH-001/002`.
- **Scope:** قراردادهای *داخلی* و اجرای آزمون صرفاً با داده ساختگی. نام‌های مفهومی زیر قرارداد پیشنهادشده‌اند؛ **نه REST API عمومی، نه schema پایگاه داده نهایی، نه اعلام رضایت قانونی واقعی**.

## ۱. مرزهای مالکیت و وابستگی

| Owner | Command/query مجاز در این Slice | حق انجام‌نداده |
|---|---|---|
| Identity & Access | ثبت/خواندن actor مصنوعی و assertion آزمونی؛ `EvaluateAccess` | احراز هویت ملی واقعی، صدور نقش بالینی واقعی، اعتباردادن به assertion کلاینت |
| Consent & Privacy | `RecordSyntheticConsentEvidence`، `EvaluateSyntheticConsent` و ثبت history | اخذ رضایت معتبر درمانی/پژوهشی واقعی، انتقال پرونده یا برداشت رضایت ضمنی |
| Audit & Operations | `AppendAuditRecord` و read فقط برای نقش/مسیر مجاز آزمونی | ویرایش/حذف رکوردهای ثبت‌شده، نگهداری PII/PHI در payload ممیزی |
| Test Harness | ساخت actor/member/tenant/test-origin با نشان مصنوعی | استفاده از credential/provider واقعی یا پذیرش test assertion در Production |

- Identity مالک *واقعیت ادعای هویت* است، اما **اعتبار IdP و مرجع احراز** برای Production هنوز BLOCKED است. بخش حریم خصوصی مالک شواهد رضایت است؛ خدمات دیگر صرفاً از قرارداد ارزیابی دسترسی/رضایت استفاده می‌کنند، نه از داده درونی یکدیگر.
- هر شناسه عضو/سازمان تست از namespace مصنوعی و صریحاً غیرقابل تبدیل به شناسه حقیقی استفاده کند. وجود شناسه سازمان به خودی خود مالکیت داده پزشکی یا انتقال حق دسترسی نیست.

## ۲. قرارداد هویت/مجوز داخلی پیشنهادی

درون‌داد مفهومی `AccessRequest`:
- `actor_ref`, `subject_ref`, `resource_type`, `resource_ref`, `action`, `scope_ref` (اگر دامنه سازمانی دارد)، `assertion_origin`, `evidence_ref`, `correlation_ref`.
- در تست، `assertion_origin` فقط باید از `SYNTHETIC_TEST_HARNESS` و با نشانه محیط مجزای غیرProduction صادر شود. این قرارداد **هرگز bearer token واقعی یا IdP ادعایی تعریف نمی‌کند**.
- برون‌داد `AccessDecision`: `DENY` به‌صورت پیش‌فرض؛ `ALLOW` فقط وقتی actor، منبع، عمل، scope/ownership و شاهد اختیار معتبرِ آزمونی همگی صریحاً مطابقت دارند. `reason_code` غیرحساس و `audit_ref` قابل پیگیری باشد؛ **نبود داده یا خطای ارزیابی = DENY**.
- کارفرما/حامی صرفاً با نقش پرداخت‌کننده عضویت به پرونده، صورت‌حساب درمان یا کیف پول خصوصی دسترسی ندارد؛ کاربران یک عضو/سازمان به داده شخص دیگر دسترسی پیش‌فرض ندارند.
- فهرست نهایی نقش‌ها و scopeهای واقعی پزشکی و قرارداد OIDC/IdP هنوز نیازمند تأیید امنیتی/حقوقی‌اند؛ تست نمونه نقش‌های `MEMBER_TEST`، `CLINICIAN_TEST`، `COORDINATOR_TEST`، `SPONSOR_TEST` **تعریف مجوز واقعی نیست**.
- نگاشت actor تست به Production ممنوع؛ آزمون must-fail برای درخواست با origin ناشناس/غیرآزمونی یا ادعای self-assigned role.

## ۳. قرارداد رضایت تفکیک‌شده و نسخه‌دارِ تستی

`SyntheticConsentEvidence` فقط با این ویژگی‌های مفهومی:
- `evidence_ref` یکتا، `actor_ref`، `subject_ref`، `purpose`، `scope_ref`، `source_type`، `source_version_ref`، `recorded_at` و `audit_ref`. بدون PHI در payload.
- `purpose` برای tests دست‌کم متمایز است: `CARE_COMMUNICATION_TEST`، `HEALTH_DATA_SHARE_TEST`، `SECONDARY_AI_RESEARCH_TEST`. هیچ‌یک به تنهایی مجوز حقوقی واقعی محسوب نمی‌شود و نگاشت نهایی purposeهای واقعی بعداً تأیید می‌شود.
- `SyntheticConsentDecision` مستقل برای هر purpose/scope است؛ رضایت/عضویت درمانی **به رضایت پژوهشی یا AI سرایت نمی‌کند**. نبود شاهد، mismatch هویت/نسخه/حوزه، انقضا یا وضعیت نامعلوم => `DENY`.
- جریان آزمایشی انصراف باید یک **رویداد جدید** با lineage به نسخه پیشین باشد؛ بازنویسی/حذف تاریخچه مجاز نیست. اثر حقوقی انصراف بر داده و مشتقات قبلی نامعین است و از این آزمایش استنباط نمی‌شود.
- `ConsentEvidence` واقعی بدون الگوی حقوقی معتبر، ثبت رضایت و فرایند احراز هویت **اصلاً Code-ready نیست**. برای پزشک حتی وجود رضایت، شرط مجوز حرفه‌ای/نیاز درمانی را حذف نمی‌کند.
- ثبت اطلاع و انتخاب مالیِ `TRN-BIZ-052` **کاملاً مستقل** از انواع رضایت سلامت/پژوهش و از تعهد مالی رسمی است؛ در این Sprint فقط جداسازی مفهومی و تست ممنوعیت conflation انجام می‌شود.

## ۴. قرارداد ممیزی الحاقی (Append-only) و حداقل‌گرایی

`AuditRecord` تستی باید به رویداد دامنه‌ای یا ارزیابی دسترسی/رضایت **قابل ردیابی** باشد و دست‌کم `event_ref`, `actor_ref` (یا `SYSTEM_TEST` با علت)، `action`, `decision`, `resource_category`, `source_ref`, `recorded_at`, `correlation_ref` و `test_origin` داشته باشد.
- append-only: عملیات `UPDATE` و `DELETE` برای رکورد ثبت‌شده **تحت تست باید رد شوند**؛ اصلاح صرفاً با رکورد جدیدِ اشاره‌کننده به نسخه قبلی باشد.
- ثبت تغییر state آزمونیِ محافظت‌شده باید همزمان/اتمیک با شاهد audit متناظر پذیرفته شود یا کل تغییر رد شود؛ سازوکار transaction/outbox به پشته انتخابی و review بعدی وابسته است. **موفقیت بدون شاهد audit برای mutation محافظت‌شده ممنوع است**.
- payload ممیزی نباید token، کلید، کدملی، متن پرونده، شرح درمان، صورتحساب، آدرس کامل یا اطلاعات حساس دیگر را حمل کند. لاگ خطا هم نباید این داده‌ها را نشت دهد.
- از شناسه قابل ردیابی مصنوعی و reference استفاده شود؛ الگوریتم hash/retention مدت‌دار/امضای قانونی هنوز تعریف و تصویب نشده است. هیچ ادعای tamper-proof بودن در Production مطرح نشود.

## ۵. سناریوهای پذیرش قابل تبدیل مستقیم به تست

| Test ID | Arrange (همگی Synthetic) | Expected |
|---|---|---|
| ID-01 | درخواست بدون actor معتبر | DENY و ثبت رد بدون PII |
| ID-02 | actor معتبر اما resource مربوط به عضو دیگر | DENY |
| ID-03 | actor عضو با scope درست و مجوز آزمونی صریح | ALLOW فقط برای action دقیق |
| ID-04 | ادعای role توسط client بدون assertion مورد اعتماد | DENY |
| ID-05 | actor آزمونی از tenant/سازمان نامرتبط | DENY |
| ID-06 | sponsor فقط پرداخت‌کننده حق عضویت و خواهان پرونده یا هزینه عضو | DENY |
| ID-07 | شناسه/نسخه/شاهد مجوز مفقود یا stale | DENY |
| ID-08 | درخواست با origin تست در محیط غیرآزمونی | DENY؛ harness قابل اتصال به Production نیست |
| C-01 | رضایت تستی درمانی اما درخواست استفاده AI پژوهشی | DENY |
| C-02 | scope/subject/purpose mismatch | DENY |
| C-03 | رضایت معتبر آزمونی دقیق در زمینه مجاز | success محدود به همان purpose/scope و audit |
| C-04 | انصراف آزمونی پس از اعطای رضایت | ارزیابی‌های بعدی مطابق وضعیت جدید DENY؛ تاریخچه حفظ |
| C-05 | عدم دسترسی به شاهد رضایت | DENY؛ نه رضایت پیش‌فرض |
| C-06 | ثبت انتخاب هزینه TRN-BIZ-052 | **هیچ** رضایت بالینی/AI یا تعهد پرداخت خودکار |
| A-01 | append audit با شناسه/ردپای معتبر تستی | append؛ query محدود به مجوز |
| A-02 | تلاش برای update/delete رکورد ممیزی | reject و شاهد آزمون |
| A-03 | mutation محافظت‌شده با شکست audit | کل mutation rollback / بدون state نیمه‌ثبت‌شده |
| A-04 | تلاش ثبت token یا PHI در audit یا error log | reject/redact طبق قرارداد تأییدشده، بدون افشا |
| A-05 | replay تکراری یک شناسه event/correlation | بدون دو بار اثر تغییر state؛ سیاست خطای دقیق در design انتخاب شود |
| B-01 | درخواست استفاده از بیمه/پول/پرونده بالینی واقعی | unsupported/fail-closed؛ هیچ اتصال خارجی یا اثر مالی |

**این‌ها Test Cases پیشنهادی برای Code آینده‌اند و اجرای سبز یا وجود کد را ادعا نمی‌کنند.** بعد از انتخاب Stack، هر سناریو به unit/contract/integration/security test با fixture مصنوعی و گیت CI تبدیل شود.

## ۶. خروجی و معیار بسته‌شدن قرارداد

این سند زمانی READY FOR CODE REVIEW فنی است که: mapping دقیق actor/resource/action و شناسه امن، سیاست خطا و همزمانی، قرارداد واقعی transport و schema/versioning، شکل ذخیره‌سازی/audit و تست‌های fail-closed با Stack منتخب در PR مستقل Sprint تعیین و بازبینی شوند. **انتخاب IdP، داده سلامت واقعی، رضایت قانونی، صورت‌حساب یا خدمت پزشکی خارج از Slice است.**
