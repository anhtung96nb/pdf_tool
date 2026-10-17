from pathlib import Path
from PIL import Image, ImageOps, ImageDraw
from pypdf import PdfReader

root = Path(__file__).resolve().parents[2]
reader = PdfReader(root / 'output/pdf/cleanup-demo.pdf')
assert len(reader.pages) == 8
assert 'ORIGINAL TEXT KEPT 12345' in reader.pages[0].extract_text()
assert 'ALIGNMENT LINE 23' in reader.pages[3].extract_text()
assert reader.pages[7].get('/Annots')
images = sorted((root / 'tmp/pdfs').glob('cleanup-page-*.png'))
assert len(images) == 8, len(images)
sheet = Image.new('RGB', (1600, 1200), '#dddddd')
draw = ImageDraw.Draw(sheet)
for index, path in enumerate(images):
    with Image.open(path) as page:
        thumb = ImageOps.contain(page.convert('RGB'), (370, 550))
        x, y = (index % 4) * 400 + 15, (index // 4) * 600 + 35
        sheet.paste(thumb, (x, y))
        draw.text((x, y - 20), f'Output page {index + 1}', fill='black')
sheet.save(root / 'tmp/pdfs/contact-sheet.png')
print('PASS: pypdf page count, native text, annotation; 8 Poppler page renders.')
