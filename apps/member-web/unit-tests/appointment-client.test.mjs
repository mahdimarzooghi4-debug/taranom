import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readAppointments } from '../src/appointment-client.ts';
const access = { getAccessToken: async () => 'synthetic-test-token' };
const signal = () => new AbortController().signal;
const item = { centerRef: 'test-center', revision: 1, bookingRef: 'test-booking', memberRef: 'test-member', slotRef: 'test-slot', startsAt: '2026-11-05T10:00:00Z', state: 'Booked' };
test('does not send a request without a session', async () => {
  await assert.rejects(readAppointments({ getAccessToken: async () => null }, signal(), () => assert.fail('unexpected fetch')), { kind: 'signed-out' });
});
test('reads only authenticated self endpoint and prevents caching or redirects', async () => {
  const result = await readAppointments(access, signal(), async (url, options) => {
    assert.equal(url, '/api/v1/me/appointments');
    assert.equal(options.cache, 'no-store'); assert.equal(options.redirect, 'error');
    assert.equal(options.credentials, 'omit');
    assert.equal(options.headers.Authorization, 'Bearer synthetic-test-token');
    return Response.json({ appointments: [item, { ...item, state: 'CancelledByMember' }] });
  });
  assert.equal(result.length, 2);
});
test('empty account returns an empty list', async () => {
  assert.deepEqual(await readAppointments(access, signal(), async () => Response.json({ appointments: [] })), []);
});
for (const [status, kind] of [[401, 'signed-out'], [403, 'forbidden'], [500, 'unavailable']]) {
  test(`handles HTTP ${status} without exposing response details`, async () => {
    await assert.rejects(readAppointments(access, signal(), async () => new Response('internal details', { status })), { kind });
  });
}
for (const bad of [{}, { appointments: null }, { appointments: [{ ...item, startsAt: 'bad' }] }, { appointments: [{ ...item, state: 'Unknown' }] }, { appointments: [{ ...item, revision: -1 }] }]) {
  test(`rejects malformed response ${JSON.stringify(bad)}`, async () => {
    await assert.rejects(readAppointments(access, signal(), async () => Response.json(bad)), { kind: 'unavailable' });
  });
}
test('aborting during token retrieval prevents network request', async () => {
  const controller = new AbortController();
  await assert.rejects(readAppointments({ getAccessToken: async () => { controller.abort(); return 'token'; } }, controller.signal, () => assert.fail('unexpected fetch')), { name: 'AbortError' });
});
