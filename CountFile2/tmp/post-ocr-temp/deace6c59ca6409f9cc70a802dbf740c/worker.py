"""Deterministic protocol fixture, used only by the service integration tests."""
import argparse
import json
import os
from pathlib import Path
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument("--device", default="cpu")
parser.add_argument("--rotation")
args = parser.parse_args()
Path(__file__ + ".pid").write_text(str(os.getpid()))


def emit(data):
    print(json.dumps(data, ensure_ascii=False), flush=True)


if args.device == "test-fatal":
    emit({"type": "error", "error": "Missing test weights"})
    raise SystemExit(1)
emit({"type": "ready", "protocol": 1, "model": "test_fixture", "capabilities": ["page_number_v1"]})
for raw in sys.stdin:
    request = json.loads(raw)
    if not Path(request["image"]).is_file():
        raise RuntimeError("Unicode image path was corrupted")
    with Path(__file__ + ".requests").open("a", encoding="utf-8") as log:
        log.write(json.dumps(request, ensure_ascii=False) + "\n")
    if args.device in ("test-slow", "test-timeout"):
        time.sleep(30)
    if args.device == "test-page-error" and request["page"] == 2:
        emit({"type": "error", "id": request["id"], "error": "Test page failed"})
        continue
    text = "Nội dung tiếng Việt trang " + str(request["page"])
    number = dict(status="disabled", value=None, confirmed=False)
    settings = request.get("page_number") or {}
    if settings.get("enabled"):
        w, h = request["width_points"], request["height_points"]
        label = "003" if request["page"] == 1 else "12"
        number = dict(status="recognized", value=label, raw_text=label, confidence=0.98, confirmed=False,
                      source="vietocr_roi", warnings=[], candidates=[],
                      roi_polygon_points=[[w * (1 - settings["right_percent"] / 100), 0], [w, 0], [w, h * settings["top_percent"] / 100], [w * (1 - settings["right_percent"] / 100), h * settings["top_percent"] / 100]],
                      polygon_points=[[w * 0.9, h * 0.02], [w * 0.98, h * 0.02], [w * 0.98, h * 0.06], [w * 0.9, h * 0.06]])
        if args.device == "test-number-ambiguous":
            number.update(status="needs_review", value=None, raw_text="O03", warnings=["ambiguous_or_low_score_number"])
        if args.device == "test-number-error":
            number.update(status="error", value=None, warnings=["ROI model failure"])
    if request.get("operation") == "page_number":
        emit({"type": "page", "id": request["id"], "document_page_number": number})
        continue
    if args.device == "test-markdown":
        text += "\n# Giữ *nguyên* [nhãn](https://example.invalid)\n<b>&amp;</b> | 1.\n\n    Không biến thành code"
    emit({"type": "page", "id": request["id"], "status": "completed", "text": text, "document_page_number": number,
          "lines": [{"text": text, "confidence": 0.95, "polygon_pixels": [[1, 1], [20, 1], [20, 20], [1, 20]]}]})
