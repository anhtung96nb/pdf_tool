"""Real-model smoke with synthetic digits, not a handwriting accuracy benchmark."""
import json
from pathlib import Path
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "CountFile2" / "VietOcr"))
from worker import Engine

root = Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=False)
engine = Engine("cpu", 0)
cases = []
for label in ("003", "12", "1"):
    image = np.full((1700, 1200, 3), 255, dtype=np.uint8)
    cv2.putText(image, label, (1020, 85), cv2.FONT_HERSHEY_SCRIPT_SIMPLEX, 1.3, (140, 40, 15), 2, cv2.LINE_AA)
    path = root / (label + ".png")
    cv2.imencode(".png", image)[1].tofile(str(path))
    request = dict(id=len(cases) + 1, image=str(path), page=len(cases) + 1, width_points=600, height_points=850,
                   page_number=dict(enabled=True, right_percent=20, top_percent=10))
    start = time.perf_counter()
    result = engine.recognize(request)
    number = result["document_page_number"]
    assert result["type"] == "page" and "text" in result and number["candidates"]
    assert number["status"] in ("recognized", "needs_review")
    assert number["value"] is None or number["value"].isdigit()
    assert number["roi_polygon_pixels"] == [[960, 0], [1200, 0], [1200, 170], [960, 170]]
    cases.append(dict(expected=label, status=number["status"], value=number["value"], raw_text=number["raw_text"],
                      seconds=round(time.perf_counter() - start, 3), confidence=number["confidence"]))
    (root / (label + ".json")).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print("REAL NUMBER SUBSTEP PASS")
print(json.dumps(cases, ensure_ascii=False, indent=2))
