from PIL import Image, ImageDraw
from pathlib import Path

src = Path(r"C:\Users\LENOVO\.cursor\projects\f-work-SaaS-QuickFind-QuickFind\assets\QuickFind-icon-1024.jpg")
assets = Path(__file__).resolve().parent
out = assets / "QuickFind.ico"
png_out = assets / "QuickFind.png"

img = Image.open(src).convert("RGBA")
w, h = img.size
side = min(w, h)
left = (w - side) // 2
top = (h - side) // 2
img = img.crop((left, top, left + side, top + side))


def rounded_mask(size: int, radius_ratio: float = 0.22) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    r = int(size * radius_ratio)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=r, fill=255)
    return m


base = img.resize((512, 512), Image.Resampling.LANCZOS)
base.putalpha(rounded_mask(512))
base.save(png_out, format="PNG")

sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
base.save(out, format="ICO", sizes=sizes)
print(f"wrote {out} ({out.stat().st_size} bytes)")
print(f"wrote {png_out} ({png_out.stat().st_size} bytes)")
