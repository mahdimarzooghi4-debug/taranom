import { useEffect, useRef, useState, type MouseEvent } from 'react';
import {ReleaseSourcePanel} from './release-source-panel';
import {SyntheticCostWorkbench} from './synthetic-cost-workbench';

type PageKey =
  | 'overview' | 'membership' | 'credit' | 'consents' | 'care'
  | 'records' | 'costs' | 'billing' | 'support' | 'prevention';

type MemberPage = {
  key: PageKey;
  path: string;
  label: string;
  subtitle: string;
  owner: string;
  detail: string;
  symbol: string;
};

export const memberPages: readonly MemberPage[] = [
  { key: 'overview', path: '/member', label: 'نمای کلی', subtitle: 'فضای شخصی عضو', owner: 'هویت و عضویت',
    detail: 'برای مشاهده اطلاعات شخصی، ابتدا باید اتصال امن و احراز هویت واقعی برقرار شود.', symbol: '◈' },
  { key: 'membership', path: '/member/membership', label: 'عضویت من', subtitle: 'وضعیت و سوابق عضویت', owner: 'سامانه عضویت',
    detail: 'وضعیت عضویت و دوره انتظار فقط از سرویس معتبر عضویت قابل دریافت است.', symbol: '◇' },
  { key: 'credit', path: '/member/credit', label: 'اعتبار درمان', subtitle: 'کیف پول غیرنقدی عضو', owner: 'هسته اعتبار و مالی',
    detail: 'موجودی و اعتبار درمان تنها پس از اتصال به منبع مالی معتبر نشان داده می‌شوند. هیچ عدد پیش‌فرضی وجود ندارد.', symbol: '◎' },
  { key: 'consents', path: '/member/consents', label: 'رضایت‌ها و حریم خصوصی', subtitle: 'مجوزهای مستقل داده', owner: 'حریم خصوصی و رضایت',
    detail: 'مجوزهای درمان، اشتراک داده و پژوهش هوش مصنوعی مستقل‌اند و بدون شواهد معتبر فعال نمی‌شوند.', symbol: '⊙' },
  { key: 'care', path: '/member/care', label: 'هماهنگی درمان', subtitle: 'پیگیری خدمات سلامت', owner: 'هماهنگی درمان',
    detail: 'ارجاع و خدمات واقعی به اتصال معتبر شبکه ارائه‌دهندگان و تصمیم متخصص مجاز نیاز دارد.', symbol: '✳' },
  { key: 'prevention', path: '/member/prevention', label: 'آموزش و پیشگیری', subtitle: 'مشارکت اختیاری و مراقبت مستمر', owner: 'آموزش سلامت و تیم درمانی واجدصلاحیت',
    detail: 'مشارکت واقعی، محتوای دارای اعتبار علمی و مراقبت مستمر به منابع و مجوزهای مستقل نیاز دارند.', symbol: '✦' },
  { key: 'records', path: '/member/records', label: 'سوابق سلامت', subtitle: 'دسترسی‌های مجاز بالینی', owner: 'سوابق سلامت',
    detail: 'هیچ اطلاعات بالینی تا زمان احراز هویت و دریافت مجوز قانونی نمایش داده نمی‌شود.', symbol: '▤' },
  { key: 'costs', path: '/member/costs', label: 'برآورد هزینه', subtitle: 'آگاهی از هزینه پیش از خدمت', owner: 'هزینه و هماهنگی بیمه',
    detail: 'برآوردهای احتمالی باید دارای منبع و عدم‌قطعیت روشن باشند؛ برآورد، صورتحساب قطعی یا تعهد مالی نیست.', symbol: '≋' },
  { key: 'billing', path: '/member/billing', label: 'صورتحساب‌ها', subtitle: 'اسناد و وضعیت پرداخت', owner: 'صورتحساب و صادرکننده قانونی',
    detail: 'صورتحساب معتبر تنها از صادرکننده قانونی و سرویس متصل دریافت می‌شود.', symbol: '▧' },
  { key: 'support', path: '/member/support', label: 'پشتیبانی و شکایات', subtitle: 'پیگیری درخواست‌ها', owner: 'کیفیت و شکایات',
    detail: 'پیگیری واقعی درخواست و اختلاف بیمه‌ای نیازمند اتصال به مرجع مسئول است.', symbol: '☷' },
] as const;

// No money, quote, insurer response or credit balance is present in this
// disconnected page. All five positions deliberately remain unknown.
const costShares = [
  { key: 'service', label: 'هزینه احتمالی خدمت', owner: 'ارائه‌دهنده واجدصلاحیت و دارای قرارداد معتبر' },
  { key: 'primary', label: 'سهم احتمالی بیمه پایه', owner: 'مرجع قانونی بیمه پایه' },
  { key: 'supplemental', label: 'سهم احتمالی بیمه تکمیلی', owner: 'مرجع قانونی بیمه تکمیلی' },
  { key: 'credit', label: 'سهم احتمالی اعتبار درمان', owner: 'متولی دارای مجوز کیف پول درمانی' },
  { key: 'member', label: 'سهم احتمالی عضو', owner: 'اسناد معتبر نهایی مراجع مربوط' },
] as const;

// TRN-BIZ-051/052: explanatory, not an actual cost estimate, consent
// signature, binding choice, credit authorisation or active member session.
const costChoiceContinuityLanes = [
  {
    key: 'source',
    label: 'نسخه برآورد و منابع مستقل پنج سهم',
    detail: 'هر هزینه و سهم احتمالی بیمه، اعتبار و عضو باید به نسخه و منشأ مجاز همان برآورد وابسته باشد.'
  },
  {
    key: 'choice',
    label: 'آگاهی و انتخاب اعلام‌شده عضو',
    detail: 'ثبت احتمالی آگاهی یا درخواست توضیح، فقط درباره همان نسخه نشان‌داده‌شده است؛ انتخاب یا تأیید واقعی ثبت نشده است.'
  },
  {
    key: 'change',
    label: 'بازبینی مستقل پس از تغییر یا اختلاف',
    detail: 'تغییر نسخه، منبع، سهم، اطلاعات نامعلوم یا مورد اختلاف، انتخاب نسخه قدیمی را خودکار به نسخه جدید منتقل نمی‌کند.'
  },
  {
    key: 'authority',
    label: 'رضایت درمانی و تعهد مالی رسمی جداگانه',
    detail: 'رضایت بالینی با مرجع درمانی مجاز و تعهد هزینه و پرداخت با سند و مرجع قانونی خود معتبر می‌شوند؛ برآورد به‌تنهایی مجوز هیچ‌کدام نیست.'
  }
] as const;

// Post-service source categories are independent from the pre-service estimate.
// This disconnected shell has NO invoices, issuers, insured shares or credit.
const billingSources = [
  { key: 'service', label: 'هزینه خدمت در سند درمان',
    authority: 'صادرکننده قانونی سند درمان در مرکز یا ارائه‌دهنده مجاز' },
  { key: 'primary', label: 'سهم بیمه پایه در صورتحساب',
    authority: 'بیمه‌گر پایه یا مرجع صاحب‌اختیار' },
  { key: 'supplemental', label: 'سهم بیمه تکمیلی در صورتحساب',
    authority: 'بیمه‌گر تکمیلی یا مرجع صاحب‌اختیار' },
  { key: 'credit', label: 'سهم کیف پول درمانی در صورتحساب',
    authority: 'متولی قانونی و دارای مجوز اعتبار درمانی' },
  { key: 'remainder', label: 'سهم نهایی عضو در صورتحساب',
    authority: 'سند معتبر ارائه‌دهنده با لحاظ سهم‌های تأییدشده قانونی' },
] as const;

// TRN-BIZ-050/051/052: no live price, bill, insurer determination
// or authenticated cross-document service linkage. These are EXPLANATIONS.
const estimateBillComparisonStages = [
  {
    key: 'estimate',
    label: 'برآورد پیش از درمان: غیرقطعی و وابسته به پنج منشأ',
    owner: 'ترنم تنها سهم‌های احتمالی دارای منشأ را نمایش می‌دهد؛ پیش‌برآورد تضمین قیمت یا تعهد هزینه نیست.'
  },
  {
    key: 'bill',
    label: 'صورتحساب پس از خدمت: اسناد مستقل مراجع قانونی',
    owner: 'صادرکننده خدمت، بیمه‌گران و متولی اعتبار باید هر سهم را جداگانه از منبع معتبر اعلام کنند.'
  },
  {
    key: 'review',
    label: 'مقایسه اختلاف‌ها بدون حکم مالی یا پزشکی',
    owner: 'برابر یا متفاوت بودن دو ادعای مبلغ، اثبات اضافه‌دریافت، پوشش، بدهی یا استحقاق بازپرداخت نیست.'
  }
] as const;

// Two legally distinct paths share a future member-facing contact point, but
// neither a medical verdict nor an insurer decision can be issued here.
const supportPaths = [
  { key: 'care', label: 'شکایت از خدمت یا حادثه پزشکی',
    authority: 'رسیدگی تخصصی فقط با مرجع پزشکی و حقوقی صاحب‌اختیار است.',
    disclosure: 'مدارک سلامت صرفاً با مبنای قانونی و دسترسی محدود قابل بررسی‌اند.' },
  { key: 'insurance', label: 'اعتراض به پوشش یا سهم بیمه',
    authority: 'تصمیم پوشش و اصلاح سهم بیمه فقط با بیمه‌گر یا مرجع قانونی است.',
    disclosure: 'ثبت اعتراض موجب برداشت، مسدودی یا اصلاح خودکار کیف پول نمی‌شود.' },
] as const;

// TRN-BIZ-048/049/050: these are unconnected responsibilities, NOT
// official insurance appeals, insurer receipt, approved correction or payout.
const insurerDisputeHandoffStages = [
  {
    key: 'intake',
    title: 'ثبت اعتراض نزد ترنم و پیوند به نسخه صورتحساب',
    owner: 'ترنم فقط مسیر واحد دریافت و پیگیری اعتراض عضو را هماهنگ می‌کند.',
    limit: 'هیچ پرونده، نسخه رسمی بیمه یا سند مورد اختلاف در دسترس نیست.'
  },
  {
    key: 'forward',
    title: 'ارجاع از مسیر مستقیم یا واسطه بیمه‌ای مجاز',
    owner: 'بیمه‌گر یا واسطه واجد مجوز و قرارداد، در حدود اختیار قانونی مسئول دریافت درخواست است.',
    limit: 'ثبت درخواست با رسید معتبر بیمه‌گر یکی نیست؛ هیچ اتصال یا تأیید دریافتی وجود ندارد.'
  },
  {
    key: 'response',
    title: 'پاسخ قانونی بیمه‌گر و اصلاح مستقل سند',
    owner: 'فقط بیمه‌گر یا مرجع قانونی می‌تواند سهم پوشش را تصمیم‌گیری یا اصلاح کند.',
    limit: 'پاسخ ادعایی تغییر وجه، صورتحساب قطعی، بدهی عضو یا موجودی کیف پول نیست.'
  }
] as const;

// Coordinating a visit and making clinical decisions are DIFFERENT duties.
// All entries are static responsibility descriptions, not existing activities.
const careResponsibilitySteps = [
  { key: 'contact', label: 'درخواست و هماهنگی غیراضطراری',
    owner: 'ترنم: هماهنگی ارتباط و پیگیری تجربه عضو',
    restriction: 'هیچ درخواست یا پرونده واقعی هنوز ثبت نشده است.' },
  { key: 'clinical', label: 'تصمیم بالینی و ارجاع تخصصی',
    owner: 'پزشک یا مرکز درمانی دارای صلاحیت، مجوز و مسئولیت قانونی',
    restriction: 'سامانه نمی‌تواند تشخیص، درمان یا ارجاع پزشکی را خودکار تأیید کند.' },
  { key: 'followup', label: 'پیگیری پس از خدمت',
    owner: 'ترنم: هماهنگی تجربه؛ متخصص مجاز: تصمیم مراقبت و پیگیری بالینی',
    restriction: 'هیچ مراجعه، برنامه مراقبتی یا وضعیت تکمیل خدمت تأیید نشده است.' },
] as const;

// TRN-BIZ-034: urgent advice must never wait for an administrative
// or clinical receipt. This is NOT active AI triage or emergency dispatch.
const urgentSafetyLanes = [
  { key: 'warning', label: 'راهنمایی غیرتشخیصی برای دریافت کمک فوری',
    owner: 'در احتمال فوریت، از کمک فوری پزشکی مناسب بدون انتظار برای پاسخ سامانه استفاده شود.',
    limit: 'هیچ ارزیابی خطر، شماره اورژانس منطقه‌ای یا پیام شخصی‌سازی‌شده ارائه نمی‌شود.' },
  { key: 'referral', label: 'درخواست ارجاع هم‌زمان به تیم بالینی مجاز',
    owner: 'ارجاع تخصصی و مسئولیت درمان تنها با تیم دارای مجوز و قرارداد واقعی قابل اجراست.',
    limit: 'هیچ درخواست ارجاع واقعی صادر نشده و هیچ تیم کشیک یا کانال امن متصل نیست.' },
  { key: 'receipt', label: 'تأیید مستقل دریافت و پیگیری بالینی',
    owner: 'دریافت و قبول مسئولیت باید جدا از درخواست و با شواهد تیم درمانی مجاز احراز شود.',
    limit: 'داشتن یادداشت درخواست، به‌معنای ارسال، تحویل، دریافت یا درمان موفق نیست.' }
] as const;

// TRN-BIZ-047 defines ownership and duties, never real approved health
// advice, running programs, treatment plans or an assumed participation choice.
const preventionResponsibilities = [
  {
    key: 'education',
    title: 'آموزش سلامت',
    owner: 'ترنم: ارائه محتوای دارای اعتبار علمی و حقوق انتشار',
    boundary: 'محتوا، منبع، مجوز استفاده و تأیید تخصصی هنوز دریافت نشده‌اند.'
  },
  {
    key: 'coordination',
    title: 'هماهنگی فعالیت‌های پیشگیرانه',
    owner: 'ترنم: هماهنگی تجربه و درخواست اختیاری عضو',
    boundary: 'هیچ مشارکت، خدمت یا برنامه پیشگیرانه واقعی فعال نشده است.'
  },
  {
    key: 'continuity',
    title: 'مراقبت مستمر بالینی',
    owner: 'پزشک یا تیم درمانی مجاز: تصمیم، طرح مراقبت و تغییرات بالینی',
    boundary: 'هیچ تیم، پروانه، طرح مراقبتی یا پیگیری پزشکی تأیید نشده است.'
  }
] as const;

// These are independently required *categories of evidence*, NOT effective
// legal grants; nothing here reads, records or preselects member consent.
const consentBoundaries = [
  { key: 'care', label: 'اشتراک‌گذاری برای خدمات درمانی',
    explanation: 'دسترسی تیم درمانی فقط با نیاز خدمت، اختیار قانونی و مجوز مشخص ممکن است.' },
  { key: 'research', label: 'رضایت مستقل پژوهش و آموزش هوش مصنوعی',
    explanation: 'عضویت، پرداخت یا رضایت درمانی هرگز جانشین رضایت آگاهانه مستقل پژوهشی نیست.' },
  { key: 'law', label: 'مجوز و مبنای قانونی پردازش ثانویه',
    explanation: 'رضایت به‌تنهایی مجوز قانونی استفاده پژوهشی یا انتقال داده ایجاد نمی‌کند.' },
  { key: 'privacy', label: 'حفاظت داده و محدوده استفاده',
    explanation: 'نوع داده، هدف، حفاظت، نگهداری و آثار انصراف باید جداگانه احراز شوند.' },
] as const;

// TRN-BIZ-036/037/038/039: these are *categories of protections*, not
// running AI, trusted datasets, approved committees or member opt-in.
const secondaryAiLifecycle = [
  {
    key: 'consent',
    label: 'رضایت آگاهانه و مستقل برای پژوهش و AI',
    rule: 'انتخاب عضو باید برای هدف مشخص، مستقل از درمان و عضویت و با نسخه و وضعیت لغو معتبر احراز شود.'
  },
  {
    key: 'source',
    label: 'منشأ قانونی و حفاظت داده سلامت',
    rule: 'حق استفاده، نسخه منبع، کیفیت، حداقل داده و مجوز قانونی باید جداگانه بررسی شوند؛ هیچ پرونده‌ای منتقل نشده است.'
  },
  {
    key: 'partitions',
    label: 'تفکیک داده آموزش و ارزیابی مستقل',
    rule: 'منابع تکراری نباید بین آموزش و ارزیابی جابه‌جا شوند؛ هیچ Dataset واقعی یا سیاست آموزش آماده نیست.'
  },
  {
    key: 'governance',
    label: 'کاندید، کمیته مشترک و تأیید نهایی انسانی',
    rule: 'ساخت کاندید خودکار مجوز استفاده عملیاتی نیست؛ بررسی مشترک پزشکی و فنی و تصویب مسئول انسانی دو گیت مستقل‌اند.'
  }
] as const;

// Accepted TRN-BIZ-028/029 only describe possible payer arrangements;
// these are NOT active subscriptions, verified contracts or payer records.
// TRN-BIZ-035/036: no live EHR access or independent clinical audit.
// These are distinct legally controlled future RESPONSIBILITIES only.
const clinicalAccessTransparencyStages = [
  {
    key: 'request',
    label: 'درخواست محدود و مستند متخصص درمانگر',
    rule: 'درخواست ادعایی متخصص برای خدمت و نوع داده مشخص، به‌تنهایی دسترسی یا مجوز مشاهده پرونده نیست.'
  },
  {
    key: 'concern',
    label: 'اعتراض یا پرسش مستقل صاحب پرونده',
    rule: 'عضو باید بتواند طبق سازوکار قانونی معتبر درباره درخواست و دامنه استفاده از داده سؤال یا اعتراض کند؛ هیچ درخواست واقعی ثبت نشده است.'
  },
  {
    key: 'oversight',
    label: 'بررسی مستقل دامنه، مبنا و سابقه دسترسی',
    rule: 'بررسی ناظر نیازمند شواهد مستقل، هویت معتبر و ممیزی ثبت دسترسی واقعی است؛ یادداشت نظارتی به‌معنی رسیدگی یا صدور رأی نیست.'
  }
] as const;

// TRN-BIZ-004/025/026/027: exactly six planned financial source
// CATEGORIES. No bank, fund, commission, allocation, realized return or
// member amount has been verified or connected to this offline web preview.
const sixIncomeOriginStages = [
  { key: 'treatment-fund', label: 'سهم ترنم از سود صندوق توسعه درمان',
    detail: 'وجود و میزان سود قابل تخصیص فقط با قرارداد و دفاتر مالی معتبر احراز می‌شود.' },
  { key: 'health-fund', label: 'سهم ترنم از سود صندوق توسعه سلامت',
    detail: 'سهم سود صندوق دیگر، منبع مستقلی است و دوباره‌شماری آن مجاز فرض نمی‌شود.' },
  { key: 'fixed-fund', label: 'کش‌بک ترنم از کارمزد مدیریت صندوق درآمد ثابت',
    detail: 'کش‌بک کارمزد با اصل سرمایه یا کل بازده صندوق یکسان نیست.' },
  { key: 'insurance', label: 'کارمزد معرفی بیمه تکمیلی',
    detail: 'شناسایی این درآمد به قرارداد مجاز بیمه و سند مالی مستقل نیاز دارد.' },
  { key: 'hospital', label: 'کارمزد فاکتور بیمارستانی',
    detail: 'کارمزد فاکتور بیمارستانی جایگزین هزینه درمان یا سهم عضو نیست.' },
  { key: 'direct', label: 'درآمد مستقیم ترنم، شامل حق اشتراک اعضا',
    detail: 'حق اشتراک جزئی از منبع ششم است، نه منبع هفتم و نه واریز مستقیم به کیف پول پرداخت‌کننده.' }
] as const;

// TRN-BIZ-046/047: read-only explanation of OPTIONAL prevention after
// care coordination, never a medical visit, member consent or automated plan.
const postServiceContinuityStages = [
  {
    key: 'contact',
    title: 'پیگیری تجربه عضو پس از خدمت، بدون ارجاع پزشکی خودکار',
    authority: 'ترنم فقط هماهنگی غیرتشخیصی و ارتباط احتمالی با عضو را تسهیل می‌کند؛ تماس یا تکمیل خدمت واقعی ثبت نشده است.'
  },
  {
    key: 'choice',
    title: 'انتخاب مستقل عضو برای آموزش سلامت یا پیشگیری',
    authority: 'مشارکت اختیاری نیازمند انتخاب مشخص و جاری عضو، محتوای علمی معتبر و حقوق قانونی داده است؛ عضویت پولی به‌معنی رضایت نیست.'
  },
  {
    key: 'clinician',
    title: 'تصمیم متخصص مجاز درباره ادامه مراقبت و برنامه درمانی',
    authority: 'ارزیابی، ادامه مراقبت و تغییر برنامه بالینی فقط در اختیار متخصص قانونی است؛ یادداشت هماهنگی یا سند ادعایی، مجوز درمان نیست.'
  }
] as const;

// TRN-BIZ-035: a member self-view, clinician-limited disclosure and
// legal/emergency exception are DIFFERENT authority channels. None active.
const recordVisibilityLanes = [
  {
    key: 'self',
    label: 'مشاهده شخصی سوابق سلامت عضو',
    authority: 'عضو پس از احراز هویت و کنترل دسترسی اختصاصی',
    limit: 'هیچ پرونده، منبع رسمی یا نشست معتبر عضو متصل نیست.'
  },
  {
    key: 'clinician',
    label: 'اشتراک محدود با متخصص درمانگر',
    authority: 'متخصص دارای صلاحیت با نیاز درمانی مشخص و محدوده مورد تأیید عضو',
    limit: 'هویت متخصص، خدمت مرتبط، دسته داده و مجوز جاری تأیید نشده‌اند.'
  },
  {
    key: 'exception',
    label: 'استثنای قانونی یا فوریت پزشکی',
    authority: 'مرجع قانونی و بالینی صاحب‌اختیار، فقط مطابق سازوکار جداگانه مصوب',
    limit: 'هیچ استثنا یا اختیار افشای اضطراری در این نسخه تعیین یا فعال نشده است.'
  }
] as const;

const membershipFundingRoutes = [
  { key: 'public', label: 'عموم مردم',
    payer: 'خود عضو، در مسیر عضویت مستقیم',
    restriction: 'هیچ پرداخت یا عضویت واقعی ثبت نشده است.' },
  { key: 'support', label: 'نهادهای حمایتی',
    payer: 'عضو یا نهاد حمایتی، فقط مطابق قرارداد معتبر',
    restriction: 'هیچ نهاد حامی یا قرارداد واقعی تأیید نشده است.' },
  { key: 'corporate', label: 'سازمان‌ها و شرکت‌ها',
    payer: 'عضو یا کارفرما، فقط مطابق قرارداد معتبر',
    restriction: 'هیچ کارفرما یا تعهد پرداخت واقعی تأیید نشده است.' },
] as const;

// TRN-BIZ-017..024: necessary independent evidence categories, NOT
// effective legal activation conditions or official membership events.
const activationAdmissionStages = [
  {
    key: 'proofing',
    label: 'احراز هویت از ارائه‌دهنده تخصصی مجاز',
    rule: 'ادعای هویت یا ثبت‌نام کافی نیست؛ نتیجه معتبر و جاریِ احراز هویت باید از ارائه‌دهنده قانونی طرف قرارداد به عضو درست منتسب شود.'
  },
  {
    key: 'correction',
    label: 'اصلاح کد ملی و احراز مجدد قبل از فعال‌سازی',
    rule: 'متقاضی پیش از فعال‌سازی می‌تواند کد ملی نادرست را اصلاح کند؛ نتیجه قدیمی پس از اصلاح دیگر معتبر نیست و احراز جدید لازم است.'
  },
  {
    key: 'payment',
    label: 'قرارداد و پرداخت واقعی اشتراک',
    rule: 'عضویت پولی است؛ حتی اگر کارفرما یا نهاد حمایتی پرداخت کند، مالک عضویت و کیف پول شخص است و ادعای پرداخت، تأیید وصول نیست.'
  },
  {
    key: 'official',
    label: 'ثبت رسمی فعال‌سازی و مبدأ انتظار',
    rule: 'تنها پس از احراز تمام شرایط قانونی و ثبت رسمی فعال‌سازی، شمارش نخستین ۹۰ روز کامل آغاز می‌شود؛ هیچ تأییدی انجام نشده است.'
  }
] as const;

// Business-approved lifecycle semantics, NOT a live membership timetable.
// Dates, legal activation, policy entitlement and amounts remain unavailable.
const membershipLifecycle = [
  { key: 'activation', label: 'آغاز رسمی عضویت',
    rule: 'مبدأ عضویت و شمارش انتظار تنها فعال‌سازی رسمی و معتبر است؛ ثبت‌نام یا پرداخت به‌تنهایی آن را شروع نمی‌کند.' },
  { key: 'wait', label: 'انتظار اولیه ۹۰ روز کامل',
    rule: '۹۰ بازه ۲۴ساعته از زمان فعال‌سازی رسمی؛ سپری‌شدن انتظار به‌تنهایی مجوز برداشت یا مصرف نیست.' },
  { key: 'lapse', label: 'عدم تمدید و دوره وقفه',
    rule: 'اعتبار درمانی قبلی حذف نمی‌شود؛ برای دوره وقفه بدون عضویت اعتبار جدید انباشته نمی‌شود.' },
  { key: 'return', label: 'فعال‌سازی دوباره اشتراک',
    rule: 'انباشت جدید فقط از فعال‌سازی مجدد شروع می‌شود؛ انتظار تازه‌ای برای اعتبار قبلی فرض نمی‌شود.' },
] as const;

const creditLifecycle = [
  { key: 'prior', label: 'اعتبار درمانی قبلی در کیف پول شخصی',
    rule: 'یک کیف پول غیرنقدی برای خود عضو باقی می‌ماند؛ عدم تمدید به‌تنهایی اعتبار قبلی را از بین نمی‌برد.' },
  { key: 'gap', label: 'درآمد منتسب به دوره وقفه',
    rule: 'برای دوره‌ای که عضویت تمدید نشده اعتبار تازه ایجاد نمی‌شود؛ بازفعال‌سازی آن دوره را به عقب پوشش نمی‌دهد.' },
  { key: 'late', label: 'منفعت دیرثبت‌شده مربوط به عضویت معتبر قبلی',
    rule: 'تنها پس از احراز استحقاق و بازبینی مستندات توسط مرجع واقعی، انتساب احتمالی به همان کیف پول بررسی می‌شود.' },
] as const;

const descriptions = [
  { label: 'هویت و دسترسی', value: 'اتصال برقرار نیست', help: 'احراز هویت واقعی هنوز پیاده‌سازی نشده است.' },
  { label: 'اطلاعات درمانی', value: 'در دسترس نیست', help: 'بدون دسترسی مجاز هیچ پرونده‌ای نمایش داده نمی‌شود.' },
  { label: 'خدمات و اعتبار', value: 'در دسترس نیست', help: 'هیچ موجودی، بیمه یا ارائه‌دهنده‌ای شبیه‌سازی نشده است.' },
] as const;

function currentPage(): MemberPage | undefined {
  const path = window.location.pathname.replace(/\/$/, '') || '/';
  return memberPages.find((p) => p.path === path);
}

function InterruptionNotice({ detail }: { detail: string }) {
  return (
    <section className="unavailable" role="status" aria-labelledby="connection-title">
      <div className="unavailable-mark" aria-hidden="true">!</div>
      <div>
        <p className="eyebrow">وضعیت اتصال داده</p>
        <h2 id="connection-title">این بخش هنوز به سرویس واقعی متصل نیست</h2>
        <p>{detail}</p>
        <p className="quiet">این صفحه فقط ساختار رابط کاربری را نشان می‌دهد؛ حساب عضو یا داده واقعی در آن وجود ندارد.</p>
      </div>
    </section>
  );
}

export function MemberApp() {
  const [page, setPage] = useState<MemberPage | undefined>(currentPage);
  const [mobileOpen, setMobileOpen] = useState(false);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  const routeNeedsFocus = useRef(false);

  useEffect(() => {
    const changed = () => {
      routeNeedsFocus.current = true;
      setPage(currentPage());
      setMobileOpen(false);
    };
    window.addEventListener('popstate', changed);
    return () => window.removeEventListener('popstate', changed);
  }, []);

  useEffect(() => {
    document.title = page ? 'ترنم | ' + page.label + ' — متصل نیست' : 'ترنم | صفحه پیدا نشد';
    // Focus after React has rendered the new route heading (including browser Back/Forward).
    // Do not steal focus on initial deep links or during normal keyboard navigation.
    if (routeNeedsFocus.current) {
      routeNeedsFocus.current = false;
      document.getElementById(page ? 'page-title' : 'not-found-title')?.focus();
    }
  }, [page]);

  useEffect(() => {
    if (!mobileOpen) return;
    const closeWithEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMobileOpen(false);
        menuButtonRef.current?.focus();
      }
    };
    window.addEventListener('keydown', closeWithEscape);
    return () => window.removeEventListener('keydown', closeWithEscape);
  }, [mobileOpen]);

  function go(event: MouseEvent<HTMLAnchorElement>, href: string) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    if (window.location.pathname === href) {
      setMobileOpen(false);
      document.getElementById(page ? 'page-title' : 'not-found-title')?.focus();
      return;
    }
    routeNeedsFocus.current = true;
    window.history.pushState({}, '', href);
    setPage(currentPage());
    setMobileOpen(false);
  }

  return (
    <div className="app">
      <a className="skip-link" href="#main-content">پرش به محتوای اصلی</a>
      <aside className="side-panel" aria-label="ناحیه ناوبری عضو">
        <div className="identity">
          <div className="logo-emblem" aria-hidden="true">ت</div>
          <div><strong>ترنم</strong><span>فضای عضو</span></div>
        </div>
        <div className="side-heading">
          <span className="side-small">پنل عضو</span>
          <span className="side-tag">نسخه پایه</span>
        </div>
        <button ref={menuButtonRef} className="menu-toggle" type="button"
          aria-expanded={mobileOpen} aria-controls="member-nav"
          onClick={() => setMobileOpen((v) => !v)}>
          {mobileOpen ? 'بستن فهرست بخش‌ها' : 'باز کردن فهرست بخش‌ها'}
          <span aria-hidden="true">{mobileOpen ? '×' : '☰'}</span>
        </button>
        <nav id="member-nav" className={'member-nav' + (mobileOpen ? ' expanded' : '')}
          aria-label="بخش‌های وب‌اپ عضو">
          {memberPages.map((item) => (
            <a key={item.key} href={item.path} onClick={(event) => go(event, item.path)}
              className={'nav-link' + (page?.key === item.key ? ' selected' : '')}
              aria-current={page?.key === item.key ? 'page' : undefined}>
              <span className="nav-symbol" aria-hidden="true">{item.symbol}</span>
              <span>{item.label}</span>
              <span className="nav-chevron" aria-hidden="true">‹</span>
            </a>
          ))}
        </nav>
        <div className="side-footer">
          <span className="footer-icon" aria-hidden="true">⌁</span>
          <div><strong>حریم خصوصی در اولویت</strong><span>دسترسی واقعی عضو هنوز فعال نیست.</span></div>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="topbar-context"><span aria-hidden="true">⌂</span> <span>ترنم</span>
            <span className="crumb-slash" aria-hidden="true">/</span>
            <span>{page?.label ?? 'مسیر نامعتبر'}</span>
          </div>
          <div className="topbar-status" aria-label="وضعیت محیط: اتصال واقعی برقرار نیست">
            <span className="status-dot" aria-hidden="true"></span> بدون اتصال واقعی
          </div>
        </header>
        <main id="main-content" tabIndex={-1}>
          {!page ? (
            <section className="not-found" aria-labelledby="not-found-title">
              <p className="eyebrow">مسیر ناشناخته</p>
              <h1 id="not-found-title" tabIndex={-1}>صفحه پیدا نشد</h1>
              <p>این آدرس بخشی از فضای عضو نیست. هیچ محتوای خصوصی در این مسیر نمایش داده نمی‌شود.</p>
              <a className="return-link" href="/member" onClick={(event) => go(event, '/member')}>بازگشت به نمای کلی</a>
            </section>
          ) : (
            <>
              <div className="preview-bar" role="note">
                <span className="preview-label">پیش‌نمایش ساختاری</span>
                <span>بدون حساب واقعی، بدون داده درمانی و بدون اتصال مالی</span>
              </div>
              <ReleaseSourcePanel audience="MEMBER"/>
              <section className="hero" aria-labelledby="page-title">
                <div className="hero-content">
                  <span className="hero-kicker">وب‌اپ عضو ترنم <span aria-hidden="true">/</span> رابط اولیه</span>
                  <h1 id="page-title" tabIndex={-1}>{page.key === 'overview' ? 'فضایی آرام برای پیگیری سلامت' : page.label}</h1>
                  <p>{page.key === 'overview'
                    ? 'اینجا در آینده مسیرهای عضویت و خدمات سلامت عضو در یک فضای یکپارچه قرار می‌گیرند؛ در حال حاضر هیچ اطلاعات شخصی نمایش داده نمی‌شود.'
                    : page.subtitle + ' — فقط پس از اتصال به سرویس معتبر و احراز هویت قابل استفاده خواهد بود.'}</p>
                  <span className="hero-chip">اتصال عضو هنوز برقرار نیست</span>
                </div>
                <div className="hero-orbit" aria-hidden="true"><span>ت</span></div>
              </section>
              <div className="section-header">
                <div><p className="eyebrow">وضعیت دسترسی</p><h2>اطلاعات این بخش</h2></div>
                <span className="readonly-pill">صرفاً نمایشی</span>
              </div>
              {page.key === 'overview' ? (
                <div className="status-grid" aria-label="بخش‌های بدون اتصال">
                  {descriptions.map((item) => (
                    <section key={item.label} className="status-card">
                      <span className="status-card-icon" aria-hidden="true">◇</span>
                      <p className="status-card-label">{item.label}</p>
                      <h3>{item.value}</h3>
                      <p>{item.help}</p>
                    </section>
                  ))}
                </div>
              ) : null}
              {page.key === 'membership' ? (
                <section aria-labelledby="payer-boundary-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">پرداخت‌کننده اشتراک با مالک عضویت یکی نیست</p>
                      <h2 id="payer-boundary-title">مسیرهای سه‌گانه تأمین هزینه عضویت</h2>
                    </div>
                    <span className="readonly-pill">هیچ قرارداد یا پرداختی تأیید نشده است</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="مسیرهای پرداخت و مالکیت فردی">
                    {membershipFundingRoutes.map((route) => (
                      <article key={route.key} role="listitem" className="status-card">
                        <p className="status-card-label">{route.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>{route.payer}</p>
                        <p>{route.restriction}</p>
                      </article>
                    ))}
                  </div>
                  <p id="membership-ownership-boundary" className="quiet">
                    مالک عضویت و کیف پول درمانی همیشه خود فرد عضو است.
                    پرداخت حق اشتراک توسط حامی، حق دیدن پرونده سلامت، برداشت اعتبار،
                    تغییر رضایت یا تصمیم درمان را به حامی منتقل نمی‌کند.
                    مبلغ، وضعیت پرداخت، قرارداد، مدت اعتبار و استحقاق هنوز نامعلوم‌اند.
                  </p>
                  <button type="button" disabled aria-describedby="membership-ownership-boundary">
                    درخواست یا پرداخت عضویت — نیازمند قرارداد و سرویس معتبر
                  </button>
                  <section aria-labelledby="activation-admission-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">گذر از مراحل مستقل هویت، اشتراک و تصمیم رسمی</p>
                        <h2 id="activation-admission-title">شواهد لازم پیش از فعال‌سازی عضویت</h2>
                      </div>
                      <span className="readonly-pill">هیچ عضویت رسمی فعال نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="پیش‌شرط‌های مستقل فعال‌سازی عضویت">
                      {activationAdmissionStages.map((item) => (
                        <article className="status-card" role="listitem" key={item.key}>
                          <p className="status-card-label">{item.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{item.rule}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="activation-admission-limit">
                      وضعیت احراز هویت، مرجع قانونی، قرارداد حامی، پرداخت معتبر، تمامی شروط
                      فعال‌سازی و تاریخ رسمی آن از سامانه واقعی دریافت نشده است. حتی تکمیل
                      ادعایی این مراحل به معنای فعال‌شدن خودکار عضویت یا شروع ۹۰ روز نیست.
                      هنگام قطع سرویس احراز یا نتیجه غیرقطعی، درخواست بدون فعال‌سازی باقی می‌ماند.
                    </p>
                    <button type="button" disabled aria-describedby="activation-admission-limit">
                      اصلاح و احراز مجدد اطلاعات — نیازمند هویت و سرویس معتبر
                    </button>
                    <button type="button" disabled aria-describedby="activation-admission-limit">
                      بررسی فعال‌سازی رسمی — نیازمند شواهد همه مراجع قانونی
                    </button>
                  </section>
                  <section aria-labelledby="membership-lifecycle-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">چرخه حق عضویت جدا از ایجاد اعتبار واقعی</p>
                        <h2 id="membership-lifecycle-title">مرزهای زمانی عضویت و اعتبار درمانی</h2>
                      </div>
                      <span className="readonly-pill">تاریخ رسمی موجود نیست</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="قواعد مصوب چرخه عضویت">
                      {membershipLifecycle.map((item) => (
                        <article className="status-card" key={item.key} role="listitem">
                          <p className="status-card-label">{item.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{item.rule}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="membership-lifecycle-warning">
                      هیچ عضویت، پرداخت، تاریخ فعال‌سازی، مدت انتظار سپری‌شده یا استحقاق واقعی
                      عضو تأیید نشده است. تأیید همه شرایط عضویت نیازمند منابع قانونی مستقل است.
                    </p>
                    <button type="button" disabled aria-describedby="membership-lifecycle-warning">
                      مشاهده تاریخچه معتبر عضویت — نیازمند هویت و منبع رسمی
                    </button>
                  </section>
                </section>
              ) : null}
              {page.key === 'credit' ? (
                <section aria-labelledby="individual-wallet-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">مالکیت فردی مستقل از پرداخت‌کننده اشتراک</p>
                      <h2 id="individual-wallet-title">اختیار کیف پول درمانی متعلق به عضو است</h2>
                    </div>
                    <span className="readonly-pill">موجودی نامعلوم</span>
                  </div>
                  <p className="quiet">
                    تأمین هزینه اشتراک توسط سازمان یا نهاد حمایتی، مالکیت کیف پول یا دسترسی
                    به اطلاعات درمانی عضو ایجاد نمی‌کند. اعتبار درمان نقدشدنی نیست و
                    هیچ مبلغ، تراکنش، مصرف یا تخصیصی در این پیش‌نمایش وجود ندارد.
                  </p>
                  <button type="button" disabled>
                    مشاهده گردش اعتبار — نیازمند احراز هویت و منبع مالی معتبر
                  </button>
                  <section aria-labelledby="six-revenue-origins-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">شش منشأ کسب درآمد، یک کیف پول درمانی فردی</p>
                        <h2 id="six-revenue-origins-title">شش منشأ درآمد برای تأمین اعتبار درمانی</h2>
                      </div>
                      <span className="readonly-pill">هیچ درآمد یا تخصیص واقعی تأیید نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="شش منشأ مستقل تأمین اعتبار درمانی">
                      {sixIncomeOriginStages.map((source) => (
                        <article className="status-card" role="listitem" key={source.key}>
                          <p className="status-card-label">{source.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{source.detail}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="six-revenue-evidence-boundary">
                      برابری سهم قابل‌تخصیص میان اعضای واجدشرایط یک اصل مصوب است،
                      نه تأیید مبلغ، درصد یکسان برای هر شش منبع، دوره استحقاق یا واریز واقعی.
                      شواهد تکراری از یک رویداد درآمدی نیازمند بررسی مستقل برای پرهیز از
                      دوباره‌شماری‌اند. نبود سند یک منبع به معنی درآمد صفر نیست.
                      صرف دریافت حق اشتراک، سود تضمینی یا افزایش خودکار کیف پول عضو ایجاد نمی‌کند.
                    </p>
                    <button type="button" disabled aria-describedby="six-revenue-evidence-boundary">
                      بررسی منشأ درآمدها — نیازمند دفاتر و متولی مالی مجاز
                    </button>
                    <button type="button" disabled aria-describedby="six-revenue-evidence-boundary">
                      مشاهده تخصیص واقعی — نیازمند احراز استحقاق و سیاست مالی مصوب
                    </button>
                  </section>
                  <section aria-labelledby="credit-history-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">انتساب دیرهنگام جدا از ایجاد اعتبار در وقفه</p>
                        <h2 id="credit-history-title">منشأ زمانی منافع و حفظ اعتبار قبلی</h2>
                      </div>
                      <span className="readonly-pill">هیچ مبلغی بررسی یا تأیید نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مرزهای تاریخچه اعتبار عضو">
                      {creditLifecycle.map((item) => (
                        <article className="status-card" key={item.key} role="listitem">
                          <p className="status-card-label">{item.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{item.rule}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="credit-history-limit">
                      زمان ثبت منفعت با دوره تعلق آن یکسان نیست. صرف ادعای منبع درآمد،
                      مشاهده سابقه عضویت یا ثبت یادداشت بررسی، موجودی یا اعتبار قابل‌مصرف ایجاد نمی‌کند.
                      برداشت نقدی، انتقال و پرداخت واقعی فعال نیست.
                    </p>
                    <button type="button" disabled aria-describedby="credit-history-limit">
                      بررسی منافع دیرثبت‌شده — نیازمند اسناد و مرجع مالی معتبر
                    </button>
                  </section>
                </section>
              ) : null}
              {page.key === 'records' ? (
                <section aria-labelledby="records-sharing-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">کنترل عضو، کمینه‌سازی داده و نیاز درمانی</p>
                      <h2 id="records-sharing-title">مسیرهای مجزای مشاهده و اشتراک پرونده</h2>
                    </div>
                    <span className="readonly-pill">هیچ دسترسی فعالی وجود ندارد</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="حدود مجاز دسترسی به پرونده سلامت">
                    {recordVisibilityLanes.map((lane) => (
                      <article key={lane.key} role="listitem" className="status-card">
                        <p className="status-card-label">{lane.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>{lane.authority}</p>
                        <p>{lane.limit}</p>
                      </article>
                    ))}
                  </div>
                  <p id="records-legal-boundary" className="quiet">
                    پرداخت اشتراک توسط کارفرما یا نهاد حمایتی مجوز مشاهده پرونده سلامت نیست.
                    هماهنگ‌کننده یا شبکه درمانی نیز بدون نیاز درمانی مشخص، صلاحیت مستقل،
                    مجوز جاری عضو و محدودیت نوع داده حق دسترسی فراگیر ندارد.
                  </p>
                  <button type="button" disabled aria-describedby="records-legal-boundary">
                    مشاهده پرونده شخصی — نیازمند هویت و منبع معتبر
                  </button>
                  <button type="button" disabled aria-describedby="records-legal-boundary">
                    مدیریت دامنه اشتراک با متخصص — نیازمند رضایت و بررسی قانونی
                  </button>
                  <button type="button" disabled aria-describedby="records-legal-boundary">
                    سوابق دسترسی به پرونده — نیازمند ممیزی معتبر
                  </button>
                  <section aria-labelledby="clinical-access-transparency-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">حق پرسش عضو مستقل از ادعای دسترسی متخصص است</p>
                        <h2 id="clinical-access-transparency-title">ردپای درخواست دسترسی و پیگیری حریم خصوصی</h2>
                      </div>
                      <span className="readonly-pill">هیچ سابقه دسترسی واقعی موجود نیست</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مراحل مستقل پیگیری دسترسی بالینی">
                      {clinicalAccessTransparencyStages.map((stage) => (
                        <article className="status-card" role="listitem" key={stage.key}>
                          <p className="status-card-label">{stage.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{stage.rule}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="clinical-access-transparency-limit">
                      ثبت درخواست، ادعای بررسی یا یادداشت اعتراض به‌معنی دسترسی واقعی، افشای پرونده،
                      تأیید پزشک یا رأی نهایی نیست. نمایش حتی سوابق درخواست نیز به احراز مستقل
                      خود عضو و محدوده قانونی دسترسی نیاز دارد. کارفرما، حامی اشتراک و
                      هماهنگ‌کننده درمان حق مشاهده این تاریخچه خصوصی را از نقش خود به دست نمی‌آورند.
                      استثنای دسترسی در فوریت پزشکی و رضایت پژوهش هوش مصنوعی مجوز مستقل می‌خواهند.
                    </p>
                    <button type="button" disabled aria-describedby="clinical-access-transparency-limit">
                      بررسی درخواست‌های دسترسی — نیازمند احراز هویت و ممیزی واقعی
                    </button>
                    <button type="button" disabled aria-describedby="clinical-access-transparency-limit">
                      ثبت پرسش حریم خصوصی — نیازمند مرجع و فرایند قانونی
                    </button>
                  </section>
                  <p className="quiet" id="records-research-boundary">
                    اشتراک برای درمان رضایت پژوهش یا آموزش هوش مصنوعی نیست. لغو، محدودسازی
                    یا تغییر رضایت باید جداگانه و بر اساس شواهد و قرارداد معتبر بررسی شود.
                    هشدار فوریت پزشکی به‌خودی‌خود مجوز افشای اطلاعات ایجاد نمی‌کند.
                  </p>
                  <p className="emergency-note">
                    در فوریت پزشکی دریافت کمک فوری را به احراز دسترسی پرونده یا تکمیل درخواست اشتراک موکول نکنید.
                  </p>
                </section>
              ) : null}
              {page.key === 'consents' ? (
                <section aria-labelledby="consent-boundary-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">رضایت درمانی با استفاده پژوهشی یکی نیست</p>
                      <h2 id="consent-boundary-title">مرزهای مستقل رضایت و حفاظت داده</h2>
                    </div>
                    <span className="readonly-pill">هیچ رضایت معتبری دریافت نشده است</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="پیش‌شرط‌های مستقل اشتراک و استفاده داده">
                    {consentBoundaries.map((item) => (
                      <article key={item.key} role="listitem" className="status-card">
                        <p className="status-card-label">{item.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>{item.explanation}</p>
                      </article>
                    ))}
                  </div>
                  <p className="quiet" id="secondary-consent-limits">
                    وضعیت این صفحه دلیل وجود رضایت، مجوز پژوهش، اجازه اشتراک پرونده یا
                    ورود اطلاعات به Dataset هوش مصنوعی نیست. نحوه لغو رضایت، نگهداری
                    و استفاده از داده‌های مشتق هنوز قرارداد معتبر ندارد.
                  </p>
                  <button type="button" disabled aria-describedby="secondary-consent-limits">
                    ثبت یا تغییر رضایت — نیازمند احراز هویت و قرارداد قانونی
                  </button>
                  <section aria-labelledby="ai-lifecycle-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">داده پژوهشی فقط با مجوز مستقل، مدل فقط با گیت انسانی</p>
                        <h2 id="ai-lifecycle-title">مراحل حفاظت داده تا بررسی نسخه هوش مصنوعی</h2>
                      </div>
                      <span className="readonly-pill">هیچ داده یا نسخه‌ای فعال نیست</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مراحل مستقل حاکمیت داده و AI">
                      {secondaryAiLifecycle.map((item) => (
                        <article className="status-card" role="listitem" key={item.key}>
                          <p className="status-card-label">{item.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{item.rule}</p>
                        </article>
                      ))}
                    </div>
                    <p id="ai-lifecycle-limit" className="quiet">
                      صرف حضور در درمان، پرداخت اشتراک یا ثبت رضایت درمانی به معنی اجازه
                      استفاده از پرونده در آموزش هوش مصنوعی نیست. حتی بررسی موفق منابع و
                      ساخت کاندید، تأیید مدل یا فعال‌سازی خودکار Production محسوب نمی‌شود.
                      سرویس هوش مصنوعی خارجی در اجرای ترنم مجاز نیست.
                    </p>
                    <button type="button" disabled aria-describedby="ai-lifecycle-limit">
                      مشاهده وضعیت رضایت و استفاده پژوهشی — نیازمند منبع و مجوز واقعی
                    </button>
                    <button type="button" disabled aria-describedby="ai-lifecycle-limit">
                      بررسی نسخه هوش مصنوعی — فقط مرجع پزشکی و فنی صاحب‌اختیار
                    </button>
                  </section>
                </section>
              ) : null}
              {page.key === 'costs' ? (
                <section aria-labelledby="nonbinding-cost-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">شفافیت هزینه بدون فرض مبلغ</p>
                      <h2 id="nonbinding-cost-title">اجزای برآورد غیرقطعی</h2>
                    </div>
                    <span className="readonly-pill">همه مقادیر نامعلوم‌اند</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="سهم‌های نامشخص پیش از خدمت">
                    {costShares.map((share) => (
                      <article key={share.key} role="listitem" className="status-card">
                        <p className="status-card-label">{share.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>منبع مورد انتظار: {share.owner}؛ هیچ سند معتبر یا مبلغی دریافت نشده است.</p>
                      </article>
                    ))}
                  </div>
                  <p id="cost-awareness-limits" className="quiet">
                    این نما تضمین قیمت، پوشش بیمه، موجودی کیف پول یا بدهی عضو نیست.
                    برآورد، مجوز برداشت یا رزرو اعتبار نیست و تصمیم نهایی فقط با اسناد معتبر مراجع قانونی ممکن است.
                  </p>
                  <button type="button" disabled aria-describedby="cost-awareness-limits">
                    ثبت آگاهی و انتخاب — نیازمند اتصال امن
                  </button>
                  {import.meta.env.VITE_TARANOM_LOCAL_COST_WORKBENCH === '1' ? (
                    <SyntheticCostWorkbench />
                  ) : null}
                  <section aria-labelledby="cost-choice-continuity-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">نسخه جدید برآورد با انتخاب قبلی یکسان نیست</p>
                        <h2 id="cost-choice-continuity-title">پیوستگی آگاهی عضو پس از تغییر هزینه</h2>
                      </div>
                      <span className="readonly-pill">هیچ انتخاب یا نسخه‌ای تأیید نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مرزهای مستقل آگاهی و تغییر برآورد">
                      {costChoiceContinuityLanes.map((lane) => (
                        <article className="status-card" role="listitem" key={lane.key}>
                          <p className="status-card-label">{lane.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{lane.detail}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="cost-choice-continuity-limit">
                      مبلغ نامعلوم صفر نیست و عدد یا پوشش مورد اختلاف معتبر فرض نمی‌شود.
                      رضایت قبلی برای یک برآورد، تعهد مالی یا رضایت درمانی امضاشده نیست؛
                      هیچ انتخاب قبلی به برآورد تازه منتقل نشده و هیچ کیف پولی برداشت یا مسدود نمی‌شود.
                      کارفرما یا نهاد حامی با پرداخت اشتراک حق مشاهده هزینه درمان عضو را پیدا نمی‌کند.
                    </p>
                    <button type="button" disabled aria-describedby="cost-choice-continuity-limit">
                      بررسی تغییرات نسخه برآورد — نیازمند اسناد و عضو واقعی
                    </button>
                    <button type="button" disabled aria-describedby="cost-choice-continuity-limit">
                      ثبت انتخاب جدید — نیازمند نمایش معتبر و احراز هویت
                    </button>
                    <p className="emergency-note">
                      در فوریت پزشکی، دریافت کمک درمانی را به بررسی برآورد، انتخاب هزینه یا تأیید مالی موکول نکنید.
                    </p>
                  </section>
                </section>
              ) : null}
              {page.key === 'billing' ? (
                <section aria-labelledby="billing-source-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">صورتحساب پس از خدمت، مستقل از برآورد پیش از درمان</p>
                      <h2 id="billing-source-title">اسناد و سهم‌های صورتحساب درمان</h2>
                    </div>
                    <span className="readonly-pill">هیچ سندی تأیید نشده است</span>
                  </div>
                  <p className="quiet">
                    این صفحه جای سند رسمی نیست. برآورد غیرقطعی پیش از خدمت در بخش «برآورد هزینه»
                    جداگانه نمایش داده می‌شود؛ برآورد، فاکتور نهایی یا بدهی قطعی عضو نیست.
                  </p>
                  <div className="status-grid" role="list" aria-label="منشأهای مستقل صورتحساب پس از خدمت">
                    {billingSources.map((source) => (
                      <article className="status-card" role="listitem" key={source.key}>
                        <p className="status-card-label">{source.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>مرجع مورد انتظار: {source.authority}</p>
                        <p>اصل سند، نسخه و منشأ معتبر هنوز دریافت یا بررسی نشده است.</p>
                      </article>
                    ))}
                  </div>
                  <section aria-labelledby="estimate-bill-compare-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">مقایسه اطلاعات غیرقطعی، نه صدور حکم مالی</p>
                        <h2 id="estimate-bill-compare-title">برآورد پیش از خدمت در برابر صورتحساب پس از خدمت</h2>
                      </div>
                      <span className="readonly-pill">هیچ تطبیق رسمی انجام نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مرزهای مقایسه برآورد و صورتحساب">
                      {estimateBillComparisonStages.map((stage) => (
                        <article className="status-card" role="listitem" key={stage.key}>
                          <p className="status-card-label">{stage.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{stage.owner}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="estimate-bill-compare-limit">
                      پیوند پیش‌برآورد و خدمت درمانی بدون منبع و هویت معتبر تأیید نشده است.
                      مبلغ نامعلوم با صفر برابر نیست؛ سهم بیمه مورد اختلاف نیز مبلغ قطعی نیست.
                      برابری یا تفاوت دو ادعای مبلغ، تصمیم قطعی اضافه‌دریافت، جریمه،
                      طلب عضو، تأیید بیمه یا مجوز برداشت و استرداد اعتبار ایجاد نمی‌کند.
                      انتخاب اعلام‌شده عضو درباره برآورد، رضایت درمانی یا تعهد رسمی پرداخت نیست.
                    </p>
                    <button type="button" disabled aria-describedby="estimate-bill-compare-limit">
                      مقایسه سندها — نیازمند برآورد و صورتحساب معتبر
                    </button>
                    <button type="button" disabled aria-describedby="estimate-bill-compare-limit">
                      درخواست اصلاح حقوقی — فقط با مرجع مسئول و شواهد واقعی
                    </button>
                  </section>
                  <p className="quiet" id="billing-correction-limit">
                    سهم بیمه و پوشش را فقط بیمه‌گر یا مرجع قانونی تعیین می‌کند. اختلاف، رد یا
                    اصلاح سند باید با منشأ و نسخه معتبر پیگیری شود؛ ثبت اعتراض موجب اصلاح،
                    رزرو، برداشت یا تسویه خودکار کیف پول عضو نمی‌شود.
                  </p>
                  <button type="button" disabled aria-describedby="billing-correction-limit">
                    پیگیری اختلاف یا اصلاح — نیازمند سند و مرجع قانونی
                  </button>
                  <button type="button" disabled aria-describedby="billing-correction-limit">
                    دریافت صورتحساب رسمی — نیازمند صادرکننده معتبر
                  </button>
                  <p className="quiet">
                    نهاد حامی یا کارفرما صرفاً به‌دلیل پرداخت اشتراک به صورتحساب،
                    سابقه درمان یا اعتبار شخصی عضو دسترسی ندارد. سهم پرداختی عضو نیز
                    بدون اسناد رسمی و تأیید منابع صالح قابل تعیین نیست.
                  </p>
                  <p className="emergency-note">
                    در فوریت پزشکی دریافت کمک درمانی را به مشاهده صورتحساب یا بررسی بیمه موکول نکنید.
                  </p>
                </section>
              ) : null}
              {page.key === 'prevention' ? (
                <section aria-labelledby="prevention-responsibility-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">پیشگیری و آموزش، بدون تصمیم خودکار درمانی</p>
                      <h2 id="prevention-responsibility-title">سه مسیر مستقل پیشگیری و مراقبت</h2>
                    </div>
                    <span className="readonly-pill">هیچ برنامه فعالی وجود ندارد</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="مسیرهای آموزش پیشگیری و مراقبت">
                    {preventionResponsibilities.map((item) => (
                      <article key={item.key} className="status-card" role="listitem">
                        <p className="status-card-label">{item.title}</p>
                        <h3>شواهد معتبر موجود نیست</h3>
                        <p>{item.owner}</p>
                        <p>{item.boundary}</p>
                      </article>
                    ))}
                  </div>
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">کنترل و انتخاب با خود عضو</p>
                      <h2 id="prevention-participation-title">مشارکت اختیاری و رضایت مستقل</h2>
                    </div>
                    <span className="readonly-pill">انتخاب عضو نامعلوم است</span>
                  </div>
                  <p className="quiet" id="prevention-consent-boundary">
                    عضو می‌تواند در آینده درخواست مشارکت کند یا پیشنهاد تیم درمانی واجدصلاحیت را بررسی کند.
                    هیچ پیشنهاد، ثبت‌نام، رضایت درمانی یا اشتراک‌گذاری پرونده به صورت خودکار پذیرفته نمی‌شود.
                    مشارکت در آموزش یا مراقبت، رضایت مستقل پژوهش و آموزش هوش مصنوعی نیست.
                  </p>
                  <button type="button" disabled aria-describedby="prevention-consent-boundary">
                    درخواست مشارکت — نیازمند هویت و رضایت معتبر
                  </button>
                  <p className="quiet" id="prevention-care-limit">
                    برنامه مراقبت مستمر فقط با ارزیابی و تصمیم متخصص مجاز قابل شروع یا تغییر است.
                    هیچ پایش خودکار دائمی، تشخیص پزشکی یا محتوای آموزشیِ تأییدشده در این نسخه ارائه نمی‌شود.
                  </p>
                  <button type="button" disabled aria-describedby="prevention-care-limit">
                    مشاهده پیشنهاد تیم درمانی — نیازمند مرجع بالینی معتبر
                  </button>
                  <p className="emergency-note">
                    در احتمال فوریت، دریافت کمک فوری پزشکی را به محتوای آموزشی،
                    هماهنگی پیشگیری یا مراقبت پیگیری‌شده در این سامانه موکول نکنید.
                  </p>
                </section>
              ) : null}
              {page.key === 'care' ? (
                <section aria-labelledby="care-boundary-title">
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">هماهنگی خدمت، جدا از تصمیم پزشکی</p>
                      <h2 id="care-boundary-title">مراحل و مسئولیت‌های مسیر درمان</h2>
                    </div>
                    <span className="readonly-pill">هیچ مسیر فعالی وجود ندارد</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="مرزهای هماهنگی و درمان">
                    {careResponsibilitySteps.map((step) => (
                      <article className="status-card" role="listitem" key={step.key}>
                        <p className="status-card-label">{step.label}</p>
                        <h3>اطلاعات موجود نیست</h3>
                        <p>{step.owner}</p>
                        <p>{step.restriction}</p>
                      </article>
                    ))}
                  </div>
                  <p id="care-safety-limit" className="quiet">
                    هماهنگی ترنم جایگزین ارزیابی متخصص، تأیید ارجاع یا مجوز دسترسی به پرونده سلامت نیست.
                    مشارکت عضو و استفاده از اطلاعات سلامت فقط با مبنای قانونی و مجوز معتبر انجام می‌شود.
                  </p>
                  <button type="button" disabled aria-describedby="care-safety-limit">
                    درخواست هماهنگی — نیازمند هویت و مجوز واقعی
                  </button>
                  <section aria-labelledby="post-service-prevention-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">هماهنگی پس از خدمت از مشارکت در مراقبت جداست</p>
                        <h2 id="post-service-prevention-title">پیوستگی ایمن پیگیری درمان و پیشگیری</h2>
                      </div>
                      <span className="readonly-pill">هیچ مشارکت یا پیگیری واقعی آغاز نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مراحل مستقل پیگیری و مشارکت عضو">
                      {postServiceContinuityStages.map((stage) => (
                        <article className="status-card" role="listitem" key={stage.key}>
                          <p className="status-card-label">{stage.title}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{stage.authority}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="post-service-continuity-limit">
                      یادداشت تماس هماهنگ‌کننده یا ارجاع ادعایی به متخصص، اثبات ملاقات،
                      شروع برنامه مراقبتی یا رضایت عضو نیست. عضو می‌تواند مشارکت را نپذیرد؛
                      هیچ یادآوری، پایش سلامت، پیام شخصی‌سازی‌شده یا تغییر خودکار مراقبت فعال نیست.
                      برای استفاده پژوهشی یا آموزش هوش مصنوعی رضایت مستقل و مبنای قانونی لازم است.
                      اطلاعات سلامت عضو در اختیار حامی اشتراک یا کارفرما قرار نمی‌گیرد.
                    </p>
                    <button type="button" disabled aria-describedby="post-service-continuity-limit">
                      بررسی پیگیری پس از خدمت — نیازمند هویت و مرجع درمانی واقعی
                    </button>
                    <button type="button" disabled aria-describedby="post-service-continuity-limit">
                      بررسی انتخاب مشارکت — نیازمند رضایت مستقل عضو
                    </button>
                  </section>
                  <p className="emergency-note">
                    در احتمال فوریت پزشکی، دریافت کمک فوری را به هماهنگی غیراضطراری یا پاسخ پزشک در سامانه موکول نکنید.
                  </p>
                  <section aria-labelledby="urgent-safety-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">راهنمایی فوری و پیگیری متخصص، دو مسئولیت مستقل</p>
                        <h2 id="urgent-safety-title">سه مرز ایمنی در احتمال فوریت پزشکی</h2>
                      </div>
                      <span className="readonly-pill">هیچ ارجاع یا دریافت واقعی ثبت نشده است</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="مرزهای راهنمایی فوری و ارجاع موازی">
                      {urgentSafetyLanes.map((lane) => (
                        <article className="status-card" role="listitem" key={lane.key}>
                          <p className="status-card-label">{lane.label}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{lane.owner}</p>
                          <p>{lane.limit}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="urgent-no-service-warning">
                      ترنم در این نسخه به سامانه اورژانس عمومی، شبکه پزشک کشیک یا
                      مسیر ارسال و دریافت ارجاع متصل نیست و خدمت پایش شبانه‌روزی ارائه نمی‌کند.
                      هیچ هشدار شخصی‌سازی‌شده، تشخیص پزشکی یا تضمین رسیدگی صادر نشده است.
                      در احتمال فوریت، منتظر هماهنگی، بیمه، پرداخت یا شکایت نمانید.
                    </p>
                    <button type="button" disabled aria-describedby="urgent-no-service-warning">
                      بررسی رسید ارجاع بالینی — نیازمند تیم و سامانه واقعی
                    </button>
                    <button type="button" disabled aria-describedby="urgent-no-service-warning">
                      آغاز ارجاع پزشکی — نیازمند احراز اختیار و پروتکل بالینی معتبر
                    </button>
                  </section>
                </section>
              ) : null}
              {page.key === 'support' ? (
                <section aria-labelledby="support-paths-title">
                  {import.meta.env.VITE_TARANOM_LOCAL_QA_HOP === '1' &&
                    window.location.hostname === '127.0.0.1' &&
                    window.location.port === '4318' && (
                    <nav aria-label="گردش‌کار آزمایشی ایزوله" className="source-strip">
                      <div>
                        <strong>آزمایش فنی با داده ساختگی — جدا از سامانه عضو</strong>
                        <p>این پیوند فقط در Build ویژه CI و با سرویس مستقل localhost:4320 فعال است.
                          پرونده آزمایشی در PostgreSQL ذخیره می‌شود ولی هیچ شکایت رسمی یا حساب واقعی ایجاد نمی‌کند.</p>
                        <a href="http://127.0.0.1:4320/member-demo/">
                          آزمایش پرونده پشتیبانی با PostgreSQL
                        </a>
                      </div>
                    </nav>
                  )}
                  <div className="section-header">
                    <div>
                      <p className="eyebrow">مسیر واحد پیگیری؛ اختیارهای مستقل</p>
                      <h2 id="support-paths-title">موضوعات شکایت و اعتراض</h2>
                    </div>
                    <span className="readonly-pill">ثبت واقعی غیرفعال</span>
                  </div>
                  <div className="status-grid" role="list" aria-label="مسیرهای مستقل رسیدگی">
                    {supportPaths.map((path) => (
                      <article className="status-card" role="listitem" key={path.key}>
                        <p className="status-card-label">{path.label}</p>
                        <h3>ثبت و پیگیری هنوز متصل نیست</h3>
                        <p>{path.authority}</p>
                        <p>{path.disclosure}</p>
                        <button type="button" disabled>
                          ثبت درخواست — نیازمند احراز هویت و مرجع معتبر
                        </button>
                      </article>
                    ))}
                  </div>
                  <section aria-labelledby="insurance-handoff-title">
                    <div className="section-header">
                      <div>
                        <p className="eyebrow">اعتراض بیمه از رأی مرجع قانونی و اصلاح مالی مستقل است</p>
                        <h2 id="insurance-handoff-title">سه مرحله مستقل پیگیری اختلاف سهم بیمه</h2>
                      </div>
                      <span className="readonly-pill">هیچ نتیجه یا رسید واقعی وجود ندارد</span>
                    </div>
                    <div className="status-grid" role="list" aria-label="پیوند اعتراض بیمه با اسناد رسمی">
                      {insurerDisputeHandoffStages.map((stage) => (
                        <article className="status-card" role="listitem" key={stage.key}>
                          <p className="status-card-label">{stage.title}</p>
                          <h3>اطلاعات موجود نیست</h3>
                          <p>{stage.owner}</p>
                          <p>{stage.limit}</p>
                        </article>
                      ))}
                    </div>
                    <p className="quiet" id="insurer-handoff-limit">
                      اعتراض درباره سهم بیمه پایه یا تکمیلی باید به نسخه دقیق سند همان عضو
                      وابسته باشد. درخواست ارسال‌شده، رسید بیمه‌گر یا رأی نهایی نیست.
                      حتی با وجود پاسخ بیمه‌گر، اصلاح حقوقی صورتحساب و تسویه مالی مستقل‌اند.
                      هیچ برداشت، مسدودی، جبران، برگشت یا اصلاح خودکار اعتبار درمان انجام نمی‌شود.
                      حامی یا کارفرما با پرداخت اشتراک حق دیدن اسناد درمان عضو را پیدا نمی‌کند.
                    </p>
                    <button type="button" disabled aria-describedby="insurer-handoff-limit">
                      بررسی رسید بیمه‌گر — نیازمند کانال و سند معتبر
                    </button>
                    <button type="button" disabled aria-describedby="insurer-handoff-limit">
                      درخواست بررسی اصلاح سهم — فقط با مرجع قانونی
                    </button>
                  </section>
                  <p className="quiet" id="support-legal-limit">
                    این صفحه رأی پزشکی، تصمیم بیمه‌گر، حکم جبران خسارت یا وضعیت یک پرونده واقعی نیست.
                    هیچ شماره پرونده، زمان پاسخ یا نتیجه رسیدگی ساخته نشده است.
                  </p>
                  <p className="emergency-note">
                    در فوریت پزشکی، کمک پزشکی و تماس با خدمات فوریت را به ثبت شکایت یا بررسی بیمه موکول نکنید.
                  </p>
                </section>
              ) : null}
              <InterruptionNotice detail={page.detail} />
              <div className="source-strip" aria-label="مالک داده">
                <span className="source-symbol" aria-hidden="true">↗</span>
                <div><strong>منبع معتبر این بخش</strong><span>{page.owner} — اتصال و قرارداد عملیاتی هنوز برقرار نشده است.</span></div>
              </div>
              <p className="emergency-note">در وضعیت اضطراری، دریافت کمک پزشکی را به ورود به سامانه یا مسائل اداری و پرداخت موکول نکنید.</p>
            </>
          )}
        </main>
        <footer className="site-footer">
          <span>ترنم · چارچوب اولیه وب‌اپ عضو</span>
          <span>بدون احراز هویت و بدون داده واقعی</span>
        </footer>
      </div>
    </div>
  );
}
