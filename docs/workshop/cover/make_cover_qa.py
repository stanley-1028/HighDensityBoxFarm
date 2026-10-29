"""Cover QA + cleanup + title mockups for Seedream output (docs/workshop/cover/seedream-raw.jpg).

Run:
  C:\\Users\\user\\.dsh\\dsh-runtimes\\dsh-primary-runtime\\dependencies\\python\\python.exe \
      docs/workshop/cover/make_cover_qa.py

Produces (all in docs/workshop/cover/):
  clean-1920x1080.jpg        watermark cropped away, exactly 16:9
  qa-preview-322.png         clean image at browse-grid size
  qa-center-square-1080.png  clean image, centre 1:1 crop
  mockup-A-*.jpg             title top-left, no panel
  mockup-B-*.jpg             title top-left on a dark panel
  mockup-C-*.jpg             title on a bottom band
  + each mockup at 322 px wide for small-size legibility check
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
RAW = HERE / "seedream-raw.jpg"
FONT_TITLE = r"C:\Windows\Fonts\impact.ttf"
FONT_SUB = r"C:\Windows\Fonts\ariblk.ttf"

CREAM = (247, 236, 208, 255)
DARK = (32, 20, 12, 255)
SHADOW = (12, 8, 5, 170)
PANEL = (28, 18, 12, 165)
GOLD = (255, 226, 160, 255)


def find_watermark_bbox(im: Image.Image) -> tuple[int, int, int, int]:
    """Find the light-grey 豆包 AI watermark in the bottom-right corner."""
    w, h = im.size
    box = (int(w * 0.82), int(h * 0.90), w, h)
    crop = im.crop(box).convert("RGB")
    px = crop.load()
    minx, miny, maxx, maxy = crop.width, crop.height, -1, -1
    for y in range(crop.height):
        for x in range(crop.width):
            r, g, b = px[x, y]
            if r > 150 and g > 150 and b > 150 and max(r, g, b) - min(r, g, b) < 40:
                minx, miny = min(minx, x), min(miny, y)
                maxx, maxy = max(maxx, x), max(maxy, y)
    if maxx < 0:
        raise SystemExit("watermark not found")
    return box[0] + minx, box[1] + miny, box[0] + maxx, box[1] + maxy


def fit_169(im: Image.Image) -> Image.Image:
    w, h = im.size
    if w / h > 16 / 9:
        new_w = int(round(h * 16 / 9))
        left = (w - new_w) // 2
        im = im.crop((left, 0, left + new_w, h))
    else:
        new_h = int(round(w * 9 / 16))
        top = (h - new_h) // 2
        im = im.crop((0, top, w, top + new_h))
    return im.resize((1920, 1080), Image.LANCZOS)


def title_layer(size, lines, font_path, font_size, sub, sub_size):
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    f = ImageFont.truetype(font_path, font_size)
    fs = ImageFont.truetype(FONT_SUB, sub_size)
    x, y = 62, 58
    for line in lines:
        # soft shadow
        draw.text((x + 7, y + 8), line, font=f, fill=SHADOW, stroke_width=9, stroke_fill=SHADOW)
        draw.text((x, y), line, font=f, fill=CREAM, stroke_width=7, stroke_fill=DARK)
        y += int(font_size * 1.08)
    draw.text((x + 4, y + 2), sub, font=fs, fill=SHADOW, stroke_width=5, stroke_fill=SHADOW)
    draw.text((x, y), sub, font=fs, fill=GOLD, stroke_width=4, stroke_fill=DARK)
    return layer


def main() -> None:
    raw = Image.open(RAW).convert("RGB")
    w, h = raw.size
    # The 豆包 AI watermark sits in the bottom-right strip (measured: y = h-90 .. h-22,
    # x = w-285 .. w-20). Cropping the bottom 105 px removes it without touching the
    # beavers, and fit_169 then trims only the left/right edges.
    clean = raw.crop((0, 0, w, h - 105))
    print("after watermark crop:", clean.size)
    clean = fit_169(clean)
    clean.save(HERE / "clean-1920x1080.jpg", quality=92, optimize=True)

    # Browse-grid legibility + centre square crop (Steam square-thumbnail variant)
    clean.resize((322, 181), Image.LANCZOS).save(HERE / "qa-preview-322.png")
    left = (1920 - 1080) // 2
    clean.crop((left, 0, left + 1080, 1080)).save(HERE / "qa-center-square-1080.png")

    lines = ["HIGH-DENSITY", "BOX FARM"]
    sub = "600 CROPS  ·  3x3  ·  STACKABLE"

    # A: plain top-left title
    a = clean.convert("RGBA")
    a.alpha_composite(title_layer(a.size, lines, FONT_TITLE, 122, sub, 40))
    a.convert("RGB").save(HERE / "mockup-A-1920x1080.jpg", quality=92, optimize=True)
    a.convert("RGB").resize((322, 181), Image.LANCZOS).save(HERE / "mockup-A-322.png")

    # B: same, on a soft dark panel for guaranteed contrast
    b = clean.convert("RGBA")
    panel = Image.new("RGBA", b.size, (0, 0, 0, 0))
    pd = ImageDraw.Draw(panel)
    pd.rounded_rectangle((34, 30, 1215, 470), radius=26, fill=PANEL)
    panel = panel.filter(ImageFilter.GaussianBlur(18))
    b.alpha_composite(panel)
    b.alpha_composite(title_layer(b.size, lines, FONT_TITLE, 122, sub, 40))
    b.convert("RGB").save(HERE / "mockup-B-1920x1080.jpg", quality=92, optimize=True)
    b.convert("RGB").resize((322, 181), Image.LANCZOS).save(HERE / "mockup-B-322.png")

    # C: full-width bottom band, single-line title
    c = clean.convert("RGBA")
    band = Image.new("RGBA", c.size, (0, 0, 0, 0))
    bd = ImageDraw.Draw(band)
    bd.rectangle((0, 900, 1920, 1080), fill=(22, 14, 9, 185))
    band = band.filter(ImageFilter.GaussianBlur(6))
    c.alpha_composite(band)
    dc = ImageDraw.Draw(c)
    f = ImageFont.truetype(FONT_TITLE, 96)
    fs = ImageFont.truetype(FONT_SUB, 38)
    t = "HIGH-DENSITY BOX FARM"
    tw = dc.textlength(t, font=f)
    dc.text(((1920 - tw) / 2 + 5, 928), t, font=f, fill=SHADOW, stroke_width=7, stroke_fill=SHADOW)
    dc.text(((1920 - tw) / 2, 923), t, font=f, fill=CREAM, stroke_width=6, stroke_fill=DARK)
    sw = dc.textlength(sub, font=fs)
    dc.text(((1920 - sw) / 2, 1018), sub, font=fs, fill=GOLD, stroke_width=4, stroke_fill=DARK)
    c.convert("RGB").save(HERE / "mockup-C-1920x1080.jpg", quality=92, optimize=True)
    c.convert("RGB").resize((322, 181), Image.LANCZOS).save(HERE / "mockup-C-322.png")

    print("done:", sorted(p.name for p in HERE.glob("*")))


if __name__ == "__main__":
    main()
