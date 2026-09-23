#!/usr/bin/env python3
"""Validate embedded catalogs and literal key references without game or .NET dependencies.

This is a key/placeholder check, not a substitute for reviewing user-visible literals
or for the Steam-launched preview workflow in docs/localization.md.
"""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'src' / 'WhoCarried'

def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        assert key not in result, f'duplicate key: {key}'
        result[key] = value
    return result

catalogs = {p.stem: json.loads(p.read_text(encoding='utf-8'), object_pairs_hook=unique_pairs)
            for p in sorted((root / 'Localization').glob('*.json'))}
english = catalogs['eng']

def placeholders(text):
    return sorted(re.findall(r'(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})', text))

for language, catalog in catalogs.items():
    assert not catalog.keys() - english.keys(), f'{language}: unknown keys'
    for key, value in catalog.items():
        assert re.fullmatch(r'WHO_CARRIED\.[a-z0-9_]+(?:\.[a-z0-9_]+)*', key), key
        assert isinstance(value, str) and value.strip(), f'{language}: empty {key}'
        assert placeholders(value) == placeholders(english[key]), f'{language}: placeholders for {key}'
    # Partial future translations are valid: English supplies the missing entries.
    print(f'{language}: {len(catalog)} keys, {len(english.keys() - catalog.keys())} English fallbacks')
assert catalogs['zhs'].keys() == english.keys(), 'Simplified Chinese must be complete'
references = set()
for path in root.rglob('*.cs'):
    if 'obj' in path.parts or 'bin' in path.parts:
        continue
    references.update(re.findall(r'"(WHO_CARRIED\.[a-z0-9_.]+)"', path.read_text(encoding='utf-8')))
assert not references - english.keys(), f'Undefined keys: {sorted(references - english.keys())}'
assert not english.keys() - references, f'Unused keys: {sorted(english.keys() - references)}'
print(f'OK: {len(references)} referenced keys; no missing or unused keys')
