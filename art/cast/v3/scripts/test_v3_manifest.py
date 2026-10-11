"""Plain-Python regression checks for the cast v3 catalogue (no Blender, no writes).
cd art/cast/v3/scripts && python3 test_v3_manifest.py"""
import itertools, json, unittest
from pathlib import Path
import v3_manifest as M
import v3_common as K
import v3_spec as S

class CatalogueV3(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.man = json.loads((K.EXPORT / 'manifest_v3.json').read_text())
        cls.bases = cls.man['bases']
        cls.fams = M.load_families()
        cls.partmap = json.loads((K.EXPORT / 'runtime_part_map_v3.json').read_text())

    def test_bases_and_gender_balance(self):
        self.assertEqual(set(self.bases), set(S.MODULAR) | set(S.UBC) | set(S.HEROES))
        self.assertEqual(len(self.bases), 25)
        b = self.man['balance']
        self.assertLessEqual(abs(b['female'] - b['male']), 1)
        self.assertEqual(b['combos'], {'f': 4, 'm': 4})
        self.assertEqual(b['npc_presets']['f'], b['npc_presets']['m'])
        self.assertNotIn('hero_tactical', self.bases)

    def test_no_tv_head_and_starter_set(self):
        for bid, b in self.bases.items():
            with self.subTest(base=bid):
                self.assertNotIn('tv_head', b['accessories'])
                self.assertTrue(set(K.STARTER_ACCESSORIES) <= set(b['accessories']))

    def test_default_assembly_matches_merged_tiers(self):
        for bid, b in self.bases.items():
            if b.get('geometry_root'):
                continue     # heroes: v2 geometry, checked by the v2 receipt
            with self.subTest(base=bid):
                r = M.compose(b, self.fams)
                self.assertEqual(r['triangles'], {t: x['triangles'] for t, x in b['tiers'].items()})
                self.assertFalse(r['over_budget'])

    def test_every_compatible_family_combination_fits_caps(self):
        for fam, F in self.fams.items():
            base = self.bases[F['bases'][0]]
            lib = F['library']
            n = 0
            for h, t, bo, fe in itertools.product(lib['head'], lib['top'], lib['bottom'], lib['feet']):
                if fe not in F['compatibility']['bottom_feet'][bo] or bo not in F['compatibility']['top_bottom'][t]:
                    with self.assertRaises(ValueError):
                        M.compose(base, self.fams, {'parts': {'head': h, 'top': t, 'bottom': bo, 'feet': fe}})
                    continue
                r = M.compose(base, self.fams, {'parts': {'head': h, 'top': t, 'bottom': bo, 'feet': fe}})
                self.assertFalse(r['over_budget'], (fam, h, t, bo, fe, r['triangles']))
                n += 1
            self.assertGreater(n, 1000)

    def test_rejections(self):
        um, uw, ubc, hero = self.bases['umm_suit'], self.bases['umw_suit'], self.bases['ubc_m'], self.bases['hero_gynoid']
        with self.assertRaises(ValueError):
            M.compose(um, self.fams, {'parts': {'top': 'dress'}})          # women's part on the men's family
        with self.assertRaises(ValueError):
            M.compose(ubc, self.fams, {'parts': {'top': 'suit'}})          # UBC has no outfit swaps
        with self.assertRaises(ValueError):
            M.compose(um, self.fams, {'hair': 'Hair_Long'})                # modular hair = head part
        with self.assertRaises(ValueError):
            M.compose(hero, self.fams, {'hair': 'Hair_Long'})
        with self.assertRaises(ValueError):
            M.compose(um, self.fams, {'accessories': ['visor', 'laser_eyes']})   # same socket
        r = M.compose(uw, self.fams, {'accessories': ['helmet']})
        self.assertIn('hair', r['hidden'])

    def test_combos_fit_with_accessory_rule(self):
        for c in self.man['ct_hero_combos']:
            r = M.compose(self.bases[c['base']], self.fams, {'parts': c.get('parts', {}), 'accessories': c['accessories']})
            self.assertFalse(r['over_budget'], (c['id'], r['triangles']))
            self.assertEqual(r['accessories_dropped']['lod2'], c['accessories'])
            self.assertLessEqual(r['triangles']['lod2'], K.CAPS['lod2'])

    def test_ubc_hair_replaces_and_beard_adds(self):
        r = M.compose(self.bases['ubc_m'], self.fams, {'hair': 'Hair_Buzzed', 'facial': ['Hair_Beard']})
        self.assertNotIn('hair', r['parts']); self.assertIn('hair_Hair_Buzzed', r['parts']); self.assertIn('facial_Hair_Beard', r['parts'])

    def test_swapped_part_brings_its_own_hat_colour(self):
        uw = self.bases['umw_tee']
        self.assertNotIn('hat', uw['look_defaults'])
        t = M.resolve_tints(uw, self.fams, {'head': 'worker'})
        self.assertEqual(t['hat'], self.bases['umw_worker']['look_defaults']['hat'])
        self.assertEqual(t['hair'], uw['look_defaults']['hair'])          # the wearer's hair wins
        t2 = M.resolve_tints(uw, self.fams, {'head': 'worker'}, {'hair': 'platinum'})
        self.assertEqual(t2['brows'], K.brow_color(K.HAIR_COLORS['platinum']))
        for fam, F in self.fams.items():                                    # every region of every part resolves to a colour
            for slot, entries in F['library'].items():
                for key, e in entries.items():
                    if slot == 'extra':
                        continue
                    base = self.bases[F['bases'][0]]
                    t = M.resolve_tints(base, self.fams, {slot: key}) if slot != 'feet' and slot != 'bottom' else None
                    if t is not None:
                        for r in e['regions']:
                            if r in K.TINT_REGIONS:
                                self.assertIn(r, t, (fam, slot, key, r))

    def test_brows_never_white(self):
        import numpy as np
        for name, hexc in list(K.HAIR_COLORS.items()) + [('white', '#ffffff')]:
            b = K.brow_color(hexc).lstrip('#')
            c = np.array([int(b[i:i + 2], 16) / 255 for i in (0, 2, 4)])
            lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
            self.assertLessEqual(float(lin @ [0.2126, 0.7152, 0.0722]), 0.08, name)

    def test_skin_palette_fair_to_deep(self):
        tones = self.man['palettes']['skin']
        self.assertEqual(len(tones), 10)
        L = [t['lab'][0] for t in tones]
        self.assertEqual(L, sorted(L, reverse=True))

    def test_look_defaults_and_sockets(self):
        for bid, b in self.bases.items():
            with self.subTest(base=bid):
                self.assertEqual(set(b['sockets']), set(K.SOCKETS))
                for s, v in b['sockets'].items():
                    self.assertEqual(v['bone'], K.SOCKETS[s]['bone'])
                if not b.get('rare_masked_look'):
                    self.assertIn(b['look_defaults']['skin_tone'], [f'skin_{i:02d}' for i in range(1, 11)])

    def test_default_skin_tones_spread(self):
        tones = [b['look_defaults']['skin_tone'] for b in self.bases.values() if 'skin_tone' in b['look_defaults']]
        self.assertEqual(len(set(tones)), 10)

    def test_family_shares_one_material_and_atlas(self):
        for fam, F in self.fams.items():
            texs = {json.dumps(self.bases[b]['textures'], sort_keys=True) for b in F['bases']}
            self.assertEqual(len(texs), 1, fam)

    def test_ct_archetypes_cover_both_genders(self):
        for a in ('anon', 'whale', 'degen', 'kol_caller', 'builder', 'sniper_bot', 'laser_eyes_maxi', 'cabal_insider'):
            g = {self.bases[b]['gender'] for b in self.man['archetype_suggestions'].get(a, [])}
            self.assertEqual(g, {'f', 'm'}, a)

    def test_asset_hashes(self):
        for rel, e in self.man['inventory']['assets'].items():
            p = (K.EXPORT / rel).resolve()
            self.assertEqual(M.sha(p), e['sha256'], rel)

    def test_validation_receipt(self):
        v = json.loads((K.EXPORT / 'validation_v3.json').read_text())
        self.assertTrue(v['all_pass'], v['failures'][:3])
        self.assertEqual(v['summary']['fbx_files'], len(v['files']))
        for h, r in v['hero_v2_pins'].items():
            self.assertFalse(r['drift'], h)

    def test_unsafe_path_rejected(self):
        with self.assertRaises(ValueError):
            M.asset('../scripts/v3_spec.py')

if __name__ == '__main__':
    unittest.main()
