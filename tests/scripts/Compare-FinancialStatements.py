"""Compose visual QA evidence; never change the approved reference images."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[2]
output = root / 'docs/verification/financial-statements'
for kind, reference in [('profit-loss', 'resultatrakning'), ('balance-sheet', 'balansrakning')]:
    target = Image.open(root / f'docs/design/references/{reference}-reference.png').convert('RGB')
    actual = Image.open(output / f'{kind}-native.jpg').convert('RGB')
    assert target.size == (1536, 1024)
    # The browser's normal screenshot can omit its scrollbar gutter and uniformly
    # scale the capture. Normalize only the comparison canvas, keeping raw evidence.
    assert abs(actual.width / actual.height - 1.5) < 0.002, (kind, actual.size)
    if actual.size != target.size:
        actual = actual.resize(target.size, Image.Resampling.LANCZOS)
    comparison = Image.new('RGB', (3072, 1024), 'white')
    comparison.paste(target, (0, 0))
    comparison.paste(actual, (1536, 0))
    comparison.save(output / f'{kind}-side-by-side.png')
    Image.blend(target, actual, 0.5).save(output / f'{kind}-overlay.png')
