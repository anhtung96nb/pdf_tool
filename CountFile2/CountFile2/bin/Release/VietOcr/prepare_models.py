"""Run during setup, never during OCR. Save config/weights for offline inference."""
import hashlib
import json
from pathlib import Path
import urllib.request

import yaml


def load_config(name):
    url = "https://raw.githubusercontent.com/pbcquoc/vietocr/master/config/" + name
    with urllib.request.urlopen(url, timeout=60) as response:
        return yaml.safe_load(response.read().decode("utf-8"))


def main():
    root = Path(__file__).resolve().parent / "models"
    root.mkdir(exist_ok=True)
    config = load_config("base.yml")
    config.update(load_config("vgg-seq2seq.yml"))
    target = root / "vgg_seq2seq.pth"
    if not target.exists():
        temporary = target.with_suffix(".download")
        try:
            print("Downloading VietOCR weights:", config["weights"], flush=True)
            with urllib.request.urlopen(config["weights"], timeout=120) as response, temporary.open("wb") as stream:
                while True:
                    chunk = response.read(1024 * 1024)
                    if not chunk:
                        break
                    stream.write(chunk)
            # Check the downloaded object is a readable state_dict before publishing.
            import torch
            torch.load(str(temporary), map_location="cpu", weights_only=True)
            temporary.replace(target)
        finally:
            temporary.unlink(missing_ok=True)
    config["cnn"]["pretrained"] = False
    config["predictor"]["beamsearch"] = False
    config["device"] = "cpu"
    config["weights"] = target.name
    (root / "config.yaml").write_text(yaml.safe_dump(config, allow_unicode=True), encoding="utf-8")
    checksum = hashlib.sha256(target.read_bytes()).hexdigest()
    (root / "manifest.json").write_text(json.dumps({"model": "vgg_seq2seq", "sha256": checksum}, indent=2), encoding="utf-8")
    print("Model ready:", target, "sha256:", checksum, flush=True)


if __name__ == "__main__":
    main()
