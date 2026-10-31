"""Small, optional substep of a page OCR request. No extra models or downloads."""
import math
import re


def empty_number(status="disabled", **extra):
    result = dict(raw_text="", value=None, status=status, confidence=None,
                  confirmed=False, source="vietocr_roi", polygon_points=[],
                  polygon_pixels=[], candidates=[], warnings=[])
    result.update(extra)
    return result


def read_number(engine, image, request, to_original):
    settings = request.get("page_number") or {}
    if not settings.get("enabled", False):
        return empty_number()
    try:
        return _read(engine, image, request, settings, to_original)
    except Exception as error:
        return empty_number("error", warnings=[str(error)])


def _read(engine, image, request, settings, to_original):
    cv2, np = engine.cv2, engine.np
    right, top = float(settings.get("right_percent", 20)), float(settings.get("top_percent", 10))
    if not (1 <= right <= 100 and 1 <= top <= 100):
        raise ValueError("Vùng số trang phải từ 1 đến 100 phần trăm.")
    h, w = image.shape[:2]
    x0, y1 = int(w * (1 - right / 100)), max(1, int(math.ceil(h * top / 100)))
    roi = image[:y1, x0:]
    original_w, original_h = request["original_width"], request["original_height"]

    def polygons(rect):
        x, y, rw, rh = rect
        pixels = [to_original(x0 + a, b) for a, b in ((x, y), (x + rw, y), (x + rw, y + rh), (x, y + rh))]
        points = [[round(a * request["width_points"] / original_w, 3),
                   round(b * request["height_points"] / original_h, 3)] for a, b in pixels]
        return [[round(a, 2), round(b, 2)] for a, b in pixels], points

    roi_pixels, roi_points = polygons((0, 0, roi.shape[1], roi.shape[0]))
    result = empty_number("not_found", roi_polygon_pixels=roi_pixels, roi_polygon_points=roi_points,
                          roi=dict(right_percent=right, top_percent=top), rotation_clockwise=getattr(engine, "rotation", 0))
    gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
    # Do not run a recognizer on a blank/uniform crop (it can hallucinate digits).
    if float(gray.std()) < 2 or int((gray < 200).sum()) < 6:
        return result
    boxes, _ = engine.detector.text_det(roi)
    rects = []

    def add_rect(rect):
        x, y, rw, rh = map(int, rect)
        pad = max(2, int(rh * 0.08))
        l, t = max(0, x - pad), max(0, y - pad)
        r, b = min(roi.shape[1], x + rw + pad), min(roi.shape[0], y + rh + pad)
        if r - l < 3 or b - t < 5:
            return
        new = (l, t, r - l, b - t)
        for a, c, aw, ah in rects:
            intersection = max(0, min(r, a + aw) - max(l, a)) * max(0, min(b, c + ah) - max(t, c))
            if intersection / max(1, min(aw * ah, (r - l) * (b - t))) > 0.75:
                return
        rects.append(new)

    for box in [] if boxes is None else boxes:
        add_rect(cv2.boundingRect(box.astype(np.float32)))
    # Connected components also find isolated digits that a text-line detector misses.
    _, mask = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
    count, _, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    components = [tuple(map(int, s[:4])) for s in stats[1:count]
                  if s[4] >= 5 and s[3] >= max(5, y1 * 0.025) and s[2] >= 2
                  and s[2] < s[3] * 12 and s[3] < y1 * 0.95]
    groups = []
    for x, y, rw, rh in sorted(components, key=lambda r: r[0]):
        found = None
        for group in groups:
            a, b, aw, ah = group
            overlap = min(y + rh, b + ah) - max(y, b)
            if overlap > min(rh, ah) * 0.4 and x - (a + aw) < max(rh, ah) * 1.3:
                found = group
                break
        if found is None:
            groups.append([x, y, rw, rh])
        else:
            a, b, aw, ah = found
            found[:] = [min(a, x), min(b, y), max(a + aw, x + rw) - min(a, x), max(b + ah, y + rh) - min(b, y)]
    for rect in sorted(groups, key=lambda r: r[1]):
        add_rect(rect)
    if not rects:
        result.update(status="needs_review", warnings=["ink_without_number_candidate"])
        return result
    candidates = []
    for rect in rects[:6]:
        x, y, rw, rh = rect
        crop = roi[y:y + rh, x:x + rw]
        pixels, points = polygons(rect)
        candidate = dict(polygon_pixels=pixels, polygon_points=points, readings=[])
        gray_crop = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
        variants = [("original", cv2.cvtColor(crop, cv2.COLOR_BGR2RGB)),
                    ("gray", cv2.cvtColor(cv2.normalize(gray_crop, None, 0, 255, cv2.NORM_MINMAX), cv2.COLOR_GRAY2RGB))]
        for variant, sample in variants:
            try:
                raw, score = engine.predictor.predict(engine.Image.fromarray(sample), return_prob=True)
                score = float(score) if score is not None else None
                if score is not None and not math.isfinite(score):
                    score = None
                label = raw.strip()
                candidate["readings"].append(dict(raw_text=raw, value=label if re.fullmatch(r"[0-9]{1,32}", label) else None,
                                                   confidence=score, variant=variant))
            except Exception as error:
                candidate["readings"].append(dict(raw_text="", value=None, confidence=None, variant=variant, error=str(error)))
        candidates.append(candidate)
    result["candidates"] = candidates
    numeric = [(c, r) for c in candidates for r in c["readings"] if r["value"] is not None]
    readings = [(c, r) for c in candidates for r in c["readings"]]
    best_c, best = max(numeric or readings, key=lambda p: p[1]["confidence"] or 0)
    result.update(raw_text=best["raw_text"], confidence=best["confidence"],
                  polygon_pixels=best_c["polygon_pixels"], polygon_points=best_c["polygon_points"])
    labels = {r["value"] for _, r in numeric}
    agreed = [r for r in best_c["readings"] if r["value"] == best["value"] and (r["confidence"] or 0) >= 0.9]
    if len(labels) == 1 and len(agreed) == 2:
        result.update(status="recognized", value=best["value"], confidence=min(r["confidence"] for r in agreed))
    else:
        result.update(status="needs_review", warnings=["ambiguous_or_low_score_number"])
    if len(rects) > 6:
        result.update(status="needs_review", value=None, warnings=result["warnings"] + ["too_many_candidates"])
    return result
