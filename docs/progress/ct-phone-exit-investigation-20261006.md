# Seeker app-close investigation — 6 October 2026

Captured before installing or restarting CT Life. Installed package is 0.1.0, versionCode 1,
last updated 10:21:35 device time. Original PID 21292 was alive since 10:31:11; its task was
paused in the background with no review extras shown in its task Intent.

## Android system evidence

The retained 16 ApplicationExitInfo entries contain nine EXIT_SELF/status 0, five
USER REQUESTED/FORCE STOP, and two PACKAGE UPDATED exits. No retained CRASH, CRASH_NATIVE,
ANR or LOW_MEMORY entry appears. This is a bounded retained history, not proof that the
app cannot crash. Full package-specific receipt:
`screens/stability-20261006/seeker-exit-info-before-changes.txt`.

The EXIT_SELF at 10:21:57.634 matches the archived M5 capture's explicit `Quit requested`
following capture completion at 10:21:56.732. The previous test runner also intentionally
force-stops CT Life. Those recorded exits are test behavior, not a native crash diagnosis.

## Normal resume

Started the MAIN/LAUNCHER activity with no extras. Android reported HOT / Status ok, existing
task brought forward in 125 ms. PID remained 21292. Subsequent Unity telemetry rendered
frames (59.6 and 60.1 average FPS in two five-second windows, zero registered avatars).
These are sign-in/UI workload observations, not city performance acceptance.

Resume emitted Mali gralloc unsupported-format messages (0x38/0x3b). They did not terminate
the process in this observation; their underlying driver cause has not been diagnosed.
The earlier background memory snapshot was 484459 KB total PSS including 294221 KB swap
PSS and 189058 KB graphics; it is not an active city memory or leak measurement.

## Normal cold launch

After preserving the earlier history, root deliberately force-stopped the old process and
launched MAIN/LAUNCHER with no extras. Android reported COLD / Status ok, 606 ms activity
launch (not full game-ready time). New PID 8384 stayed alive through more than 40 seconds
of observed foreground rendering. No fatal exception, fatal signal or Quit requested line
was found in that PID capture. Config refresh could not reach its backend and used the
local snapshot; this was a warning, not a process exit.
Receipt: `screens/stability-20261006/seeker-normal-cold-start.txt`.

Next: foreground/background survival and the updated landscape build's actual city
workload, followed by fresh exit history. Keep Editor package/reload failures separate
from Android process termination unless a trace connects them.

Android's official [ApplicationExitInfo reference](https://developer.android.com/reference/android/app/ApplicationExitInfo)
defines the recorded exit reasons.

## User clarification and review workflow

The user confirmed the closure happened **after automated previews/screenshots**. This
matches the archived explicit screenshot review exit and Android EXIT_SELF record.
The reported phone closure is therefore accounted for by review automation. This does not
claim that every possible runtime failure has been tested.

The interactive performance runner now leaves CT Life open by default; `--stop-after` is
the explicit unattended-cleanup opt-in. Its initial force-stop still creates a fresh test
process. Phone screenshot runs should omit `reviewExit`; Editor capture jobs retain that
explicit flag so their automation process completes. CLI help parses and documents the
new option; its next real device run will verify the end behavior.
