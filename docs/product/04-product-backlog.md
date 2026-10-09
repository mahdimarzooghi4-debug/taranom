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
