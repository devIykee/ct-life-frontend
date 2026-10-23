// Local authenticated fault injection. Driver uses the proxy; observer bypasses it.
// SDK console output is suppressed because reconnection diagnostics may include bearer-like tokens.
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { writeFileSync, renameSync } from 'node:fs';
const arg = (key, fallback) => { const i = process.argv.indexOf('--' + key); return i < 0 ? fallback : process.argv[i + 1]; };
const require = createRequire(resolve(process.env.CTLIFE_BACKEND_DIR ?? '../ct-life-backend-audit20261019', 'package.json'));
const { Client } = require('@colyseus/sdk'), { Keypair } = require('@solana/web3.js');
const durationMs = Number(arg('duration-ms', '2000')), out = arg('out', '/tmp/ctlife-fault-probe.json');
if (!Number.isInteger(durationMs) || durationMs < 100 || durationMs > 120000) throw new Error('Invalid duration');
for (const name of ['log', 'info', 'warn', 'error', 'debug']) console[name] = () => {};
const result = { startedAt: new Date().toISOString(), durationMs, phase: 'starting', checks: [], events: [], metrics: {} };
const rooms = [], timers = [], sleep = ms => new Promise(r => setTimeout(r, ms));
const save = () => { writeFileSync(out + '.tmp', JSON.stringify(result, null, 2) + '\n'); renameSync(out + '.tmp', out); };
function event(type, detail = {}) { result.events.push({ at: new Date().toISOString(), type, ...detail }); save(); }
function check(ok, name) { result.checks.push({ name, pass: !!ok }); save(); if (!ok) throw new Error(name); }
async function until(predicate, name, timeout = 10000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) { if (predicate()) return; await sleep(50); }
  throw new Error(name);
}
async function auth() {
  const response = await fetch('http://127.0.0.1:4012/v1/auth/dev', { method: 'POST', headers: { 'content-type': 'application/json', 'idempotency-key': randomUUID() }, body: JSON.stringify({ wallet: Keypair.generate().publicKey.toBase58() }) });
  if (response.status !== 200) throw new Error('dev_auth_http_' + response.status);
  return response.json();
}
async function join(client, roomName, authData, label) {
  const room = await client.joinOrCreate(roomName, { token: authData.accessToken, ...(roomName === 'street' ? { district: 'timeline' } : {}) });
  rooms.push(room); room.onMessage('*', () => {});
  room.onDrop(code => event('drop', { room: label, code }));
  room.onReconnect(() => event('reconnect', { room: label }));
  room.onLeave(code => event('leave', { room: label, code }));
  room.onError(code => event('room_error', { room: label, code: Number.isFinite(code) ? code : null }));
  room.onMessage('rejected', m => event('rejected', { room: label, action: m.type, code: m.code }));
  event('joined', { room: label }); return room;
}
try {
  save();
  const observerAuth = await auth(), driverAuth = await auth();
  const direct = new Client('ws://127.0.0.1:2569'), proxied = new Client('ws://127.0.0.1:2570');
  const observerStreet = await join(direct, 'street', observerAuth, 'observer_street');
  const observerVehicles = await join(direct, 'vehicles', observerAuth, 'observer_vehicles');
  const driverStreet = await join(proxied, 'street', driverAuth, 'driver_street');
  const driverVehicles = await join(proxied, 'vehicles', driverAuth, 'driver_vehicles');
  let sendDriver = true, observerX = 1;
  timers.push(setInterval(() => {
    observerStreet.send('move', { x: observerX, z: -12, rotY: 0 });
    if (sendDriver) driverStreet.send('move', { x: 2, z: -12, rotY: 0 });
  }, 200));
  await until(() => driverStreet.state?.players?.get(driverStreet.sessionId), 'driver_presence');
  const vehicleId = 'audit_proxy_' + randomUUID();
  driverVehicles.send('vehicle_enter', { id: vehicleId, kind: 'compact', x: 2, y: 0, z: -12, yaw: 0 });
  await until(() => observerVehicles.state?.vehicles?.get(vehicleId)?.driver === driverAuth.user.id, 'observer_claim');
  check(true, 'direct observer sees proxied driver ownership');
  await sleep(6000); // Satisfy the installed SDK's default five-second minUptime.
  driverVehicles.send('vehicle_move', { id: vehicleId, x: 3, y: 0, z: -12, yaw: 0, pitch: 0, roll: 0, speed: 2 });
  await until(() => observerVehicles.state?.vehicles?.get(vehicleId)?.x === 3, 'observer_driver_motion');
  check(true, 'direct observer sees vehicle movement before cut');
  sendDriver = false;
  const cutAt = Date.now();
  const response = await fetch('http://127.0.0.1:2571/cut', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ durationMs }) });
  const cut = await response.json();
  check(response.status === 200 && cut.cutPairs >= 2 && cut.activePairs === 0, 'proxy destroys existing driver socket pairs');
  result.metrics.cutPairs = cut.cutPairs; result.metrics.cutAt = new Date(cutAt).toISOString(); save();
  await until(() => result.events.filter(e => e.type === 'drop' && e.room.startsWith('driver_')).length >= 2, 'both_driver_rooms_drop');
  check(true, 'actual SDK drop events received on both driver rooms');
  await until(() => observerVehicles.state?.vehicles?.get(vehicleId)?.driver === '', 'observer_release');
  result.metrics.releaseObservedAfterMs = Date.now() - cutAt; event('observer_driver_released');
  check(true, 'published backend releases driver without reconnection grace');
  observerX = 1.25;
  await until(() => observerStreet.state?.players?.get(observerStreet.sessionId)?.x === 1.25, 'observer_unaffected');
  check(!result.events.some(e => e.type === 'drop' && e.room.startsWith('observer_')), 'direct observer remains connected and gets movement acknowledgment');
  await until(() => result.events.filter(e => e.type === 'leave' && e.room.startsWith('driver_')).length >= 2, 'SDK_retries_finish', Math.max(75000, durationMs + 20000));
  await sleep(Math.max(0, cutAt + durationMs + 200 - Date.now()));
  check(!result.events.some(e => e.type === 'reconnect' && e.room.startsWith('driver_')), 'no unsupported same-session resume claimed');
  const after = await (await fetch('http://127.0.0.1:2571/status')).json();
  check(after.refused > cut.refused, 'new connection attempts rejected during outage');
  result.metrics.refusedDuringScenario = after.refused - cut.refused;
  const newStreet = await join(proxied, 'street', driverAuth, 'fresh_street');
  const newVehicles = await join(proxied, 'vehicles', driverAuth, 'fresh_vehicles');
  newStreet.send('move', { x: 3, z: -12, rotY: 0 });
  await until(() => newStreet.state?.players?.get(newStreet.sessionId), 'fresh_presence');
  newVehicles.send('vehicle_enter', { id: vehicleId });
  await until(() => observerVehicles.state?.vehicles?.get(vehicleId)?.driver === driverAuth.user.id, 'fresh_claim');
  check(true, 'fresh authenticated room join can reclaim released vehicle after service returns');
  newVehicles.send('vehicle_exit', { id: vehicleId, x: 3, z: -12, yaw: 0 });
  await until(() => observerVehicles.state?.vehicles?.get(vehicleId)?.driver === '', 'fresh_exit');
  result.phase = 'complete'; result.finishedAt = new Date().toISOString(); save();
  process.stdout.write('REALTIME_FAULT_PROBE_COMPLETE checks=' + result.checks.length + '\n');
} catch (error) {
  result.phase = 'failed'; result.failedCheck = error.message?.startsWith('fetch') ? 'transport_request_failed' : (error instanceof Error ? error.name + ':' + result.checks.at(-1)?.name : 'unknown');
  result.finishedAt = new Date().toISOString(); save(); process.exitCode = 1;
  process.stdout.write('REALTIME_FAULT_PROBE_FAILED see redacted receipt\n');
} finally {
  for (const timer of timers) clearInterval(timer);
  for (const room of rooms) { room.reconnection.enabled = false; if (room.connection?.isOpen) void room.leave(); }
  await sleep(500);
}
