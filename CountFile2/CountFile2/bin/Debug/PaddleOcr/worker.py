"""PaddleOCR 3.x adapter for the application's JSON-lines page protocol."""
import argparse
import contextlib
import json
import math
from pathlib import Path
import sys
import unicodedata

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "VietOcr"))
from worker import original_point, reading_order

PROTOCOL = sys.stdout
MODEL = "PP-OCRv5_server_det + latin_PP-OCRv5_mobile_rec"


def emit(value):
    PROTOCOL.write(json.dumps(value, ensure_ascii=False, allow_nan=False) + "\n")
    PROTOCOL.flush()


def convert(result, request, width, height, rotation):
    texts, scores, polygons = result["rec_texts"], result["rec_scores"], result["rec_polys"]
    if not (len(texts) == len(scores) == len(polygons)):
        raise ValueError("PaddleOCR trả về số dòng và tọa độ không khớp.")
    entries = []
    for text, score, polygon in zip(texts, scores, polygons):
        score = float(score)
        if not math.isfinite(score):
            score = None
        entries.append(dict(raw_text=str(text), confidence=score, polygon=polygon,
                            left=min(float(p[0]) for p in polygon), right=max(float(p[0]) for p in polygon),
                            top=min(float(p[1]) for p in polygon), bottom=max(float(p[1]) for p in polygon)))
    lines = []
    for index, entry in enumerate(reading_order(entries), 1):
        polygon = [original_point(float(x), float(y), width, height, rotation) for x, y in entry["polygon"]]
        polygon = [[round(min(width, max(0, x)), 2), round(min(height, max(0, y)), 2)] for x, y in polygon]
        text = unicodedata.normalize("NFC", entry["raw_text"]).strip()
        score = entry["confidence"]
        lines.append(dict(id=f'{request["page"]}-{index}', reading_order=index, text=text,
                          raw_text=entry["raw_text"], confidence=score, polygon_pixels=polygon,
                          polygon_points=[[round(x * request["width_points"] / width, 3),
                                           round(y * request["height_points"] / height, 3)] for x, y in polygon],
                          warnings=["low_recognition_score"] if not text or score is None or score < .8 else []))
    warnings = ["no_text_detected_blank_or_missed"] if not lines else []
    review = bool(warnings) or any(line["warnings"] for line in lines)
    return dict(type="page", id=request["id"], status="needs_review" if review else "completed",
                image_width=width, image_height=height, text="\n".join(line["text"] for line in lines),
                lines=lines, warnings=warnings, language="vi",
                layout_note="Thứ tự đọc theo khoảng trắng; chưa khôi phục cấu trúc ô bảng.")


class Engine:
    def __init__(self, device, rotation):
        import cv2
        import numpy as np
        from paddleocr import PaddleOCR
        self.cv2, self.np, self.rotation = cv2, np, rotation
        self.ocr = PaddleOCR(
            text_detection_model_name="PP-OCRv5_server_det",
            text_recognition_model_name="latin_PP-OCRv5_mobile_rec",
            device="gpu:0" if device == "cuda:0" else "cpu",
            enable_mkldnn=False,
            cpu_threads=4, text_det_limit_side_len=1600, text_det_limit_type="max",
            use_doc_orientation_classify=False, use_doc_unwarping=False,
            use_textline_orientation=False, text_rec_score_thresh=0.0)

    def recognize(self, request):
        image = self.cv2.imdecode(self.np.fromfile(request["image"], dtype=self.np.uint8), self.cv2.IMREAD_COLOR)
        if image is None:
            raise ValueError("Không đọc được ảnh trang.")
        height, width = image.shape[:2]
        if self.rotation:
            image = self.np.ascontiguousarray(self.np.rot90(image, -(self.rotation // 90)))
        results = list(self.ocr.predict(image))
        if len(results) != 1:
            raise ValueError("PaddleOCR không trả về đúng một trang.")
        return convert(results[0], request, width, height, self.rotation)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", choices=("cpu", "cuda:0"), default="cpu")
    parser.add_argument("--rotation", type=int, choices=(0, 90, 180, 270), default=0)
    parser.add_argument("--prepare", action="store_true")
    args = parser.parse_args()
    try:
        with contextlib.redirect_stdout(sys.stderr):
            engine = Engine(args.device, args.rotation)
        emit(dict(type="ready", protocol=1, model=MODEL, device=args.device))
    except Exception as error:
        emit(dict(type="error", error=str(error)))
        return 1
    if args.prepare:
        return 0
    for raw in sys.stdin:
        request = {}
        try:
            request = json.loads(raw)
            with contextlib.redirect_stdout(sys.stderr):
                result = engine.recognize(request)
            emit(result)
        except Exception as error:
            emit(dict(type="error", id=request.get("id"), error=str(error)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
