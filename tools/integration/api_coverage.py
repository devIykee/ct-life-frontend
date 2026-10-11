#!/usr/bin/env python3
"""Read-only, stdlib-only Fastify/Pino request coverage (not an E2E verdict).

Usage: python3 tools/integration/api_coverage.py --log ~/.ctlife-local/logs/api.log \
    --openapi /path/to/contract/openapi.yaml --json --out coverage.json

--since/--until accept timezone-qualified ISO-8601 or Unix seconds/milliseconds.
Window is [since, until), selected by request START time; pair completions from
all supplied log lines first. Repeat --log for chronological rotated files.
JSON OpenAPI is supported. YAML support deliberately covers block-style paths,
methods and servers used by CT Life, not general YAML; unsupported path-item
references/inline operations fail closed. This does not validate response bodies.
No bodies, headers, queries, identifiers, raw log text or raw unknown paths are
emitted. Unknown paths retain only contract literal segments in matching prefix
positions; all other segments become {redacted} (distinct paths may coalesce).
"""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit
from typing import Any

METHODS = {'GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS', 'TRACE'}


def route_pattern(path):
    return re.compile('^' + ''.join('[^/]+' if p.startswith('{') and p.endswith('}')
                                  else re.escape(p) for p in re.split(r'(\{[^}]+\})', path)) + '$')


def load_openapi(path):
    """Return method/path pairs; prepend the root server's base URL path."""
    text = Path(path).read_text(encoding='utf-8')
    if text.lstrip().startswith('{'):
        spec = json.loads(text)
        servers = spec.get('servers', [])
        prefix = urlsplit(servers[0]['url']).path.rstrip('/') if servers else ''
        routes = []
        for url, item in spec['paths'].items():
            if '$ref' in item or 'servers' in item:
                raise ValueError('Unsupported path reference or per-path server')
            for method, operation in item.items():
                if method.upper() in METHODS:
                    if 'servers' in operation:
                        raise ValueError('Unsupported per-operation server')
                    routes.append((method.upper(), prefix + url))
    else:
        prefix = ''
        saw_server = False
        section = None
        current = None
        routes = []
        for line in text.splitlines():
            if not line.strip() or line.lstrip().startswith('#'):
                continue
            if not line.startswith(' '):
                section = line.split(':', 1)[0]
                current = None
                continue
            if section == 'servers' and not saw_server:
                m = re.match(r'\s+-\s+url:\s*[\"\']?([^\s\"\']+)', line)
                if m:
                    prefix = urlsplit(m.group(1)).path.rstrip('/')
                    saw_server = True
            if section != 'paths':
                continue
            m = re.match(r'^  ([\"\']?)(/.*?)\1:\s*(?:#.*)?$', line)
            if m:
                current = m.group(2)
                continue
            if re.match(r'^  \S', line):
                raise ValueError('Unsupported YAML path mapping')
            m = re.match(r'^    (get|post|put|patch|delete|head|options|trace):\s*(?:#.*)?$', line)
            if m and current:
                routes.append((m.group(1).upper(), prefix + current))
            elif re.match(r'^    (\$ref|servers|get|post|put|patch|delete|head|options|trace):', line) or re.match(r'^      servers:', line):
                raise ValueError('Unsupported YAML operation, reference or server override')
    if not routes:
        raise ValueError('No REST operations found in OpenAPI')
    if len(set(routes)) != len(routes):
        raise ValueError('Duplicate OpenAPI operations')
    return sorted(set(routes))


def timestamp(value):
    if value is None or isinstance(value, bool):
        return None
    try:
        n = float(value)
        if n != n or abs(n) == float('inf'):
            return None
        return n / 1000 if abs(n) >= 100_000_000_000 else n
    except (ValueError, TypeError):
        try:
            dt = datetime.fromisoformat(str(value).replace('Z', '+00:00'))
            if dt.tzinfo is None:
                return None
            return dt.timestamp()
        except (ValueError, OverflowError):
            return None


def iso(value):
    if value is None:
        return None
    try:
        return datetime.fromtimestamp(value, timezone.utc).isoformat().replace('+00:00', 'Z')
    except (ValueError, OverflowError, OSError):
        return None


def coverage(logs, operations, since=None, until=None):
    patterns = sorted([(m, p, route_pattern(p)) for m, p in operations],
                      key=lambda x: (x[1].count('{'), -len(x[1]), x[0]))
    literal_prefixes = set()
    for _, path in operations:
        parts = path.split('/')
        for i, part in enumerate(parts):
            if part and '{' not in '/'.join(parts[:i + 1]):
                literal_prefixes.add('/'.join(parts[:i + 1]))
    def bucket(method, url):
        try:
            path = urlsplit(url).path
        except ValueError:
            path = ''
        for m, p, pattern in patterns:
            if method == m and pattern.fullmatch(path):
                return (m, p), False
        # A known URL with the wrong verb still has a safe contract template.
        for _, p, pattern in patterns:
            if pattern.fullmatch(path):
                return (method, p), True
        parts = path.split('/')
        safe = ['' if not v else v if '/'.join(parts[:i+1]) in literal_prefixes else '{redacted}'
                for i, v in enumerate(parts)]
        return (method, '/'.join(safe) or '/{redacted}'), True
    def blank(method, path) -> dict[str, Any]:
        return dict(method=method, path=path, requests=0, completed=0, incomplete=0, statuses={})
    rows = {key: blank(*key) for key in operations}
    unknown = {}
    pending = {}
    counters = Counter()
    observed_times = []
    statuses = Counter()
    def consume(request, status=None):
        method, url, started = request
        if (since is not None or until is not None) and started is None:
            counters['requests_without_time_excluded'] += 1
            return
        if (since is not None and started < since) or (until is not None and started >= until):
            counters['requests_outside_window'] += 1
            return
        key, is_unknown = bucket(method, url)
        row = unknown.setdefault(key, blank(*key)) if is_unknown else rows[key]
        row['requests'] += 1
        counters['requests'] += 1
        if started is not None:
            observed_times.append(started)
        if status is None:
            row['incomplete'] += 1
            counters['incomplete'] += 1
        else:
            row['completed'] += 1
            counters['completed'] += 1
            row['statuses'][str(status)] = row['statuses'].get(str(status), 0) + 1
            statuses[str(status)] += 1
    for log in logs:
        with Path(log).expanduser().open(encoding='utf-8', errors='replace') as stream:
            for line in stream:
                counters['lines'] += 1
                try:
                    event = json.loads(line)
                except (ValueError, TypeError):
                    counters['non_json_lines'] += 1
                    continue
                if not isinstance(event, dict):
                    counters['non_object_json_lines'] += 1
                    continue
                req = event.get('req')
                res = event.get('res')
                req_id = event.get('reqId')
                key = (str(event.get('hostname', '')), str(event.get('pid', '')), str(req_id))
                if (isinstance(req, dict) and isinstance(req.get('url'), str)
                        and isinstance(req.get('method'), str)
                        and event.get('msg') in (None, 'incoming request')):
                    method = req['method'].upper()
                    method = method if method in METHODS else 'OTHER'
                    request = (method, req['url'], timestamp(event.get('time')))
                    if req_id is None:
                        counters['requests_without_req_id'] += 1
                        consume(request)
                    else:
                        if key in pending:
                            counters['reused_pending_req_ids'] += 1
                            consume(pending.pop(key))
                        pending[key] = request
                if isinstance(res, dict) and isinstance(res.get('statusCode'), int) and not isinstance(res['statusCode'], bool) and 100 <= res['statusCode'] <= 599:
                    if req_id is not None and key in pending:
                        consume(pending.pop(key), res['statusCode'])
                    else:
                        counters['orphan_completions'] += 1
    for request in pending.values():
        consume(request)
    route_rows = [rows[k] for k in sorted(rows)]
    unhit = [r['method'] + ' ' + r['path'] for r in route_rows if not r['requests']]
    summary: dict[str, Any] = {k: counters[k] for k in ('requests', 'completed', 'incomplete')}
    summary.update(contract_routes=len(rows), hit_routes=len(rows) - len(unhit),
                   unhit_routes=len(unhit), unknown_path_buckets=len(unknown),
                   unknown_requests=sum(r['requests'] for r in unknown.values()),
                   statuses=dict(sorted(statuses.items())))
    return dict(schema_version=1, evidence='Observed API log requests only; not functional, UI, or realtime proof.',
                window=dict(since=iso(since), until_exclusive=iso(until), basis='request_start'),
                observed=dict(first_request=iso(min(observed_times)) if observed_times else None,
                              last_request=iso(max(observed_times)) if observed_times else None),
                summary=summary, diagnostics=dict(sorted(counters.items())), routes=route_rows,
                unknown_paths=[unknown[k] for k in sorted(unknown)], unhit_paths=unhit,
                privacy='Known route templates only; queries/bodies/headers/request IDs omitted. Unknown segments redacted; buckets may coalesce.')


def markdown(report):
    summary = report['summary']
    lines = ['# API log coverage', '', report['evidence'], '',
             f"Requests: {summary['requests']}; completed: {summary['completed']}; incomplete: {summary['incomplete']}.",
             f"Contract routes hit: {summary['hit_routes']}/{summary['contract_routes']}; unhit: {summary['unhit_routes']}.",
             f"Unknown requests: {summary['unknown_requests']} in {summary['unknown_path_buckets']} redacted buckets.",
             '', 'Window: `' + json.dumps(report['window']) + '`',
             'Observed: `' + json.dumps(report['observed']) + '`',
             'Statuses: `' + json.dumps(summary['statuses']) + '`', '',
             '| Method | Path | Requests | Completed | Incomplete | Status counts |',
             '|---|---|---:|---:|---:|---|']
    for row in report['routes'] + report['unknown_paths']:
        lines.append(f"| {row['method']} | `{row['path']}` | {row['requests']} | {row['completed']} | {row['incomplete']} | {json.dumps(row['statuses'], sort_keys=True)} |")
    lines += ['', '## Unhit contract routes', ''] + ['- `' + p + '`' for p in report['unhit_paths']]
    lines += ['', '## Diagnostics', '', '`' + json.dumps(report['diagnostics'], sort_keys=True) + '`', '', report['privacy'], '']
    return '\n'.join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--log', action='append', required=True, help='Pino JSONL file; repeat in chronological order')
    parser.add_argument('--openapi', required=True, help='OpenAPI JSON or CT Life block-style YAML')
    parser.add_argument('--since', help='Inclusive request-start time (timezone-qualified ISO or epoch)')
    parser.add_argument('--until', help='Exclusive request-start time (timezone-qualified ISO or epoch)')
    parser.add_argument('--out', help='Write the report to this file (default stdout)')
    parser.add_argument('--json', action='store_true', help='Emit JSON instead of Markdown')
    args = parser.parse_args(argv)
    since, until = timestamp(args.since), timestamp(args.until)
    if (args.since is not None and since is None) or (args.until is not None and until is None):
        parser.error('Invalid time: use ISO-8601 with timezone or epoch seconds/milliseconds')
    if since is not None and until is not None and since >= until:
        parser.error('--since must be before --until')
    try:
        if args.out and Path(args.out).resolve() in [Path(p).expanduser().resolve() for p in args.log + [args.openapi]]:
            raise ValueError('Output must not overwrite an input')
        report = coverage(args.log, load_openapi(args.openapi), since, until)
        rendered = json.dumps(report, indent=2, sort_keys=True) + '\n' if args.json else markdown(report)
        if args.out:
            Path(args.out).parent.mkdir(parents=True, exist_ok=True)
            Path(args.out).write_text(rendered, encoding='utf-8')
        else:
            print(rendered, end='')
    except (OSError, ValueError, KeyError, TypeError) as exc:
        # Never echo raw input or exception text (may contain a credential-bearing URL).
        print('Coverage failed (' + type(exc).__name__ + '): check readable logs and supported OpenAPI layout.', file=sys.stderr)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main())
