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
emit({"type": "ready", "protocol": 1, "model": "test_fixture"})
for raw in sys.stdin:
    request = json.loads(raw)
    if not Path(request["image"]).is_file():
        raise RuntimeError("Unicode image path was corrupted")
    if args.device in ("test-slow", "test-timeout"):
        time.sleep(30)
    if args.device == "test-page-error" and request["page"] == 2:
        emit({"type": "error", "id": request["id"], "error": "Test page failed"})
        continue
    text = "Nội dung tiếng Việt trang " + str(request["page"])
    emit({"type": "page", "id": request["id"], "status": "completed", "text": text,
          "lines": [{"text": text, "confidence": 0.95, "polygon_pixels": [[1, 1], [20, 1], [20, 20], [1, 20]]}]})
