# TRN-PB-001 — Product Backlog اولیه مبتنی بر تصمیم‌های مصوب

- **وضعیت: DRAFT / برای Scrum Refinement**؛ نه MVP رسمی و نه مجوز Code
- مبنا: `TRN-BIZ-001…052` و [TRN-TECH-001](../technical/00-architecture-baseline.md)
- سیاست: ترتیب ساخت تدریجی داخلی است؛ **نخستین عرضه رسمی باید در دامنه جامع و ملی مصوب باشد** و گیت‌های امنیت، حقوق، قرارداد، QA و Release را بگذراند. توسعه/آزمایش محدود با عرضه رسمی اشتباه نشود.

| Epic | ارزش/خروجی | آمادگی برای توسعه | گیت |
|---|---|---|---|
| TRN-EP-01 Identity & Access | کنترل کاربر، نقش، حق دسترسی و ممیزی | **Technical-first** | تعیین IdP و قرارداد هویت پیش از واقعی‌شدن |
| TRN-EP-02 Consent & Data Governance | رضایت/اشتراک جداگانه، حداقل داده و ممیزی | **Technical-first** | حق پردازش و سیاست قانونی قبل از داده واقعی |
| TRN-EP-03 Membership | فعال‌سازی رسمی، ۹۰ روز، تمدید و عدم تمدید | Contract-needed | شرایط قرارداد/مبدأ فعال‌سازی و اثبات پرداخت |
| TRN-EP-04 Provider Registry | مدرک صلاحیت و قراردادهای مستقیم/شبکه‌ای | Contract-needed | مجوز/قرارداد و صاحب اختیار واقعی |
| TRN-EP-05 Care Coordination | درخواست غیرتشخیصی، ارتباط، پیگیری، فوریتِ تفکیک‌شده | Clinically gated | مسئول بالینی، ایمنی، محدوده خدمت |
| TRN-EP-06 Personal Health Record | پرونده مستقل عضو و دسترسی محدود تیم درمان | Legally gated | رضایت، حفاظت داده و مجوز |
| TRN-EP-07 Insurance Coordination | استحقاق، سهم بیمه و اختلاف با مرجع صاحب‌اختیار | Externally gated | بیمه‌گر/واسطه واقعی و قرارداد/API |
| TRN-EP-08 Member Credit & Finance | کیف پول شخصی، تخصیص برابر واجدشرایط‌ها، پرداخت مستقیم مجاز | Financially gated | فرمول مصوب، پشتوانه، نهاد/مجوز و حسابرسی |
| TRN-EP-09 Billing & Cost Awareness | برآورد غیرقطعی، ثبت انتخاب، صورتحساب رسمی و اختلاف | Partially design-ready | سند رسمی، حقوق پذیرش هزینه و داده معتبر |
| TRN-EP-10 Quality & Complaints | کیفیت/صلاحیت، مسیر واحد اعتراض، مسئولیت تفکیک‌شده | Contract-needed | شبکه، قانون، حق رسیدگی |
| TRN-EP-11 Internal AI | Dataset مجاز، ارزیابی، Candidate، Human Promotion | Data/clinical gated | رضایت داده، سنجش ایمنی، مدل داخلی، ارزیابی |
| TRN-EP-12 Operational Readiness | Stage/QA، ثبت شواهد، monitoring، backup/recovery، release | Technical-first | محیط واقعی و تایید Release |

## بسته بعدی قابل کار

**Sprint Candidate 01: `TRN-SP-01`** — [برنامه اولیه](../sprints/01-identity-privacy-audit-foundation.md). خروجی پایه فنی غیرحساس با داده ساختگی و عدم اتصال خارجی؛ قبل از Code باید Technical Gate و آزمون‌های دقیق پذیرفته شوند.

**قاعده Refinement:** فقط ابهام‌هایی را برای تصمیم جدید بالا بیاورید که فوراً قرارداد Sprint را مسدود می‌کنند؛ برای سایر موارد `Blocked by real legal/financial/clinical evidence` کافی است. دیگر زنجیره پرسش‌های تک‌تصمیمی Business ادامه داده نمی‌شود.

## Refinement فنی گام بعد — TRN-TECH-003/004 (۲۰۲۶-۱۰-۰۹)
برای `TRN-EP-01/02/12`، [قرارداد داخلی Sprint 01](../technical/02-sprint-01-internal-interface-and-security-contract.md) سناریوهای منفی/امنیتی `ID-01…08`, `C-01…06`, `A-01…05`, `B-01` را تعریف کرده است. [Technical Gate](../technical/03-technical-gate-and-stack-proposal.md) هنوز **NOT CODE-READY** است؛ Stack/transport/storage/CI و Code Authorization مسدودند. سایر Epicها به مجوز و قرارداد واقعی گره خورده‌اند و هیچ پرداخت، داده سلامت یا AI واقعی از Backlog قابل فعال‌سازی نیست.

## مالکیت فناوری پس از TRN-TECH-005 (۲۰۲۶-۱۰-۰۹)
[مصوبه معماری ترکیبی](../technical/04-hybrid-dotnet-python-architecture-decision.md) برای `TRN-EP-01…10/12` جهت‌گیری **هسته عملیاتی C#/.NET** را پذیرفته است؛ `TRN-EP-11` فقط دارای **مؤلفه محاسباتی Python داخلی** با حاکمیت/مجوز و Human Promotion متعلق به هسته است. این نگاشت **به‌معنای آمادگی کدنویسی Epicهای حساس نیست**: همه مجوزها، قراردادهای واقعی، منابع معتبر، backend integration و safety gates مستقل باقی‌اند. `TRN-SP-01` فقط پایه .NET با داده ساختگی است؛ AI Python هنوز وارد Sprint نمی‌شود.

## گیت‌های فنی صریح برای TRN-SP-01 — TRN-TECH-006/007/008 (۲۰۲۶-۱۰-۰۹)
پشته نسخه‌دار [TRN-TECH-006](../technical/05-sprint-01-reproducible-stack-candidate.md)، آزمون‌های [TRN-TECH-007](../technical/06-sprint-01-dotnet-contracts-and-test-acceptance.md) و [گیت Technical](../technical/07-sprint-01-technical-gate-review.md) پشتوانه آینده `TRN-EP-01/02/12` هستند. **نسخه‌ها هنوز تأیید نهایی/آزموده نشده‌اند**؛ هزینه تست/امنیت و Code Authorization پیش‌شرط‌اند، نه بهانه‌ای برای شروع گردش مالی یا داده سلامت واقعی. سایر Epicها تحت قراردادهای تخصصی مربوطه BLOCKED می‌مانند.

## بسته نهایی‌سازی فنی Sprint 01 — TRN-TECH-009/010 (۲۰۲۶-۱۰-۰۹)
[Internal contract freeze candidate](../technical/08-sprint-01-versioned-internal-contract-freeze-candidate.md) و [admission review](../technical/09-sprint-01-db-ci-review-and-admission-record.md) گیت‌های `TRN-EP-01/02/12` را مشخص می‌کنند: تأیید V1 قراردادهای تستی، تأیید/قفل پشته، اجرای ۲۸ آزمون طرح‌شده و CI exact-head پس از **مجوز صریح Code**. هیچ داده شخص حقیقی، AI Python، بیمه، درمان، وجه یا endpoint بیرونی در Sprint 01 وارد اجرا نمی‌شود.

## TRN-EP-13 — Member Web Application / وب‌اپ عضو (MANDATORY)

**Owner clarification (2026-10-09):** a dedicated **member-facing Web App** is required; a backend-only or admin-only product is insufficient. [Delivery note](../sprints/02-member-web-app-delivery-note.md).
- Candidate UI foundation: React/TypeScript, Persian RTL, responsive and accessible; this is a *technical candidate*, not a production-deployed product.
- Expected journeys depend on accepted contracts: authenticated member workspace, membership and noncashable wallet read views, lawful consent and health data access, care coordination, nondiagnostic safety information, cost disclosure and choice tracking, insurance/billing dispute follow-up.
- Clinical, wallet, financial and insurer data must be sourced from lawful backend owners with actual integrations; **no hardcoded fake balance/coverage/treatment** as live data.
- Member Web App implementation is a **separate future Sprint with separate Code Authorization**; Sprint 01 remains strictly synthetic .NET identity/consent/audit.

## TRN-SP-02 PLANNING CANDIDATE — Mandatory member-facing Web App (2026-10-09)

The owner-mandated `TRN-EP-13` now has [Sprint 02 planning](../sprints/03-member-web-app-sprint-02-planning.md) and a [security/read-model contract candidate](../technical/10-member-web-app-interface-contract-candidate.md). Proposed first slice: React/TypeScript Persian RTL responsive member shell, protected-layout contracts, safe unavailable states and browser tests — **planning only, not Code-authorized**. Real member account, wallet, insurance, clinical and care read models must be supplied by lawful authoritative .NET owners; a synthetic Sprint 01 identity is not a real member login. The first official national release still requires comprehensive readiness. This documentation-only PR must remain Draft/Open and unmerged.

## TRN-SP-03 CONTRACT READINESS — Member Web real identity/read integration (2026-10-10)

Sprint 01 has a **synthetic-only .NET foundation** in [Draft PR #3](https://github.com/mahdimarzooghi4-debug/taranom/pull/3), and owner-authorized Sprint 02 has a **disconnected RTL Member Web shell** in [Draft PR #5](https://github.com/mahdimarzooghi4-debug/taranom/pull/5), with 16 real Chromium browser cases and exact-head CI green. These newer facts supersede older historical “no Web App code/no Code Authorization” status paragraphs above; the old statements remain for chronological traceability.

Actual integration remains **BLOCKED**. [TRN-TECH-012](../technical/12-member-session-and-governed-read-boundary-candidate.md) and [Sprint 03 contract-readiness plan](../sprints/05-member-integration-sprint-03-planning.md) map authoritative member identity, optional BFF trust boundary, lawful per-owner data reads and explicit negative tests before any real Code Authorization. No real IdP, patient data, finance, insurer, provider or speculative API can be introduced. Independent reviews for PR #3 and #5 are tracked in [issue #6](https://github.com/mahdimarzooghi4-debug/taranom/issues/6) and [reviewer handoff](../reviews/TRN-independent-code-review-handoff.md): **OPEN, not approved**. All stacked PRs stay Draft/Open/unmerged; no Stage/QA/Release/Production.

## TRN-SP-41 refinement — TRN-EP-07/09/13 (2026-10-11)

Following the accepted TRN-BIZ-048/049/050, authorize only a **test-only after-service source-lineage read boundary**, not operational insurance/billing. [TRN-TECH-050](../technical/50-unified-billing-source-lineage.md) and [TRN-SP-41](../sprints/49-sprint-41-unified-billing-source-lineage.md) pin five source/authority categories and corrected/disputed/withdrawn provenance with independent browser checks. Sprint28's **nonbinding pre-treatment estimate** remains separate. Official insurer decisions, lawful invoices, actual credit balance, settlement, contract and real API remain externally gated; national launch still denied by default.

## Sprint 42 refinement — TRN-EP-01/02/06/13 (2026-10-11)

The accepted TRN-BIZ-035/036 requires member-controlled *clinical need* access without sponsor privileges or implicit AI/research consent. [TRN-TECH-051](../technical/51-member-clinical-record-sharing-boundary.md) and [TRN-SP-42](../sprints/50-sprint-42-clinical-record-sharing.md) define a **synthetic-only** clinician/tenant/member/service/data-category scoped prerequisite gate, separate from existing member self-read and research-consent tests, and a richer `/member/records` disconnected RTL view. No legal emergency exception, real identity, FHIR source, actual medical record, data use or grant is designed or enabled. Release remains blocked.

## Sprint 43 refinement — member credit lifecycle transparency (2026-10-11)

For TRN-EP-03/08/13, approved TRN-BIZ-012/013/014/015/016 requires the member to understand the difference between original 90-day waiting, retained previously accrued treatment credit on nonrenewal, no new accrual during lapse, resumed prospective accrual after reactivation, and possible reviewed old-period benefit recorded late. [TRN-TECH-052](../technical/52-synthetic-member-credit-period-review.md) and [Sprint43](../sprints/51-sprint-43-member-credit-period-review.md) reuse existing waiting/coverage reducers for a **synthetic-only** batch read-side projection and disconnected Member Web explanations, NOT new ledger, financial accrual policy, price calculation, allocation, entitlement or payment. Actual monetary obligations and source verification remain gated.
