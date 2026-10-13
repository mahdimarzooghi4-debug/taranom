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

## Sprint 44 refinement — TRN-EP-02/11/13 internal-AI data admission (2026-10-11)

Accepted TRN-BIZ-036/037/038/039 distinguishes governed automatic *potential* training/candidate creation from any automatic data-use right or model Production release. [TRN-TECH-053](../technical/53-synthetic-ai-dataset-consent-partitions.md) / [Sprint44](../sprints/52-sprint-44-ai-dataset-source-review.md) strictly inspects **only untrusted synthetic training/evaluation source references** with existing independent consent assessor, explicit policy flags, no source overlap, no invented thresholds or model training; member-facing consent page explains four separate stages. No real dataset, health data, medical/legal approval, automatic training or Production AI is enabled.

## Sprint 45 refinement — TRN-EP-04/10/12 provider quality and incident accountability (2026-10-11)

Accepted TRN-BIZ-043/044/045 requires both paths of the hybrid network to retain independently traceable professional quality oversight and separate member complaint/medical incident legal authority. [TRN-TECH-054](../technical/54-provider-quality-incident-handoff.md) and [Sprint45](../sprints/53-sprint-45-provider-quality-incident-handoff.md) add an internal **synthetic-only** provider/service/network-scoped unverified quality evidence trail and disconnected center qualification/quality explanations. These notes do NOT adjudicate a complaint, prove qualifications, set scores, issue a real incident notice, approve/ban a provider or change a wallet. Actual national network and legal/clinical authorities remain pending.

## Sprint 46 refinement — TRN-EP-05/11/12/13 urgent non-diagnostic safety communication (2026-10-11)

TRN-BIZ-033/034 mandates immediate, non-diagnostic advice to seek emergency help in any *clinically validated* potential-urgency workflow, independent of a simultaneous referral to a licensed medical team. [TRN-TECH-055](../technical/55-urgent-safety-parallel-referral-lineage.md) and [Sprint46](../sprints/54-sprint-46-urgent-warning-referral-lineage.md) add a **synthetic-only** immutable trail separating the hypothetical warning paper note, proposed clinical-referral paper request, and independently claimed team-receipt paper note, plus a disconnected Member Web explanation. No symptom criteria, severity prediction, actual patient alert, emergency routing or receipt verification is implemented. Real clinical safety, lawful rights, available teams, emergency service integrations and official nationwide launch remain DENIED.

## Sprint 47 refinement — TRN-EP-01/03/08/13 correction-aware activation checklist (2026-10-11)

Approved TRN-BIZ-017/018/019/020/021/022/023/024 requires conditional official activation only after separately verified identity, applicant correction/re-verification when necessary, and paid membership; funding from support or employer cannot transfer member's private wallet/clinical rights. [TRN-TECH-056](../technical/56-membership-activation-evidence-preconditions.md) / [Sprint47](../sprints/55-sprint-47-membership-activation-evidence.md) compose the ALREADY implemented identity proofing and payer checks into a purely synthetic source-sensitive read-only precondition inspection, plus member-facing explanation. No real approval, payment, first activation time, entitlement, 90-day wait or wallet mutation; all business/legal/technical external integration remains blocked.

## Sprint 48 refinement — TRN-EP-07/09/10/13 insurance dispute handoff and correction authority (2026-10-11)

Approved TRN-BIZ-048/049/050 reserves final insurer coverage/correction authority to the insurer, while Taranom offers a unified member-facing dispute track and any authorized intermediary acts only within its real contract. [TRN-TECH-057](../technical/57-insurer-dispute-source-handoff.md) and [Sprint48](../sprints/56-sprint-48-insurer-dispute-handoff.md) add a **synthetic-only** insurer-lane source-pinned handoff note trail that composes existing separate support intake and unified treatment billing history. No real insurer, claims outcome, adjusted invoice, automatic credit/wallet mutation, refund or payment; all operations and national launch remain blocked.

## Sprint 49 refinement — TRN-EP-09/12/13 cost-estimate revision and prior choice isolation (2026-10-11)

TRN-BIZ-051/052 authorizes nonbinding five-source pre-treatment estimates with separately traceable unverified member choice, never clinical consent or a legally binding payment obligation. [TRN-TECH-058](../technical/58-cost-choice-continuity.md) and [Sprint49](../sprints/57-sprint-49-cost-choice-continuity.md) **reuse** existing synthetic estimate, immutable choice journal and PostgreSQL fixtures for a read-only original/new preview comparison: all five source changes, unknown/conflicted values, newer revisions and distinct preview identities NEVER silently inherit an old paper choice. Member Web clarifies the three distinct authorities; no real patient, service, insurer, price, contract, wallet, signature, payment or clinical decision. Nationwide release DENIED.

## Sprint 50 refinement — TRN-EP-07/09/10/13 estimate to source-pinned bill comparison (2026-10-11)

TRN-BIZ-050/051/052 explicitly separates nonbinding five-source pre-treatment estimates, member's claimed awareness and independent post-service legal-source billing. [TRN-TECH-059](../technical/59-estimate-bill-comparison.md) and [Sprint50](../sprints/58-sprint-50-estimate-bill-comparison.md) compose the existing synthetic preview/choice and post-service invoice reducers into a strictly **non-authorizing** read-side five-category comparison. Category difference or equality is **not** official price, overcharge, refund, medical consent or money. The preview has no treatment reference and any claimed treatment correspondence is UNVERIFIED. No real data/issuer, price, coverage, financial adjustment, automatic billing or official national launch.

## Sprint 51 refinement — TRN-EP-01/02/06/10/13 independent clinical access request transparency (2026-10-11)

TRN-BIZ-029/035/036/041/046/047 separates member-controlled clinician need-to-know sharing from sponsor rights, research/AI consent and emergency legal exceptions. [TRN-TECH-060](../technical/60-clinical-access-request-transparency.md) and [Sprint51](../sprints/59-sprint-51-clinical-access-transparency.md) compose the existing *synthetic* clinical disclosure inspection and personal self-read gate into a source-scoped paper-only clinician request / member concern / unverified independent oversight timeline. Even paper metadata cannot be projected across member/tenant or under sponsor role. No real patient record, access event, actual authenticated privacy complaint, clinical data disclosure, grant, human legal disposition or emergency exception exists. Official nationwide launch remains DENIED.

## Sprint 52 refinement — TRN-EP-03/08/09/12/13 six operational income sources (2026-10-11)

TRN-BIZ-004/005/024/025/026/027 admits six operating-income categories, keeps membership subscription as PART OF sixth direct-income category, one treatment-only wallet per member, and principle of future equal distribution to legally eligible members. It does **not** approve the monetary amount, dates, percentage mapping, realized income, eligible cohort or allocation formula. [TRN-TECH-061](../technical/61-six-revenue-origin-map.md) and [Sprint52](../sprints/60-sprint-52-six-revenue-origins.md) introduce a typed synthetic source-claim reconciliation map: detect potential same-event double attribution across streams/revisions, unrepresented input sources, missing legal/accounting/custodian review and subscription-as-seventh misclassification; never credit/recognize/distribute money or infer zero. Member wallet view explains the sources without promising profit; official national launch remains DENIED.

## Sprint 53 refinement — TRN-EP-02/05/06/10/13 safe post-service prevention continuity (2026-10-11)

TRN-BIZ-046/047 accepts nonclinical Taranom coordination and voluntary member-controlled health education/prevention while licensed clinicians own longitudinal care decisions. [TRN-TECH-062](../technical/62-post-service-prevention-continuity.md) and [Sprint53](../sprints/61-sprint-53-care-prevention-continuity.md) compose existing **synthetic-only** care notes and prevention participation prerequisites in a source-replayed, revision-pinned READ. A claimed link between a care case and preventive program is NOT a real clinical referral, member consent, approved care plan or AI research right. No automatic follow-up, contact, diagnosis, public health content or clinic integration; official national first release DENIED.

## Sprint 54 refinement — TRN-EP-01/02/06/11/12/13 public Git history safety (2026-10-11)

Following explicit owner Public visibility of Taranom to recover GitHub-hosted CI, accepted TRN-BIZ-030/035/036/040/041 and TRN-TECH-002 T-03/T-08 require preventing accidental public credentials/private files. [TRN-TECH-063](../technical/63-public-source-history-safety.md) and [Sprint54](../sprints/62-sprint-54-public-source-history-safety.md) implement an **offline fail-closed limited-pattern scan of locally fetched reachable Git history**, with 28 Python synthetic Git tests and an inherited 12-job workflow that checks old deleted blobs too. A clean limited scan is NOT an independent guarantee that the full publication is safe or that actual member medical/legal data, prior clones, binary data and secret scanning alerts have been cleared. Owner-led actual source rights, security/privacy investigation and national release readiness remain OPEN.


## Sprint 55 alternate security refinement — Commit-message history credential guard (2026-10-11)

[TRN-TECH-064](../technical/64-public-commit-message-credential-safety.md) and [Sprint55 security branch](../sprints/63-sprint-55-commit-message-credential-safety.md) extend the earlier limited public Git history checker to inspect reachable commit messages for high-confidence credential leakage, with redacted rule-only findings and fail-closed malformed framing. This complements (rather than replaces) private evidence custody and does not establish publication/privacy or national release authorization.

## Sprint 55 refinement — private custody of authentic external evidence (2026-10-11)

Across TRN-EP-01…13, the three required markets and national coverage, the deny-only 17-dimension inventory still reflects unverified external facts. [TRN-TECH-064](../technical/64-private-external-evidence-custody.md) and [Sprint55](../sprints/63-sprint-55-private-external-evidence-custody.md) add a genuine *byte-integrity and private custody check contract*, NOT another synthetic clinical read model. An operator may check real artifacts only on their own private machine, never in GitHub/CI; SHA-256 binds actual files and current source blocker inventory. Public CI covers tests and an empty template only. Nothing becomes VERIFIED, approved or release-ready by passing integrity. Clinical, legal, financial and infrastructure review/authorities remain external. First comprehensive nationwide three-market launch continues DENIED_BY_DEFAULT.


## Sprint 56 refinement — private evidence path-race hardening (2026-10-11)

TRN-TECH-065 and Sprint56 build on the real external-file custody mechanism from Sprint55, addressing a documented file-path time-of-check/time-of-use risk: safely anchored POSIX descriptors, no-follow path traversal and inode/metadata/path rechecks protect private evidence intake from accidental or adversarial pathname substitution. A private intake report may no longer be written inside the Public repository. These controls make the future *local operator verification mechanism* safer, not the authenticity, legal admissibility, signer identity, medical/financial authority or readiness of any submitted evidence. All 17 national blockers and the full three-market launch remain DENIED_BY_DEFAULT.


## Sprint 57 refinement — protect public GitHub CI workflow source (2026-10-11)

For TRN-EP-01/02/11/12, after public Git history audit and private external-evidence custody hardening, the next real technical risk is mutable GitHub Actions workflows in a Public repository. [TRN-TECH-066](../technical/66-public-ci-workflow-trust-guard.md) / [Sprint57](../sprints/65-sprint-57-public-ci-workflow-trust-guard.md) enforce a narrow, fail-closed policy across actual workflow source: SHA-pinned approved actions, read-only tokens, safe checkout and triggers, and no unreviewed deployment/secret contexts. The new guard runs with inherited backend/Postgres/built Chromium and national DENY-ONLY tests; passing static checks cannot authenticate external evidence or authorize launch. Real partner, IDP, legal, clinical, finance and Production integrations remain blocked until external proof and separate approval.


## Sprint 58 refinement — opt-in, real TLS transport diagnostic for identity vendors (2026-10-11)

TRN-BIZ-019 and [TRN-TECH-012](../technical/12-member-session-and-governed-read-boundary-candidate.md) distinguish licensed specialist identity proofing from authenticated member login, with neither actual vendor selected nor real IdP/KYC/contract/data rights established. [TRN-TECH-067](../technical/67-external-identity-tls-preflight.md) and [Sprint58](../sprints/66-sprint-58-external-identity-tls-preflight.md) add a bounded, operator-run outbound TLS test for a **future human-selected public hostname**, with offline CI/no hostname = TARGET_NOT_CONFIGURED, no real network in test workflows, no URLs/credentials/KYC/member data, redacted report and national DENIED status. A later transport PASS establishes **only certificate-verified connectivity**, not a verified provider, OIDC authentication, lawful proofing, entitlement, clinical data right or release. Real partner selection and independent identity/security/legal contracts are still blockers.


## Sprint 59 refinement — private operator target and report ingress for identity TLS preflight (2026-10-11)

[TRN-TECH-068](../technical/68-private-identity-operator-ingress.md) and [Sprint59](../sprints/67-sprint-59-private-identity-operator-ingress.md) close the concrete risk that the first real vendor's hostname may leak via shell process arguments or a Public Git report path. A future separately authorized human operator uses a private owner-restricted local hostname file and obtains a redacted private report that cannot overwrite existing files. No live hostname or provider is selected, CI remains offline and default-blocked, no OIDC/KYC/member activation service is invented, and official national first release remains DENIED_BY_DEFAULT.


## Sprint 60 refinement — 13 Epic evidence-based progress tracking (2026-10-11)

Product owner authorized measuring progress against the actual 13 mandatory Epics, demonstrable deliverable capabilities and acceptance tests instead of a heuristic ~20% or counting Sprints. [TRN-TECH-069](../technical/69-epic-capability-progress-measurement.md) and [Sprint60](../sprints/68-sprint-60-epic-progress-baseline.md) propose an explicit **DRAFT** 39-slot decomposition (3 candidate acceptance questions × 13 Epics, not a final weighted scope) and attach real source/test *file references* where possible, carefully marked synthetic/disconnected/offline-only. The checker rejects invented acceptance, self-certified percentages, denominator shrinkage and deleted market/national obligations. Technical reference coverage is not product completion. The true whole-product implementation percentage remains **UNBASELINED/null** until independent owner approval of final scoped acceptance units and actual verified human-accepted real capabilities; all 17 national release obligations stay BLOCKED_UNVERIFIED and first official nationwide three-market launch remains DENIED_BY_DEFAULT.


## Sprint 61 refinement — integrated functional offline progress control console (2026-10-11)

Owner requested large, substantive packages. [TRN-TECH-070](../technical/70-offline-epic-progress-console.md) and [Sprint61](../sprints/69-sprint-61-offline-epic-progress-console.md) build a locally usable Persian RTL HTML 13-Epic governance console with search, filter, evidence class, direct source-code/test refs, status per candidate and 17 official blockers, plus offline 43-row CSV and deterministic input digest; real Chromium browser tests and comprehensive Python negative tests join full inherited CI. The dashboard reports technical *file-link coverage*, not approved capability Done or fabricated product %, and has no IdP, PHI, money, partner, medical or external AI interaction. Baseline remains DRAFT_UNAPPROVED, percentages UNBASELINED and national comprehensive three-market release DENIED_BY_DEFAULT.


## Sprint 62 refinement — actual local .NET ↔ React HTTP vertical slice (2026-10-11)

[TRN-TECH-071](../technical/71-real-local-ops-http-react-vertical-slice.md) / [Sprint62](../sprints/70-sprint-62-local-ops-http-react-vertical-slice.md) establish the first narrowly allowed true process-to-browser technical HTTP interaction: localhost-only .NET operational-status server and separate built React client, same-origin GET of *public, unchanged DENY-only* national blocker metadata, 503 on invalid source, no POST/credentials/cookies/private data/external integrations. Thirteenth job builds backend and frontend and verifies actual HTTP and Chromium while retaining inherited 12 jobs. A working localhost connection is not a verified member identity, clinical/insurance/finance service, infrastructure admission, independent product acceptance or Production readiness. All 17 blockers persist and official full three-market national launch remains DENIED_BY_DEFAULT.


## Sprint 63 refinement — functional .NET domain rule via real anonymous loopback React (2026-10-11)

[TRN-TECH-072](../technical/72-hypothetical-waiting-time-http-react.md) / [Sprint63](../sprints/71-sprint-63-hypothetical-waiting-period-vertical-slice.md) move an already-approved, tested Membership rule from an isolated .NET domain library to a true **local-only HTTP+React, hypothetical educational** user experience: exact 90 full 24-hour periods from a **hypothetical** official activation timestamp, never '3 months', without using account creation, login, PSP, identity proofing or wallet data. It has strict UTC, size/schema and extra-property denial; never grants actual activation, eligibility or financial rights, and never persists personal input. Genuine Python HTTP tests + Chromium browser QA join the inherited full 13-job Push/PR CI. Member app's real session and all sensitive integrations remain blocked independently; national three-market first launch still DENIED_BY_DEFAULT.


## Sprint 64 — Real PostgreSQL-backed center workflow through .NET and React (test-only, 2026-10-11)

[TRN-TECH-073](../technical/73-ephemeral-center-postgres-react-workflow.md) / [Sprint64](../sprints/72-sprint-64-center-workflow-postgres-react.md) implement an end-to-end **four-operation center domain workflow** (department, unverified doctor affiliation, assignment, ending affiliation) through an actual loopback ASP.NET host and actual ephemeral PostgreSQL-backed existing append-only center journal with a built Persian RTL React browser interface. Concurrency/stale revisions, idempotency, audit, domain event/projection integrity and denial of real IDs are exercised by genuine HTTP and Chromium regression in a 14th CI job, without replacing the existing owner/domain journal. A positive test-only journal transition is NOT professional license, manager authentication, clinic operation, real provider contract or production authorization. Only \`test:\` cases, explicit ephemeral DB name and startup sentinel are accepted. No real center/session/clinical integration is enabled and national comprehensive release remains DENIED_BY_DEFAULT.


## Sprint 65 — Read-only event history and as-of-version replay in the existing actual PostgreSQL center workflow (2026-10-11)

[TRN-TECH-074](../technical/74-center-history-and-point-in-time-read.md) / [Sprint65](../sprints/73-sprint-65-center-history-vertical-slice.md): extend the working test-only Sprint64 React→.NET→PostgreSQL center flow with strict paginated accepted-event timeline and reproducible **historical snapshot at any past accepted version** using exactly the existing replay-verified journal and canonical reducer. The UI really fetches and navigates pages, presents original test-only statuses, compares past to latest, and clears stale state after writes or outages. Python real-HTTP and five additional Chromium cases run within the inherited 14-job Push/PR CI. This is **genuine integrated test-only functionality** with no new authorization, real staff, professional credential, clinical record, identity or financial source. Nationwide comprehensive first official launch remains DENIED_BY_DEFAULT with 17/17 dimensions blocked.


## Sprint 66 — Unverified member support intake and follow-up, actual tech-stack connection (2026-10-11)

[TRN-TECH-075](../technical/75-synthetic-support-full-stack-intake.md) and [Sprint66](../sprints/74-sprint-66-member-support-full-stack.md) connect the already existing canonical synthetic member support intake/follow-up reducer to a separate immutable PostgreSQL support event/audit path and real localhost .NET/React workflow. Only fake test references and unverified counts are displayed. Original and follow-up reference lineage, idempotent replay, stale/mismatch denial, and audit integrity are exercised by actual HTTP/Python and Chromium alongside all inherited 14 jobs. No legally filed complaint, authenticated real member, external insurer/provider, clinical rights, actual settlement or Production access. The comprehensive national first release remains DENIED_BY_DEFAULT.


## Sprint 67 — Separate technical member and center browser environments (2026-10-11)

[TRN-TECH-076](../technical/76-separate-member-center-technical-surfaces.md) / [Sprint67](../sprints/75-sprint-67-separate-member-center-demos.md): resolve a concrete Sprint66 architecture gap where synthetic member support was rendered in the center UI. The separate member-support React bundle is now served at a distinct local-only URL and retains actual PostgreSQL-backed support request/follow-up capability; center React shows only center department/doctor operations and accepted event history. Cross-surface real browser tests assert absent other-role controls, retained input/HTTP failures and no remote requests. **UI separation is not server authorization** and neither app is a live authenticated Product portal. No trusted member identity, tenant permission or real consent provider exists, so both remain strictly localhost \`test:\` demonstrations. Nationwide release still DENIED_BY_DEFAULT and all 17 blockers unverified.


## Sprint 68 — Secure test-only HTTP Host authority and verified concurrent journal correctness (2026-10-11)

[TRN-TECH-077](../technical/77-loopback-http-host-and-race-hardening.md) / [Sprint68](../sprints/76-sprint-68-loopback-integrity-and-concurrency.md) close a concrete DNS-rebinding/Host trust risk and wrong-frontend wildcard fallback in the **existing** test-only Kestrel app. Deny spoofed Host or forwarded authority prior to center/member static and JSON handlers; ensure unknown purported real routes 404, not silent center UI; test real concurrently competing and identical commands against canonical PostgreSQL center and support journals. Python true HTTP and 3 additional Chromium/API regressions run with existing two independently built React bundles in the inherited 14-job matrix. This does **not** replace actual IdP, lawful clinical/financial contracts, RBAC or tenant authorization. All live access remains DENIED and first three-market nationwide release DENIED_BY_DEFAULT.


## Sprint 69 — Harden remaining real localhost ASP.NET operational service (2026-10-12)

[TRN-TECH-078](../technical/78-ops-http-authority-and-denial.md) / [Sprint69](../sprints/77-sprint-69-ops-http-authority-denial.md): extend the previously completed Sprint68 DNS-rebinding/Host integrity defense and unknown-route fail-close from the center demo to the **separate, previously unguarded** real .NET operations-status/90-full-day teaching server. Explicit exact localhost Host/port 4318, deny untrusted forwarded authority, no SPA wildcard pretending \`/login\` or unbuilt live finance/clinical/member endpoints exist, and preserve existing correct 17/17 DENIED blockers/503 source failure and hypothetical-only waiting logic. Real HTTP attack tests plus four Chromium cases join the inherited 14-job Push/PR CI. This does **not** authorize real identity, real member/tenant data, signed browser session, Stage or national release; \`TRN-TECH-012\` still NOT CODE-AUTHORIZED and all 17 launch dimensions remain BLOCKED_UNVERIFIED.

## Sprint 70 — Real private external-evidence batch preparation and 17-dimension gap visibility (2026-10-12)

[TRN-TECH-079](../technical/79-private-evidence-batch-workbench.md) / [Sprint70](../sprints/78-sprint-70-private-evidence-batch-workbench.md) add trusted-local operator tooling instead of another fabricated clinical or financial read model. The tool accepts owner-only private batch instructions outside Public Git, securely hashes actual external files through the existing no-follow descriptor-based custody checker, creates a new-only SHA-pinned private intake manifest, revalidates every byte, and gives a private redacted 17-dimension / three-market list of blocker categories with and without **unreviewed submitted files**. Never creates or infers signer/legal/clinical/financial validity, IdP/session, individual rights, provider selection, Release Approval, Production, or readiness from file integrity. All 17 official blockers stay BLOCKED_UNVERIFIED and first comprehensive national three-market launch DENIED_BY_DEFAULT. Independent human review remains required.

## Sprint 71 — One canonical secure 14-gate regression matrix (2026-10-12)

[TRN-TECH-080](../technical/80-shared-secure-full-ci.md) / [Sprint71](../sprints/79-sprint-71-shared-secure-full-ci.md) replace the proliferating full 705-line per-sprint GitHub Actions workflow copies for future work with a **same-repository, read-only, no-input, no-secret reusable full matrix** and a tiny Sprint-specific dispatcher. The actual 14 checks are preserved: .NET domain, PostgreSQL integrated workflows, real React/Chromium, public Git history trust, secret/AI exclusions, external private evidence controls and 17 national DENIED_BY_DEFAULT dimensions. Tighten the public workflow source guard so only this named local workflow_call is allowed; malicious/remote/dynamic/conditional/skipped calls and missing required job IDs are rejected. This is tangible delivery/supply-chain reliability work, **not** identity, medical/financial/insurer integration or Production admission. The 17 national blockers remain unverified and product completion remains UNBASELINED.

## Sprint 72 — Three real market information routes, no invented onboarding (2026-10-12)

[TRN-TECH-081](../technical/81-three-market-public-entry.md) / [Sprint72](../sprints/80-sprint-72-three-market-entry.md) connect actual localhost .NET public 17-blocker source and compiled React into three **independent human-readable Persian entry routes** for the accepted individual/household, support-institution and corporate channels. Paths, live source validation, disabled-action boundaries, sponsor non-access to member health and individually owned wallet are tested with genuine HTTP/Chromium; arbitrary login/subscription routes fail closed. A third-party payer is never assumed valid without its own real contract. This does not invent a real login, vendor, individual, financial/medical data or Prod/public launch. All 17 launch dimensions remain BLOCKED_UNVERIFIED and nationwide three-market release DENIED_BY_DEFAULT.

## Sprint 73 — Private evidence change reconciliation, without changing release authority (2026-10-12)

[TRN-TECH-082](../technical/82-private-evidence-delta-reconciliation.md) / [Sprint73](../sprints/81-sprint-73-private-evidence-delta-reconciliation.md) supply a real-world trusted-local comparison tool for actual submitted external evidence across two private, separately hashed snapshots. Every record's actual private bytes must be reverified against the current approved-denial source before unreviewed additions, withdrawals, changes and byte-identical retention are reported for all 13 Epics, three markets and national coverage. The anonymized report never contains partner names, filenames, hashes or custodian references and never acts as license/legal/clinical/financial/release approval. The full shared 14-job CI with PostgreSQL, Chromium, public-source history, external-AI ban and national release denial remains mandatory.

## Sprint 74 — Actual operator-selected OIDC metadata preflight, never authentication (2026-10-12)

[TRN-TECH-083](../technical/83-private-oidc-discovery-preflight.md) / [Sprint74](../sprints/82-sprint-74-private-oidc-discovery-preflight.md) advance the **real external identity integration prerequisite** beyond TLS-only: *after independent selection and authorization*, an operator may inspect one real root-issuer OIDC Discovery response through numeric-IP-pinned trusted TLS and a single fixed anonymous GET. Strict same-host issuer/authorization/token/JWKS URI syntax and advertised authorization-code/PKCE-S256/signing capabilities are verified, but the report never includes a private host and cannot activate login, prove token signature, IdP contract, identity/KYC, clinical rights or membership. GitHub CI runs only mocked/offline/no-target mode, no real provider or credentials. Full 14-job matrix remains mandatory and all 17 nationwide blockers remain DENIED.

## Sprint 75 — Real PostgreSQL support event history and as-of case reads (2026-10-12)

[TRN-TECH-084](../technical/84-support-case-history-timeline.md) / [Sprint75](../sprints/83-sprint-75-support-case-history-timeline.md) connect the existing *synthetic-only* member complaint/insurer-dispute journal to chronological, cursor-paged accepted events and point-in-time case reconstruction through the real existing ASP.NET/PostgreSQL→member-support React workflow. All accepted history is read from the original digest-verified, append-only PostgreSQL journal and the historic read replays the existing canonical support reducer. This improves end-to-end source explainability with exact earlier versions and browser failure/stale-state tests, **not** genuine insurance correspondence, member identity, legal complaint or clinical resolution. The complete shared 14-job full Push+PR regression is mandatory. All 17 national launch blockers remain BLOCKED_UNVERIFIED, national full first official release DENIED_BY_DEFAULT and independent Code Review and Stage/QA/Release approvals remain open.

## Sprint 76 — Opt-in real FHIR metadata technical discovery (2026-10-12)

[TRN-TECH-085](../technical/85-private-fhir-capability-preflight.md) / [Sprint76](../sprints/84-sprint-76-private-fhir-capability-preflight.md) give an authorized operator a strictly bounded, read-only **real FHIR server metadata** inspection against a privately selected and legally approved potential healthcare integration host. Under a no-token single GET /metadata, the tool reports only safely typed *advertised* HL7 resource/interaction names with no patient data or fictitious partner. Real hostname and source response never enter Public Git; default CI has no network, all hostile transport/data are mocked. Metadata never approves interoperability, lawful rights, consent, insurer/clinical integration or national operational coverage. All 17 launch requirements remain BLOCKED_UNVERIFIED; official comprehensive nationwide three-market first launch remains DENIED_BY_DEFAULT.

## Sprint 77 — Prove actual ephemeral PostgreSQL backup restoration (2026-10-12)

[TRN-TECH-086](../technical/86-real-ephemeral-postgres-recovery.md) / [Sprint77](../sprints/85-sprint-77-ephemeral-postgres-recovery.md) close a real operational safety gap in TRN-EP-12. The same disposable PostgreSQL service that holds fully tested .NET center and synthetic support Journals now executes a **genuine custom-format pg_dump + pg_restore to a second, fresh ephemeral database**, compares all allowed table rows and relational schema, checks audit/event parity and verifies append-only trigger enforcement *after restore*. Backup bytes are private and ephemeral and are NEVER uploaded. This is source-backed recovery evidence in the sandbox, not real Production DR proof, RPO/RTO target, national release or approval. Exact-head Push+PR 14/14 full regressions and independent human Code Review gates remain.

## Sprint 78 — Real same-origin product navigation of all member, center and market screens (2026-10-12)

[TRN-TECH-087](../technical/87-unified-local-product-surfaces.md) / [Sprint78](../sprints/86-sprint-78-unified-local-product-surfaces.md) integrate the already separately built Persian member and center Web Apps with the genuine existing localhost-only ASP.NET/.NET market orientation service. All ten member pages and five center pages now have independently compiled assets, explicit same-origin GET routing and browser deep-link reload, with market-to-member/center navigation. Every mount requires valid 17-dimension canonical DENIED release inventory and forbids fabricated auth, private records or clinical/financial authority; unknown pages 404, wrong-method 405, spoofed Host 403. Actual Python HTTP and Chromium tests plus the full inherited 14-job .NET, PostgreSQL recovery and security CI are mandatory. This is a **cohesive functioning UI preview**, not authenticated health/finance services or real public launch.

## Sprint 79 — real opt-in CI-only cross-portal QA into PostgreSQL (2026-10-12)

[TRN-TECH-088](../technical/88-cross-portal-postgres-qa-journeys.md) / [Sprint79](../sprints/87-sprint-79-cross-portal-postgres-qa.md) extend the actual functioning 4318 Member and Center preview and 4320 **isolated** support/center event journal to a fully automated real browser QA walkthrough. In one PostgreSQL job, only an explicit CI compiled opt-in shows clearly labeled hyperlinks between the two hosts. QA browser follows, writes first-party synthetic-only event through true .NET/PostgreSQL, reloads and verifies exact accepted version, then returns to denied-only Member/Center overview. No BFF, real member identity, clinical, insurer or payment authority is inferred or exposed. Every normal browser build still has no link to a running test workflow. All 14 exact-head Push and PR jobs incl the independent actual pg_dump/restore remain required; national release remains DENIED_BY_DEFAULT.

## Sprint 80 — Canonical .NET national blockers in Member and Center

TRN-TECH-089: all 15 existing Member and Center pages on loopback ASP.NET 4318 now display the existing canonical 17 blocked dimensions and evidence references; invalid, READY, duplicate or unavailable source fails closed and removes stale details. Standalone browsers never fetch source. Official three-market national first release DENIED_BY_DEFAULT. Push+PR full 14-job CI and human review gates retained.

## Sprint 81 — Interactive payer choice and individual wallet ownership for all markets (2026-10-12)

[TRN-TECH-090](../technical/90-three-market-payer-individual-ownership.md) / [Sprint81](../sprints/89-sprint-81-three-market-payer-guide.md): the same existing source-validated three-market React workspace now offers a real interactive comparison of self-paid vs future contracted third-party subscription under TRN-BIZ-029. Person always owns healthcare wallet and membership, and sponsor gets no implicit clinical access. No finance/product activation; missing/forged source removes guidance, not pretending approval. Full 14 Push/PR jobs and human review remain gates.

## Sprint 82 — No duplicate support case on lost PostgreSQL acknowledgement (2026-10-12)

[TRN-TECH-091](../technical/91-support-uncertain-write-idempotent-recovery.md) / [Sprint82](../sprints/90-sprint-82-support-uncertain-write-retry.md): the existing genuine test-only member support PostgreSQL workflow now preserves an immutable command envelope and original requestId after an ambiguous HTTP outcome, blocks further writes and permits only explicit exact replay. Real Chromium/PostgreSQL tests drop an accepted response **after commit** and prove retry yields Replayed with no duplicate journal event; forged success remains untrusted. No legal complaint/real member service. 17 national blockers and production denial unchanged; full 14 Push/PR CI mandatory.

## Sprint 83 — Explicit manual post-reload test support command recovery (2026-10-12)

[TRN-TECH-092](../technical/92-manual-support-retry-receipt.md) / [Sprint83](../sprints/91-sprint-83-support-manual-recovery.md) provide a portable plain-text, **synthetic-only** command snapshot when support POST acknowledgement is uncertain. After browser restart manual paste validates canonical source and exact original request ID/command but never POSTs or authorizes anything. A separate manual replay relies on real PostgreSQL request idempotency. Real Chromium tests prove receipt→reload→import without write→replay existing accepted event once for both original submission and follow-up; malformed receipt and real refs denied. No cookies, automatic storage or real patient workflow. The 14-job Push/PR gate and 17-national DENIED remain unchanged.


## Additional Sprint 81 payer scenario — test-only contractual responsibility matrix

[TRN-TECH-090](../technical/90-three-market-payer-boundary-preview.md) and [Sprint 81 parallel branch](../sprints/89-sprint-81-payer-boundary-preview.md) include a separate same-origin .NET/React policy what-if across individual, support and corporate member sponsorship. A sponsor requires a real future contract; member ownership and clinical privacy never transfer. No real payment, insurer coverage or contract is implied. This parallel deliverable was integrated alongside the Sprint 81 informational market guide without replacing it.

## Additional Sprint 84 center guided workflow — actual synthetic PostgreSQL journal

[TRN-TECH-093 (center)](../technical/93-guided-center-postgres-workflow.md) and [parallel Sprint 84](../sprints/92-sprint-84-guided-center-workflow.md) give the tester a four-step manual journal-based sequence for a test department and unverified professional affiliation, assignment and end. Every transition is human-triggered, re-reads PostgreSQL and never asserts a real licensed provider, clinician or authorization; no automatic progression, clinical approval or official release.


## Sprint 87 — Mainline exact-SHA full regression admission (2026-10-12)

The owner-authorized through-Sprint-86 Merge (#97, `0d8cad694292c7810c9f292598fcbb14a450f695`) has **no GitHub Actions run on `main`** because prior Sprint dispatchers only target Sprint branches. [TRN-TECH-096](../technical/TRN-TECH-096-mainline-continuous-regression.md) / [Sprint87](../sprints/93-sprint-87-mainline-ci-integrity.md) bind exact `main` Push and incoming PR events to the canonical read-only 14-job reusable workflow. The public CI trust checker rejects removal of the mandatory mainline dispatcher or tampering with its event/branch/job boundary. This is a **continuous integration quality gate**, not Production deploy, signed independent review, or automatic release authorization. All 17 national gates remain BLOCKED_UNVERIFIED and comprehensive three-market official launch DENIED_BY_DEFAULT.


## Sprint 88 — Independently reattested cost-to-insurance-support test handoff (2026-10-12)

[TRN-TECH-097](../technical/TRN-TECH-097-synthetic-insurance-cost-support-handoff.md) and [Sprint88](../sprints/94-sprint-88-insurance-cost-support-handoff.md) join two pre-existing immutable synthetic-only PostgreSQL journals via explicit member/cost-reference verification. The user manually records an unverified coverage dispute through the original support case reducer, then independently reads both journals through a new GET-only correspondence check. Exact UUID retries after a lost acknowledgement cannot duplicate a support event. No insurer adjudication, real member identity, pricing, payment or legal case is implied; the national first launch remains DENIED_BY_DEFAULT with 17 unverified gates.


## Sprint 89 — Frozen-revision audit timeline for unverified cost choice (2026-10-13)

[TRN-TECH-098](../technical/TRN-TECH-098-synthetic-cost-choice-point-in-time-history.md) and [Sprint89](../sprints/95-sprint-89-synthetic-cost-history.md) reuse the canonical append-only PostgreSQL cost choice and audit journal for bounded page-by-page event history and as-of historical versions. An expectedCurrentRevision pins the event cursor to prevent mixed-version pages when a concurrent synthetic writer appends. No legal member choice, payment or insurer adjustment is inferred. Comprehensive first national three-market launch remains DENIED_BY_DEFAULT; all 17 real readiness gates BLOCKED_UNVERIFIED.
