// TRN-TECH-107: human-portable, unsigned, synthetic-only uncertain-write receipt.
// This is an UNTRUSTED transport of the original exact POST bytes, not authority.
export type RecoveryKind = 'DEFINE_DEPARTMENT' | 'RECORD_DOCTOR' | 'ASSIGN_DOCTOR' | 'END_DOCTOR';
export type UncertainCenterCommand = {
  center: string; expectedVersion: number; kind: RecoveryKind; body: string;
};
const ownKeys = (v: Record<string, unknown>, names: string[]) =>
  Object.keys(v).join('|') === names.join('|');
const object = (v: unknown): v is Record<string, unknown> =>
  v !== null && typeof v === 'object' && !Array.isArray(v);
const testRef = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= 128 &&
  /^test:[A-Za-z0-9_-]{1,123}$/.test(v);
const uuidV4 = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);

export function makeCenterRecoveryReceipt(receipt: UncertainCenterCommand): string {
  // Only the browser-created original command is exported; not a credential.
  return JSON.stringify({
    contract: 'TRN-TECH-107', syntheticOnly: true,
    centerRef: receipt.center, originalBody: receipt.body
  });
}
export function parseCenterRecoveryReceipt(text: string): UncertainCenterCommand | null {
  // Keep this small, canonical and human-controlled. No network or storage.
  if (text.length < 2 || text.length > 2400) return null;
  const trimmed = text.trim();
  try {
    const envelope: unknown = JSON.parse(trimmed);
    if (!object(envelope) ||
        !ownKeys(envelope, ['contract', 'syntheticOnly', 'centerRef', 'originalBody']) ||
        envelope.contract !== 'TRN-TECH-107' || envelope.syntheticOnly !== true ||
        !testRef(envelope.centerRef) || typeof envelope.originalBody !== 'string' ||
        envelope.originalBody.length > 768 ||
        // Refuse extra or duplicate keys, whitespace tricks and reordered
        // command/envelope fields. The *exact* original bytes must be replayed.
        JSON.stringify(envelope) !== trimmed) return null;
    const original: unknown = JSON.parse(envelope.originalBody);
    if (!object(original) ||
        !ownKeys(original, ['requestId','expectedVersion','kind','departmentRef','doctorRef']) ||
        JSON.stringify(original) !== envelope.originalBody ||
        !uuidV4(original.requestId) ||
        !Number.isSafeInteger(original.expectedVersion) ||
        (original.expectedVersion as number) < 0 ||
        (original.expectedVersion as number) >= Number.MAX_SAFE_INTEGER ||
        !['DEFINE_DEPARTMENT','RECORD_DOCTOR','ASSIGN_DOCTOR','END_DOCTOR'].includes(
          String(original.kind))) return null;
    const department = original.departmentRef;
    const doctor = original.doctorRef;
    const correct =
      (original.kind === 'DEFINE_DEPARTMENT' && testRef(department) && doctor === null) ||
      (original.kind === 'RECORD_DOCTOR' && department === null && testRef(doctor)) ||
      (original.kind === 'ASSIGN_DOCTOR' && testRef(department) && testRef(doctor)) ||
      (original.kind === 'END_DOCTOR' && department === null && testRef(doctor));
    if (!correct) return null;
    return {
      center: envelope.centerRef, expectedVersion: original.expectedVersion as number,
      kind: original.kind as RecoveryKind, body: envelope.originalBody
    };
  } catch {
    return null;
  }
}
