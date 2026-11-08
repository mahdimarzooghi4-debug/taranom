import { useEffect, useState } from 'react';
import { AppointmentReadError, readAppointments, type Appointment, type AppointmentAccess } from './appointment-client';
type State = { kind: 'loading' } | { kind: 'ready'; items: Appointment[] } | { kind: 'error'; message: string };
export function MemberAppointments({ access }: { access: AppointmentAccess }) {
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ access: AppointmentAccess; attempt: number; state: State }>();
  const state: State = result?.access === access && result.attempt === attempt ? result.state : { kind: 'loading' };
  useEffect(() => {
    const controller = new AbortController();
    void readAppointments(access, controller.signal).then(items => {
      if (!controller.signal.aborted) setResult({ access, attempt, state: { kind: 'ready', items } });
    }).catch((error: unknown) => {
      if (controller.signal.aborted) return;
      const message = error instanceof AppointmentReadError && error.kind === 'signed-out'
        ? 'برای مشاهده نوبت‌ها وارد حساب خود شوید.'
        : error instanceof AppointmentReadError && error.kind === 'forbidden'
          ? 'دسترسی مشاهده نوبت‌ها برای این حساب فعال نیست.'
          : 'دریافت نوبت‌ها انجام نشد. دوباره تلاش کنید.';
      setResult({ access, attempt, state: { kind: 'error', message } });
    });
    return () => controller.abort();
  }, [access, attempt]);
  return <section aria-labelledby="my-appointments-title" aria-busy={state.kind === 'loading'}>
    <div className="section-header"><h2 id="my-appointments-title">نوبت‌های من</h2>
      <button type="button" disabled={state.kind === 'loading'} onClick={() => setAttempt(x => x + 1)}>تازه‌سازی نوبت‌ها</button></div>
    {state.kind === 'loading' && <p role="status">در حال دریافت نوبت‌ها…</p>}
    {state.kind === 'error' && <p role="alert">{state.message}</p>}
    {state.kind === 'ready' && (state.items.length === 0 ? <p role="status">هنوز نوبتی برای شما ثبت نشده است.</p>
      : <ul>{state.items.map(item => <li key={`${item.centerRef}:${item.bookingRef}`}>
        <time dateTime={item.startsAt}>{new Intl.DateTimeFormat('fa-IR', {
          dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Tehran',
        }).format(new Date(item.startsAt))}</time>
        {' — '}{item.state === 'Booked' ? 'رزرو شده' : 'لغو شده توسط شما'}
      </li>)}</ul>)}
  </section>;
}
