"""The one names table (contract/ids/names.json, QUALITY-BAR.md) for the art generators that bake names into textures
(island facade signs, venue sign atlases, the McDonald's and Casino art). Never hard-code a brand or place name in a
generator: ask this table, so a rename is one edit plus a re-run.

    import names_table as N
    N.name('venue.rekt_burger')          # "McDonald's"
    N.upper('venue.launchpad_tower')     # "PUMP.FUN TOWER"
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PATH = os.path.join(ROOT, 'contract', 'ids', 'names.json')
_names = None


def names():
    global _names
    if _names is None:
        _names = json.load(open(PATH, encoding='utf-8'))['names']
    return _names


def name(key, fallback=None):
    v = names().get(key)
    if v is None and fallback is None:
        raise KeyError(f'{key} is not in {PATH}')
    return v if v is not None else fallback


def upper(key, fallback=None):
    v = name(key, fallback)
    return v.upper() if v is not None else None


def has(key):
    return key in names()


BRANDS = os.path.join(ROOT, 'contract', 'ids', 'brands.json')
_brands = None


def brand(key):
    """A brand's mark and colours from contract/ids/brands.json: {'name', 'mark' (absolute path), 'svg' (its text), 'colors'}."""
    global _brands
    if _brands is None:
        _brands = json.load(open(BRANDS, encoding='utf-8'))['brands']
    b = dict(_brands[key])
    b['mark'] = os.path.join(ROOT, b['mark'])
    b['svg'] = open(b['mark'], encoding='utf-8').read()
    return b
