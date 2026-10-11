#!/usr/bin/env python3
"""Download the FREE files of a 'name your own price' itch.io asset page without an account.

Usage: python3 itch_download.py <itch game url> <out dir> [substring filter ...]
Follows itch's public "No thanks, just take me to the downloads" flow:
  1. GET game page (csrf token)       2. POST <game>/download_url -> download page URL
  3. GET download page (upload ids)   4. POST <game>/file/<id>?source=game_download -> CDN url (no key for free pages)
Only files that are free (no minimum price) are offered on that page.
"""
import http.cookiejar, json, os, re, sys, urllib.parse, urllib.request

def main():
    game, out = sys.argv[1].rstrip('/'), sys.argv[2]
    filters = sys.argv[3:]
    os.makedirs(out, exist_ok=True)
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    op.addheaders = [('User-Agent', 'Mozilla/5.0 (Macintosh) ct-life-asset-pipeline')]
    page = op.open(game).read().decode('utf8', 'replace')
    csrf = re.search(r'name="csrf_token" (?:content|value)="([^"]+)"', page).group(1)
    r = op.open(game + '/download_url', urllib.parse.urlencode({'csrf_token': csrf}).encode()).read()
    dl_page_url = json.loads(r)['url']
    dl = op.open(dl_page_url).read().decode('utf8', 'replace')
    key = dl_page_url.rstrip('/').split('/')[-1]
    csrf2 = re.search(r'name="csrf_token" (?:content|value)="([^"]+)"', dl).group(1)
    uploads = re.findall(r'data-upload_id="(\d+)"[^>]*>.{0,400}?<strong title="([^"]+)"', dl, re.S)
    if not uploads:
        uploads = [(u, u) for u in sorted(set(re.findall(r'data-upload_id="(\d+)"', dl)))]
    print('uploads:', uploads)
    for uid, name in uploads:
        if filters and not any(f.lower() in name.lower() for f in filters):
            continue
        u = f"{game}/file/{uid}?source=game_download"
        j = json.loads(op.open(u, urllib.parse.urlencode({'csrf_token': csrf2}).encode()).read())
        if 'url' not in j:
            print('skip', name, j); continue
        cdn = j['url']
        if name == uid:
            name = urllib.parse.unquote(urllib.parse.urlparse(cdn).path.split('/')[-1]) or uid
        dest = os.path.join(out, name)
        print('downloading', name, '->', dest, flush=True)
        with op.open(cdn) as resp, open(dest, 'wb') as f:
            while True:
                b = resp.read(1 << 20)
                if not b: break
                f.write(b)
        print('ok', name, os.path.getsize(dest), flush=True)

if __name__ == '__main__':
    main()
