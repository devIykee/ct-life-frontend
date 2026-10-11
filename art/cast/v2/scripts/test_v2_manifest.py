"""Plain-Python catalogue/assembly regression checks; no Blender or project mutation."""
import copy, json, unittest
from pathlib import Path
import v2_manifest as M

class Catalogue(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.man=json.loads((M.EXPORT/'manifest_v2.json').read_text());cls.bases=cls.man['bases']
        cls.partmap=json.loads((M.EXPORT/'runtime_part_map.json').read_text())
    def test_all_seven_bases_and_preserved_orphan(self):
        self.assertEqual(set(self.bases),set(M.SPEC.BASES))
        self.assertEqual(self.man['inventory']['preserved_unreferenced_files'],['bases/ubc_f/parts/ubc_f__body.fbx'])
    def test_default_parts_match_imported_whole(self):
        for bid,b in self.bases.items():
            with self.subTest(base=bid):
                self.assertEqual(M.compose(b)['triangles'],{t:x['triangles'] for t,x in b['tiers'].items()})
    def test_hair_replaces_and_beard_adds(self):
        b=self.bases['ubc_m'];r=M.compose(b,'Hair_Buzzed',['Hair_Beard'])
        self.assertNotIn('hair',r['parts']);self.assertIn('hair_Hair_Buzzed',r['parts']);self.assertIn('facial_Hair_Beard',r['parts'])
        self.assertFalse(r['over_budget'])
    def test_all_hair_combinations_fit_base_caps(self):
        for bid in ('ubc_m','ubc_f'):
            b=self.bases[bid]
            for hair in b['hair_library']['hair']:
                for facial in ([],list(b['hair_library']['facial'])):
                    with self.subTest(base=bid,hair=hair,facial=facial):self.assertFalse(M.compose(b,hair,facial)['over_budget'])
    def test_reject_invalid_and_cross_base_parts(self):
        for hair in ('umm_punk','Hair_BuzzedFemale'):
            with self.assertRaises(ValueError):M.compose(self.bases['ubc_m'],hair)
        with self.assertRaises(ValueError):M.compose(self.bases['hero_hazmat'],'Hair_Long')
        with self.assertRaises(ValueError):M.compose(self.bases['ubc_f'],facial=['Hair_Beard'])
    def test_accessory_occlusion_and_socket_conflicts(self):
        r=M.compose(self.bases['ubc_m'],'Hair_Long',['Hair_Beard'],['tv_head'])
        for hidden in ('head','eyes','brows','hair_Hair_Long','facial_Hair_Beard'):self.assertNotIn(hidden,r['parts'])
        with self.assertRaises(ValueError):M.compose(self.bases['ubc_m'],accessories=['visor','laser_eyes'])
    def test_accessories_cannot_silently_exceed_crowd_budget(self):
        self.assertIn('lod2',M.compose(self.bases['ubc_f'],accessories=['helmet'])['over_budget'])
    def test_complete_head_and_no_invented_wire_alias(self):
        self.assertEqual(set(self.partmap['bases']['ubc_f']['complete_head_parts']),{'eyes','head','brows'})
        for b in self.partmap['bases'].values():
            self.assertIsNone(b['wire_head_id']);self.assertIsNone(b['wire_look']);self.assertFalse(b['cross_base_swaps'])
    def test_current_asset_hashes(self):
        for rel,entry in self.man['inventory']['assets'].items():self.assertEqual(M.sha(M.asset(rel)),entry['sha256'],rel)
    def test_failures_are_not_reclassified_as_accepted(self):
        self.assertFalse(self.man['validation']['all_pass'])
        self.assertTrue(self.bases['hero_tactical']['validation']['structural_errors'])
        self.assertEqual(self.bases['hero_assassin']['tiers']['lod2']['triangles'],1122)
        self.assertEqual(self.bases['hero_assassin']['source_metadata_triangles']['lod2'],1125)
    def test_unsafe_path_rejected(self):
        with self.assertRaises(ValueError):M.asset('../../scripts/v2_spec.py')

if __name__=='__main__':unittest.main()
