"""Resolve the newest published Windows installer, including Alpha releases."""
import json
import os
import re
import urllib.request
from pathlib import Path

headers = {'Accept': 'application/vnd.github+json', 'User-Agent': 'DeskPages-site'}
if os.environ.get('GITHUB_TOKEN'):
    headers['Authorization'] = 'Bearer ' + os.environ['GITHUB_TOKEN']
request = urllib.request.Request('https://api.github.com/repos/AdamAbuToamy/DeskPages/releases?per_page=100', headers=headers)
with urllib.request.urlopen(request, timeout=30) as response:
    releases = json.load(response)
candidates = []
for release in releases:
    if release.get('draft'):
        continue
    for asset in release.get('assets', []):
        match = re.fullmatch(r'DeskPages-Setup-(\d+(?:\.\d+)+)\.exe', asset['name'])
        if match:
            candidates.append((tuple(map(int, match[1].split('.'))), release.get('published_at') or '', release, asset, match[1]))
if not candidates:
    raise SystemExit('No published Windows installer found. Refusing to publish a broken download.')
_, _, release, asset, version = max(candidates, key=lambda item: (item[0], item[1]))
url = asset['browser_download_url']
if not url.startswith('https://github.com/AdamAbuToamy/DeskPages/releases/download/'):
    raise SystemExit('Unexpected installer URL')
path = Path('docs/index.html')
html = path.read_text()
html = re.sub(r'https://github\.com/AdamAbuToamy/DeskPages/releases/download/[^"\s<>]+/DeskPages-Setup-[^"\s<>]+\.exe', url, html)
html = re.sub(r'https://github\.com/AdamAbuToamy/DeskPages/releases/tag/[^"\s<>]+', release['html_url'], html)
html = re.sub(r'Download DeskPages \d+(?:\.\d+)+', 'Download DeskPages ' + version, html)
html = re.sub(r'"softwareVersion": "[^"]+"', '"softwareVersion": ' + json.dumps(version + (' Alpha' if release['prerelease'] else '')), html)
path.write_text(html)
print('Download verified against published GitHub assets:', asset['name'])
