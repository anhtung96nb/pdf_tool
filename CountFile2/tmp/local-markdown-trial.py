"""Test the specified already-running Gemma model using localhost only."""
import importlib.util
import json
import pathlib
import sys
import time
import hashlib

import requests
import jsonschema

ROOT = pathlib.Path(__file__).resolve().parent.parent
MODEL = "ggml-org/gemma-3-1b-it-qat-GGUF:Q4_0"
BASE = "http://127.0.0.1:9931/v1"
spec = importlib.util.spec_from_file_location("markdown_helpers", ROOT / "tmp/gemini-markdown-trial.py")
helpers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helpers)


def save(path, value):
    path.write_text(value if isinstance(value, str) else json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def report(output, result):
    lines = ["# Kiểm tra Gemma local với Markdown", "", "- Model: `" + MODEL + "`.", "- API: `" + BASE + "`.",
             "- Trạng thái thử trích xuất: **" + result["status"] + "**.",
             "- Chỉ gọi localhost; không gọi Gemini hoặc API cloud.",
             "- Đầu vào: toàn bộ Markdown 2 trang, 119 dòng có mã bằng chứng; chưa đối chiếu ảnh/PDF.",
             "- Model chỉ hỗ trợ văn bản. Mọi giá trị đều chưa được xác nhận.", ""]
    for attempt in result["attempts"]:
        lines.append("- Lượt %d: HTTP %s; %.2f giây; finish_reason=%s. %s" %
                     (attempt["attempt"], attempt.get("http_status", "—"), attempt["seconds"], attempt.get("finish_reason", "—"), attempt.get("error", "")))
    if result["status"] == "completed":
        lines += ["", "## Giá trị có chữ nguồn đối chiếu", "", "| Trường | Giá trị | Trang | Mã bằng chứng |", "|---|---|---|---|"]
        for field in result["fields"]:
            if field["value"] is not None:
                value = field["value"].replace("|", "\\|").replace("\n", "<br>")
                lines.append("| %s | %s | %s | %s |" % (field["key"], value, ", ".join(map(str, field["source_pages"])), ", ".join(field["evidence_ids"])))
        lines += ["", "## Vấn đề cần rà soát", ""]
        lines += ["- " + issue for issue in result["issues"]]
        for field in result["fields"]:
            if field.get("issue"):
                lines.append("- `%s`: %s" % (field["key"], field["issue"]))
            if field.get("date_warning"):
                lines.append("- `%s`: %s" % (field["key"], field["date_warning"]))
        if result["requested_image_pages"]:
            lines.append("- Model yêu cầu ảnh trang " + ", ".join(map(str, result["requested_image_pages"])) + "; thử nghiệm này chỉ có Markdown.")
    else:
        lines += ["", "Chưa nhận được kết quả đầy đủ, đúng schema và mã bằng chứng. Phản hồi gốc được lưu riêng; chưa chấp nhận làm dữ liệu đã xác nhận."]
    lines += ["", "Đây là kiểm tra một tài liệu. JSON đúng cấu trúc và có chữ nguồn không tự chứng minh việc chọn đúng ngày, cơ quan hoặc người ký."]
    save(output / "report.md", "\n".join(lines) + "\n")


def main():
    source, output = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
    output.mkdir(parents=True, exist_ok=False)
    original = source.read_text(encoding="utf-8-sig")
    annotated, mapping = helpers.annotate(original)
    schema = helpers.make_schema()
    prompt = (ROOT / "artifacts/gemini-markdown-20261026-part13-01/system-prompt.txt").read_text(encoding="utf-8")
    session = requests.Session()
    session.trust_env = False
    listed = session.get(BASE + "/models", timeout=5, allow_redirects=False)
    listed.raise_for_status()
    selected = next((m for m in listed.json().get("data", []) if m.get("id") == MODEL), None)
    if selected is None:
        raise RuntimeError("Requested model missing from local /v1/models; no fallback")
    model_info = {key: selected.get(key) for key in ("id", "owned_by", "architecture", "context_length", "meta")}
    model_info["loaded_status"] = selected.get("status", {}).get("value")
    save(output / "model-info.json", model_info)
    save(output / "input-with-line-ids.md", annotated)
    save(output / "evidence-map.json", mapping)
    save(output / "system-prompt.txt", prompt)
    result = {"provider": "CompatibleLocal", "base_url": BASE, "model_requested": MODEL, "model_reported": None,
              "source_markdown": str(source), "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "page_count_from_markdown": max(m["page"] for m in mapping.values()), "source_line_count": len(mapping),
              "status": "failed", "attempts": [], "cloud_called": False, "pdf_sent": False, "fields": [], "issues": []}
    feedback = ""
    for number in range(1, 3):
        body = {"model": MODEL, "stream": False, "temperature": 0, "max_tokens": 4096,
                "messages": [{"role": "system", "content": prompt}, {"role": "user", "content": [{"type": "text", "text": annotated + feedback}]}],
                "response_format": {"type": "json_schema", "json_schema": {"name": "vietnamese_document", "strict": True, "schema": schema}}}
        save(output / ("request-%02d.json" % number), body)
        attempt, started = {"attempt": number}, time.monotonic()
        print("Local Markdown extraction: attempt %d/2; %d lines; %s" % (number, len(mapping), MODEL), flush=True)
        try:
            response = session.post(BASE + "/chat/completions", json=body, timeout=(5, 180), allow_redirects=False)
            attempt["http_status"] = response.status_code
            payload = response.json()
            save(output / ("response-%02d.json" % number), payload)
            if response.status_code != 200:
                attempt["error"] = str(payload.get("error", "HTTP error"))[:700]
                break
            result["model_reported"] = payload.get("model")
            result["usage"] = payload.get("usage", {})
            result["timings"] = payload.get("timings", {})
            choices = payload.get("choices", [])
            if len(choices) != 1:
                raise ValueError("Expected exactly one choice")
            attempt["finish_reason"] = choices[0].get("finish_reason")
            text = choices[0].get("message", {}).get("content", "")
            save(output / ("model-output-%02d.txt" % number), text)
            if attempt["finish_reason"] != "stop":
                attempt["error"] = "Model did not complete; partial JSON is not accepted"
                break
            data = json.loads(text)
            save(output / ("model-output-%02d.json" % number), data)
            helpers.validate(data, schema, mapping)
            result.update(status="completed", document_groups=data["document_groups"], fields=helpers.grounded_fields(data, mapping),
                          requested_image_pages=data["requested_image_pages"], issues=data["issues"])
            break
        except (ValueError, jsonschema.ValidationError) as error:
            attempt["error"] = str(error)[:700]
            feedback = "\n\nLượt trước không hợp lệ: " + attempt["error"] + ". Trả lại JSON đúng schema. evidence_ids chỉ chứa các mã [pN:lNNN] đã gửi; không dùng các mã tự tạo."
        except requests.RequestException as error:
            attempt["error"] = type(error).__name__ + ": " + str(error)[:500]
            break
        finally:
            attempt["seconds"] = round(time.monotonic() - started, 2)
            result["attempts"].append(attempt)
            save(output / "result.json", result)
            report(output, result)
            print(json.dumps(attempt, ensure_ascii=True), flush=True)
    print(json.dumps({"status": result["status"], "model_reported": result["model_reported"], "fields": len(result["fields"]),
                      "nonempty_fields": sum(f["value"] is not None for f in result["fields"]), "output": str(output)}, ensure_ascii=True), flush=True)
    return 0 if result["status"] == "completed" else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print("Local trial stopped: " + type(error).__name__ + ": " + str(error)[:800], flush=True)
        sys.exit(1)
