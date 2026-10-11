#!/usr/bin/env python3
"""Measure the development-only, offline visual review with explicit warm-up and evidence.

No install unless --install is supplied. Leaves CT Life open after the review.
Use --stop-after for unattended runs that require explicit shutdown.
Example: python3 client/Tools~/seeker_review.py --seconds 180 --crowd 62 --output /tmp/ct-perf
"""
import argparse
import json
import math
import os
from pathlib import Path
import re
import subprocess
import time


def thermal_status(dump):
    """Use Android's aggregate status, not a chosen sensor's local status."""
    match = re.search(r'^Thermal Status:\s*([0-6])\s*$', dump, re.MULTILINE)
    if match is None:
        raise RuntimeError('No aggregate Android thermal status; workload was not authorised by preflight')
    return int(match.group(1))


def require_cool_start(dump):
    status = thermal_status(dump)
    if status >= 3:
        raise RuntimeError(f'Device already thermally throttled (Android status {status}); '
                           'let it cool before starting this workload')
    return status


def validate_steady_windows(steady, expected_avatars, requested_seconds):
    """Reject partial runs and workloads that differ from the authored-crowd request."""
    if len(steady) < 2:
        raise RuntimeError('Insufficient complete windows after warm-up')
    expected = {'avatars': expected_avatars, 'animatorAvatars': expected_avatars,
                'proceduralAvatars': 0, 'droppedSamples': 0}
    for index, row in enumerate(steady):
        for key in ('frames', 'sampleSeconds', 'p1', 'p99ms', 'min'):
            try:
                value = float(row[key])
            except (KeyError, ValueError) as error:
                raise RuntimeError(f'Invalid timing window {index}: {key}') from error
            if not math.isfinite(value) or value <= 0:
                raise RuntimeError(f'Invalid timing window {index}: {key}={value}')
        for key, wanted in expected.items():
            try:
                actual = int(row[key])
            except (KeyError, ValueError) as error:
                raise RuntimeError(f'Missing/invalid workload evidence in window {index}: {key}') from error
            if actual != wanted:
                raise RuntimeError(f'Unexpected workload in window {index}: {key}={actual}, expected {wanted}')
    seconds = sum(float(row['sampleSeconds']) for row in steady)
    # Two discarded 5-second windows + one unfinished trailing window + 5 seconds
    # for startup/log attachment. At 180 seconds this requires at least 160 sampled
    # seconds; it does not relabel the excluded interval as measured performance.
    allowance = 20.0
    minimum = max(10.0, requested_seconds - allowance)
    if seconds < minimum:
        raise RuntimeError(f'Incomplete performance coverage: {seconds:.3f}s sampled, '
                           f'at least {minimum:.3f}s required for {requested_seconds}s request')
    return seconds, minimum, allowance


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--adb', default='/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb')
    parser.add_argument('--serial', default=os.environ.get('SEEKER_SERIAL', 'SM02G40619141343'))
    parser.add_argument('--install', type=Path)
    parser.add_argument('--seconds', type=int, default=180)
    parser.add_argument('--crowd', type=int, choices=range(1, 63), default=62)
    parser.add_argument('--look', choices=('creator.look.everyday', 'creator.look.breeze'), default='creator.look.everyday')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--stop-after', action='store_true',
                        help='Force-stop CT Life after the run; default leaves the review open')
    args = parser.parse_args()
    if args.seconds < 30:
        parser.error('--seconds must be at least 30; warm-up windows are excluded')
    args.output.mkdir(parents=True, exist_ok=True)
    base = [args.adb, '-s', args.serial]

    def adb(*command, binary=False):
        return subprocess.check_output(base + list(command), text=not binary, timeout=45)

    def evidence(name, *command):
        value = adb(*command)
        (args.output / name).write_text(value)
        return value

    # Read before install, launch or force-stop. A warm device cannot establish a
    # repeatable baseline. Status >= 3 is Android SEVERE, not normal game load.
    evidence('battery-before.txt', 'shell', 'dumpsys', 'battery')
    require_cool_start(evidence('thermal-before.txt', 'shell', 'dumpsys', 'thermalservice'))
    if args.install:
        print(adb('install', '-r', str(args.install.resolve())), flush=True)
    adb('shell', 'am', 'force-stop', 'com.ctlife.app')
    require_cool_start(evidence('thermal-before-launch.txt', 'shell', 'dumpsys', 'thermalservice'))
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    log_process = None
    paused_for_cooling = False
    try:
        print(adb('shell', 'am', 'start', '-W', '-n',
                  'com.ctlife.app/com.unity3d.player.UnityPlayerGameActivity',
                  '-e', 'review', '1', '-e', 'reviewScreen', 'city',
                  '-e', 'reviewCrowd', str(args.crowd), '-e', 'reviewLook', args.look), flush=True)
        pid = adb('shell', 'pidof', 'com.ctlife.app').strip()
        with (args.output / 'device.log').open('w') as stream:
            log_process = subprocess.Popen(base + ['logcat', '--pid=' + pid, '-v', 'time'], stdout=stream)
            start = time.monotonic()
            next_thermal_poll = start
            while time.monotonic() - start < args.seconds:
                time.sleep(2)
                if log_process.poll() is not None:
                    raise RuntimeError('Device log stream ended unexpectedly')
                if time.monotonic() >= next_thermal_poll:
                    elapsed = time.monotonic() - start
                    dump = adb('shell', 'dumpsys', 'thermalservice')
                    status = thermal_status(dump)
                    with (args.output / 'thermal-during.jsonl').open('a') as thermal_log:
                        thermal_log.write(json.dumps({'elapsedSeconds': round(elapsed, 3),
                                                      'androidStatus': status}) + '\n')
                    next_thermal_poll = time.monotonic() + 5
                    if status >= 4:
                        (args.output / 'thermal-abort.txt').write_text(dump)
                        adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
                        paused_for_cooling = True
                        raise RuntimeError(f'Thermal stop: Android status {status}; '
                                           'app backgrounded for cooling; no performance pass')
            # Device inspection/readback is outside the measured interval.
            log_process.terminate()
            log_process.wait(timeout=10)
            log_process = None
        if adb('shell', 'pidof', 'com.ctlife.app').strip() != pid:
            raise RuntimeError('Review application stopped or restarted during the measurement')
        evidence('memory-after.txt', 'shell', 'dumpsys', 'meminfo', 'com.ctlife.app')
        evidence('battery-after.txt', 'shell', 'dumpsys', 'battery')
        evidence('thermal-after.txt', 'shell', 'dumpsys', 'thermalservice')
        (args.output / 'city-after.png').write_bytes(adb('exec-out', 'screencap', '-p', binary=True))
    finally:
        if log_process is not None:
            log_process.terminate()
            log_process.wait(timeout=10)
        if args.stop_after:
            adb('shell', 'am', 'force-stop', 'com.ctlife.app')
            print('CT Life stopped: explicit --stop-after cleanup.', flush=True)
        elif paused_for_cooling:
            print('CT Life paused in the background for cooling; process was not force-stopped.', flush=True)
        else:
            print('CT Life left open for interactive review.', flush=True)

    lines = (args.output / 'device.log').read_text(errors='replace').splitlines()
    expected_head = 'head_rain' if args.look == 'creator.look.breeze' else 'head_snow'
    selected = next((line for line in lines if 'Authored profile look:' in line), None)
    if selected is None or ('|' + expected_head + '|') not in selected:
        raise RuntimeError('No matching authored profile look in device evidence; not a valid character run')
    ready = next((i for i, line in enumerate(lines) if 'Synthetic local crowd ready:' in line), None)
    if ready is None:
        raise RuntimeError('No review crowd-ready evidence; not a valid performance run')
    rows = [dict(re.findall(r'(\w+)=([^\s]+)', line)) for line in lines[ready + 1:] if 'CTLIFE [fps] avg=' in line]
    # First window can straddle spawn; discard it and the next complete window for warm-up.
    steady = rows[2:]
    seconds, minimum, allowance = validate_steady_windows(steady, args.crowd, args.seconds)
    def numbers(key):
        result = []
        for row in steady:
            try:
                result.append(float(row[key]))
            except (KeyError, ValueError):
                pass
        return result
    report = {
        'scope': 'Offline city, synthetic local crowd, current character assets; no voice or realtime server',
        'requested_avatars': args.crowd,
        'requested_look': args.look,
        'duration_requested_seconds': args.seconds,
        'discarded_initial_fps_windows': 2,
        'steady_windows': len(steady),
        'sampled_seconds': round(seconds, 3),
        'validation': {'expected_registered_avatars': args.crowd,
                       'expected_animator_avatars': args.crowd, 'required_procedural_avatars': 0,
                       'required_dropped_samples': 0, 'minimum_sampled_seconds': minimum,
                       'coverage_allowance_seconds': allowance, 'same_process_alive_at_end': True},
        'weighted_average_fps': round(sum(numbers('frames')) / seconds, 2),
        'worst_window_one_percent_low_fps': min(numbers('p1')),
        'worst_window_p99_frame_ms': max(numbers('p99ms')),
        'minimum_observed_frame_fps': min(numbers('min')),
        'dropped_samples': sum(numbers('droppedSamples')),
        'ranges': {key: [min(values), max(values)] if (values := numbers(key)) else 'unavailable'
                   for key in ('avatars', 'proceduralAvatars', 'animatorAvatars', 'lod0Enabled', 'lod1Enabled',
                               'lod2Enabled', 'distanceCulled', 'renderScaleAsset', 'apRenderScaleMultiplier',
                               'drawCallsLast', 'batchesLast', 'trianglesLast')},
        'thermal_observations': sorted({(r.get('thermalSource'), r.get('thermalRaw'), r.get('thermalApplied')) for r in steady}),
        'limits': 'Render-counter ranges are sparse last-frame observations, not peaks. Window percentiles are not a whole-run percentile. FPS uses Unity update times, not Android presented frames. ADB samples aggregate thermal status approximately every 5 seconds during the workload.',
        'windows': steady,
    }
    (args.output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: v for k, v in report.items() if k != 'windows'}, indent=2), flush=True)


if __name__ == '__main__':
    main()
