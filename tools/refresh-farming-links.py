"""Refresh public destination links, not map assets or spawn databases."""
import concurrent.futures
import datetime
import html
import json
import pathlib
import re
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
BASE = 'https://aion2maps.com'

def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    with urllib.request.urlopen(request, timeout=25) as response:
        return response.read().decode('utf-8')

def normalize(s):
    return ' '.join(html.unescape(re.sub('<[^>]+>', '', s)).casefold().split())

index = fetch(BASE + '/items/')
links = {}
for path, label in re.findall(r'<a\b[^>]*href="([^"]+)"[^>]*>(.*?)</a>', index, re.S):
    label = normalize(label)
    if label.startswith('soul: ') and label.endswith(' (bound)'):
        links[label[6:-8]] = urllib.parse.urljoin(BASE, html.unescape(path))

def build(pet):
    source = links.get(normalize(pet['Name']))
    if not source:
        return {'PetId': pet['Id'], 'SourceUrl': None, 'Maps': []}
    page = fetch(source)
    destinations = set()
    for href in re.findall(r'href="([^"]+)"', page):
        url = urllib.parse.urljoin(BASE, html.unescape(href))
        parsed = urllib.parse.urlparse(url)
        query = urllib.parse.parse_qs(parsed.query)
        if parsed.scheme == 'https' and parsed.netloc == 'aion2maps.com' and re.fullmatch(r'item:\d+', query.get('show', [''])[0]):
            destinations.add(url)
    maps = [{'Region': urllib.parse.urlparse(url).path.strip('/').replace('-', ' ').title(), 'Url': url} for url in sorted(destinations)]
    return {'PetId': pet['Id'], 'SourceUrl': source, 'Maps': maps}

pets = json.loads((ROOT / 'assets/catalog.json').read_text(encoding='utf-8-sig'))
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    rows = list(pool.map(build, pets))
result = {'VerifiedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'Provider': BASE, 'Rows': rows}
(ROOT / 'src/Core/FarmingLinks.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'pets': len(rows), 'source_pages': sum(bool(r['SourceUrl']) for r in rows), 'pets_with_maps': sum(bool(r['Maps']) for r in rows), 'map_links': sum(len(r['Maps']) for r in rows)}))
