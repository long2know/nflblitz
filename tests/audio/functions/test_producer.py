import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("sound_function_producer", ROOT / "tools/audio/prepare_functions.py")
producer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(producer)


class ProducerChecks(unittest.TestCase):
    def test_comments_preserve_exact_lines(self):
        text = "first\r\n// fake()\r\n/* multi\n line */ actual()"
        result = producer.strip_comments(text)
        self.assertEqual(text.count("\n"), result.count("\n"))
        self.assertEqual(text.index("actual"), result.index("actual"))
        self.assertNotIn("fake", result)

    def test_numeric_expression_is_not_executed(self):
        for text in ["f()", "1+2", "1/0", "__import__('os')", "x"]:
            with self.subTest(text=text), self.assertRaises(producer.FunctionDataError):
                producer.integer(text, {})

    def test_pinned_integer_forms(self):
        self.assertEqual(producer.integer("0x40", {}), 64)
        self.assertEqual(producer.integer("-7", {}), -7)
        self.assertEqual(producer.integer("V", {"V": "211-13"}), 198)

    def test_definitions_ignore_comments(self):
        result = producer.definitions("#define V 211-13 // comment\n// #define BAD 9\n#define M 0x40\n")
        self.assertEqual(result, {"V": "211-13", "M": "0x40"})

    def test_function_prototype_not_a_body(self):
        source = "void sample(int);\nvoid sample(int x)\n{\n if(x){x++;}\n}"
        result = producer.function_source(source, "sample")
        self.assertEqual(result["line"], 2)
        self.assertIn("x++;", result["source"])

    def test_missing_or_unterminated_function(self):
        with self.assertRaises(producer.FunctionDataError):
            producer.function_source("void other() {}", "sample")
        with self.assertRaises(producer.FunctionDataError):
            producer.function_source("void sample() {", "sample")

    def test_balanced_source_arguments(self):
        values = producer.split_arguments("bank, table[randrng(sizeof(table)/sizeof(int))], volume, 123, priority")
        self.assertEqual(len(values), 5)
        self.assertEqual(values[1], "table[randrng(sizeof(table)/sizeof(int))]")
        with self.assertRaises(producer.FunctionDataError):
            producer.split_arguments("a,(b,c")

    def test_active_calls_only_and_callee_line(self):
        source = "void sample(){\n// snd_scall_bank(fake);\n snd_scall_bank(plyr_bnk_str,snd1,V,123,P);\n}"
        result = producer.call_sites(producer.function_source(source, "sample"))
        self.assertEqual(len(result), 1)
        self.assertEqual(result[0]["line"], 3)

    def test_wrong_call_bank_or_arity_rejected(self):
        for call in ["snd_scall_bank(other,snd1,V,123,P)", "snd_scall_bank(plyr_bnk_str,snd1)"]:
            with self.subTest(call=call), self.assertRaises(producer.FunctionDataError):
                producer.call_sites(producer.function_source("void sample(){" + call + ";}", "sample"))

    def test_canonical_output_order(self):
        self.assertEqual(producer.canonical({"b": 2, "a": 1}), producer.canonical({"a": 1, "b": 2}))


if __name__ == "__main__":
    unittest.main()
