from pathlib import Path
from types import SimpleNamespace
import sys
import unittest

import cv2
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "CountFile2" / "VietOcr"))
from page_number import read_number
from worker import original_point


class Predictor:
    def __init__(self, readings):
        self.readings, self.calls = readings, 0

    def predict(self, image, return_prob=False):
        value = self.readings[self.calls % len(self.readings)]
        self.calls += 1
        return value


def setup(readings=None):
    predictor = Predictor(readings or [("003", 0.98)])
    detector = SimpleNamespace(text_det=lambda image: (None, 0))
    return SimpleNamespace(cv2=cv2, np=np, Image=Image, predictor=predictor, detector=detector)


def request(enabled=True, right=20, top=10):
    return dict(original_width=1000, original_height=600, width_points=500, height_points=300,
                page_number=dict(enabled=enabled, right_percent=right, top_percent=top))


def sample():
    image = np.full((600, 1000, 3), 255, dtype=np.uint8)
    cv2.putText(image, "003", (850, 40), cv2.FONT_HERSHEY_SIMPLEX, 1, (100, 40, 10), 2)
    return image


class NumberTests(unittest.TestCase):
    def test_default_roi_and_leading_zeros(self):
        engine = setup()
        result = read_number(engine, sample(), request(), lambda x, y: (x, y))
        self.assertEqual(result["status"], "recognized")
        self.assertEqual(result["value"], "003")
        self.assertFalse(result["confirmed"])
        self.assertEqual(result["roi_polygon_pixels"], [[800, 0], [1000, 0], [1000, 60], [800, 60]])
        self.assertTrue(all(x >= 800 and y <= 60 for x, y in result["polygon_pixels"]))

    def test_empty_and_disabled_do_not_call_model(self):
        engine = setup()
        self.assertEqual(read_number(engine, sample(), request(False), lambda x, y: (x, y))["status"], "disabled")
        blank = np.full((600, 1000, 3), 255, dtype=np.uint8)
        self.assertEqual(read_number(engine, blank, request(), lambda x, y: (x, y))["status"], "not_found")
        self.assertEqual(engine.predictor.calls, 0)

    def test_outside_roi_and_adjustment(self):
        image = np.full((600, 1000, 3), 255, dtype=np.uint8)
        cv2.putText(image, "12", (720, 80), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 0), 2)
        engine = setup([("12", 0.99)])
        self.assertEqual(read_number(engine, image, request(), lambda x, y: (x, y))["status"], "not_found")
        self.assertEqual(read_number(engine, image, request(right=35, top=20), lambda x, y: (x, y))["value"], "12")

    def test_do_not_force_digits_or_guess(self):
        for raw in ("O03", "0 03", "003.", "12/100", "Số 12"):
            result = read_number(setup([(raw, 0.99)]), sample(), request(), lambda x, y: (x, y))
            self.assertEqual(result["status"], "needs_review", raw)
            self.assertIsNone(result["value"])
            self.assertEqual(result["raw_text"], raw)

    def test_disagreement_and_low_score(self):
        for readings in ([("003", 0.99), ("008", 0.99)], [("003", 0.75)]):
            result = read_number(setup(readings), sample(), request(), lambda x, y: (x, y))
            self.assertEqual(result["status"], "needs_review")
            self.assertIsNone(result["value"])

    def test_rotation_maps_back_to_original(self):
        upright = np.full((1000, 600, 3), 255, dtype=np.uint8)
        cv2.putText(upright, "12", (510, 55), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 0), 2)
        result = read_number(setup([("12", 0.99)]), upright, request(), lambda x, y: original_point(x, y, 1000, 600, 90))
        self.assertEqual(result["value"], "12")
        self.assertEqual(result["roi_polygon_pixels"], [[0, 120], [0, 0], [100, 0], [100, 120]])

    def test_roi_error_is_local(self):
        result = read_number(setup(), sample(), request(right=0), lambda x, y: (x, y))
        self.assertEqual(result["status"], "error")
        self.assertIsNone(result["value"])


if __name__ == "__main__":
    unittest.main()
