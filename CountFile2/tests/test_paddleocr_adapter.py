import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("paddle_adapter", Path(__file__).resolve().parents[1] / "CountFile2/PaddleOcr/worker.py")
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class AdapterTests(unittest.TestCase):
    def test_vietnamese_and_rotated_coordinates(self):
        result = adapter.convert(dict(rec_texts=["Tiếng Việt"], rec_scores=[.95],
                                      rec_polys=[[[10, 20], [40, 20], [40, 30], [10, 30]]]),
                                 dict(id=7, page=2, width_points=100, height_points=200), 100, 200, 90)
        self.assertEqual(result["text"], "Tiếng Việt")
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["lines"][0]["polygon_points"][0], [20, 190])

    def test_empty_and_low_score_require_review(self):
        request = dict(id=1, page=1, width_points=100, height_points=100)
        empty = adapter.convert(dict(rec_texts=[], rec_scores=[], rec_polys=[]), request, 100, 100, 0)
        self.assertEqual(empty["status"], "needs_review")
        result = adapter.convert(dict(rec_texts=["dấu"], rec_scores=[.4], rec_polys=[[[0, 0], [10, 0], [10, 10], [0, 10]]]), request, 100, 100, 0)
        self.assertEqual(result["lines"][0]["warnings"], ["low_recognition_score"])

    def test_mismatched_results_fail(self):
        with self.assertRaises(ValueError):
            adapter.convert(dict(rec_texts=["lost"], rec_scores=[], rec_polys=[]), {}, 100, 100, 0)


if __name__ == "__main__":
    unittest.main()
