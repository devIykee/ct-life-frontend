# Proposal: v3 vehicle kinds (frontend, 2026-10-22)

The client's v3 vehicle set adds three road cars that the realtime contract does not name yet:
`sedan`, `taxi` and `police`. Later drops add `pickup`, `van` and `bike`.

## Request

Add these values to `VEHICLE_KINDS` in `contract/realtime/messages.ts`, with the same public lease rules and speed caps as `compact`:

- `sedan`, `taxi`, `police`, `pickup`, `van`: 60 m/s
- `bike`: 25 m/s, the same as `moped`

## Until then

`IslandProtocol.WireKind` sends `sedan`, `taxi` and `police` as `compact`. The server's lease and speed cap are therefore
unchanged, but other players see a compact in that bay instead of the actual model. The `roadstar` wire kind now carries
the v3 hovercar, which is the player's owned flying car. Its kind and speed cap are unchanged.
