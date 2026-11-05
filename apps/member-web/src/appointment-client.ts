export type Appointment = {
  centerRef: string; revision: number; bookingRef: string;
  memberRef: string; slotRef: string; startsAt: string;
  state: 'Booked' | 'CancelledByMember';
};
export class AppointmentReadError extends Error {
  readonly kind: 'signed-out' | 'forbidden' | 'unavailable';
  constructor(kind: 'signed-out' | 'forbidden' | 'unavailable') { super(kind); this.kind = kind; }
}
export type AppointmentAccess = { getAccessToken: () => Promise<string | null> };
// Tokens are supplied by the authenticated host and are never persisted here.
export async function readAppointments(
  access: AppointmentAccess, signal: AbortSignal, request: typeof fetch = fetch,
): Promise<Appointment[]> {
  const token = await access.getAccessToken();
  signal.throwIfAborted();
  if (!token) throw new AppointmentReadError('signed-out');
  const response = await request('/api/v1/me/appointments', {
    signal, cache: 'no-store', credentials: 'omit', redirect: 'error',
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
  });
  if (!response.ok) throw new AppointmentReadError(
    response.status === 401 ? 'signed-out' : response.status === 403 ? 'forbidden' : 'unavailable',
  );
  const data: unknown = await response.json();
  if (!data || typeof data !== 'object' || !('appointments' in data) ||
      !Array.isArray(data.appointments) || !data.appointments.every(isAppointment)) {
    throw new AppointmentReadError('unavailable');
  }
  return data.appointments;
}
function isAppointment(value: unknown): value is Appointment {
  if (!value || typeof value !== 'object') return false;
  const item = value as Record<string, unknown>;
  return ['centerRef', 'bookingRef', 'memberRef', 'slotRef'].every(
    key => typeof item[key] === 'string' && item[key].length > 0,
  ) && Number.isSafeInteger(item.revision) && Number(item.revision) >= 0 &&
    typeof item.startsAt === 'string' && Number.isFinite(Date.parse(item.startsAt)) &&
    (item.state === 'Booked' || item.state === 'CancelledByMember');
}
