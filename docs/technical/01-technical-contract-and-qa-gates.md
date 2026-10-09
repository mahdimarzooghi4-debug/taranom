# TRN-TECH-002 — قراردادهای طراحی و گیت‌های QA

- وضعیت: **DRAFT**؛ مکمل TRN-TECH-001 و چرخه مادر
- اصل: هر گام حداقل یک قرارداد، سناریوی خطا، ردپا و آزمون لازم دارد.

## قراردادهای اولویت‌دار برای اجرا

| Contract Gate | باید در مرحله Technical مشخص شود | قبل از فعال‌شدن چه چیزی ممنوع است؟ |
|---|---|---|
| T-01 Identity/Permissions | مرجع معتبر هویت، actor/tenant isolation، نقش و سیاست deny-by-default | دریافت ادعای هویت واقعی |
| T-02 Membership | آغاز رسمی، انتظار ۹۰ روز، تاریخچه تمدید و انباشت واجدشرایط | ایجاد اعتبار واقعی از نامعلوم |
| T-03 Privacy/Consent | نوع رضایت مستقل، دامنه داده، انصراف، کنترل نقش و audit | اشتراک/آموزش روی داده واقعی |
| T-04 Care/Provider | نسخه مدرک صلاحیت/قرارداد، مسیر ارجاع انسانی، پذیرش خدمت | معرفی پزشک/مرکز غیرواقعی به‌عنوان فعال |
| T-05 Wallet/Finance | مصدر داده، فرمول مصوب، قابلیت ردیابی، idempotency و برگشت حسابداری | هر مانده قابل‌مصرف/انتقال وجه واقعی |
| T-06 Insurance/Billing | مرجع استحقاق، منشأ اسناد، وضعیت غیرقطعی، اختلاف و اصلاح | ادعای تأیید بیمه/فاکتور جعلی |
| T-07 Safety/AI | خطرهای بالینی، مرز غربالگری غیرتشخیصی، Dataset/Model Governance | تشخیص خودکار یا فعال‌سازی مدل |
| T-08 Release/Ops | داده تست، CI، امنیت، Stage، Backup/Recovery و Release Approval | عرضه ملی یا استقرار Production |

**با گذار به Technical فقط T-01/T-03 و Audit غیرحساس** برای Slice نخست بازبینی می‌شوند؛ سایر گیت‌ها برای حوزه مرتبط پابرجا هستند.

## قواعد Code و تست

1. **قرارداد و تست قبل از پیاده‌سازی**؛ API، payload، رویداد، schema یا آستانه عددیِ تأییدنشده نباید صرفاً برای ساختن demo اختراع شود.
2. **Cross-domain authorization و tenant isolation** با تست رد دسترسی، stale-data و تعارض بازیابی اجرا شوند. No implicit success on missing evidence.
3. هر گردش پولیِ آینده نیازمند invariant مانده غیرمنفی، transactionality، idempotency و ledger غیرقابل‌دستکاری با ممیزی مستقل است؛ اکنون پیاده‌سازی و فرمول مالی آن **BLOCKED** است.
4. هر سناریوی داده پزشکی فقط با منابع مجاز/رضایت واقعی یا داده کاملاً ساختگی آزمایش شود. سرویس اضطراری، بیمه‌گر، بانک و درمان واقعی شبیه‌سازی عملیاتی نشود.
5. **Sprint DoD:** تست واحد/قرارداد/امنیت و static analysis سبز، Code Review مستقل، شواهد عدم خروج از محدوده، و پذیرش در گیت مربوط. **این سند هیچ‌یک را پاس‌شده نمی‌نامد.**

## گیت جزئی Sprint 01 — TRN-TECH-003/004 (۲۰۲۶-۱۰-۰۹)
- T-01 و T-03 و Audit اکنون [قرارداد و ماتریس ۲۰ سناریوی آزمون پیشنهادی](02-sprint-01-internal-interface-and-security-contract.md) دارند؛ **هنوز اجرا نشده‌اند** و قراردادهای stack-specific و role/actor واقعی OPEN می‌مانند.
- [Tech Gate Review](03-technical-gate-and-stack-proposal.md) نتیجه را **PARTIAL / NOT CODE-READY** اعلام می‌کند: Stack باید تصمیم/ثبت شود؛ سپس schema/versioned interfaces، پاسخ خطا، transaction/audit atomicity، synthetic fixtures و تست security تحت CI مشخص شوند.
- هر mismatch، stale/missing evidence، درخواست sponsor به PHI/Wallet، رضایت پژوهشی ضمنی و replay باید fail-closed باشد. Synthetic harness نباید وارد Production شود. **Code Authorization، Stage و QA واقعی صادر نشده‌اند**.
