import importlib.util
from pathlib import Path
import unittest
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "CountFile2" / "VietOcr"))

spec = importlib.util.spec_from_file_location("worker", Path(__file__).resolve().parents[1] / "CountFile2" / "VietOcr" / "worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class GeometryTests(unittest.TestCase):
    def test_two_columns_between_spanning_header_and_footer(self):
        def box(name, x, y, w, h):
            return dict(name=name, left=x, top=y, right=x + w, bottom=y + h)
        boxes = [box("right2", 600, 160, 400, 30), box("footer", 0, 300, 1000, 40),
                 box("left1", 0, 100, 400, 30), box("header", 0, 0, 1000, 40),
                 box("right1", 600, 100, 400, 30), box("left2", 0, 160, 400, 30)]
        self.assertEqual([b["name"] for b in worker.reading_order(boxes)],
                         ["header", "left1", "left2", "right1", "right2", "footer"])

    def test_coordinates_map_back_to_original_page(self):
        # Original page 100 x 200; original point (20,30) after clockwise rotation.
        for angle, rotated in [(0, (20, 30)), (90, (170, 20)), (180, (80, 170)), (270, (30, 80))]:
            self.assertEqual(worker.original_point(*rotated, 100, 200, angle), (20, 30))


if __name__ == "__main__":
    unittest.main()
