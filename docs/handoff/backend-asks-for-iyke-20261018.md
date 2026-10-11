CT Life backend asks, round 2 (from Dominion's frontend, 18 Oct). Checked against main eeef49d.

Thanks for eeef49d: repay, debt due, ledger, perp PnL and live DMs are all great. The app is wiring them in now. (Skipping prediction markets and the rent toast makes sense.)

What changed on our side: the city is now ONE connected island (CT Island v2, about 1.6 x 1.2 km, island metres, +X east, +Z north, origin at the downtown plaza). Players walk and drive between venues with no loading screen, and there are vehicles: cars, scooter, helicopter, drone and a flying "Roadstar". These asks make multiplayer work in that world.

A. Venue rooms (small fixes in server/realtime/rooms/venue.ts)
1. Join placement: onJoin finds the ".entrance" spot but then puts the player at walkable[0].x0/z0, the rect's corner. Please use the entrance spot's own x/z.
2. Move speed check: handleMove measures speed between message ARRIVALS and rejects anything over 6 m/s, and drops messages under 90 ms apart. Network jitter or one dropped message makes an honest sprint (6 m/s) fail, and that player freezes for everyone. Suggested fix: allow distance since the last accepted move of up to 6.5 m/s * elapsed + 0.5 m slack, and clamp instead of rejecting (or send back the corrected position).
3. Two rooms at once: when a player walks from one venue into the next, the client joins the new venue room about 1 s before leaving the old one, so nobody pops. It works locally; please confirm this is allowed and won't count as a duplicate session.

B. Street rooms (new; people walking between venues)
4. Add a "street" room type, one per district: timeline, trenches, builder_hills, airdrop_farm, dubai_marina, residences, beachfront, mountain. It uses the same PlayerState schema and the same move/say/emote messages as venue rooms. Moves are in island metres. Bounds: the district polygon inside the island outline. I'll send you a JSON with exact polygons; don't hard-code numbers from this message. Same speed rule as A2. Until this exists, players on the streets can't see each other.
5. Optional: let venue rooms also accept island coordinates, so the client doesn't convert. Low priority.

C. Vehicles (new)
6. Room state gets a "vehicles" map: { id, kind, driver, x, y, z, yaw, pitch, roll, speed, flags }. kind is compact | suv | sports | moped | roadstar | heli | drone.
7. Messages:
   - vehicle_enter { id }: the server checks the vehicle is free and within 4 m of the player.
   - vehicle_move { id, x, y, z, yaw, pitch, roll, speed }: from the driver at 10 Hz.
   - vehicle_exit { id, x, z, yaw }.
   Server checks: speed caps per kind (cars 60 m/s, moped 25, roadstar 70, heli 60, drone 20), height up to 214 m, island bounds, one driver per vehicle.
8. After vehicle_exit, accept the first player move at the vehicle's last position, without applying the walking speed rule.
9. At 150 km/h a car crosses a district in about 3.5 s. Rather than handing off rooms every few seconds, suggest ONE island-wide vehicle room that only sends each client the vehicles near them (interest radius about 250 m).
10. Hits: vehicle_hit { victim, velocity }, relayed only to the victim, who ragdolls on their own client. The server checks proximity and speed, and rate-limits it.
11. Optional: the server owns the parked cars per district, built from our parking bays (we can send the bay list). Today each client parks its own.

None of this blocks the demo video: the app runs vehicles locally until these exist.

Pending for the next message to Iyke (not sent yet):
- debtWeeklyDueCents stays at the full weekly amount after the player pays it that week (nothing tracks this week's paid amount), so the app keeps saying "$120 due today" after a $120 repay.
- openapi: a stray `description` under /payments/intents/{id} fails strict spec validation.
- openapi: the debtDueAt description is unquoted flow YAML and gets cut at ", Sun 20:00 UTC)".
- ids/archetypes.json 1.0.2 removed spaces_host and added barista/patron with no lines.
