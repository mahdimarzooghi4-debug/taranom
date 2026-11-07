// Navigation readiness only. Each destination enforces authentication and backend authorization itself.
export const patientServices={
 membership:{title:'عضویت و گروه اعضا',description:'حساب مستقل و مدیریت گروه',href:null},
 finance:{title:'اعتبار حمایتی و سوابق مالی',description:'جزئیات اعتبار و تراکنش‌ها',href:null},
 guide:{title:'راهنمای هوشمند',description:'کمک به انتخاب مسیر و تخصص',href:null},
 booking:{title:'نوبت و پزشک یا مرکز',description:'انتخاب مرکز و مشاهدهٔ ظرفیت',href:'/patient-app/booking'},
 records:{title:'پرونده و نسخه‌ها',description:'سوابق ثبت‌شده برای همین حساب',href:null},
 prescriptions:{title:'نسخه‌های من',description:'نسخه‌های ثبت‌شده برای همین حساب',href:null},
 referrals:{title:'ارجاع‌های درمانی',description:'ارجاع ثبت‌شده از پزشک یا مرکز',href:null},
 appointments:{title:'نوبت‌های من',description:'پیگیری نوبت‌های حساب',href:'/patient-app/appointments'},
 account:{title:'حساب من',description:'اطلاعات و تنظیمات حساب',href:'/patient-app/account'},
 profile:{title:'اطلاعات فردی',description:'مشخصات اعلامی شما',href:'/patient-app/personal-info'},
 notifications:{title:'اعلان‌ها',description:'اعلان‌های ثبت‌شدهٔ حساب',href:'/patient-app/notifications'},
 support:{title:'پشتیبانی',description:'ثبت درخواست و مشاهدهٔ پاسخ',href:'/patient-app/support'},
} as const;
export type PatientServiceKey=keyof typeof patientServices;
