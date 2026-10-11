#!/usr/bin/env python3
"""Source guards for the Unity adapter; not a substitute for live EditMode/device tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
UI = ROOT / 'client/Assets/Scripts/UI'


class QaUiContractTests(unittest.TestCase):
    def test_android_flag_is_in_authenticated_allowlist(self):
        source = (ROOT / 'client/Assets/Scripts/Core/Env/LaunchArgs.cs').read_text()
        match = re.search(r'ReadAndroidExtras\(new\[\] \{([^}]+)', source)
        assert match is not None
        call = match.group(1)
        self.assertIn('"qaUi"', call)
        self.assertLess(source.index('if (!KeyMatches(expected, given))'), source.index('_args[k] = v'))

    def test_exporter_is_guarded_opt_in_read_only(self):
        path = UI / 'Dev/QaUiSnapshotExporter.cs'
        self.assertTrue(path.exists(), 'Unity snapshot adapter missing')
        source = path.read_text()
        self.assertTrue(source.startswith('#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD'))
        self.assertIn('LaunchArgs.Flag("qaUi")', source)
        self.assertIn('Application.persistentDataPath', source)
        self.assertIn('"qa-ui.json"', source)
        self.assertIn('QaUiSnapshotFile.Write', source)
        self.assertIn('ScreenToPanel', source)
        self.assertIn('.Pick(', source)
        self.assertIn('AudioSource', source)
        # Only these two fixed Unity geometry/policy queries may be reflected; never callbacks.
        self.assertEqual(re.findall(r'(\w+)\.Invoke\(', source), ['ShouldClip', 'LocalClippingRect'])
        self.assertEqual(re.findall(r'GetMethod\(\s*"([^"]+)"', source),
                         ['ShouldClip', 'GetLocalClippingRect'])
        for forbidden in ['Microphone.', '.GetData(', '.SendEvent(', 'HttpListener', 'TcpListener',
                          'DynamicInvoke', '.SetValue(', 'PlayerPrefs']:
            self.assertNotIn(forbidden, source)

    def test_lifecycle_hook_is_guarded(self):
        source = (UI / 'UiRoot.cs').read_text()
        blocks = re.findall(r'#if \(UNITY_EDITOR \|\| DEVELOPMENT_BUILD\) && !CTLIFE_PROD(.*?)#endif', source, re.S)
        guarded = '\n'.join(blocks)
        self.assertIn('QaUiSnapshotExporter', guarded)
        self.assertIn('.Tick(', guarded)
        self.assertIn('.Stop(', guarded)


if __name__ == '__main__':
    unittest.main()
