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

## خروجی Contract Refinement — ۲۰۲۶-۱۰-۰۹
- **SP01-01**: [پیشنهاد پشته و بررسی گیت](../technical/03-technical-gate-and-stack-proposal.md): Python/FastAPI/PostgreSQL/React صرفاً candidate؛ انتخاب نهایی و CI/build contract OPEN.
- **SP01-02**: [AccessRequest/AccessDecision مفهومی و سناریوهای ID-01…08](../technical/02-sprint-01-internal-interface-and-security-contract.md)؛ deny-by-default، actor/subject isolation، عدم اعتبار role کلاینت.
- **SP01-03**: ارزیابی evidence نسخه‌دار و scope/purpose با C-01…06؛ رضایت پژوهشی مستقل، ممنوعیت conflation با رضایت/تعهد هزینه.
- **SP01-04**: append-only AuditRecord و atomicity mutation/audit با A-01…05؛ schema تراکنش واقعی پس از Stack.
- **SP01-05**: synthetic-only B-01؛ قطع مسیر mock در Production، unit/contract/security suite آینده.
- **وضعیت: Sprint Candidate / Planning Only و NOT CODE-READY.** در هیچ‌یک از موارد بالا کد، تست سبز یا Code Authorization واقعی ایجاد نشده است.

## هم‌راستاسازی Sprint 01 با معماری ترکیبی — TRN-TECH-005 (۲۰۲۶-۱۰-۰۹)
- [جهت‌گیری مصوب](../technical/04-hybrid-dotnet-python-architecture-decision.md): **C#/.NET برای Skeleton و ownerهای Identity/Consent/Audit این Sprint**. پیشنهاد قدیمی Python/FastAPI در `TRN-TECH-004` تاریخی/منسوخ برای Backend اصلی است.
- `SP01-01`: ساختار .NET، تست و migration/CI پس از تصمیم جداگانه نسخه SDK/پایگاه داده/ابزار؛ فعلاً **Technical Planning Only**.
- `SP01-02…05`: اجرای آینده سناریوهای `TRN-TECH-003` در هسته .NET با synthetic-only، deny-by-default، append-only audit و rollback atomicity.
- مؤلفه **Python AI، مدل/Training/Inference و هر اتصال واقعی** خارج از Sprint 01 باقی می‌ماند و تنها contract boundary آینده در Technical ثبت می‌شود.
- **گیت Code هنوز OPEN**: قرارداد نسخه‌دار و تست، پشته اجرایی/نسخه‌ها، CI و Review فنی مستقل، سپس اجازه صریح Code؛ هیچ Stage/QA/Production یا Merge مجاز نشده است.

## Candidate execution baseline — TRN-TECH-006/007/008 (۲۰۲۶-۱۰-۰۹)
- **SP01-01**: [پشته .NET 10 کاندید](../technical/05-sprint-01-reproducible-stack-candidate.md) با SDK `10.0.401`، EF `10.0.12`، Npgsql provider `10.0.3`، PostgreSQL `18.6` و xUnit `4.0.1`؛ Release verification انجام شده ولی اجرای build/restore هنوز خیر.
- **SP01-02/03/04**: [قرارداد .NET و سناریوهای امنیتی](../technical/06-sprint-01-dotnet-contracts-and-test-acceptance.md) با ۲۰ مورد اصلی و ۸ مورد تکمیلی پیشنهادی؛ rollback اتمیک و append-only باید در DB واقعی اثبات شوند.
- **SP01-05**: CI شامل locked restore، static analysis، unit/contract/security، PostgreSQL migration/concurrency، synthetic-only isolation؛ [Gate](../technical/07-sprint-01-technical-gate-review.md) همچنان **NOT CODE-READY** است.
- فقط بعد از تأیید قرارداد/نسخه‌ها و **دستور صریح Code Authorization**، پیاده‌سازی در PR جدید Draft آغاز شود؛ هیچ Python AI، Frontend، بیمه/پول/هویت/درمان واقعی در Sprint 01 فعال نیست.

## خروجی بررسی قرارداد V0 و گیت اجرایی — TRN-TECH-009/010 (۲۰۲۶-۱۰-۰۹)
[قرارداد V0](../technical/08-sprint-01-versioned-internal-contract-freeze-candidate.md) برای `SP01-01…05` مرز test-only .NET، `AccessPolicy/ConsentHistory/AuditAppend`، نسخه‌گذاری و خطای امن را تعیین می‌کند؛ [گزارش بازبینی](../technical/09-sprint-01-db-ci-review-and-admission-record.md) تمام ریسک‌های P0 و شواهد لازم قبل از اجرای کد را مشخص می‌کند. **Contract V1، کدنویسی، CI، DB migration و Code Authorization هنوز انجام نشده‌اند**؛ آزمایش فقط پس از اجازه کدنویسی در PR Draft مستقل ممکن است.

## Sprint 01 Code Authorization — 2026-10-09

The owner explicitly authorized **only** synthetic C#/.NET identity/access, separate consent, append-only audit and automated tests, with **a new Draft code PR, CI, and no Merge/Stage/Production**. This supersedes the prior planning-only gate solely for the named Code slice. Technical documents retain their earlier historical review status.
- New branch `sprint-01-dotnet-identity-consent-audit` stacked on `docs/technical-foundation-01`.
- Test harness and fixtures never count as real identity/consent and must not be included in a production application.
- Mandatory member Web App requirement is recorded in the backlog as `TRN-EP-13` but its code is explicitly **out of scope** of Sprint 01.

## Hardening after initial green CI (Sprint 01 scope)

- Test-only PostgreSQL consent store constructor restricted to friend smoke-test assembly; no externally constructible production consent provider.
- Added a deliberately failing audit-trigger integration test to prove protected consent mutation rolls back on audit failure; added append-only evidence SQL refusal and invalid-purpose tests.
- CI explicitly verifies absence of deployable API, AI, Clinical and Payment code projects.
- The **Member Web App remains an obligatory separate Epic**, not hidden in Sprint 01 or treated as implemented. See [TRN-EP-13](../product/04-product-backlog.md).

## Code Review findings — CR-01…CR-04 (۲۰۲۶-۱۰-۰۹)
[گزارش بازبینی](../reviews/TRN-SP-01-security-code-review.md) شامل رفع خطاهای جداسازی tenant در رضایت، خطای مثبت کاذب در آزمون‌ها، جداسازی نقش محدود PostgreSQL و تفکیک stale-version از replay است. **هر دو CI کد SHA `9f002fc2c442f2d0366552cf845ff3cf7f3db67c` موفق‌اند**؛ اما **Code Review مستقل هنوز تأیید نشده است**، همه سناریوهای قراردادی تحت پوشش آزمون کامل نیستند و Stage/QA/Release/Production یا Merge انجام نشده است. وب‌اپ عضو در `TRN-EP-13` الزامی و خارج از Sprint 01 باقی می‌ماند.

## Security follow-up CR-05 — audited synthetic access (2026-10-09)

[Updated review](../reviews/TRN-SP-01-security-code-review.md) records `CR-05`: synthetic .NET access policy decisions are now durably logged as `ACCESS_POLICY_TEST` before returning ALLOW/DENY; test-only consent read/write paths fail closed if this audit write is unavailable. Real PostgreSQL fault injection and allow/deny verification passed for `c50044f9bab90c27311fda5912f5b0334da82ab8` in [PR CI](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37954557243) and [push CI](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/37954549116). This is not full contractual coverage or independent Code Review approval. Web App `TRN-EP-13` remains mandatory and not yet coded. No merge, Stage, QA or Production.
