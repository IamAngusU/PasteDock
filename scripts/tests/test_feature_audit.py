import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("feature_audit", ROOT / "scripts" / "audit-features.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class FeatureAuditTests(unittest.TestCase):
    def test_all_declared_features_have_aliases(self):
        keys = {feature[0] for feature in MODULE.FEATURES}
        self.assertEqual(keys, set(MODULE.FEATURE_ALIASES))

    def test_find_diff_points_to_existing_diff(self):
        matches = MODULE.find_existing("compare two clipboard clips", MODULE.audit())
        self.assertTrue(matches)
        self.assertEqual("bounded-two-clip-diff", matches[0]["feature"])
        self.assertEqual("present", matches[0]["source_presence"])

    def test_find_paste_as_file_points_to_existing_file_workflow(self):
        matches = MODULE.find_existing("paste text as file", MODULE.audit())
        self.assertTrue(any(row["feature"] == "paste-as-temporary-file" for row in matches))

    def test_blank_query_is_not_a_false_match(self):
        self.assertEqual([], MODULE.find_existing("   ", MODULE.audit()))


if __name__ == "__main__":
    unittest.main()
