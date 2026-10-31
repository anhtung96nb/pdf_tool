"""Local JSON-lines worker. Only protocol messages go to stdout; no network at inference."""
import argparse
import contextlib
import hashlib
import json
import math
from pathlib import Path
import sys
import unicodedata
from page_number import read_number

PROTOCOL = sys.stdout


def emit(value):
    PROTOCOL.write(json.dumps(value, ensure_ascii=False, allow_nan=False) + "\n")
    PROTOCOL.flush()


def reading_order(boxes):
    """Recursive whitespace cut; spanning headings separate before column cuts."""
    if len(boxes) < 2:
        return boxes
    heights = sorted(max(1, b["bottom"] - b["top"]) for b in boxes)
    unit = heights[len(heights) // 2]

    def gap(axis, threshold):
        lo, hi = ("left", "right") if axis == "x" else ("top", "bottom")
        ordered = sorted(boxes, key=lambda b: b[lo])
        end, best = ordered[0][hi], None
        for i, box in enumerate(ordered[1:], 1):
            space = box[lo] - end
            if space > threshold and (best is None or space > best[0]):
                best = (space, ordered[:i], ordered[i:])
            end = max(end, box[hi])
        return best

    cut = gap("y", unit * 1.5) or gap("x", unit * 2) or gap("y", unit * 0.35)
    if cut:
        return reading_order(cut[1]) + reading_order(cut[2])
    # Align fragments of a physical line before sorting left to right.
    rows = []
    for box in sorted(boxes, key=lambda b: (b["top"], b["left"])):
        if rows and abs(box["top"] - rows[-1][0]["top"]) < unit * 0.5:
            rows[-1].append(box)
        else:
            rows.append([box])
    return [b for row in rows for b in sorted(row, key=lambda b: b["left"])]


def original_point(x, y, width, height, rotation):
    if rotation == 90:
        return y, height - x
    if rotation == 180:
        return width - x, height - y
    if rotation == 270:
        return width - y, x
    return x, y


class Engine:
    def __init__(self, device, rotation):
        import cv2
        import numpy as np
        import torch
        import yaml
        from PIL import Image
        from rapidocr_onnxruntime import RapidOCR
        from vietocr.tool.predictor import Predictor

        if device == "cuda:0" and not torch.cuda.is_available():
            raise RuntimeError("Không có CUDA khả dụng. Hãy chọn CPU.")
        if device not in ("cpu", "cuda:0"):
            raise ValueError("Thiết bị không hợp lệ.")
        torch.set_num_threads(min(4, max(1, torch.get_num_threads())))
        self.cv2, self.np, self.Image = cv2, np, Image
        self.rotation = rotation
        root = Path(__file__).resolve().parent
        config_path = root / "models" / "config.yaml"
        if not config_path.is_file():
            raise FileNotFoundError("Chưa có model. Chạy VietOcr/setup.ps1 trước.")
        with config_path.open(encoding="utf-8") as stream:
            config = yaml.safe_load(stream)
        weights = root / "models" / "vgg_seq2seq.pth"
        if not weights.is_file():
            raise FileNotFoundError("Thiếu trọng số vgg_seq2seq.pth. Chạy lại setup.ps1.")
        manifest_path = root / "models" / "manifest.json"
        if manifest_path.is_file():
            expected = json.loads(manifest_path.read_text(encoding="utf-8"))["sha256"]
            digest = hashlib.sha256()
            with weights.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            if digest.hexdigest() != expected:
                raise RuntimeError("Trọng số model bị thay đổi hoặc hỏng. Hãy chạy lại cài đặt model.")
        config["weights"] = str(weights)
        config["device"] = device
        config["cnn"]["pretrained"] = False
        config["predictor"]["beamsearch"] = False
        self.predictor = Predictor(config)
        self.max_ratio = config["dataset"]["image_max_width"] / config["dataset"]["image_height"]
        self.detector = RapidOCR(use_rec=False, use_cls=False, det_limit_side_len=1920, det_limit_type="max",
                                 intra_op_num_threads=2, inter_op_num_threads=2)

    def recognize_crop(self, crop):
        """Split long lines at whitespace so the recognizer does not squeeze/truncate them."""
        cv2, np = self.cv2, self.np
        height, width = crop.shape[:2]
        # Moderate horizontal resizing is normal for VietOCR. Split only very long
        # lines, preserving word boundaries rather than gaps between letters.
        limit = max(32, int(height * self.max_ratio * 2))
        start, texts, scores, warnings = 0, [], [], []
        ink = (cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY) < 200).mean(axis=0)
        while start < width:
            end = min(width, start + limit)
            if end < width:
                begin = start + int(limit * 0.6)
                mask = ink[begin:end] < 0.015
                edges = np.diff(np.concatenate(([False], mask, [False])).astype(np.int8))
                runs = [(int(a), int(b)) for a, b in zip(np.flatnonzero(edges == 1), np.flatnonzero(edges == -1))
                        if b - a >= max(2, height * 0.15)]
                if runs:
                    a, b = runs[-1]
                    end = begin + (a + b) // 2
                else:
                    warnings.append("long_line_split_without_space")
            part = crop[:, start:end]
            text, score = self.predictor.predict(self.Image.fromarray(cv2.cvtColor(part, cv2.COLOR_BGR2RGB)), return_prob=True)
            texts.append(text)
            if len(text) >= 125:
                warnings.append("possible_sequence_truncation")
            score = float(score) if score is not None else None
            scores.append(score if score is not None and math.isfinite(score) else None)
            start = end
        valid_scores = [score for score in scores if score is not None]
        return " ".join(texts), min(valid_scores) if len(valid_scores) == len(scores) else None, sorted(set(warnings))

    def recognize(self, request):
        cv2, np = self.cv2, self.np
        # imdecode supports Windows Unicode paths, unlike some builds of imread.
        image = cv2.imdecode(np.fromfile(request["image"], dtype=np.uint8), cv2.IMREAD_COLOR)
        if image is None:
            raise ValueError("Không đọc được ảnh trang.")
        height, width = image.shape[:2]
        if self.rotation:
            image = np.ascontiguousarray(np.rot90(image, -(self.rotation // 90)))
        request = dict(request, original_width=width, original_height=height)
        to_original = lambda x, y: original_point(x, y, width, height, self.rotation)
        if request.get("operation") == "page_number":
            return {"type": "page", "id": request["id"], "document_page_number": read_number(self, image, request, to_original)}
        boxes, _ = self.detector.text_det(image)
        entries = []
        for box in [] if boxes is None else boxes:
            entries.append({"polygon": box, "left": float(box[:, 0].min()),
                            "top": float(box[:, 1].min()), "right": float(box[:, 0].max()),
                            "bottom": float(box[:, 1].max())})
        warnings, lines = [], []
        # Table structure is intentionally not inferred from recognition output.
        for index, entry in enumerate(reading_order(entries), 1):
            box = entry["polygon"].astype(np.float32)
            original = [original_point(float(x), float(y), width, height, self.rotation) for x, y in box]
            polygon = [[round(min(width, max(0, x)), 2), round(min(height, max(0, y)), 2)] for x, y in original]
            points = [[round(x * request["width_points"] / width, 3),
                       round(y * request["height_points"] / height, 3)] for x, y in polygon]
            line = {"id": str(request["page"]) + "-" + str(index), "reading_order": index,
                    "polygon_pixels": polygon, "polygon_points": points,
                    "raw_text": "", "text": "", "confidence": None, "warnings": []}
            try:
                crop = self.detector.get_crop_img_list(image, [box])[0]
                crops, angles, _ = self.detector.text_cls([crop])
                crop = crops[0]
                text, probability, crop_warnings = self.recognize_crop(crop)
                line["warnings"].extend(crop_warnings)
                line.update(raw_text=text, text=unicodedata.normalize("NFC", text).strip(),
                            confidence=probability, crop_rotation=angles[0][0])
                if not line["text"] or probability is None or probability < 0.8:
                    line["warnings"].append("low_recognition_score")
            except Exception as error:
                line["warnings"].append("recognition_failed: " + str(error))
            lines.append(line)
        if not entries:
            gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            warnings.append("no_text_detected_blank_or_missed" if float((gray < 200).mean()) > 0.001 else "blank_page")
        bad_lines = sum(bool(line["warnings"]) for line in lines)
        if bad_lines:
            warnings.append("check_flagged_lines")
        failed = bool(lines) and all(any(w.startswith("recognition_failed:") for w in line["warnings"]) for line in lines)
        result = {"type": "page", "id": request["id"], "status": "failed" if failed else "needs_review" if bad_lines or "no_text_detected_blank_or_missed" in warnings else "completed",
                    "image_width": width, "image_height": height, "warnings": warnings,
                    "text": "\n".join(line["text"] for line in lines), "lines": lines,
                    "layout_note": "Thứ tự đọc theo khoảng trắng; bảng được lưu dưới dạng dòng chữ và tọa độ, chưa khôi phục ô bảng."}
        if failed:
            result["error"] = "Không nhận dạng được dòng chữ nào. Xem cảnh báo từng dòng."
        result["document_page_number"] = read_number(self, image, request, to_original)
        return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--rotation", type=int, choices=(0, 90, 180, 270), default=0)
    args = parser.parse_args()
    try:
        with contextlib.redirect_stdout(sys.stderr):
            engine = Engine(args.device, args.rotation)
        emit({"type": "ready", "protocol": 1, "model": "vgg_seq2seq", "device": args.device, "capabilities": ["page_number_v1"]})
    except Exception as error:
        emit({"type": "error", "error": str(error)})
        return 1
    for raw in sys.stdin:
        request = {}
        try:
            request = json.loads(raw)
            with contextlib.redirect_stdout(sys.stderr):
                result = engine.recognize(request)
            emit(result)
        except Exception as error:
            emit({"type": "error", "id": request.get("id"), "error": str(error)})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
