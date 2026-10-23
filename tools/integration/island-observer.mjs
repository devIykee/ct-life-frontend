// Authenticated second client for island/vehicle device verification. Uses the backend's pinned SDK.
// CTLIFE_BACKEND_DIR=../ct-life-backend-audit20261019 node tools/integration/island-observer.mjs \
//   --api http://127.0.0.1:4012 --ws ws://127.0.0.1:2569 --out /tmp/island-observer.json --seconds 900
// Records pseudonymous peers and aggregate movement/ownership observations; never tokens or wallet keys.
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import { writeFileSync, renameSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
const report = console.log.bind(console);
// SDK reconnect diagnostics can contain the token-bearing URL. Emit only our fixed status lines.
for (const method of ['log', 'warn', 'error', 'debug', 'info']) console[method] = () => {};
const arg = (key, fallback) => { const i = process.argv.indexOf('--' + key); return i >= 0 ? process.argv[i + 1] : fallback; };
const require = createRequire(resolve(process.env.CTLIFE_BACKEND_DIR ?? '../ct-life-backend', 'package.json'));
const { Client } = require('@colyseus/sdk');
const { Keypair } = require('@solana/web3.js');
const api = arg('api', 'http://127.0.0.1:4012'), ws = arg('ws', 'ws://127.0.0.1:2569');
const out = arg('out', '/tmp/ctlife-island-observer.json'), district = arg('district', 'timeline');
const venueId = arg('venue', null);
const seconds = Number(arg('seconds', '900')), x = Number(arg('x', '1')), z = Number(arg('z', '-12'));
const status = { startedAt: new Date().toISOString(), phase: 'authenticating', district, venueId, coverage: { live: false }, peers: {}, venuePeers: {}, vehicles: {}, rejected: [], errors: [], events: [] };
const rooms = [], people = new Map(), cars = new Map(), venueSessions = new Map();
const authRequest = new AbortController();
let timer, finished = false;
const save = () => { writeFileSync(out + '.tmp', JSON.stringify(status, null, 2)); renameSync(out + '.tmp', out); };
const event = (type, fields) => status.events.push({ at: new Date().toISOString(), type, ...fields });
function alias(map, id, prefix) { if (!map.has(id)) map.set(id, prefix + (map.size + 1)); return map.get(id); }
function ensureRunning() { if (finished) throw new Error('observer stopped'); }
function retire(room) {
  // leave() can wait indefinitely after transport loss. Close the observer's transport directly.
  room.reconnection.enabled = false;
  try { room.connection?.close(); } catch { /* Already closed; never emit SDK error text. */ }
}
async function finish(reason) {
  if (finished) return;
  finished = true; clearInterval(timer); authRequest.abort();
  status.phase = reason; status.finishedAt = new Date().toISOString();
  // Presence fields remain the last observed state, not a claim about peers after coverage ends.
  status.coverage = { ...status.coverage, live: false, endedAt: status.finishedAt, reason };
  save(); rooms.forEach(retire);
}
function loseCoverage(label, type, code) {
  if (finished) return;
  event(type, { room: label, code: Number.isFinite(code) ? code : null });
  void finish('coverage_lost');
}
async function joinRoom(client, label, options) {
  ensureRunning();
  const room = await client.joinOrCreate(label, options);
  room.reconnection.enabled = false; // A gap ends this receipt; restart explicitly for fresh coverage.
  if (finished) { retire(room); ensureRunning(); }
  rooms.push(room);
  room.onMessage('*', () => {});
  room.onMessage('rejected', m => { if (!finished) status.rejected.push({ type: m.type, code: m.code }); });
  room.onDrop(code => loseCoverage(label, 'observer_drop', code));
  room.onReconnect(() => {
    if (finished) retire(room);
    else loseCoverage(label, 'observer_reconnect');
  });
  room.onLeave(code => loseCoverage(label, 'observer_leave', code));
  room.onError(code => { if (!finished) status.errors.push({ room: label, code: Number.isFinite(code) ? code : null }); });
  return room;
}
process.once('SIGINT', () => finish('stopped')); process.once('SIGTERM', () => finish('stopped'));
try {
  save();
  const response = await fetch(api + '/v1/auth/dev', { signal: authRequest.signal, method: 'POST', headers: { 'content-type': 'application/json', 'idempotency-key': randomUUID() }, body: JSON.stringify({ wallet: Keypair.generate().publicKey.toBase58() }) });
  ensureRunning();
  if (response.status !== 200) throw new Error('dev auth HTTP ' + response.status);
  const auth = await response.json(), client = new Client(ws);
  ensureRunning();
  const street = await joinRoom(client, 'street', { token: auth.accessToken, district });
  const vehicles = await joinRoom(client, 'vehicles', { token: auth.accessToken });
  const venue = venueId ? await joinRoom(client, 'venue', { token: auth.accessToken, venueId }) : null;
  ensureRunning();
  const started = Date.now(); status.phase = 'observing'; status.coverage = { live: true, startedAt: new Date().toISOString() }; save(); report('ISLAND_OBSERVER_READY district=' + district);
  timer = setInterval(() => {
    if (finished) return;
    const closed = rooms.findIndex(room => room.connection?.isOpen !== true);
    if (closed >= 0) { loseCoverage(['street', 'vehicles', 'venue'][closed], 'observer_connection_closed'); return; }
    street.send('move', { x, z, rotY: 0 });
    const seenPeers = new Set(), seenCars = new Set(), seenVenuePeers = new Set();
    street.state?.players?.forEach((p, key) => {
      if (key === street.sessionId || p.userId === auth.user.id) return;
      const id = alias(people, p.userId, 'peer'), previous = status.peers[id];
      seenPeers.add(id);
      if (!previous?.present) event('peer_present', { peer: id });
      status.peers[id] = { present: true, x: p.x, z: p.z, yaw: p.rotY, samples: (previous?.samples ?? 0) + 1,
        movements: (previous?.movements ?? 0) + (previous && (previous.x !== p.x || previous.z !== p.z) ? 1 : 0), lastSeenAt: new Date().toISOString() };
    });
    venue?.state?.players?.forEach((p, key) => {
      if (key === venue.sessionId || p.userId === auth.user.id) return;
      const id = alias(people, p.userId, 'peer'), previous = status.venuePeers[id];
      const changed = venueSessions.has(id) && venueSessions.get(id) !== key;
      venueSessions.set(id, key); // Session IDs stay in memory; only a change count is persisted.
      seenVenuePeers.add(id);
      if (!previous?.present) event('venue_peer_present', { peer: id });
      if (changed) event('venue_session_changed', { peer: id });
      status.venuePeers[id] = { present: true, x: p.x, z: p.z, ix: p.ix, iz: p.iz,
        samples: (previous?.samples ?? 0) + 1, sessionChanges: (previous?.sessionChanges ?? 0) + (changed ? 1 : 0),
        movements: (previous?.movements ?? 0) + (previous && (previous.x !== p.x || previous.z !== p.z) ? 1 : 0), lastSeenAt: new Date().toISOString() };
    });
    vehicles.state?.vehicles?.forEach(v => {
      const id = alias(cars, v.id, 'vehicle'), previous = status.vehicles[id];
      const driver = v.driver ? alias(people, v.driver, 'peer') : null;
      seenCars.add(id);
      if (!previous?.present) event('vehicle_present', { vehicle: id, kind: v.kind });
      if (driver !== (previous?.driver ?? null)) event('driver_changed', { vehicle: id, from: previous?.driver ?? null, to: driver,
        x: v.x, z: v.z, speed: v.speed, previousSpeed: previous?.speed ?? null });
      status.vehicles[id] = { present: true, kind: v.kind, driver, x: v.x, y: v.y, z: v.z, speed: v.speed, samples: (previous?.samples ?? 0) + 1,
        maxSpeed: Math.max(previous?.maxSpeed ?? 0, Math.abs(v.speed)), movingSamples: (previous?.movingSamples ?? 0) + (Math.abs(v.speed) > 0.5 ? 1 : 0),
        minX: Math.min(previous?.minX ?? v.x, v.x), maxX: Math.max(previous?.maxX ?? v.x, v.x),
        minZ: Math.min(previous?.minZ ?? v.z, v.z), maxZ: Math.max(previous?.maxZ ?? v.z, v.z),
        movements: (previous?.movements ?? 0) + (previous && (previous.x !== v.x || previous.z !== v.z) ? 1 : 0),
        claims: (previous?.claims ?? 0) + (driver && driver !== previous?.driver ? 1 : 0),
        exits: (previous?.exits ?? 0) + (!driver && previous?.driver ? 1 : 0), lastSeenAt: new Date().toISOString() };
    });
    for (const [id, peer] of Object.entries(status.peers)) if (peer.present && !seenPeers.has(id)) {
      peer.present = false; event('peer_absent', { peer: id });
    }
    for (const [id, peer] of Object.entries(status.venuePeers)) if (peer.present && !seenVenuePeers.has(id)) {
      peer.present = false; event('venue_peer_absent', { peer: id });
    }
    for (const [id, car] of Object.entries(status.vehicles)) if (car.present && !seenCars.has(id)) {
      car.present = false; event('vehicle_absent', { vehicle: id });
    }
    save(); if (Date.now() - started >= seconds * 1000) void finish('complete');
  }, 200);
} catch (error) {
  if (!finished) {
    status.errors.push({ phase: status.phase, errorType: error?.name ?? 'unknown' }); process.exitCode = 1;
    await finish('failed'); report('ISLAND_OBSERVER_FAILED (details redacted)');
  }
}
