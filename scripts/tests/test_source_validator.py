"""Self-tests for the narrow source checker, not C# application/runtime tests."""
from __future__ import annotations
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

MODULE_PATH = Path(__file__).resolve().parents[1] / 'validate-source.py'
spec = importlib.util.spec_from_file_location('source_validator', MODULE_PATH)
assert spec is not None and spec.loader is not None
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class SourceValidatorTests(unittest.TestCase):
    def test_empty_input(self):
        self.assertEqual('', validator.mask_literals(''))

    def test_regular_escaped_string(self):
        source = r'var x = "value \" [ { ( end"; Call();'
        result = validator.mask_literals(source)
        self.assertEqual(len(source), len(result))
        self.assertIn('Call();', result)
        self.assertNotIn('value', result)
        self.assertNotIn('[', result)

    def test_line_comment(self):
        result = validator.mask_literals('Call(); // } ] )\nNext(); // tail')
        self.assertEqual(1, result.count('\n'))
        self.assertIn('Next();', result)
        self.assertNotIn('}', result)
        self.assertNotIn('tail', result)

    def test_block_comment_preserves_lines(self):
        source = '/* {\n] */\nCall();'
        result = validator.mask_literals(source)
        self.assertEqual([i for i, c in enumerate(source) if c == '\n'],
                         [i for i, c in enumerate(result) if c == '\n'])
        self.assertNotIn('{', result)
        self.assertIn('Call();', result)

    def test_verbatim_doubled_quote(self):
        self.assertNotIn('value', validator.mask_literals('var x = @"value "" [ ]";'))

    def test_nested_interpolation_literal(self):
        source = 'var x = $"value {Format("{", new[] {1, 2})}"; Call();'
        result = validator.mask_literals(source)
        self.assertNotIn('Format', result)
        self.assertNotIn('{', result)
        self.assertIn('Call();', result)

    def test_escaped_interpolation_braces(self):
        result = validator.mask_literals('var x = $"{{value}} {n}";')
        self.assertNotIn('{', result)
        self.assertNotIn('value', result)

    def test_character_literals(self):
        result = validator.mask_literals(r"char x = '\''; char y = '}';")
        self.assertNotIn('}', result)
        self.assertEqual(2, result.count(';'))

    def test_unicode_code_is_preserved(self):
        source = 'var größe = 2; // 雪\n'
        result = validator.mask_literals(source)
        self.assertIn('größe', result)
        self.assertNotIn('雪', result)

    def test_unterminated_constructs_fail(self):
        for source in ['/* missing', '"missing', "'missing", '$"{missing']:
            with self.subTest(source=source), self.assertRaises(ValueError):
                validator.mask_literals(source)

    def test_raw_strings_fail_explicitly(self):
        with self.assertRaisesRegex(ValueError, 'compiler-grade lexer'):
            validator.mask_literals('var x = """raw""";')

    def test_report_explicitly_leaves_runtime_validation_outstanding(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / 'report.json'
            with patch('sys.argv', ['validate-source.py', '--report', str(target)]), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, validator.main())
            report = json.loads(target.read_text(encoding='utf-8'))
            self.assertEqual('source-static-only', report['validation_type'])
            self.assertEqual('passed-with-runtime-validation-outstanding', report['status'])
            self.assertIn('Core test execution', report['not_performed'])
            self.assertIn('C# compilation/type checking', report['not_performed'])
            self.assertGreaterEqual(report['counts']['declared_core_tests'], 134)


if __name__ == '__main__':
    unittest.main()
