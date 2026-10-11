"""Offline checks for gdrive_fetch's untrusted-name handling (no network).
python3 art/cast/scripts/test_gdrive_fetch.py"""
import os, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gdrive_fetch as G


class SafeNames(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = os.path.realpath(self.tmp.name)
        G.ROOT = self.root
        os.makedirs(os.path.join(self.root, 'sub'))

    def tearDown(self):
        self.tmp.cleanup()

    def test_traversal_names_are_reduced_to_basename_inside_root(self):
        for evil in ('../../etc/passwd', '..\\..\\x.fbx', '/abs/path/y.blend', 'a/b/../../../z.png', 'C:\\w\\v.gltf'):
            d = G.safe_join(os.path.join(self.root, 'sub'), evil)
            self.assertEqual(os.path.dirname(d), os.path.join(self.root, 'sub'), evil)
            self.assertNotIn('..', os.path.relpath(d, self.root))

    def test_unusable_names_are_skipped(self):
        for bad in ('', '.', '..', '   ', 'x\x00y', 'download-manifest.json', 'dir/..'):
            self.assertIsNone(G.safe_join(self.root, bad), repr(bad))

    def test_symlinked_parent_cannot_escape(self):
        outside = tempfile.TemporaryDirectory()
        try:
            link = os.path.join(self.root, 'link')
            os.symlink(outside.name, link)
            with self.assertRaises(SystemExit):
                G.safe_join(link, 'file.fbx')
        finally:
            outside.cleanup()

    def test_symlinked_file_target_cannot_escape(self):
        outside = tempfile.TemporaryDirectory()
        try:
            os.symlink(os.path.join(outside.name, 'victim.txt'), os.path.join(self.root, 'trap.fbx'))
            with self.assertRaises(SystemExit):
                G.safe_join(self.root, 'trap.fbx')
        finally:
            outside.cleanup()

    def test_sibling_prefix_directory_is_not_inside(self):
        # '/x/root-evil' starts with '/x/root' but is not inside it (old startswith check)
        sib = self.root + '-evil'
        os.makedirs(sib, exist_ok=True)
        try:
            with self.assertRaises(SystemExit):
                G.safe_join(sib, 'f.fbx')
        finally:
            os.rmdir(sib)

    def test_requires_root(self):
        G.ROOT = None
        with self.assertRaises(SystemExit):
            G.safe_join(self.root, 'a.fbx')


if __name__ == '__main__':
    unittest.main()
