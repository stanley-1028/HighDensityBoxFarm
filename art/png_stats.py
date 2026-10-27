"""HDBF v2 - deterministic PNG decoding + patch statistics (pure Python friendly).

Why this module exists
----------------------
The ART-05 material fix (2026-10-27) replaced the fake 2048x2048 ART-01 atlas
with **per-vanilla-material flat patches taken from the real `_D` albedo PNG of
each MaterialCollection material** (the textures extracted from the game and
kept read-only under ``%USERPROFILE%\\.hdbf\\sdk\\vanilla-tex-all`` and
``...\\vanilla-tex-extra``).  The patch statistics (mean RGB, per-channel std)
must be reproducible by the offline generator (``material_texture_analysis.py``)
and re-checkable by the validators, so both decode the *same* PNG bytes with the
same convention:

* PNG 8-bit colour types 0/2/4/6 (grey / RGB / grey+alpha / RGBA); 16-bit and
  palette images are rejected because the extracted vanilla albedo textures are
  8-bit RGBA.  Exception: PIL, when installed, is used as a fast decoder and is
  verified to produce the identical RGB byte stream convention (we always take
  the colour channels, alpha is ignored).
* Values are the **raw sRGB bytes** (0..255), NOT linear light.  This matches
  what the game (and the earlier ``gen_textures.py`` patch check) treats as
  "albedo value", and it makes the ``std <= 18/255`` threshold a stable,
  human-readable number.
* ``pixel_rect`` is ``(x0, y0, x1, y1)`` with a TOP-LEFT pixel origin and
  exclusive ``x1``/``y1``, exactly like ``art/atlas_layout.REGIONS`` and
  ``material_texture_map.json``; the PNG rows are decoded top-down.

``std`` definition (used everywhere in this pipeline)
-----------------------------------------------------
For a patch: mean and population std are computed per R/G/B channel over all
patch pixels; the reported ``std`` is the **largest of the three channel stds**
(the worst channel), so "std <= X/255" is a conservative uniformity statement.

The module is pure Python (``zlib``/``struct`` only); ``numpy`` is used only in
``patch_stats`` when available and is optional.  No bpy import: the pure-Python
``validate_timbermesh.py`` and the Blender validators can both import it.
"""

from __future__ import annotations

import struct
import zlib

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


# --------------------------------------------------------------------------- #
# decoding
# --------------------------------------------------------------------------- #
def _paeth(a, b, c):
    p = a + b - c
    pa = p - a
    if pa < 0:
        pa = -pa
    pb = p - b
    if pb < 0:
        pb = -pb
    pc = p - c
    if pc < 0:
        pc = -pc
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def _decode_pure(data):
    """Decode an 8-bit non-interlaced PNG to (width, height, channels, rgb)."""
    if data[:8] != PNG_SIGNATURE:
        raise ValueError("not a PNG file")
    pos = 8
    width = height = bit_depth = color_type = interlace = None
    idat = []
    palette = None
    while pos < len(data):
        if pos + 8 > len(data):
            break
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        ctype = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + length]
        pos += 12 + length  # 4 len + 4 type + data + 4 crc
        if ctype == b"IHDR":
            width, height, bit_depth, color_type, _comp, _filt, interlace = (
                struct.unpack(">IIBBBBB", payload))
        elif ctype == b"IDAT":
            idat.append(payload)
        elif ctype == b"PLTE":
            palette = payload
        elif ctype == b"IEND":
            break
    if width is None:
        raise ValueError("PNG has no IHDR")
    if bit_depth != 8 or interlace != 0:
        raise ValueError("unsupported PNG (bit depth %r, interlace %r): need 8-bit "
                         "non-interlaced" % (bit_depth, interlace))
    if color_type not in (0, 2, 4, 6):
        raise ValueError("unsupported PNG colour type %r" % (color_type,))
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[color_type]
    stride = width * channels
    raw = zlib.decompress(b"".join(idat))
    if len(raw) < (stride + 1) * height:
        raise ValueError("truncated PNG image data")
    out = bytearray(stride * height)
    prev = bytearray(stride)
    for y in range(height):
        base = y * (stride + 1)
        ftype = raw[base]
        row = bytearray(raw[base + 1:base + 1 + stride])
        if ftype == 0:
            pass
        elif ftype == 1:  # Sub
            for i in range(channels, stride):
                row[i] = (row[i] + row[i - channels]) & 0xFF
        elif ftype == 2:  # Up
            for i in range(stride):
                row[i] = (row[i] + prev[i]) & 0xFF
        elif ftype == 3:  # Average
            for i in range(channels):
                row[i] = (row[i] + (prev[i] >> 1)) & 0xFF
            for i in range(channels, stride):
                row[i] = (row[i] + ((row[i - channels] + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:  # Paeth
            for i in range(channels):
                row[i] = (row[i] + prev[i]) & 0xFF
            for i in range(channels, stride):
                row[i] = (row[i] + _paeth(row[i - channels], prev[i],
                                          prev[i - channels])) & 0xFF
        else:
            raise ValueError("bad PNG filter type %r at row %d" % (ftype, y))
        out[y * stride:(y + 1) * stride] = row
        prev = row
    if color_type == 6:
        rgb = bytearray(width * height * 3)
        for i in range(width * height):
            rgb[3 * i:3 * i + 3] = out[4 * i:4 * i + 3]
        return width, height, 3, bytes(rgb)
    if color_type == 4:
        rgb = bytearray(width * height * 3)
        for i in range(width * height):
            v = out[2 * i]
            rgb[3 * i] = v
            rgb[3 * i + 1] = v
            rgb[3 * i + 2] = v
        return width, height, 3, bytes(rgb)
    if color_type == 0:
        rgb = bytearray(width * height * 3)
        for i in range(width * height):
            v = out[i]
            rgb[3 * i] = v
            rgb[3 * i + 1] = v
            rgb[3 * i + 2] = v
        return width, height, 3, bytes(rgb)
    return width, height, 3, bytes(out)


def decode_png(path):
    """Decode a PNG file to a dict.

    Keys: ``path``, ``width``, ``height``, ``channels`` (always 3 for the
    returned ``rgb`` stream), ``rgb`` (``width*height*3`` raw sRGB bytes,
    row-major top-down), ``decoder`` ("pil" or "pure-python").
    """
    with open(path, "rb") as handle:
        data = handle.read()
    try:  # PIL is optional: fast path, identical byte values
        from PIL import Image  # noqa: F401  (import inside try on purpose)
        img = Image.open(path)
        if img.size[0] <= 0 or img.size[1] <= 0:
            raise ValueError("empty image")
        rgb = img.convert("RGB").tobytes()
        return {
            "path": path,
            "width": int(img.size[0]),
            "height": int(img.size[1]),
            "channels": 3,
            "rgb": rgb,
            "decoder": "pil",
        }
    except ImportError:
        pass
    width, height, channels, rgb = _decode_pure(data)
    return {
        "path": path,
        "width": width,
        "height": height,
        "channels": channels,
        "rgb": rgb,
        "decoder": "pure-python",
    }


# --------------------------------------------------------------------------- #
# patch statistics
# --------------------------------------------------------------------------- #
def _clamp_rect(rect, width, height):
    x0, y0, x1, y1 = [int(round(v)) for v in rect]
    x0 = max(0, min(x0, width - 1))
    y0 = max(0, min(y0, height - 1))
    x1 = max(x0 + 1, min(x1, width))
    y1 = max(y0 + 1, min(y1, height))
    return x0, y0, x1, y1


def patch_stats(decoded, pixel_rect):
    """Mean RGB + per-channel std of a top-left-origin pixel rect of a decoded PNG.

    Returns ``{mean_rgb, std, std_by_channel, min_rgb, max_rgb, size_px,
    pixels}`` with ``std`` = max channel std (see module docstring).  Values are
    raw sRGB 0..255 floats.
    """
    width = decoded["width"]
    height = decoded["height"]
    rgb = decoded["rgb"]
    x0, y0, x1, y1 = _clamp_rect(pixel_rect, width, height)
    try:
        import numpy as np
        arr = np.frombuffer(rgb, dtype=np.uint8).reshape(height, width, 3)
        win = arr[y0:y1, x0:x1, :].astype(np.float64)
        flat = win.reshape(-1, 3)
        mean = flat.mean(axis=0)
        std_by_channel = flat.std(axis=0)
        return {
            "mean_rgb": [round(float(v), 4) for v in mean],
            "std": round(float(std_by_channel.max()), 4),
            "std_by_channel": [round(float(v), 4) for v in std_by_channel],
            "min_rgb": [int(v) for v in win.reshape(-1, 3).min(axis=0)],
            "max_rgb": [int(v) for v in win.reshape(-1, 3).max(axis=0)],
            "size_px": [x1 - x0, y1 - y0],
            "pixels": (x1 - x0) * (y1 - y0),
        }
    except ImportError:
        pass
    # pure-python fallback
    n = (x1 - x0) * (y1 - y0)
    sums = [0.0, 0.0, 0.0]
    sumsq = [0.0, 0.0, 0.0]
    mins = [255, 255, 255]
    maxs = [0, 0, 0]
    stride = width * 3
    for y in range(y0, y1):
        row_start = y * stride
        for x in range(x0, x1):
            p = row_start + x * 3
            for c in range(3):
                v = rgb[p + c]
                sums[c] += v
                sumsq[c] += v * v
                if v < mins[c]:
                    mins[c] = v
                if v > maxs[c]:
                    maxs[c] = v
    mean = [s / n for s in sums]
    stds = []
    for c in range(3):
        var = sumsq[c] / n - mean[c] * mean[c]
        stds.append(var ** 0.5 if var > 0 else 0.0)
    return {
        "mean_rgb": [round(v, 4) for v in mean],
        "std": round(max(stds), 4),
        "std_by_channel": [round(v, 4) for v in stds],
        "min_rgb": mins,
        "max_rgb": maxs,
        "size_px": [x1 - x0, y1 - y0],
        "pixels": n,
    }


def uv_rect_from_pixel_rect(pixel_rect, width, height):
    """top-left pixel rect -> Unity/Blender bottom-left UV rect [u0, v0, u1, v1]."""
    x0, y0, x1, y1 = pixel_rect
    return [x0 / float(width), 1.0 - y1 / float(height),
            x1 / float(width), 1.0 - y0 / float(height)]


def texture_sha256(path):
    import hashlib
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def main(argv=None):
    import argparse
    import json
    import os
    parser = argparse.ArgumentParser(description="HDBF PNG patch statistics")
    parser.add_argument("png")
    parser.add_argument("pixel_rect", nargs=4, type=int,
                        help="x0 y0 x1 y1 (top-left origin, exclusive x1/y1)")
    args = parser.parse_args(argv)
    if not os.path.isfile(args.png):
        raise SystemExit("missing PNG: " + args.png)
    decoded = decode_png(args.png)
    stats = patch_stats(decoded, args.pixel_rect)
    stats["texture"] = os.path.basename(args.png)
    stats["texture_size"] = [decoded["width"], decoded["height"]]
    stats["sha256"] = texture_sha256(args.png)
    stats["decoder"] = decoded["decoder"]
    stats["uv_rect"] = [round(v, 6) for v in
                        uv_rect_from_pixel_rect(args.pixel_rect,
                                                decoded["width"], decoded["height"])]
    print(json.dumps(stats, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
