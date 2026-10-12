# TRN-TECH-004 — بررسی گیت Technical، پیشنهاد پشته و شروط ورود به Sprint 01

- تاریخ: 2026-10-09
- **وضعیت ارزیابی: PARTIAL / NOT CODE-READY**؛ بررسی مستندی فعلی، **نه تأیید مستقل Code Review** و نه اجازه Code
- مرجع: `TRN-TECH-001/002/003`، `TRN-PB-001`، `TRN-SP-01` و Business `TRN-BIZ-001…052`.

## ۱. پشته پیشنهادی، نه مصوب

**پیشنهاد قابل review برای بهینه‌سازی سرعت توسعه و کیفیت آزمون**:
- Backend: **Python + FastAPI**؛ persistence: **PostgreSQL**؛ migrations: **Alembic**؛ ORM: **SQLAlchemy**؛ تست **Pytest** و static checks.
- Web UI: **TypeScript + React**؛ browser regression با **Playwright**. جداسازی داده بالینی واقعی از web client و حفاظت session design باید در قرارداد امنیتی مشخص شود.
- معماری اجرای اولیه: **modular monolith** با ماژول‌های owner-oriented و APIهای داخلی/نسخه‌دار. خدمات حساس را به صورت جداشدنی طراحی کنیم ولی بدون شواهد مقیاس فعلی microserviceهای متعدد ایجاد نکنیم.
- امنیت دسترسی واقعی: **OIDC/OAuth با client مناسب و اعتبارسنجی سمت سرور** به‌عنوان مسیر پیشنهادی؛ **هیچ IdP، Issuer، Client ID، جریان login، Provider identity-verification، hostname یا token فعلی انتخاب/تأیید نشده است**.
- داده، message transport، زیرساخت، tenancy مدل واقعی و AI model/runtime: **OPEN**؛ انتخاب نسخه و deployment target باید جداگانه بر اساس نیاز/تست و قیود واقعی انجام شود.

این پیشنهاد **صرفاً ADR candidate / DESIGN PROPOSAL** است. این سند قرارداد الزام‌آور انتخاب vendor، dependency version، host، فرمول مالی یا مدل AI نیست. نه stack نهایی و نه ظرفیت سخت‌افزار را از سایر پروژه‌ها به ترنم تعمیم ندهید.

## ۲. جدول ارزیابی گیت

| Gate | شواهد موجود | وضعیت | کار بعدی |
|---|---|---|---|
| Business reference/traceability | TRN-BIZ-001…052 + TRN-BUS-HANDOFF-001 | READY FOR TECH DESIGN؛ نه clearing مجوزهای واقعی | نگه‌داشتن link از هر contract/test به تصمیم |
| Bounded Context ownership | TRN-TECH-001 | DRAFT | تأیید boundaries و transaction ownership در review |
| Actor/authorization/consent/audit contract | TRN-TECH-003 + test matrix | PROPOSED | بازبینی role mapping، boundary، failure modes |
| Stack/CI/test execution target | پیشنهاد فوق | OPEN / BLOCKS CODE | تصمیم صریح/ثبت ADR منتخب + قابل اجرای reproducible tests |
| HTTP/storage/error/version contract | کلیات TRN-TECH-002/003 | OPEN / BLOCKS CODE | قرارداد دقیق Sprint 01 پس از stack |
| Synthetic-only data/test boundary | TRN-TECH-003 | DESIGN READY، TESTS NOT EXECUTED | fixtureها، negative tests، جداسازی محیط آزمون |
| IdP and legal real identity | هیچ provider/مجوز/قرارداد احراز نشده | EXTERNAL BLOCKER؛ فقط قابلیت واقعی | Adapter fail-closed، عدم پنهان‌کردن mock به‌عنوان real |
| Real health data, finances, providers, insurer | وابسته به مجوز/قرارداد واقعی | BLOCKED | خارج از Sprint 01؛ بدون استفاده از Production data |
| CI / Stage / QA / release | هیچ workflow/test اجراشده‌ای برای این PR ثبت نشده | NOT RUN / NOT ACCEPTED | بعد از Code Contract/Authorization، CI؛ Stage تنها با دستور صریح |

## ۳. یافته‌های review مستندی

1. **No P0 contract contradiction observed** در سطح مفهومی: `TRN-TECH-003` بین «هویت واقعی/تستی»، «رضایت درمانی/پژوهشی»، «ثبت انتخاب هزینه/تعهد رسمی»، «نمای صورتحساب/پرداخت کیف پول» تمایز می‌گذارد؛ هیچ permission یا پرداخت واقعی از داده نامعلوم استنباط نمی‌شود.
2. **Open critical gate #1:** Tech Stack هنوز تصویب نشده و schema/transport نهایی و مدل transaction بدون آن قابل اعلام READY نیست.
3. **Open critical gate #2:** تعریف واقعی نقش‌ها، قرارداد session/IdP و legal consent خارج از test-only boundary است؛ demo باید با synthetic-only و deny-by-default اجرا شود.
4. **Open critical gate #3:** معماری append-only ممیزی و آزمون atomicity باید با مدل persistence منتخب و کد واقعی اثبات شود؛ متن مستند ضمانت اجرای DB نیست.
5. **No CI evidence:** این review هیچ build/lint/test سبز، Code Review مستقل، Stage Admission یا QA انجام‌شده را ادعا نمی‌کند.

## ۴. بسته اجرایی مرحله بعد، بدون سؤال‌های Business

- **Technical sign-off package:** تصمیم درباره پشته پیشنهادی یا جایگزین آن و ثبت ADR؛ تعیین دقیق قرارداد داخلی actor/role/tenant+subject, purpose/version consent, audit atomicity, error contract, test boundary.
- سپس **Backlog refinement** برای SP01-01…05 و **Sprint Code Authorization** جداگانه، با branch/PR Draft جدید از head فنی، نه روی main یا PR Business.
- تا آن زمان PR #1 و PR #2 **Draft/Open/unmerged**. این سند اجازه تغییر Production، داده/پرداخت واقعی، AI بالینی یا bypass گیت ندارد.

**نتیجه:** طراحی فنی مشخص‌تر و آماده review است؛ **Technical Gate هنوز APPROVED نیست**. تصمیم بعدی، یک انتخاب فنی واقعی (پشته) است، نه سؤال تجاری جدید.

## مصوبه مؤخر مالک محصول درباره پشته ترکیبی — TRN-TECH-005 (۲۰۲۶-۱۰-۰۹)
[سند تصمیم معماری](04-hybrid-dotnet-python-architecture-decision.md) **جهت‌گیری ترکیبی C#/.NET برای هسته عملیاتی و Python برای AI داخلی** را جایگزین نامزدی قبلی Python/FastAPI برای کل Backend کرد. بخش «پشته پیشنهادی، نه مصوب» در ابتدای این سند **تصویر تاریخیِ قبل از این مصوبه** است و از این پس پیشنهاد Backend اصلی Python محسوب نمی‌شود.
- **ACCEPTED (Architecture direction):** C#/.NET operational core + Python internal AI computation، سیاست‌ها و تغییر داده حساس تنها از مالک .NET، خروجی Python صرفاً مشروط به مجوز، lineage و gate انسانی.
- **OPEN (Concrete Stack):** .NET SDK/runtime versions، انتخاب ORM/migration/tests و database، نسخه Python/AI dependencies، frontend، internal boundary transport، CI/hosting/IdP/model family.
- **Sprint 01:** فقط هویت/رضایت/ممیزی مصنوعی در C#/.NET؛ Python AI خارج از Slice. قرارداد `TRN-TECH-003` و موارد fail-closed پابرجا است.
- **Technical Gate STATUS: PARTIAL / NOT CODE-READY.** تصمیم معماری به‌تنهایی Code Authorization، CI موفق، Code Review یا Release Approval نمی‌شود.

## تکمیل گیت با TRN-TECH-006/007/008 (۲۰۲۶-۱۰-۰۹)
پشته نسخه‌دار پیشنهادی برای `Sprint 01` اکنون در [TRN-TECH-006](05-sprint-01-reproducible-stack-candidate.md) و قراردادها/برنامه آزمون در [TRN-TECH-007](06-sprint-01-dotnet-contracts-and-test-acceptance.md) آمده است. [Ledger پذیرش](07-sprint-01-technical-gate-review.md) نشان می‌دهد **نسخه‌ها release-verified اما build/test-unverified** هستند. انتخاب قبلی Python/FastAPI برای Backend کل سامانه منسوخ است؛ هیچ Code، CI سبز یا ورود به Stage نتیجه نشده است. وضعیت رسمی: **PARTIAL / NOT CODE-READY** تا review و مجوز صریح.
