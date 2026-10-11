// Headless second client for CT Life integration runs (editor or Seeker + this bot).
// Signs in with dev auth, rolls the lottery, creates a character, joins a venue, walks a small loop,
// emotes and chats, and records what it sees of every other player into a JSON status file.
//
//   node tools/integration/observer-bot.mjs --out /tmp/bot.json [--venue timeline_square] [--seconds 300]
//        [--api http://localhost:4010] [--ws ws://localhost:2567] [--name obsbot]
//
// Uses the backend checkout's own pinned @colyseus/sdk and @solana/web3.js (no extra installs):
// CTLIFE_BACKEND_DIR (default ../ct-life-backend next to this repo).
import { createRequire } from 'node:module';
import { writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const backend = process.env.CTLIFE_BACKEND_DIR ?? resolve(here, '../../../ct-life-backend');
const req = createRequire(resolve(backend, 'package.json'));
const { Client } = req('@colyseus/sdk');
const { Keypair } = req('@solana/web3.js');

const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i >= 0 ? process.argv[i + 1] : d; };
const api = arg('api', 'http://localhost:4010');
const ws = arg('ws', 'ws://localhost:2567');
const venueId = arg('venue', 'timeline_square');
const seconds = Number(arg('seconds', '300'));
const out = arg('out', resolve(here, 'bot-status.json'));
const name = arg('name', 'obsbot' + Math.floor(Math.random() * 1e4));

const status = { startedAt: new Date().toISOString(), name, venueId, phase: 'starting', userId: null, wallet: null,
  sessionId: null, roomId: null, roomVenueId: null, players: {}, npcs: 0, said: [], rejected: [], errors: [], sent: { moves: 0, emotes: 0, says: 0 } };
const save = () => { try { writeFileSync(out, JSON.stringify(status, null, 1)); } catch (e) { console.error(e); } };
const idem = () => crypto.randomUUID();

async function post(path, body, token) {
  const r = await fetch(api + '/v1' + path, { method: 'POST', headers: { 'content-type': 'application/json', 'idempotency-key': idem(),
    ...(token ? { authorization: 'Bearer ' + token } : {}) }, body: JSON.stringify(body ?? {}) });
  const text = await r.text();
  let json = null; try { json = JSON.parse(text); } catch { /* not json */ }
  return { status: r.status, json };
}

async function main() {
  const wallet = Keypair.generate().publicKey.toBase58();
  const auth = await post('/auth/dev', { wallet });
  if (auth.status !== 200) throw new Error('dev auth ' + auth.status);
  const token = auth.json.accessToken;
  status.userId = auth.json.id ?? auth.json.user?.id; status.wallet = wallet; status.phase = 'signed-in'; save();
  // Contract order: lottery first (backend main treats an existing character as rolled).
  const roll = await post('/lottery/roll', {}, token);
  status.lottery = roll.json; 
  const look = { skinTone: 'skin_tone3', headId: 'head_rain', hairId: 'hair_rain_ponytail', topId: 'top_hoodie_black',
    bottomId: 'bottom_jeans_indigo', shoesId: 'shoes_sneakers_white', accId: 'acc_none' };
  const ch = await post('/characters', { alias: name, look, traitIds: ['fast_talker', 'iron_will'], dreamId: 'make_10k', tribeId: 'trencher', ageConfirmed: true }, token);
  status.character = ch.status; save();

  const client = new Client(ws);
  const room = await client.joinOrCreate('venue', { token, venueId, name, tribeId: 'trencher', look: Object.values(look) });
  status.sessionId = room.sessionId; status.roomId = room.roomId; status.phase = 'joined'; save();
  room.onMessage('welcome', () => {});
  room.onMessage('said', (m) => { status.said.push({ ...m, receivedAt: Date.now() }); save(); });
  room.onMessage('rejected', (m) => { status.rejected.push({ ...m, at: Date.now() }); });
  room.onMessage('toast', () => {});
  room.onMessage('*', () => {});
  room.onLeave((code) => { status.phase = 'left:' + code; save(); });

  const t0 = Date.now();
  let i = 0;
  const cx = 2, cz = 2;
  const timer = setInterval(() => {
    const st = room.state;
    if (st) {
      status.roomVenueId = st.venueId;
      const seen = {};
      st.players?.forEach((p, sid) => {
        if (sid === room.sessionId) return;
        const prev = status.players[sid];
        seen[sid] = { userId: p.userId, name: p.name, x: p.x, z: p.z, anim: p.anim, emoteId: p.emoteId, emoteAt: p.emoteAt,
          moves: (prev?.moves ?? 0) + (prev && (prev.x !== p.x || prev.z !== p.z) ? 1 : 0),
          emotesSeen: [...new Set([...(prev?.emotesSeen ?? []), ...(p.emoteId ? [p.emoteId] : [])])] };
      });
      status.players = seen;
      let n = 0; st.npcs?.forEach(() => n++); status.npcs = n;
    }
    // Walk a 1 m circle at ~0.6 m/s (well under the 6 m/s cap), 5 moves per second.
    const a = (i++ / 25) * Math.PI * 2;
    room.send('move', { x: cx + Math.cos(a), z: cz + Math.sin(a), rotY: a }); status.sent.moves++;
    if (i % 25 === 0) { room.send('emote', { emoteId: 'emote_wave' }); status.sent.emotes++; }
    if (i % 35 === 0) { room.send('say', { text: 'bot ping ' + i }); status.sent.says++; }
    save();
    if (Date.now() - t0 > seconds * 1000) { clearInterval(timer); room.leave().finally(() => { status.phase = 'done'; save(); process.exit(0); }); }
  }, 200);
}

main().catch((e) => { status.errors.push(String(e?.stack ?? e)); status.phase = 'failed'; save(); console.error(e); process.exit(1); });
