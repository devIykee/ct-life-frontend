// Local audit only. Raw TCP forwarding preserves Colyseus HTTP + WebSocket traffic without inspecting it.
// Control: POST http://127.0.0.1:2571/cut {"durationMs":2000}; GET /status; POST /resume.
// Every cut destroys BOTH sockets of every active pair and rejects new sockets during the outage.
// Receipts contain counts/times only: never request paths, headers, bytes, tokens or peer addresses.
import net from 'node:net';
import http from 'node:http';
import { writeFileSync, renameSync } from 'node:fs';

const arg = (key, fallback) => { const i = process.argv.indexOf('--' + key); return i < 0 ? fallback : process.argv[i + 1]; };
const port = Number(arg('port', '2570')), targetPort = Number(arg('target-port', '2569'));
const controlPort = Number(arg('control-port', '2571')), out = arg('out', '/tmp/ctlife-fault-proxy.json');
for (const n of [port, targetPort, controlPort]) if (!Number.isInteger(n) || n < 1024 || n > 65535) throw new Error('Invalid port');
if (new Set([port, targetPort, controlPort]).size !== 3) throw new Error('Ports must differ');
const host = '127.0.0.1', pairs = new Set();
const state = { startedAt: new Date().toISOString(), phase: 'starting', port, targetPort, controlPort,
  accepted: 0, refused: 0, cuts: 0, blockedUntil: 0, events: [] };
let closing = false;
const snapshot = () => ({ ...state, activePairs: pairs.size, remainingBlockedMs: Math.max(0, state.blockedUntil - Date.now()) });
const save = () => { writeFileSync(out + '.tmp', JSON.stringify(snapshot(), null, 2) + '\n'); renameSync(out + '.tmp', out); };
function record(type, detail = {}) { state.events.push({ at: new Date().toISOString(), type, ...detail }); save(); }
function destroy(socket) {
  if (!socket || socket.destroyed) return;
  try { socket.resetAndDestroy(); } catch { socket.destroy(); }
}
function closePair(pair, reason) {
  if (pair.closed) return;
  pair.closed = true; pairs.delete(pair); destroy(pair.downstream); destroy(pair.upstream);
  record('pair_closed', { pair: pair.id, reason, downstreamDestroyed: pair.downstream.destroyed, upstreamDestroyed: pair.upstream.destroyed });
}
const proxy = net.createServer(downstream => {
  if (closing || Date.now() < state.blockedUntil) {
    state.refused++; downstream.on('error', () => {}); destroy(downstream); record('connection_refused'); return;
  }
  downstream.pause(); downstream.setNoDelay(true);
  const upstream = net.createConnection({ host, port: targetPort }); upstream.setNoDelay(true);
  const pair = { id: ++state.accepted, downstream, upstream, closed: false }; pairs.add(pair);
  record('pair_opened', { pair: pair.id });
  downstream.on('error', () => closePair(pair, 'downstream_error'));
  upstream.on('error', () => closePair(pair, 'upstream_error'));
  downstream.on('close', () => closePair(pair, 'downstream_closed'));
  upstream.on('close', () => closePair(pair, 'upstream_closed'));
  upstream.on('connect', () => {
    if (pair.closed || closing || Date.now() < state.blockedUntil) { closePair(pair, 'outage_during_connect'); return; }
    downstream.pipe(upstream); upstream.pipe(downstream); downstream.resume();
  });
});
const control = http.createServer(async (req, res) => {
  const reply = (code, body) => { res.writeHead(code, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); };
  if (req.method === 'GET' && req.url === '/status') return reply(200, snapshot());
  if (req.method !== 'POST' || !['/cut', '/resume'].includes(req.url)) return reply(404, { error: 'NOT_FOUND' });
  let raw = '';
  for await (const chunk of req) { raw += chunk; if (raw.length > 512) return reply(413, { error: 'BODY_TOO_LARGE' }); }
  let body;
  try { body = raw ? JSON.parse(raw) : {}; } catch { return reply(400, { error: 'INVALID_JSON' }); }
  if (req.url === '/resume') { state.blockedUntil = 0; record('resumed'); return reply(200, snapshot()); }
  const durationMs = body.durationMs;
  if (!Number.isInteger(durationMs) || durationMs < 100 || durationMs > 120000) return reply(400, { error: 'DURATION_100_TO_120000_REQUIRED' });
  state.cuts++; state.blockedUntil = Date.now() + durationMs;
  const cutPairs = [...pairs];
  for (const pair of cutPairs) closePair(pair, 'injected_cut');
  record('cut', { durationMs, cutPairs: cutPairs.length });
  reply(200, { ...snapshot(), cutPairs: cutPairs.length });
});
async function stop() {
  if (closing) return; closing = true;
  for (const pair of [...pairs]) closePair(pair, 'proxy_shutdown');
  await Promise.all([new Promise(r => proxy.close(r)), new Promise(r => control.close(r))]);
  state.phase = 'stopped'; record('stopped');
}
process.once('SIGTERM', stop); process.once('SIGINT', stop);
function failed() { state.phase = 'failed'; record('listener_error'); void stop(); process.exitCode = 1; }
proxy.on('error', failed); control.on('error', failed);
await new Promise((resolve, reject) => { proxy.once('error', reject); proxy.listen(port, host, resolve); });
await new Promise((resolve, reject) => { control.once('error', reject); control.listen(controlPort, host, resolve); });
state.phase = 'listening'; record('ready');
console.log('REALTIME_FAULT_PROXY_READY port=' + port + ' target=' + targetPort + ' control=' + controlPort);
