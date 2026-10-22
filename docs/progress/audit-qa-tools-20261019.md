# Seeker QA tool lifecycle review — 19 October 2026

The final host-only review found and reproduced three defects in the audit tools. The launcher could continue after a failed adb reverse mapping. The observer could count cached room state after its own connection dropped. A stop signal during authentication or joining could leave late startup work running. These are QA-tool defects, separate from the game repairs and unexplained device input.

## Corrections

- `tools/integration/seeker_launch.sh` validates backend ports before any adb call, retains the isolated API/realtime overrides, and exits on failed setup or forwarding before launching CT Life. A failed mapping may leave an older mapping in place; the app is not launched by that failed invocation.
- `tools/integration/island-observer.mjs` records an explicit coverage interval and ends the receipt on its first own drop, leave, unexpected reconnect or closed connection. It disables automatic reconnect and stops sampling cached state. Presence fields after coverage ends are the last observed values, not current presence claims.
- Authentication is aborted on stop; startup checks its stopped state across awaited boundaries. A late successful room join is immediately retired and cannot start another join or sampling timer. SDK diagnostics remain suppressed because they can contain token-bearing URLs; persisted reports use pseudonymous peers and error codes/types without raw error messages.

This observer deliberately does not merge samples across its own connection gaps. Start a new receipt for new coverage. Pending SDK joining exposes no cancellation handle until it settles; retirement happens on late success. Direct transport closure does not send consented `LEAVE_ROOM`, so server presence may remain until normal disconnect/lease cleanup. These limits are not device or server validation claims.

## Source verification and tests

The installed backend dependency is `@colyseus/sdk` **0.18.5**, inspected locally under `ct-life-backend-audit20261019/node_modules/@colyseus/sdk/src/`: `Room.ts` exposes reconnection configuration at57 and checks enabled at745–749; drop handling at156–176 precedes reconnection. `Connection.ts:88–94` exposes direct close/isOpen. `Room.ts:201–216` shows leave waiting for onLeave. Its state getter at527 and `serializer/SchemaSerializer.ts:45–54` show that teardown clears decoder references while retaining the state object. No dependency was added or upgraded.

The harness loads the actual launcher and observer source. It substitutes adb/openssl, fetch, SDK objects, timers and signal delivery; no real device, network, service or Unity invocation occurs. It checks valid custom-port launch, invalid ports, failed forwarding, ordinary observer cleanup, dropped/left/silently closed transport, and stopping during pending auth or join.

- Baseline at18:47:03UTC: **13 cases, 2 passed, 11 failed**. Normal launch and normal observer sampling/stop passed; the failure-path assertions failed.
- Corrected source at18:47:58UTC: **13/13 passed**.
- Shell/JavaScript syntax and whitespace checks passed. This is host mock validation, not another live socket/device acceptance run.
- An initial staged whitespace check also reported CRLF CSV lines. The coverage exporter now emits LF; all38 parsed affected-parking rows were unchanged after normalization. Six proposal JSON files and the SVG also parse successfully.

Sanitized results and a source/hash receipt are in external `ct-life-evidence/audit-20261019/qa-tools/`. The harness and baseline source copies are retained privately at `/Users/mac/.ctlife-local/qa-tool-lifecycle-review-20261019/`.

| Frozen file | SHA256 |
| --- | --- |
| `tools/integration/island-observer.mjs` | `438766412021c01e5ac034ff748d153017760ba4c840d19c81e239a1cd6203fe` |
| `tools/integration/seeker_launch.sh` | `49d343ca8408640ba9ba43fc65029024404337115b4988cb63fa85d1dab47087` |

The completed entry observer receipt remains unchanged:16:56:14.625–17:26:14.881UTC, phase complete, zero errors/rejections and zero observer drop/leave events. No evidence in that receipt shows the disconnected-observer path occurring. The installed APK and its2,067-test native checkpoint are unchanged by this tool review. For device state and unfinished gameplay acceptance, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md).
