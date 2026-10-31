"""Optional export QA: requires markdown-it-py and beautifulsoup4 in the test Python."""
from pathlib import Path
import sys

from bs4 import BeautifulSoup
from markdown_it import MarkdownIt

root = Path(sys.argv[1])
parser = MarkdownIt("commonmark", {"html": True})


def rendered(folder):
    path = next((root / folder).glob("*.md"))
    return BeautifulSoup(parser.render(path.read_text(encoding="utf-8")), "html.parser")


document = rendered("markdown")
assert [h.get_text() for h in document.find_all("h1")] == ["OCR: văn bản.pdf"]
assert [h.get_text() for h in document.find_all("h2")] == ["Trang 1 / 2", "Trang 2 / 2"]
assert len(document.find_all("hr")) == 1
assert "Nội dung tiếng Việt trang 1" in document.get_text()
assert "Nội dung tiếng Việt trang 2" in document.get_text()

literal = rendered("markdown-escape")
assert len(literal.find_all("br")) >= 4
text = literal.get_text()
assert "# Giữ *nguyên* [nhãn](https://example.invalid)" in text
assert "<b>&amp;</b> | 1." in text
assert "Không biến thành code" in text
assert not literal.find_all(["a", "img", "b", "script", "pre", "code", "table"])
assert len(literal.find_all("h1")) == 1
assert len(literal.find_all("h2")) == 2

failed = rendered("markdown-error")
assert "Test page failed" in failed.find("blockquote").get_text()
assert "Nội dung tiếng Việt trang 1" in failed.get_text()
print("COMMONMARK RENDER PASS")
