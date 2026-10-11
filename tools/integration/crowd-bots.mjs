// N headless players in one venue for crowd/perf tests: dev auth, lottery, character, join, wander, emote, chat.
//   node tools/integration/crowd-bots.mjs --bots 30 --seconds 600 [--venue timeline_square]
import { createRequire } from 'node:module';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const here = dirname(fileURLToPath(import.meta.url));
const req = createRequire(resolve(process.env.CTLIFE_BACKEND_DIR ?? resolve(here, '../../../ct-life-backend'), 'package.json'));
const { Client } = req('@colyseus/sdk'); const { Keypair } = req('@solana/web3.js');
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i >= 0 ? process.argv[i + 1] : d; };
const api = arg('api', 'http://localhost:4010'), ws = arg('ws', 'ws://localhost:2567'), venueId = arg('venue', 'timeline_square');
const n = Number(arg('bots', '30')), seconds = Number(arg('seconds', '600'));
const post = async (p, b, t) => (await fetch(api + '/v1' + p, { method: 'POST', headers: { 'content-type': 'application/json', 'idempotency-key': crypto.randomUUID(), ...(t ? { authorization: 'Bearer ' + t } : {}) }, body: JSON.stringify(b ?? {}) })).json().catch(() => ({}));
async function bot(i) {
  const a = await post('/auth/dev', { wallet: Keypair.generate().publicKey.toBase58() });
  await post('/lottery/roll', {}, a.accessToken);
  await post('/characters', { alias: 'crowd' + i + '_' + Math.floor(Math.random() * 1e4), look: { skinTone: 'skin_tone3', headId: 'h', hairId: 'h', topId: 't', bottomId: 'b', shoesId: 's', accId: 'acc_none' }, traitIds: ['fast_talker', 'iron_will'], dreamId: 'make_10k', tribeId: 'trencher', ageConfirmed: true }, a.accessToken);
  const room = await new Client(ws).joinOrCreate('venue', { token: a.accessToken, venueId, name: 'crowd' + i });
  room.onMessage('*', () => {});
  let x = (Math.random() * 24) - 12, z = (Math.random() * 20) - 10, k = 0;
  const t = setInterval(() => {
    x = Math.max(-18, Math.min(18, x + (Math.random() - 0.5) * 0.6)); z = Math.max(-13, Math.min(13, z + (Math.random() - 0.5) * 0.6));
    room.send('move', { x, z, rotY: Math.random() * 6.28 });
    if (++k % 40 === i % 40) room.send('emote', { emoteId: 'emote_wave' });
  }, 250);
  return { room, t };
}
const bots = [];
for (let i = 0; i < n; i++) { try { bots.push(await bot(i)); } catch (e) { console.error('bot', i, e.message); } }
console.log(`[crowd] ${bots.length}/${n} bots in ${venueId}`);
setTimeout(async () => { for (const b of bots) { clearInterval(b.t); await b.room.leave().catch(() => {}); } process.exit(0); }, seconds * 1000);
