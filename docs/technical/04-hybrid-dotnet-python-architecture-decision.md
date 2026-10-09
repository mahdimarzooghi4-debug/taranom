# TRN-TECH-005 — تصمیم معماری ترکیبی C#/.NET و Python برای ترنم

- تاریخ ثبت: 2026-10-09
- **وضعیت: ACCEPTED در سطح جهت‌گیری معماری توسط مالک محصول؛ جزئیات فنی و گیت Code هنوز OPEN**
- مبنای اختیار: نظر صریح مالک محصول در پی مقایسه Python/FastAPI با C#/.NET — «من به نظرم باید ترکیبی استفاده کنیم».
- پیشینه: `TRN-BIZ-001…052`، `TRN-TECH-001…004`، `TRN-PB-001` و `TRN-SP-01`.

## تصمیم

**معماری ترنم ترکیبی باشد، نه Backend یکپارچه صرفاً Python و نه دو Backend موازی با مالکیت مشترک هر داده.**

1. **C#/.NET — هسته اصلی عملیاتی و حاکمیت سامانه**: پیشنهاد پذیرفته‌شده برای مالکیت و اجرای Identity & Access، Membership، Consent & Privacy، Provider Network، Care Coordination غیرتشخیصی، Clinical Records با مجوز قانونی، Insurance Coordination، Billing & Cost Views، Member Credit & Finance، Quality & Complaints، Audit & Operations و **فرایند تصویب/ممیزی/Promotion مدل داخلی AI** است. مسئولیت منطق و حقیقت هر حوزه نزد مالک دامنه باقی می‌ماند. این جهت‌گیری به‌معنای فعال‌بودن هیچ خدمت پزشکی، مالی یا بیمه‌ای نیست.
2. **Python — مؤلفه محاسباتی AI داخلی و پژوهش کنترل‌شده**: تنها برای پردازش داده‌های مجاز/آماده‌سازی Dataset تحت سیاست پذیرفته‌شده، آموزش، ارزیابی، اجرای مدل مصوب و تولید نتیجه غیرتشخیصیِ دارای شواهد استفاده شود. پذیرش داده آموزشی منوط به مجوز و رضایت مستقل TRN-BIZ-036 است؛ خود Python مرجع استحقاق، تصمیم پزشکی یا مالی نیست.
3. **مرز مالکیت و اختیار**: .NET مالک policy decision، consent authorization، audit، Model Registry/approval governance و تصمیم انسانی برای Production Promotion می‌ماند؛ مؤلفه Python با ورودی محدود و مجاز و artifact/result نسخه‌دار کار می‌کند، حق تغییر مستقیم Credential/Permission، پرونده بالینی، پوشش بیمه، مانده کیف پول، حسابداری یا تصمیم بالینی ندارد.
4. **ارتباط دو محیط از طریق یک Contract داخلی کنترل‌شده** انجام خواهد شد که origin، هویت سرویس/عامل مجاز، purpose، minimum dataset، version/lineage، correlation/audit، خطا و پاسخ را قابل ردیابی نگه دارد؛ **انتخاب transport (in-process / worker / internal IPC / سایر)، schema دقیق و deployment topology هنوز OPEN است**. منع API هوش مصنوعی خارجی و نبود افشای داده به سرویس ثالث بدون مجوز پابرجاست؛ داشتن دو زبان به‌معنای الزام ایجاد API عمومی یا سرویس استنتاج بیرونی نیست.
5. **در دسترس نبودن AI، عدم احراز artifact/model version، خطای کنترل دسترسی یا گم‌شدن lineage باید fail-closed باشد** و در جریان‌های وابسته، مسیر انسانیِ مصوب به جای تشخیص/تصمیم خودکار ارائه شود؛ AI بدون مدل Production معتبر وارد جریان عملیاتی نمی‌شود.
6. **مرز تراکنش حساس**: تنها owner عملیاتی اجازه تغییر قانونی/قراردادی داده خویش را دارد؛ Python خروجی پیشنهادی/شواهد تولید می‌کند نه فرمان مستقیمِ پرداخت، تخصیص اعتبار، تغییر پوشش یا مدیریت حقوق عضو. **هیچ اتصال مشترک نوشتن به جداول عملیاتی/کیف پول/پرونده و هیچ نگاشت خودکار score به تصمیم قانونی یا درمانی** مجاز نیست.
7. **AI داخلی و تحت مالکیت ترنم** باقی می‌ماند؛ انتخاب مدل یا نام/نسخه، چارچوب Training، الگوریتم و سخت‌افزار هنوز نیازمند ارزیابی جداگانه است. هیچ API خارجی AI، تشخیص/تجویز پزشکی خودکار، یادگیری مستقیم و بی‌حاکمیت روی Production یا Promotion خودکار مجاز نمی‌شود.
8. **اولین پیاده‌سازی فقط Sprint 01 غیرحساس** است: اسکلت ماژولار .NET و قرارداد Identity/Consent/Audit با داده ساختگی و آزمون امنیتی، پس از گیت Technical و Code Authorization. **Python AI در این Sprint فقط مرز و قرارداد آینده است؛ نه Training، Inference، ارتباط واقعی یا فعال‌سازی بالینی**.

## مقایسه با طرح قبلی

| تصمیم | قبل | پس از نظر مالک محصول |
|---|---|---|
| Backend عملیاتی | Python/FastAPI صرفاً Candidate | **C#/.NET به‌عنوان جهت‌گیری پذیرفته‌شده** |
| اجرای هوش مصنوعی داخلی | Python در Backend همه‌منظوره، نامشخص | **Python محدود به مؤلفه داخلی AI و محاسبات حاکمیت‌شده** |
| Frontend | React/TypeScript Candidate | **همچنان Candidate؛ نه تأییدشده** |
| Database | PostgreSQL Candidate | **همچنان Candidate؛ نه انتخاب نهایی** |
| معماری | modular monolith Candidate | هسته .NET ماژولار به‌عنوان جهت پیشنهادی؛ مکان اجرای Python، IPC و مرز استقرار **OPEN** |
| پشته اجرایی/ورژن‌ها | نامعین | .NET SDK و framework version، ORM/migration/test stack، Python runtime/AI deps، persistence، CI/container/hosting هنوز **OPEN** |
| اجرای واقعی | مسدود | **همچنان مسدود تا Technical Gate / Code Authorization** |

## گیت‌های تصمیم و شواهد پیش از Code

1. **قرارداد دقیق Sprint 01 روی .NET**: schema/transport/versioning هویت تستی، role/subject/tenant authorization، ConsentEvidence، append-only Audit و rollback atomicity با تست‌های `TRN-TECH-003`.
2. **انتخاب زیرساخت آزمون reproducible**: pin نسخه SDK و وابستگی‌ها، persistence و مهاجرت‌ها، unit/contract/security suites و CI. هیچ نسخه، vendor، IdP یا hostname بدون بررسی و ثبت ADR تعیین نشود.
3. **مرز AI آینده**: قرارداد حداقلی و قابل رد، dataset legality، عدم دسترسی مستقیم Python به owner tables، ایزولاسیون اجرا، audit و انسانی‌بودن approval؛ AI خارج از Sprint 01 است.
4. **ارزیابی فنی مستقل**: بررسی ریسک تراکنش، مرز امنیت/حریم خصوصی، isolation، نوشتن مشترک و data lineage؛ سپس فقط در صورت تأیید گیت و مجوز صریح Code، Sprint 01 آغاز شود.

## ممنوعیت‌ها و عدم‌قطعیت‌ها

این تصمیم **انتخاب نهایی نسخه/کتابخانه/ابر/DB/IdP/مدل AI، صدور مجوز پزشکی/مالی، قرارداد با بیمه‌گر، ورود به Code/Stage/QA/Release/Production یا Merge نیست**. PR Business #1 و Technical #2 باید **Draft/Open** بمانند. هدف عرضه رسمی جامع و سراسری پس از آمادگی واقعی پابرجاست.
