#!/usr/bin/env python3
"""HDBF Box Farm -- deterministic procedural PBR atlas generator.

Runs inside Blender 4.2.1 headless (or any Python 3 with numpy):

    blender -b --factory-startup -noaudio --python art\\gen_textures.py

Outputs (all 2048 x 2048, 8 bit, no timestamps / metadata / text chunks):

    art/textures/atlas_albedo.png   RGB   sRGB colour, stylised material albedo
    art/textures/atlas_normal.png   RGB   LINEAR, tangent-space normal, OpenGL (+Y up)
    art/textures/atlas_mask.png     RGBA  LINEAR, R=metallic G=roughness
                                                B=metallic  A=smoothness (255-G)

The atlas layout, material bands, semantic pixel rects and the per-material
metallic / roughness values are NOT hard coded here: they are imported from
``art/atlas_layout.py``, the shared contract consumed by ``gen_models.py``
(face packing) and ``validate_models.py``.

Determinism
-----------
All randomness comes from an integer hash of (pixel, seed).  The ``random``
module is never used, there is no time / filesystem / locale input, and the PNG
encoder is hand written (zlib + struct) with no tIME / tEXt chunks.  Two runs on
the same machine therefore produce byte identical files; the script compares
against the files already on disk and prints the verdict.

Patch safety
------------
Model faces are packed into arbitrary sub-rects of a region, so no texture
detail may be a single large unique feature.  Every region is built from
continuous (non-repeating) multi-scale value noise plus small structural motifs
(planks, plates, rivets, weave, strands, seedlings) whose feature size is a
small fraction of the region, so any sub-window still reads as that material.

Requirement REQ-ART-08: no text, letters, digits, logos, watermarks, signatures,
barcodes or brand marks are generated anywhere.
"""

from __future__ import annotations

import hashlib
import math
import os
import struct
import sys
import time
import traceback
import zlib

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import atlas_layout as L  # noqa: E402  (shared art contract)

ATLAS = L.ATLAS_SIZE
TEXTURE_DIR = os.path.join(HERE, "textures")
SEED = 0x48444246544558A5  # "HDBFTEXA" - master seed, fixed forever

# --------------------------------------------------------------------------- #
# deterministic hashing / noise
# --------------------------------------------------------------------------- #

_U64 = np.uint64
_M64 = _U64(0xFFFFFFFFFFFFFFFF)
_INV_53 = 1.0 / 9007199254740992.0  # 2**-53


def _hash_u64(ix, iy, seed):
    """splitmix64-style integer hash of two integer lattices + seed."""
    x = np.asarray(ix, dtype=np.int64).astype(np.uint64)
    y = np.asarray(iy, dtype=np.int64).astype(np.uint64)
    h = (x * _U64(0x9E3779B97F4A7C15)) & _M64
    h = h ^ ((y * _U64(0xC2B2AE3D27D4EB4F)) & _M64)
    h = h ^ _U64(int(seed) & 0xFFFFFFFFFFFFFFFF)
    h = h ^ (h >> _U64(30))
    h = (h * _U64(0xBF58476D1CE4E5B9)) & _M64
    h = h ^ (h >> _U64(27))
    return h


def hash01(ix, iy, seed):
    """Deterministic uniform float64 in [0, 1) from integer coordinates."""
    h = _hash_u64(ix, iy, seed)
    return (h >> _U64(11)).astype(np.float64) * _INV_53


def hashf(i, seed):
    """Scalar variant of hash01 (float in [0, 1))."""
    v = hash01(np.array([int(i)], np.int64), np.zeros(1, np.int64), seed)
    return float(v[0])


def vnoise(cellx, celly, seed, X, Y):
    """Smooth (value) noise in [0, 1): lattice hashed, smoothstep interpolated.

    ``X`` / ``Y`` are float64 pixel coordinate arrays; they may be:
      * ``(1, w)`` and ``(h, 1)``   -> axis separable fields (cheap), or
      * any pair broadcastable to ``(h, w)`` (e.g. rotated / warped coords).
    """
    fx = np.asarray(X, dtype=np.float64) / float(cellx)
    fy = np.asarray(Y, dtype=np.float64) / float(celly)
    ix = np.floor(fx)
    iy = np.floor(fy)
    tx = fx - ix
    ty = fy - iy
    ix = ix.astype(np.int64)
    iy = iy.astype(np.int64)
    tx = tx * tx * (3.0 - 2.0 * tx)
    ty = ty * ty * (3.0 - 2.0 * ty)
    v00 = hash01(ix, iy, seed)
    v10 = hash01(ix + 1, iy, seed)
    v01 = hash01(ix, iy + 1, seed)
    v11 = hash01(ix + 1, iy + 1, seed)
    a = v00 + (v10 - v00) * tx
    b = v01 + (v11 - v01) * tx
    return a + (b - a) * ty


def fbm(cellx, celly, seed, X, Y, octaves=4, gain=0.5, lac=2.0, ridged=False):
    """Fractal sum of value noise, normalised to [0, 1].

    ``cellx`` / ``celly`` are the *pixel* size of the first octave lattice cell;
    cell sizes are clamped to >= 1.1 px so the highest octaves stay stable.
    """
    total = None
    amp = 1.0
    norm = 0.0
    cx = float(cellx)
    cy = float(celly)
    for i in range(octaves):
        n = vnoise(max(cx, 1.1), max(cy, 1.1), seed + 7919 * i, X, Y)
        if ridged:
            n = 1.0 - np.abs(2.0 * n - 1.0)
        if total is None:
            total = amp * n
        else:
            total = total + amp * n
        norm += amp
        amp *= gain
        cx /= lac
        cy /= lac
    return total / norm


# --------------------------------------------------------------------------- #
# small math helpers
# --------------------------------------------------------------------------- #

def clamp01(x):
    return np.clip(x, 0.0, 1.0)


def smoothstep(e0, e1, x):
    t = clamp01((np.asarray(x, dtype=np.float64) - float(e0)) / (float(e1) - float(e0)))
    return t * t * (3.0 - 2.0 * t)


def mixc(c0, c1, t):
    """Mix two RGB triples with a scalar or (h, w) weight array."""
    a = np.asarray(c0, np.float64)
    b = np.asarray(c1, np.float64)
    if isinstance(t, np.ndarray) and t.ndim > 0:
        return a + (b - a) * t[..., None]
    return a + (b - a) * float(t)


def rgb(*v):
    return np.asarray(v, np.float32)


def region_seed(name):
    """Stable per-region 64 bit seed derived from the region name."""
    h = 0x811C9DC5
    for ch in name:
        h = ((h ^ ord(ch)) * 0x01000193) & 0xFFFFFFFF
    return (SEED ^ (h * 0x9E3779B1)) & 0xFFFFFFFFFFFFFFFF


# --------------------------------------------------------------------------- #
# Region painter base class
# --------------------------------------------------------------------------- #

class Region:
    """One semantic atlas region: local float buffers + stamping helpers."""

    def __init__(self, name):
        mat, rect = L.REGIONS[name]
        self.name = name
        self.mat = mat
        self.rect = rect
        x0, y0, x1, y1 = rect
        self.w = x1 - x0
        self.h = y1 - y0
        self.seed = region_seed(name)
        # sentinel -1.0 means "neutral initialiser, not painted yet"
        self.rgb = np.full((self.h, self.w, 3), -1.0, np.float32)
        self.hgt = np.full((self.h, self.w), -1.0, np.float32)
        self.met = np.full((self.h, self.w), -1.0, np.float32)
        self.rough = np.full((self.h, self.w), -1.0, np.float32)
        self.painted = np.zeros((self.h, self.w), bool)
        self.X = np.arange(self.w, dtype=np.float64)[None, :]
        self.Y = np.arange(self.h, dtype=np.float64)[:, None]

    # -- base fill -------------------------------------------------------- #
    def begin(self, color, height=0.0):
        mp = L.MATERIAL_PARAMS[self.mat]
        self.rgb[...] = np.asarray(color, np.float32)
        self.hgt[...] = np.float32(height)
        self.met[...] = np.float32(mp["metallic"])
        self.rough[...] = np.float32(mp["roughness"])
        self.painted[...] = True

    # -- windows / stamps -------------------------------------------------- #
    def window(self, cx, cy, rad):
        x0 = max(0, int(math.floor(cx - rad)))
        x1 = min(self.w, int(math.ceil(cx + rad)) + 1)
        y0 = max(0, int(math.floor(cy - rad)))
        y1 = min(self.h, int(math.ceil(cy + rad)) + 1)
        if x1 <= x0 or y1 <= y0:
            return None
        xs = np.arange(x0, x1, dtype=np.float64)[None, :]
        ys = np.arange(y0, y1, dtype=np.float64)[:, None]
        return (y0, y1, x0, x1), xs, ys

    def apply(self, win, m, color=None, alpha=1.0, dh=None, drough=None,
              dmet=None, set_rough=None, set_met=None):
        """Composite a masked stamp.  ``dh`` / ``drough`` / ``dmet`` are already
        mask-multiplied deltas; ``set_rough`` / ``set_met`` are lerp targets."""
        y0, y1, x0, x1 = win
        mm = np.asarray(m, dtype=np.float32)
        a = np.float32(alpha) * mm
        if color is not None:
            c = np.asarray(color, np.float32)
            sub = self.rgb[y0:y1, x0:x1]
            a3 = a[:, :, None]
            sub *= (1.0 - a3)
            sub += a3 * c
        # Scalar deltas are multiplied by the stamp mask (never the raw window);
        # array deltas are already mask-multiplied by the caller.
        if dh is not None:
            if np.isscalar(dh):
                self.hgt[y0:y1, x0:x1] += mm * np.float32(dh)
            else:
                self.hgt[y0:y1, x0:x1] += np.asarray(dh, np.float32)
        if drough is not None:
            if np.isscalar(drough):
                self.rough[y0:y1, x0:x1] += mm * np.float32(drough)
            else:
                self.rough[y0:y1, x0:x1] += np.asarray(drough, np.float32)
        if dmet is not None:
            if np.isscalar(dmet):
                self.met[y0:y1, x0:x1] += mm * np.float32(dmet)
            else:
                self.met[y0:y1, x0:x1] += np.asarray(dmet, np.float32)
        if set_rough is not None:
            r = self.rough[y0:y1, x0:x1]
            r += (np.float32(set_rough) - r) * a
        if set_met is not None:
            g = self.met[y0:y1, x0:x1]
            g += (np.float32(set_met) - g) * a

    def apply_full(self, m, **kw):
        self.apply((0, self.h, 0, self.w), m, **kw)

    def ellipse_dist(self, cx, cy, rx, ry, angle=0.0, edge=1.6):
        """Return (window, normalised radial distance) for an ellipse stamp."""
        rad = max(rx, ry) + edge + 2.0
        got = self.window(cx, cy, rad)
        if got is None:
            return None
        win, xs, ys = got
        dx = xs - cx
        dy = ys - cy
        if abs(angle) > 1e-9:
            ca = math.cos(angle)
            sa = math.sin(angle)
            u = dx * ca + dy * sa
            v = -dx * sa + dy * ca
        else:
            u = dx
            v = dy
        d = np.sqrt((u / rx) ** 2 + (v / ry) ** 2)
        return win, d

    def ellipse(self, cx, cy, rx, ry, angle=0.0, color=None, alpha=1.0,
                dh=None, drough=None, dmet=None, set_rough=None, set_met=None,
                edge=1.4):
        got = self.ellipse_dist(cx, cy, rx, ry, angle, edge)
        if got is None:
            return
        win, d = got
        m = clamp01((1.0 - d) * (min(rx, ry) / max(edge, 0.6)) + 0.5)
        self.apply(win, m, color=color, alpha=alpha, dh=dh, drough=drough,
                   dmet=dmet, set_rough=set_rough, set_met=set_met)

    def dome(self, cx, cy, r, color, alpha=1.0, ry=None, dh_amp=1.0,
             set_rough=None, set_met=None, edge=1.5):
        ry = r if ry is None else ry
        got = self.ellipse_dist(cx, cy, r, ry, 0.0, edge)
        if got is None:
            return
        win, d = got
        m = clamp01((1.0 - d) * (min(r, ry) / max(edge, 0.6)) + 0.5)
        prof = np.sqrt(np.clip(1.0 - d * d, 0.0, 1.0))
        self.apply(win, m, color=color, alpha=alpha, dh=dh_amp * prof,
                   set_rough=set_rough, set_met=set_met)

    def line(self, xa, ya, xb, yb, width, color=None, alpha=1.0, dh=None,
             drough=None, dmet=None, edge=1.3):
        length = math.hypot(xb - xa, yb - ya)
        got = self.window(0.5 * (xa + xb), 0.5 * (ya + yb),
                          0.5 * length + 0.5 * width + edge + 2.0)
        if got is None:
            return
        win, xs, ys = got
        dx = xb - xa
        dy = yb - ya
        l2 = dx * dx + dy * dy
        if l2 < 1e-9:
            d = np.hypot(xs - xa, ys - ya)
        else:
            t = np.clip(((xs - xa) * dx + (ys - ya) * dy) / l2, 0.0, 1.0)
            d = np.hypot(xs - (xa + t * dx), ys - (ya + t * dy))
        m = clamp01((0.5 * width + 0.5 * edge - d) / max(edge, 0.6))
        self.apply(win, m, color=color, alpha=alpha, dh=dh, drough=drough,
                   dmet=dmet)

    # -- row band helpers ------------------------------------------------- #
    def shade_rows(self, y0, y1, fac=None, add=None):
        y0 = max(0, int(y0))
        y1 = min(self.h, int(y1))
        if y1 <= y0:
            return
        if fac is not None:
            self.rgb[y0:y1] *= np.float32(fac)
        if add is not None:
            self.rgb[y0:y1] = np.clip(self.rgb[y0:y1] + np.asarray(add, np.float32), 0.0, 1.0)

    def hgt_rows(self, y0, y1, delta):
        y0 = max(0, int(y0))
        y1 = min(self.h, int(y1))
        if y1 > y0:
            self.hgt[y0:y1] += np.float32(delta)

    def rough_rows(self, y0, y1, delta):
        y0 = max(0, int(y0))
        y1 = min(self.h, int(y1))
        if y1 > y0:
            self.rough[y0:y1] += np.float32(delta)

    def met_rows(self, y0, y1, delta):
        y0 = max(0, int(y0))
        y1 = min(self.h, int(y1))
        if y1 > y0:
            self.met[y0:y1] += np.float32(delta)


# --------------------------------------------------------------------------- #
# palette
# --------------------------------------------------------------------------- #

WOOD = {
    "base": (0.462, 0.298, 0.168),
    "light": (0.668, 0.482, 0.285),
    "dark": (0.228, 0.136, 0.076),
    "grime": (0.138, 0.098, 0.060),
}
SOIL_FULL = {
    "dry": (0.282, 0.206, 0.128),
    "moist": (0.128, 0.088, 0.055),
    "grain": (0.352, 0.278, 0.176),
    "shadow": (0.048, 0.036, 0.026),
}
SOIL_EMPTY = {
    "dry": (0.340, 0.268, 0.168),
    "light": (0.486, 0.398, 0.262),
    "grain": (0.560, 0.510, 0.420),
    "shadow": (0.086, 0.066, 0.048),
}
LEAF = {
    "dark": (0.140, 0.300, 0.092),
    "mid": (0.252, 0.498, 0.138),
    "light": (0.470, 0.702, 0.238),
    "stem": (0.222, 0.412, 0.126),
}
STEEL = {
    "base": (0.428, 0.450, 0.492),
    "dark": (0.208, 0.222, 0.252),
    "mid": (0.520, 0.545, 0.590),
    "light": (0.726, 0.752, 0.800),
    "grime": (0.180, 0.170, 0.152),
}
FIXTURE = {
    "base": (0.262, 0.274, 0.306),
    "dark": (0.112, 0.118, 0.132),
    "light": (0.548, 0.568, 0.610),
    "grime": (0.110, 0.104, 0.092),
}
TRIM = {
    "base": (0.392, 0.352, 0.302),
    "dark": (0.176, 0.150, 0.122),
    "light": (0.630, 0.596, 0.532),
    "rust": (0.404, 0.248, 0.140),
    "grime": (0.138, 0.120, 0.096),
}
GLASS = {
    "base": (0.612, 0.758, 0.762),
    "warm": (0.686, 0.792, 0.770),
    "dirt": (0.500, 0.474, 0.408),
    "bright": (0.878, 0.936, 0.938),
    "shadow": (0.415, 0.560, 0.590),
}
#: v2.2 reference-image upgrade: warm cream trowelled plaster (Intermediate
#: light flat roof + plaster facade bands).  Replaces the ART-04 ochre canvas.
#: Values match the vanilla Plaster_White albedo (~0.68/0.62/0.54) so the patch
#: reads as cream plaster, not as a blown-white surface under the preview sun.
PLASTER = {
    "base": (0.648, 0.604, 0.512),
    "light": (0.742, 0.700, 0.606),
    "stain": (0.496, 0.458, 0.382),
    "shadow": (0.398, 0.362, 0.300),
}
THATCH = {
    "base": (0.752, 0.638, 0.322),
    "light": (0.886, 0.796, 0.500),
    "dark": (0.352, 0.278, 0.126),
    "shadow": (0.180, 0.140, 0.062),
}
#: v2.2 reference-image upgrade: damp green moss / foliage (Basic Folktails).
MOSS = {
    "base": (0.148, 0.204, 0.078),
    "dark": (0.080, 0.126, 0.042),
    "mid": (0.244, 0.356, 0.112),
    "light": (0.452, 0.590, 0.190),
    "stem": (0.300, 0.420, 0.140),
}


# --------------------------------------------------------------------------- #
# painters
# --------------------------------------------------------------------------- #

def paint_wood(rg):
    """Warm brown planks: grain, seams, butt joints, knots, wear, grime."""
    h, w = rg.h, rg.w
    s = rg.seed
    X = rg.X
    rg.begin(WOOD["base"], 0.0)

    # ---- plank row layout (randomised heights, seams are horizontal) ---- #
    edges = [0]
    i = 0
    while edges[-1] < h:
        step = 96 + int(round(36.0 * hashf(i, s ^ 0x51)))
        edges.append(min(h, edges[-1] + step))
        i += 1
    n_pl = len(edges) - 1

    for p in range(n_pl):
        a, b = edges[p], edges[p + 1]
        lp = b - a
        if lp < 8:
            continue
        ps = s + 4096 * (p + 1)
        ys = np.arange(lp, dtype=np.float64)[:, None]
        t0 = hashf(p, s ^ 0x61)
        t1 = hashf(p, s ^ 0x62)
        # wavy grain: warp the x coordinate with a low frequency field
        warp = fbm(170.0, 74.0, ps ^ 0x11, X, ys, octaves=2)
        xw = X + 22.0 * (warp - 0.5)
        grain = fbm(185.0, 8.0, ps ^ 0x12, xw, ys, octaves=4, gain=0.55)
        fibres = fbm(46.0, 2.3, ps ^ 0x13, xw, ys, octaves=3, gain=0.5)
        lines = clamp01(1.0 - np.abs(2.0 * grain - 1.0) * 3.2)

        col = np.asarray(WOOD["base"], np.float64) * (0.86 + 0.30 * t0)
        col = col * np.array([1.0 + 0.10 * (t1 - 0.5), 1.0 + 0.05 * (t1 - 0.5),
                              1.0 - 0.13 * (t1 - 0.5)])
        shade = ((0.80 + 0.38 * grain) * (1.0 - 0.20 * lines)
                 * (0.98 + 0.08 * (fibres - 0.5)))
        patch = col[None, None, :] * shade[:, :, None]

        yl = (ys + 0.5) / float(lp)
        crown = np.sin(np.pi * np.clip(yl, 0.0, 1.0))
        edge = clamp01(np.minimum(yl, 1.0 - yl) / 0.032)
        patch *= (0.56 + 0.44 * edge)[:, :, None]
        patch *= (0.88 + 0.20 * crown)[:, :, None]
        bevel = np.clip(1.0 - np.abs(np.minimum(yl, 1.0 - yl) - 0.030) / 0.020, 0.0, 1.0)
        patch += (bevel * 0.06)[:, :, None] * np.asarray(WOOD["light"], np.float64)

        hgt_p = (0.32 + 0.55 * crown) - 1.05 * (1.0 - edge)
        rough_p = 0.695 + 0.055 * (1.0 - lines) + 0.045 * (1.0 - edge)

        rg.rgb[a:b] = np.clip(patch, 0.0, 1.0).astype(np.float32)
        rg.hgt[a:b] = hgt_p.astype(np.float32)
        rg.rough[a:b] = rough_p.astype(np.float32)

        # ---- butt joints (vertical seams) + nails ---- #
        jx = 140.0 + 240.0 * hashf(p, s ^ 0x64)
        j = 0
        while jx < w - 24.0:
            js = ps ^ (0x300 + 13 * j)
            wob = (hashf(1, js) - 0.5) * 2.0
            jcol = np.asarray(WOOD["dark"], np.float64) * 0.70
            rg.line(jx, a + 2.0, jx + wob, b - 1.0, 2.0, color=jcol,
                    alpha=0.55, dh=-0.55)
            rg.line(jx + 1.8, a + 3.0, jx + wob + 1.8, b - 2.0, 1.1,
                    color=np.asarray(WOOD["light"], np.float64) * 1.03,
                    alpha=0.22, dh=0.18)
            if lp > 60:
                for dn in (-1.0, 1.0):
                    for yy in (a + 5.0, b - 5.0):
                        rg.ellipse(jx + dn * 6.0, yy, 1.7, 1.7,
                                   color=np.asarray(WOOD["dark"], np.float64) * 0.62,
                                   alpha=0.80, dh=-0.30)
            jx += 180.0 + 170.0 * hashf(2, js)
            j += 1

        # ---- knots ---- #
        nk = 0
        rk = hashf(p, s ^ 0x71)
        if rk < 0.60:
            nk = 1
        if rk > 0.955:
            nk = 2
        for j in range(nk):
            ks = ps ^ (0x80 + 29 * j)
            kx = 30.0 + (w - 60.0) * hashf(1, ks)
            ky = a + lp * (0.28 + 0.44 * hashf(2, ks))
            krx = 7.5 + 8.5 * hashf(3, ks)
            kry = krx * (0.52 + 0.34 * hashf(4, ks))
            ang = (hashf(5, ks) - 0.5) * 0.7
            got = rg.ellipse_dist(kx, ky, krx * 1.8, kry * 1.8, ang, 1.6)
            if got is not None:
                win, d = got
                core = smoothstep(1.0, 0.28, d)
                rings = (1.0 - clamp01(np.abs(d - 0.80) / 0.20)) * smoothstep(0.0, 0.22, d)
                halo = smoothstep(1.55, 1.05, d) * smoothstep(0.86, 1.04, d)
                darkc = np.asarray(WOOD["dark"], np.float32)
                rg.apply(win, core, color=darkc * 0.86, alpha=0.60,
                         dh=-0.55 * core, drough=0.06 * core)
                rg.apply(win, rings, color=darkc, alpha=0.26, dh=-0.12 * rings)
                rg.apply(win, halo, color=np.asarray(WOOD["light"], np.float32),
                         alpha=0.18, dh=0.06 * halo)

    # ---- seam distance field (for grime / wear placement) ---- #
    yl_g = np.zeros((h, 1), np.float64)
    lpp = np.zeros((h, 1), np.float64)
    for p in range(n_pl):
        a, b = edges[p], edges[p + 1]
        lp = b - a
        if lp <= 0:
            continue
        yl_g[a:b, 0] = (np.arange(lp) + 0.5) / float(lp)
        lpp[a:b, 0] = float(lp)
    seam_px = np.minimum(yl_g, 1.0 - yl_g) * lpp
    near_seam = clamp01(1.0 - seam_px / 16.0)

    # ---- scuffed / worn areas (lighter, glossier) ---- #
    wear = fbm(230.0, 190.0, s ^ 0xA1, X, rg.Y, octaves=3)
    wear_m = smoothstep(0.60, 0.80, wear)
    rg.apply_full(wear_m * 0.55, color=np.asarray(WOOD["light"], np.float32) * 1.02,
                  alpha=1.0, drough=-0.12 * wear_m)

    # ---- grime, darker in the plank seams ---- #
    grime = fbm(64.0, 52.0, s ^ 0xB2, X, rg.Y, octaves=4)
    gm = smoothstep(0.52, 0.82, grime) * (0.30 + 0.70 * near_seam)
    rg.apply_full(gm * 0.50, color=np.asarray(WOOD["grime"], np.float32),
                  alpha=1.0, drough=0.11 * gm)

    # ---- wear scratches / scuffs along the grain ---- #
    for j in range(280):
        ss = s ^ (0xC00 + 37 * j)
        x = w * hashf(1, ss)
        y = h * hashf(2, ss)
        ln = 10.0 + 46.0 * hashf(3, ss)
        ang = (hashf(4, ss) - 0.5) * 0.35
        if hashf(5, ss) > 0.35:
            c = np.asarray(WOOD["light"], np.float64) * (0.98 + 0.10 * hashf(6, ss))
            al = 0.10 + 0.16 * hashf(7, ss)
            rg.line(x, y, x + ln * math.cos(ang), y + ln * math.sin(ang),
                    0.9 + 0.7 * hashf(8, ss), color=c, alpha=al,
                    dh=0.05, drough=-0.10 * al)
        else:
            c = np.asarray(WOOD["dark"], np.float64) * 0.92
            al = 0.08 + 0.12 * hashf(7, ss)
            rg.line(x, y, x + ln * math.cos(ang), y + ln * math.sin(ang),
                    0.8 + 0.5 * hashf(8, ss), color=c, alpha=al,
                    dh=-0.05, drough=0.06 * al)

    # ---- a few darker cracks following the grain ---- #
    for j in range(70):
        ss = s ^ (0xD00 + 53 * j)
        x = w * hashf(1, ss)
        y = h * hashf(2, ss)
        ln = 20.0 + 70.0 * hashf(3, ss)
        rg.line(x, y, x + ln, y + (hashf(4, ss) - 0.5) * 3.0, 0.9,
                color=np.asarray(WOOD["dark"], np.float64) * 0.55,
                alpha=0.20 + 0.16 * hashf(5, ss), dh=-0.10)


def paint_soil_full(rg):
    """Dark moist soil with green seedlings, leaves, stems and specks."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(SOIL_FULL["moist"], 0.0)

    clod = fbm(62.0, 62.0, s ^ 0x11, X, Y, octaves=5, gain=0.55)
    moist = fbm(9.0, 9.0, s ^ 0x12, X, Y, octaves=3)
    gran = fbm(2.4, 2.4, s ^ 0x13, X, Y, octaves=2)

    wet = smoothstep(0.46, 0.76, moist)
    col = mixc(SOIL_FULL["dry"], SOIL_FULL["moist"], wet)
    col = col * (0.66 + 0.62 * clod)[..., None]
    # clod tops catch a little light
    tops = clamp01((clod - 0.62) * 2.2)
    col = col + (tops * 0.07)[..., None] * np.asarray(SOIL_FULL["grain"], np.float64)
    dark_g = smoothstep(0.74, 0.95, gran)
    light_g = smoothstep(0.26, 0.05, gran)
    col = col * (1.0 - 0.32 * dark_g)[..., None]
    col = col + (light_g * 0.10)[..., None] * np.asarray(SOIL_FULL["grain"], np.float64)
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (1.05 * clod - 1.10 * dark_g - 0.55 * (1.0 - wet)).astype(np.float32)
    rg.rough[...] = (0.60 + 0.28 * (1.0 - wet) + 0.05 * dark_g).astype(np.float32)

    # mossy patches
    moss = smoothstep(0.66, 0.90, fbm(30.0, 30.0, s ^ 0x14, X, Y, octaves=3))
    rg.apply_full(moss * 0.32, color=np.asarray(LEAF["dark"], np.float32),
                  alpha=1.0, drough=0.02 * moss)

    # ---- sprouts: pass 1 shadows, pass 2 stems + leaves ---- #
    n_sprout = int(round(w * h / 1050.0))
    n_sprout = max(28, min(n_sprout, 340))

    def params(i):
        ss = s ^ (0x500 + 17 * i)
        return {
            "ss": ss,
            "bx": 5.0 + (w - 10.0) * hashf(1, ss),
            "by": 9.0 + (h - 14.0) * hashf(2, ss),
            "hh": 8.0 + 11.0 * hashf(3, ss),
            "lean": (hashf(4, ss) - 0.5) * 9.0,
            "nleaf": 2 + int(hashf(5, ss) * 2.99),
        }

    for i in range(n_sprout):
        p = params(i)
        ss = p["ss"]
        shx = p["bx"] + p["lean"] * 0.45 + 1.0
        shy = p["by"] - p["hh"] * 0.45 + 1.7
        rg.ellipse(shx, shy, p["hh"] * 0.30, p["hh"] * 0.13,
                   color=SOIL_FULL["shadow"], alpha=0.40)
        rg.line(p["bx"] + 1.0, p["by"] + 1.6, p["bx"] + p["lean"] + 1.0,
                p["by"] - p["hh"] + 1.6, 1.6,
                color=SOIL_FULL["shadow"], alpha=0.30)

    for i in range(n_sprout):
        p = params(i)
        ss = p["ss"]
        bx, by, hh, lean = p["bx"], p["by"], p["hh"], p["lean"]
        nleaf = p["nleaf"]
        # stem
        rg.line(bx, by + 1.0, bx + lean, by - hh, 1.25,
                color=LEAF["stem"], alpha=0.95, dh=0.12)
        # leaves
        for j in range(nleaf):
            lj = ss ^ (0x40 + 13 * j)
            f = 0.52 + 0.46 * (j / max(1.0, float(nleaf - 1)))
            px = bx + lean * f
            py = by - hh * f
            side = 1.0 if (j % 2 == 0) else -1.0
            ang = side * (0.72 + 0.42 * hashf(1, lj)) + 0.22 * lean / 9.0
            lrx = 2.9 + 2.6 * hashf(2, lj)
            lry = lrx * (0.40 + 0.22 * hashf(3, lj))
            tone = hashf(4, lj)
            lc = mixc(LEAF["mid"], LEAF["light"], 0.25 + 0.75 * tone)
            # soft shadow under the leaf
            rg.ellipse(px + 0.7, py + 0.9, lrx * 1.10, lry * 1.35, ang,
                       color=SOIL_FULL["shadow"], alpha=0.22)
            # blade + pointed tip (a plain ellipse reads as a green blob)
            rg.ellipse(px, py, lrx, lry, ang, color=lc, alpha=0.97,
                       dh=0.22, drough=-0.10)
            ca = math.cos(ang)
            sa = math.sin(ang)
            rg.ellipse(px + ca * lrx * 0.62, py + sa * lrx * 0.62,
                       lrx * 0.55, lry * 0.62, ang, color=lc, alpha=0.95,
                       dh=0.16, drough=-0.10)
            # midrib highlight
            rg.line(px - ca * lrx * 0.50, py - sa * lrx * 0.50,
                    px + ca * lrx * 0.80, py + sa * lrx * 0.80, 0.8,
                    color=np.asarray(LEAF["light"], np.float64) * 1.05,
                    alpha=0.42, dh=0.05)
        # base speck of soil
        rg.ellipse(bx, by + 0.6, 2.0, 1.0, color=SOIL_FULL["shadow"], alpha=0.35)

    # ---- small green specks / tiny shoots ---- #
    for i in range(520):
        ss = s ^ (0x9000 + 31 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 0.5 + 1.0 * hashf(3, ss)
        tone = hashf(4, ss)
        lc = mixc(LEAF["dark"], LEAF["mid"], 0.35 + 0.65 * tone)
        if tone > 0.72:
            lc = mixc(LEAF["mid"], LEAF["light"], 0.55)
        rg.ellipse(cx, cy, rr, rr * (0.6 + 0.5 * hashf(5, ss)),
                   color=lc, alpha=0.60 + 0.30 * hashf(6, ss),
                   drough=-0.04)

    # ---- little pebbles / clods poking through ---- #
    for i in range(90):
        ss = s ^ (0xA000 + 19 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 1.6 + 2.6 * hashf(3, ss)
        tone = 0.20 + 0.18 * hashf(4, ss)
        pc = np.array([tone * 1.10, tone * 1.00, tone * 0.84], np.float64)
        rg.ellipse(cx + 0.8, cy + 1.0, rr * 1.2, rr * 0.9, color=SOIL_FULL["shadow"], alpha=0.35)
        rg.ellipse(cx, cy, rr, rr * 0.8, color=pc, alpha=0.9, dh=0.30)


def paint_soil_empty(rg):
    """Dry, clumpy, cracked soil with pebbles - no green."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(SOIL_EMPTY["dry"], 0.0)

    clod = fbm(46.0, 46.0, s ^ 0x21, X, Y, octaves=5, gain=0.55)
    fine = fbm(3.0, 3.0, s ^ 0x22, X, Y, octaves=2)
    ridge = fbm(17.0, 17.0, s ^ 0x23, X, Y, octaves=5, ridged=True)
    crev = smoothstep(0.72, 0.93, ridge)

    col = mixc(SOIL_EMPTY["dry"], SOIL_EMPTY["light"], clamp01(clod * 1.15 - 0.15))
    shade = (0.80 + 0.36 * clod) * (1.0 - 0.42 * crev)
    col = col * shade[..., None]
    dark_g = smoothstep(0.76, 0.96, fine)
    light_g = smoothstep(0.24, 0.05, fine)
    col = col * (1.0 - 0.30 * dark_g)[..., None]
    col = col + (light_g * 0.09)[..., None] * np.asarray(SOIL_EMPTY["grain"], np.float64)
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (1.25 * clod - 1.05 * crev - 0.40 * dark_g).astype(np.float32)
    rg.rough[...] = (0.82 + 0.07 * (1.0 - clod) + 0.04 * dark_g).astype(np.float32)

    # ---- pebbles ---- #
    for i in range(190):
        ss = s ^ (0x8000 + 23 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 1.3 + 3.4 * hashf(3, ss)
        ry = rr * (0.62 + 0.36 * hashf(4, ss))
        ang = (hashf(5, ss) - 0.5) * 3.0
        tone = 0.30 + 0.24 * hashf(6, ss)
        pc = np.array([tone * 1.06, tone * 1.00, tone * 0.88], np.float64)
        rg.ellipse(cx + 0.9, cy + 1.1, rr * 1.22, ry * 1.22, ang,
                   color=SOIL_EMPTY["shadow"], alpha=0.40)
        rg.ellipse(cx, cy, rr, ry, ang, color=pc, alpha=1.0,
                   dh=0.16 * rr, drough=-0.06)
        rg.ellipse(cx - rr * 0.28, cy - ry * 0.30, rr * 0.45, ry * 0.45, ang,
                   color=pc * 1.35, alpha=0.45, dh=0.05)

    # ---- dry clumps (raised aggregates) ---- #
    for i in range(120):
        ss = s ^ (0x8100 + 41 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 2.5 + 5.0 * hashf(3, ss)
        tone = hashf(4, ss)
        cc = mixc(SOIL_EMPTY["dry"], SOIL_EMPTY["light"], 0.3 + 0.5 * tone)
        rg.ellipse(cx + 1.0, cy + 1.2, rr * 1.1, rr * 0.75, angle=(hashf(5, ss) - 0.5),
                   color=SOIL_EMPTY["shadow"], alpha=0.30)
        rg.ellipse(cx, cy, rr, rr * 0.70, angle=(hashf(5, ss) - 0.5),
                   color=cc, alpha=0.55, dh=0.30)

    # ---- a few dead stalk fragments ---- #
    for i in range(46):
        ss = s ^ (0x8200 + 29 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        ang = 1.2 + (hashf(3, ss) - 0.5) * 0.8
        ln = 5.0 + 13.0 * hashf(4, ss)
        rg.line(cx, cy, cx + ln * math.cos(ang), cy - ln * math.sin(ang),
                1.0 + 0.6 * hashf(5, ss), color=rgb(0.42, 0.33, 0.20),
                alpha=0.55, dh=0.10)


def paint_metal(rg):
    """Cool grey riveted steel plates: seams, rivets, scratches, edge wear."""
    h, w = rg.h, rg.w
    s = rg.seed
    X = rg.X
    rg.begin(STEEL["base"], 0.0)

    # ---- plate rows ---- #
    edges = [0]
    i = 0
    while edges[-1] < h:
        edges.append(min(h, edges[-1] + 152 + int(round(48.0 * hashf(i, s ^ 0x11)))))
        i += 1

    for p in range(len(edges) - 1):
        a, b = edges[p], edges[p + 1]
        lp = b - a
        if lp < 8:
            continue
        ps = s ^ (0x1200 + 32 * p)
        ys = np.arange(lp, dtype=np.float64)[:, None]
        base = np.asarray(STEEL["base"], np.float64) * (0.92 + 0.16 * hashf(p, s ^ 0x12))
        base = base * np.array([1.0 + 0.03 * (hashf(p, s ^ 0x13) - 0.5),
                                1.0 + 0.01 * (hashf(p, s ^ 0x14) - 0.5),
                                1.0 + 0.05 * (hashf(p, s ^ 0x15) - 0.5)])
        brushed = fbm(340.0, 2.6, ps ^ 0x01, X, ys, octaves=4, gain=0.6)
        mott = fbm(125.0, 92.0, ps ^ 0x02, X, ys, octaves=3)
        yl = (ys + 0.5) / float(lp)
        edge = clamp01(np.minimum(yl, 1.0 - yl) / 0.048)
        col = base[None, None, :] * ((0.89 + 0.20 * brushed)
                                     * (0.90 + 0.16 * mott)
                                     * (0.70 + 0.30 * edge))[:, :, None]
        hi = np.clip(1.0 - np.abs(yl - 0.026) / 0.017, 0.0, 1.0)
        col = col + (hi * 0.06)[:, :, None] * np.asarray(STEEL["light"], np.float64)
        rg.rgb[a:b] = np.clip(col, 0.0, 1.0).astype(np.float32)
        rg.hgt[a:b] = (0.55 * edge - 0.55 + 0.16 * (brushed - 0.5)).astype(np.float32)
        rg.rough[a:b] = (0.470 + 0.055 * (1.0 - brushed) + 0.035 * (1.0 - edge)).astype(np.float32)

    # ---- seams between plates: groove + highlight + rivet rows ---- #
    for sy in edges[1:-1]:
        sy = int(sy)
        rg.shade_rows(sy - 2, sy + 2, fac=0.62)
        rg.hgt_rows(sy - 3, sy + 3, -0.85)
        rg.hgt_rows(sy - 4, sy + 4, -0.20)
        rg.rough_rows(sy - 2, sy + 2, 0.10)
        rg.shade_rows(sy - 4, sy - 3, add=rgb(0.05, 0.05, 0.05))
        rg.shade_rows(sy + 2, sy + 3, add=rgb(0.035, 0.035, 0.035))
        for side, sj in ((-7.0, 0), (8.0, 1)):
            x = 6.0 + 26.0 * hashf(sy * 3 + sj, s ^ 0x21)
            j = 0
            while x < w - 2.0:
                ss = (s ^ 0x2200) + (sy * 131) + (sj * 977) + (j * 17)
                rr = 3.4 + 1.4 * hashf(1, ss)
                cy = sy + side
                # shadow / seat
                rg.ellipse(x + 0.8, cy + 1.0, rr * 1.30, rr * 1.05,
                           color=STEEL["dark"], alpha=0.45, dh=-0.25 * rr / 4.0)
                got = rg.ellipse_dist(x, cy, rr, rr * 0.94, 0.0, 1.4)
                if got is not None:
                    win, d = got
                    m = clamp01((1.0 - d) * (rr / 1.4) + 0.5)
                    prof = np.sqrt(np.clip(1.0 - d * d, 0.0, 1.0))
                    rg.apply(win, m, color=STEEL["mid"], alpha=1.0,
                             dh=0.55 * prof, set_met=0.96, set_rough=0.33)
                    rg.ellipse(x - rr * 0.28, cy - rr * 0.34, rr * 0.46, rr * 0.40,
                               color=np.asarray(STEEL["light"], np.float64) * 1.02,
                               alpha=0.40, dh=0.15)
                    rg.ellipse(x, cy + rr * 0.58, rr * 0.72, rr * 0.24,
                               color=STEEL["dark"], alpha=0.35, dh=-0.10)
                x += 26.0 + 9.0 * hashf(2, ss)
                j += 1

    # ---- scratches: bright metal exposed + a few dark ones ---- #
    for j in range(560):
        ss = s ^ (0x3000 + 37 * j)
        x = w * hashf(1, ss)
        y = h * hashf(2, ss)
        ln = 5.0 + 26.0 * hashf(3, ss)
        ang = (hashf(4, ss) - 0.5) * 1.1
        if hashf(5, ss) > 0.30:
            c = np.asarray(STEEL["light"], np.float64) * (0.95 + 0.32 * hashf(6, ss))
            al = 0.16 + 0.26 * hashf(7, ss)
            rg.line(x, y, x + ln * math.cos(ang), y + ln * math.sin(ang),
                    0.7 + 0.7 * hashf(8, ss), color=c, alpha=al,
                    dh=0.06, drough=-0.14 * al, dmet=0.03 * al)
        else:
            c = np.asarray(STEEL["dark"], np.float64) * 0.92
            al = 0.12 + 0.20 * hashf(7, ss)
            rg.line(x, y, x + ln * math.cos(ang), y + ln * math.sin(ang),
                    0.7 + 0.6 * hashf(8, ss), color=c, alpha=al,
                    dh=-0.06, drough=0.10 * al)

    # ---- grime / staining (raises roughness, kills metallic response) ---- #
    grime = fbm(55.0, 40.0, s ^ 0x41, X, rg.Y, octaves=4)
    stains = fbm(6.0, 260.0, s ^ 0x42, X, rg.Y, octaves=3)
    gm = smoothstep(0.52, 0.82, grime) * (0.35 + 0.65 * smoothstep(0.35, 0.75, stains))
    rg.apply_full(gm * 0.55, color=np.asarray(STEEL["grime"], np.float32),
                  alpha=1.0, drough=0.24 * gm, dmet=-0.40 * gm)
    rg.hgt_rows(0, h, 0.0)

    # ---- pitting / tiny dark specks ---- #
    for j in range(300):
        ss = s ^ (0x5000 + 43 * j)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 0.6 + 1.2 * hashf(3, ss)
        rg.ellipse(cx, cy, rr, rr, color=STEEL["dark"], alpha=0.55, dh=-0.05)


def paint_fixture(rg):
    """Darker pipe / flange / bolt metal with soot and staining."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(FIXTURE["base"], 0.0)

    mott = fbm(70.0, 70.0, s ^ 0x31, X, Y, octaves=4)
    stain = fbm(5.0, 240.0, s ^ 0x32, X, Y, octaves=3)
    col = np.asarray(FIXTURE["base"], np.float64)[None, None, :] * (
        (0.84 + 0.28 * mott) * (0.86 + 0.24 * (1.0 - stain)))[..., None]
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (0.40 * (mott - 0.5) + 0.15 * (1.0 - stain)).astype(np.float32)
    rg.rough[...] = (0.560 + 0.14 * (1.0 - mott) + 0.09 * (1.0 - stain)).astype(np.float32)

    # ---- flanges with bolts ---- #
    y = 46.0
    bi = 0
    while y < h - 26.0:
        band_h = 9.5 + 4.5 * hashf(bi, s ^ 0x33)
        y0 = int(max(1, math.floor(y - band_h)))
        y1 = int(min(h - 1, math.ceil(y + band_h)))
        if y1 > y0:
            yy = np.arange(y0, y1, dtype=np.float64)[:, None] - y
            prof = np.clip(1.0 - np.abs(yy) / band_h, 0.0, 1.0)
            sm = prof * prof * (3.0 - 2.0 * prof)
            rg.rgb[y0:y1] *= (0.78 + 0.46 * sm)[:, :, None]
            rg.rgb[y0:y1] = np.clip(
                rg.rgb[y0:y1] + (sm * 0.05)[:, :, None] * np.asarray(FIXTURE["light"], np.float64),
                0.0, 1.0)
            rg.hgt[y0:y1] += (1.45 * sm).astype(np.float32)
            rg.rough[y0:y1] -= (0.07 * sm).astype(np.float32)
            rg.met[y0:y1] += (0.04 * sm).astype(np.float32)
            edge = np.clip(1.0 - np.abs(np.abs(yy) - band_h) / 2.0, 0.0, 1.0)
            rg.rgb[y0:y1] *= (1.0 - 0.40 * edge)[:, :, None]
            rg.hgt[y0:y1] -= (0.55 * edge).astype(np.float32)
        x = 8.0 + 30.0 * hashf(bi, s ^ 0x35)
        j = 0
        while x < w - 4.0:
            ss = (s ^ 0x3600) + (bi * 131) + (j * 19)
            rr = 4.0 + 1.4 * hashf(1, ss)
            rg.ellipse(x + 0.7, y + 1.0, rr * 1.28, rr * 1.10,
                       color=FIXTURE["dark"], alpha=0.45, dh=-0.20)
            got = rg.ellipse_dist(x, y, rr, rr * 0.95, 0.0, 1.3)
            if got is not None:
                win, d = got
                m = clamp01((1.0 - d) * (rr / 1.3) + 0.5)
                prof = np.sqrt(np.clip(1.0 - d * d, 0.0, 1.0))
                rg.apply(win, m, color=FIXTURE["light"], alpha=0.92,
                         dh=0.85 * prof, set_met=0.96, set_rough=0.34)
                rg.ellipse(x - rr * 0.26, y - rr * 0.30, rr * 0.52, rr * 0.42,
                           color=np.asarray(FIXTURE["light"], np.float64) * 1.25,
                           alpha=0.5, dh=0.20)
                rg.ellipse(x, y + rr * 0.62, rr * 0.70, rr * 0.22,
                           color=FIXTURE["dark"], alpha=0.35, dh=-0.10)
            x += 34.0 + 10.0 * hashf(2, ss)
            j += 1
        y += 120.0 + 70.0 * hashf(bi + 1, s ^ 0x34)
        bi += 1

    # ---- soot / grime concentrated between flanges ---- #
    grime = fbm(48.0, 34.0, s ^ 0x41, X, Y, octaves=4)
    gm = smoothstep(0.50, 0.80, grime) * (0.40 + 0.60 * smoothstep(0.30, 0.75, stain))
    rg.apply_full(gm * 0.62, color=np.asarray(FIXTURE["grime"], np.float32),
                  alpha=1.0, drough=0.26 * gm, dmet=-0.45 * gm)

    # ---- worn edges / scratches ---- #
    for j in range(320):
        ss = s ^ (0x7000 + 41 * j)
        x = w * hashf(1, ss)
        yy = h * hashf(2, ss)
        ln = 4.0 + 24.0 * hashf(3, ss)
        ang = (hashf(4, ss) - 0.5) * 1.3
        if hashf(5, ss) > 0.4:
            rg.line(x, yy, x + ln * math.cos(ang), yy + ln * math.sin(ang),
                    0.7 + 0.6 * hashf(6, ss),
                    color=np.asarray(FIXTURE["light"], np.float64) * (0.9 + 0.3 * hashf(7, ss)),
                    alpha=0.14 + 0.20 * hashf(8, ss), dh=0.05,
                    drough=-0.10, dmet=0.05)
        else:
            rg.line(x, yy, x + ln * math.cos(ang), yy + ln * math.sin(ang),
                    0.6 + 0.5 * hashf(6, ss),
                    color=FIXTURE["dark"], alpha=0.12 + 0.18 * hashf(8, ss),
                    dh=-0.05, drough=0.08)

    # ---- pitting ---- #
    for j in range(240):
        ss = s ^ (0x7100 + 47 * j)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 0.6 + 1.3 * hashf(3, ss)
        rg.ellipse(cx, cy, rr, rr, color=FIXTURE["dark"], alpha=0.55, dh=-0.04)


def paint_trim(rg):
    """Worn grey-brown trim metal: brushed streaks, scratches, rust flecks."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(TRIM["base"], 0.0)

    mott = fbm(95.0, 85.0, s ^ 0x61, X, Y, octaves=4)
    brushed = fbm(360.0, 2.3, s ^ 0x62, X, Y, octaves=4, gain=0.6)
    dent = fbm(26.0, 26.0, s ^ 0x63, X, Y, octaves=3)
    fine = fbm(2.8, 2.8, s ^ 0x66, X, Y, octaves=2)

    col = np.asarray(TRIM["base"], np.float64)[None, None, :] * (
        (0.88 + 0.20 * mott) * (0.90 + 0.20 * brushed)
        * (0.95 + 0.10 * (dent - 0.5)))[..., None]
    low = clamp01((0.5 - dent) * 2.2)
    col = col * (1.0 - 0.10 * low)[..., None]
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (0.55 * (dent - 0.5) + 0.18 * (brushed - 0.5)
                   + 0.20 * (fine - 0.5)).astype(np.float32)
    rg.rough[...] = (0.560 + 0.10 * (1.0 - mott) + 0.06 * (1.0 - brushed)
                     + 0.05 * (fine - 0.5) + 0.05 * low).astype(np.float32)
    rg.met[...] = (0.90 - 0.05 * (1.0 - mott)).astype(np.float32)

    # ---- rust: speckled flecks, not broad soft patches ---- #
    rust_n = fbm(30.0, 30.0, s ^ 0x64, X, Y, octaves=4)
    speck = smoothstep(0.78, 0.95, fbm(2.6, 2.6, s ^ 0x67, X, Y, octaves=2))
    rust = smoothstep(0.74, 0.90, rust_n) * (0.30 + 0.70 * speck)
    rc = np.asarray(TRIM["rust"], np.float64)
    rg.apply_full(rust * 0.85, color=rc, alpha=1.0,
                  drough=0.22 * rust, dmet=-0.55 * rust, dh=0.12 * rust)
    for j in range(120):
        ss = s ^ (0x7200 + 31 * j)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 0.7 + 1.8 * hashf(3, ss)
        rg.ellipse(cx, cy, rr, rr * (0.6 + 0.5 * hashf(4, ss)),
                   color=rc * 0.60, alpha=0.50, dh=-0.05, dmet=-0.30, drough=0.14)

    # ---- edge wear: soft bright streaks along the surface, never pure white ---- #
    for j in range(240):
        ss = s ^ (0x7400 + 37 * j)
        x = w * hashf(1, ss)
        y = h * hashf(2, ss)
        ln = 8.0 + 44.0 * hashf(3, ss)
        ang = (hashf(4, ss) - 0.5) * 0.16
        c = np.asarray(TRIM["light"], np.float64) * (0.78 + 0.14 * hashf(5, ss))
        rg.line(x, y, x + ln * math.cos(ang), y + ln * math.sin(ang),
                0.8 + 0.8 * hashf(6, ss), color=c,
                alpha=0.07 + 0.11 * hashf(7, ss),
                dh=0.03, drough=-0.07 * 0.15, dmet=0.02 * 0.15)
    for j in range(180):
        ss = s ^ (0x7500 + 41 * j)
        x = w * hashf(1, ss)
        y = h * hashf(2, ss)
        ln = 4.0 + 24.0 * hashf(3, ss)
        ang = (hashf(4, ss) - 0.5) * 0.9
        rg.line(x, y, x + ln * math.cos(ang), y + ln * math.sin(ang),
                0.6 + 0.6 * hashf(5, ss), color=TRIM["dark"],
                alpha=0.10 + 0.14 * hashf(6, ss), dh=-0.04, drough=0.06)

    # ---- grime in the low dents ---- #
    gm = smoothstep(0.52, 0.80, fbm(60.0, 46.0, s ^ 0x68, X, Y, octaves=4)) * low
    rg.apply_full(gm * 0.42, color=np.asarray(TRIM["grime"], np.float32),
                  alpha=1.0, drough=0.14 * gm, dmet=-0.22 * gm)

    # ---- pitting ---- #
    for j in range(200):
        ss = s ^ (0x7600 + 43 * j)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 0.6 + 1.1 * hashf(3, ss)
        rg.ellipse(cx, cy, rr, rr, color=TRIM["dark"], alpha=0.45, dh=-0.03)


def paint_glass(rg):
    """Pale blue-green glass: dirt streaks, dust, subtle reflections."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(GLASS["base"], 0.0)

    mott = fbm(150.0, 150.0, s ^ 0x51, X, Y, octaves=4)
    col = mixc((0.556, 0.700, 0.706), (0.668, 0.806, 0.796), mott)
    grad = np.clip((Y + 0.5) / float(h), 0.0, 1.0)
    col = col * (0.965 + 0.07 * grad)[..., None]
    rough = 0.062 + 0.030 * (mott - 0.5) * 2.0
    rg.rough[...] = rough.astype(np.float32)
    rg.hgt[...] = (0.16 * (fbm(70.0, 70.0, s ^ 0x52, X, Y, octaves=3) - 0.5)).astype(np.float32)

    # ---- soft diagonal reflection bands ---- #
    ang = -0.62
    proj = X * math.cos(ang) + Y * math.sin(ang)
    band = 0.5 + 0.5 * np.sin(proj / 92.0 + 1.25)
    soft = smoothstep(0.80, 1.0, band)
    col = col + (soft * 0.070)[..., None] * np.asarray(GLASS["bright"], np.float64)
    for off, wid, amp in ((-760.0, 13.0, 0.10), (-300.0, 7.0, 0.075), (150.0, 5.0, 0.05)):
        narrow = smoothstep(wid, 0.0, np.abs(proj - off)) * amp
        col = col + narrow[..., None] * np.asarray(GLASS["bright"], np.float64)
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)

    # ---- dirt smudges: thin vertical wipe marks, not round blobs ---- #
    for i in range(26):
        ss = s ^ (0x6000 + 29 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rx = 1.6 + 3.0 * hashf(3, ss)
        ry = 20.0 + 58.0 * hashf(4, ss)
        a = (hashf(5, ss) - 0.5) * 0.10
        got = rg.ellipse_dist(cx, cy, rx * 2.0, ry, a, 2.0)
        if got is None:
            continue
        win, d = got
        g = np.exp(-d * d * 1.4)
        amt = 0.07 + 0.10 * hashf(6, ss)
        rg.apply(win, g * amt, color=GLASS["dirt"], alpha=1.0,
                 drough=1.20 * g * amt, dh=0.02 * g)
    # a few broad soft grime bands (lost reflections / general film)
    for i in range(4):
        ss = s ^ (0x6600 + 53 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rx = 10.0 + 16.0 * hashf(3, ss)
        ry = 70.0 + 170.0 * hashf(4, ss)
        got = rg.ellipse_dist(cx, cy, rx, ry, 0.0, 3.0)
        if got is None:
            continue
        win, d = got
        g = np.exp(-d * d * 1.1)
        amt = 0.05 + 0.05 * hashf(5, ss)
        rg.apply(win, g * amt, color=GLASS["dirt"], alpha=1.0,
                 drough=0.60 * g * amt, dh=0.03 * g)

    # ---- rain streaks ---- #
    for i in range(80):
        ss = s ^ (0x6200 + 23 * i)
        x = w * hashf(1, ss)
        y = h * hashf(2, ss)
        ln = 18.0 + 90.0 * hashf(3, ss)
        drift = (hashf(4, ss) - 0.5) * 6.0
        rg.line(x, y, x + drift, y + ln, 0.9 + 1.1 * hashf(5, ss),
                color=np.asarray(GLASS["bright"], np.float64),
                alpha=0.10 + 0.16 * hashf(6, ss),
                drough=0.05, dh=0.02)

    # ---- dust speckles ---- #
    for i in range(240):
        ss = s ^ (0x6400 + 19 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rr = 0.5 + 1.0 * hashf(3, ss)
        if hashf(4, ss) > 0.55:
            rg.ellipse(cx, cy, rr, rr, color=GLASS["dirt"],
                       alpha=0.14 + 0.14 * hashf(5, ss), drough=0.08)
        else:
            rg.ellipse(cx, cy, rr, rr * 0.8,
                       color=np.asarray(GLASS["bright"], np.float64) * 1.02,
                       alpha=0.10 + 0.10 * hashf(5, ss))
    # faint horizontal dirt film at the very bottom of the pane
    film = smoothstep(0.55, 1.0, grad) * smoothstep(0.55, 0.85, mott)
    rg.apply_full(film * 0.22, color=GLASS["dirt"], alpha=1.0,
                  drough=0.16 * film, dh=0.03 * film)


def paint_plaster(rg):
    """Cream trowelled plaster (reference: light flat plaster roof + bands).

    v2.2: replaces the ART-04 ochre canvas painter.  The reference images of the
    intermediate tier show a light cream/soil flat roof and cream plaster bands;
    this patch is what that material samples.
    """
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(PLASTER["base"], 0.0)

    trowel = fbm(9.0, 7.0, s ^ 0xA1, X, Y, octaves=4)
    swirl = fbm(3.2, 2.6, s ^ 0xA2, X, Y, octaves=3)
    fine = fbm(150.0, 150.0, s ^ 0xA3, X, Y, octaves=3)
    tone = fbm(22.0, 18.0, s ^ 0xA4, X, Y, octaves=3)

    base = np.asarray(PLASTER["base"], np.float64)[None, None, :]
    light = np.asarray(PLASTER["light"], np.float64)[None, None, :]
    stain = np.asarray(PLASTER["stain"], np.float64)[None, None, :]
    t = np.clip(0.28 + 0.58 * trowel + 0.20 * (tone - 0.5), 0.0, 1.0)[..., None]
    col = base * (1.0 - t) + light * t
    col = col * (0.94 + 0.10 * fine[..., None])
    stain_mask = smoothstep(0.62, 0.88, swirl) * 0.30
    col = col * (1.0 - stain_mask[..., None]) + stain * stain_mask[..., None]
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (0.32 * trowel + 0.28 * fine + 0.14 * swirl - 0.34).astype(np.float32)
    rg.rough[...] = (0.82 - 0.06 * trowel + 0.04 * (fine - 0.5)).astype(np.float32)

    # ---- trowel sweeps: soft light/dark arcs ---- #
    for i in range(70):
        ss = s ^ (0xA500 + 19 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rx = 10.0 + 34.0 * hashf(3, ss)
        ry = rx * (0.35 + 0.55 * hashf(4, ss))
        ang = (hashf(5, ss) - 0.5) * 1.1
        rg.ellipse(cx, cy, rx, ry, ang,
                   color=np.asarray(PLASTER["light"], np.float64)
                   * (0.96 + 0.08 * hashf(6, ss)),
                   alpha=0.08 + 0.14 * hashf(7, ss), dh=0.10, drough=-0.02)
    # ---- damp / soot stains ---- #
    for i in range(9):
        ss = s ^ (0xA600 + 31 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rx = 16.0 + 46.0 * hashf(3, ss)
        ry = rx * (0.55 + 0.6 * hashf(4, ss))
        got = rg.ellipse_dist(cx, cy, rx, ry, 0.0, 2.5)
        if got is None:
            continue
        win, d = got
        body = smoothstep(1.0, 0.15, d)
        amt = 0.05 + 0.09 * hashf(5, ss)
        rg.apply(win, body * amt, color=PLASTER["stain"], alpha=1.0,
                 drough=0.03 * body, dh=-0.08 * body)
    # ---- faint joint / plank shadow lines (flat roof panel joints) ---- #
    for k in range(2):
        fy = h * (0.30 + 0.40 * hashf(k + 3, s ^ 0xA7))
        rg.shade_rows(int(fy) - 2, int(fy), add=rgb(0.016, 0.014, 0.010))
        rg.shade_rows(int(fy), int(fy) + 3, fac=0.97)
        rg.hgt_rows(int(fy) - 2, int(fy), 0.20)
        rg.hgt_rows(int(fy), int(fy) + 3, -0.20)


def paint_thatch(rg):
    """Straw-yellow thatch: layered strands, bundle shadows, loose strays."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(THATCH["base"], 0.0)

    ang = math.radians(7.0)
    ca = math.cos(ang)
    sa = math.sin(ang)
    u = X * ca + Y * sa      # across the strands
    v = -X * sa + Y * ca     # along the strands

    warp = fbm(90.0, 40.0, s ^ 0x81, X, Y, octaves=3)
    warp2 = fbm(11.0, 120.0, s ^ 0x86, X, Y, octaves=2)
    uw = u + 8.0 * (warp - 0.5) + 2.5 * (warp2 - 0.5)
    pitch = 4.6
    su = uw / pitch
    si = np.floor(su)
    fu = su - si
    prof = 1.0 - np.abs(fu - 0.5) * 2.0          # 0 in the gap, 1 mid-strand

    si_i = si.astype(np.int64)
    zero_i = np.zeros_like(si_i)
    # per-strand base tone + smooth variation ALONG the strand (no hard bands)
    base_tone = hash01(si_i, zero_i, s ^ 0x82)
    along = fbm(6.5, 130.0, s ^ 0x87, uw, v, octaves=3)
    tone = clamp01(0.62 * base_tone + 0.38 * along)
    # bundles: hard per-bundle tone broken up by a smooth field
    b_i = np.floor(su / 9.0).astype(np.int64)
    bundle = clamp01(0.55 * hash01(b_i, zero_i, s ^ 0x83)
                     + 0.45 * fbm(26.0, 85.0, s ^ 0x88, uw, v, octaves=2))
    gap = smoothstep(0.30, 0.0, prof)

    tip_n = fbm(3.0, 70.0, s ^ 0x84, X, Y, octaves=2)
    tip = smoothstep(0.70, 0.95, tip_n)
    long_n = fbm(5.0, 210.0, s ^ 0x85, X, Y, octaves=2)

    col = np.asarray(THATCH["base"], np.float64)[None, None, :] * (
        (0.66 + 0.48 * tone) * (0.68 + 0.32 * prof) * (0.84 + 0.30 * bundle)
        * (0.94 + 0.12 * long_n))[..., None]
    col = col * (1.0 - 0.42 * gap)[..., None]
    col = col + (tip * 0.10)[..., None] * np.asarray(THATCH["light"], np.float64)
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (0.95 * prof + 0.26 * (bundle - 0.5) + 0.14 * (tone - 0.5)
                   - 0.55 * gap + 0.10 * (long_n - 0.5)).astype(np.float32)
    rg.rough[...] = (0.905 + 0.04 * (1.0 - prof) + 0.03 * (tone - 0.5)).astype(np.float32)

    # ---- deep shadow between bundles ---- #
    dark = np.asarray(THATCH["dark"], np.float64)
    rg.apply_full(gap * 0.55, color=dark, alpha=1.0, drough=0.02 * gap)

    # ---- loose / stray straws lying across the thatch ---- #
    for i in range(70):
        ss = s ^ (0xB000 + 29 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        a = math.radians(58.0 + (hashf(3, ss) - 0.5) * 30.0)
        ln = 14.0 + 46.0 * hashf(4, ss)
        tone2 = hashf(5, ss)
        c = mixc(THATCH["base"], THATCH["light"], 0.2 + 0.8 * tone2)
        rg.line(cx, cy, cx + ln * math.cos(a), cy + ln * math.sin(a),
                0.9 + 1.1 * hashf(6, ss), color=c, alpha=0.42,
                dh=0.28, drough=-0.02)
    # ---- brighter straw tips catching the light ---- #
    for i in range(220):
        ss = s ^ (0xB200 + 19 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        a = math.radians(84.0 + (hashf(3, ss) - 0.5) * 24.0)
        ln = 2.5 + 7.0 * hashf(4, ss)
        rg.line(cx, cy, cx + ln * math.cos(a), cy - ln * math.sin(a),
                0.8 + 0.7 * hashf(5, ss),
                color=np.asarray(THATCH["light"], np.float64) * (0.92 + 0.16 * hashf(6, ss)),
                alpha=0.28 + 0.30 * hashf(7, ss), dh=0.12)


def paint_moss(rg):
    """Damp green moss: soft clump noise, dark gaps, tiny leaf speckles."""
    h, w = rg.h, rg.w
    s = rg.seed
    X, Y = rg.X, rg.Y
    rg.begin(MOSS["base"], 0.0)

    clump = fbm(26.0, 22.0, s ^ 0x71, X, Y, octaves=4)
    fine = fbm(120.0, 120.0, s ^ 0x72, X, Y, octaves=3)
    hue = fbm(9.0, 8.0, s ^ 0x73, X, Y, octaves=3)

    base = np.asarray(MOSS["base"], np.float64)[None, None, :]
    dark = np.asarray(MOSS["dark"], np.float64)[None, None, :]
    mid = np.asarray(MOSS["mid"], np.float64)[None, None, :]
    col = base * (0.78 + 0.55 * clump[..., None]) * (0.90 + 0.20 * fine[..., None])
    col = np.where((clump < 0.40)[..., None],
                   dark * (0.82 + 0.45 * fine[..., None]), col)
    col = np.where((clump > 0.68)[..., None],
                   mid * (0.88 + 0.42 * hue[..., None]), col)
    rg.rgb[...] = np.clip(col, 0.0, 1.0).astype(np.float32)
    rg.hgt[...] = (0.60 * clump + 0.35 * fine - 0.28).astype(np.float32)
    rg.rough[...] = (0.93 - 0.06 * clump + 0.03 * (fine - 0.5)).astype(np.float32)

    # ---- soft moss cushions ---- #
    for i in range(90):
        ss = s ^ (0xB400 + 21 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        rx = 3.0 + 9.0 * hashf(3, ss)
        ry = rx * (0.55 + 0.65 * hashf(4, ss))
        rg.ellipse(cx, cy, rx, ry, (hashf(5, ss) - 0.5) * 1.2,
                   color=np.asarray(MOSS["mid"], np.float64) * (0.90 + 0.22 * hashf(6, ss)),
                   alpha=0.20 + 0.26 * hashf(7, ss), dh=0.20, drough=-0.03)

    # ---- tiny leaf / stalk speckles ---- #
    for i in range(320):
        ss = s ^ (0xC100 + 17 * i)
        cx = w * hashf(1, ss)
        cy = h * hashf(2, ss)
        ang = math.radians(20.0 + (hashf(3, ss) - 0.5) * 140.0)
        ln = 1.4 + 4.2 * hashf(4, ss)
        c = MOSS["light"] if hashf(5, ss) > 0.55 else MOSS["stem"]
        rg.line(cx, cy, cx + ln * math.cos(ang), cy - ln * math.sin(ang),
                0.7 + 0.8 * hashf(6, ss),
                color=np.asarray(c, np.float64) * (0.90 + 0.25 * hashf(7, ss)),
                alpha=0.30 + 0.38 * hashf(8, ss), dh=0.14, drough=-0.02)


PAINTERS = {
    "wood": paint_wood,
    "soil_full": paint_soil_full,
    "soil_empty": paint_soil_empty,
    "metal": paint_metal,
    "fixture": paint_fixture,
    "trim": paint_trim,
    "glass": paint_glass,
    "plaster": paint_plaster,
    "moss": paint_moss,
    "thatch": paint_thatch,
}

#: fallback painter per material, used if the frozen contract declares a region
#: name this build has no dedicated painter for (keeps the generator valid for
#: any future region set without hard-coding the region list).
MATERIAL_PAINTERS = {
    "M_HDBF_Wood": paint_wood,
    "M_HDBF_Metal": paint_metal,
    "M_HDBF_Glass": paint_glass,
    "M_HDBF_Fabric": paint_plaster,
}

#: relative tangent-normal bumpiness per region (auto scaled to a sane RMS).
NORMAL_STRENGTH = {
    "wood": 1.00,
    "soil_full": 0.95,
    "soil_empty": 1.05,
    "metal": 1.10,
    "fixture": 1.20,
    "trim": 0.95,
    "glass": 0.30,
    "plaster": 0.60,
    "moss": 1.15,
    "thatch": 1.10,
}


# --------------------------------------------------------------------------- #
# atlas assembly
# --------------------------------------------------------------------------- #

def region_normal(rg, strength):
    """Encode a tangent-space normal from the region height field.

    OpenGL convention, +Y up:  n = normalise(-dh/dcol * S, +dh/drow * S, 1).

    The per-pixel height gradient depends on the arbitrary unit scale of the
    painter, so the scale is normalised to a fixed RMS slope: every material
    gets a comparable, visible bump amount regardless of its height units.
    """
    gx = np.gradient(rg.hgt, axis=1).astype(np.float64)
    gy = np.gradient(rg.hgt, axis=0).astype(np.float64)
    rms = float(np.sqrt(np.mean(gx * gx + gy * gy)))
    scale = float(strength) * 0.38 / max(rms, 1e-8)
    nx = -gx * scale
    ny = gy * scale
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + 1.0)
    return nx * inv, ny * inv, inv


def fill_gutters(img, painted, radius=4):
    """Fill unpainted gutter pixels with a distance weighted average of the
    nearest painted pixels (i.e. the neighbouring region edges)."""
    single = (img.ndim == 2)
    buf = np.array(img, dtype=np.float32, copy=True)
    if single:
        buf = buf[:, :, None]
    h, w, ch = buf.shape
    ys, xs = np.nonzero(~painted)
    if len(ys) == 0:
        return (buf[:, :, 0] if single else buf)
    acc = np.zeros((len(ys), ch), np.float64)
    wsum = np.zeros(len(ys), np.float64)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            if dx == 0 and dy == 0:
                continue
            yy = ys + dy
            xx = xs + dx
            ok = (yy >= 0) & (yy < h) & (xx >= 0) & (xx < w)
            if not ok.any():
                continue
            yy2 = np.clip(yy, 0, h - 1)
            xx2 = np.clip(xx, 0, w - 1)
            valid = ok & painted[yy2, xx2]
            if not valid.any():
                continue
            wt = 1.0 / math.hypot(dx, dy)
            acc[valid] += buf[yy2[valid], xx2[valid]].astype(np.float64) * wt
            wsum[valid] += wt
    have = wsum > 0.0
    buf[ys[have], xs[have]] = (acc[have] / wsum[have][:, None]).astype(np.float32)
    return (buf[:, :, 0] if single else buf)


def to_u8(x):
    return np.rint(np.clip(x, 0.0, 1.0) * 255.0).astype(np.uint8)


# --------------------------------------------------------------------------- #
# PNG encode / decode (deterministic, no metadata)
# --------------------------------------------------------------------------- #

def _chunk(tag, payload):
    return (struct.pack(">I", len(payload)) + tag + payload
            + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))


def png_bytes(arr, srgb=False):
    """Encode an 8 bit greyscale / RGB / RGBA array to PNG bytes.

    Filter type 0 on every row, deflate level 6, no tIME / tEXt / iTXt chunks,
    so the bytes are a pure function of the pixel data.
    """
    arr = np.ascontiguousarray(arr, dtype=np.uint8)
    h, w = arr.shape[0], arr.shape[1]
    c = 1 if arr.ndim == 2 else arr.shape[2]
    assert c in (1, 3, 4), c
    color_type = {1: 0, 3: 2, 4: 6}[c]
    rows = arr.reshape(h, w * c)
    filtered = np.zeros((h, w * c + 1), np.uint8)   # filter byte 0 per row
    filtered[:, 1:] = rows
    comp = zlib.compress(filtered.tobytes(), 6)
    out = [b"\x89PNG\r\n\x1a\n",
           _chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color_type, 0, 0, 0))]
    if srgb:
        out.append(_chunk(b"sRGB", b"\x00"))
    out.append(_chunk(b"IDAT", comp))
    out.append(_chunk(b"IEND", b""))
    return b"".join(out)


def decode_png(data):
    """Minimal PNG decoder for the files we write (8 bit, filter 0 rows)."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos = 8
    w = h = bd = ct = None
    idat = b""
    tags = []
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + ln]
        crc = struct.unpack(">I", data[pos + 8 + ln:pos + 12 + ln])[0]
        assert crc == (zlib.crc32(tag + payload) & 0xFFFFFFFF), "bad chunk crc %r" % tag
        tags.append(tag)
        if tag == b"IHDR":
            w, h, bd, ct, comp, filt, inter = struct.unpack(">IIBBBBB", payload)
            assert bd == 8 and comp == 0 and filt == 0 and inter == 0
        elif tag == b"IDAT":
            idat += payload
        pos += 12 + ln
    assert b"IEND" in tags
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[ct]
    stride = w * ch
    raw = np.frombuffer(zlib.decompress(idat), np.uint8)
    assert raw.size == h * (stride + 1), "unexpected raw size"
    rows = raw.reshape(h, stride + 1)
    assert int(rows[:, 0].max()) == 0, "only filter type 0 supported"
    img = rows[:, 1:].reshape(h, w, ch)
    return img, ct, tags


def box_downscale(img, factor):
    if factor <= 1:
        return img
    h, w = img.shape[0], img.shape[1]
    nh = max(1, h // factor)
    nw = max(1, w // factor)
    crop = img[:nh * factor, :nw * factor]
    if img.ndim == 2:
        return crop.reshape(nh, factor, nw, factor).mean(axis=(1, 3)).astype(np.uint8)
    c = img.shape[2]
    return crop.reshape(nh, factor, nw, factor, c).mean(axis=(1, 3)).astype(np.uint8)


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #

def main():
    t_start = time.perf_counter()
    out_dir = os.environ.get("HDBF_TEX_PREVIEW_DIR")
    print("[gen_textures] HDBF procedural PBR atlas generator")
    print("[gen_textures] blender/numpy: %s | numpy %s" % (sys.version.split()[0], np.__version__))
    L.validate_contract()
    print("[gen_textures] atlas_layout contract OK: %d regions, %d materials, %d ports"
          % (len(L.REGIONS), len(L.MATERIAL_ORDER), len(L.PORT_SPECS)))
    contract_path = os.path.join(HERE, "atlas_layout.py")
    try:
        with open(contract_path, "rb") as f:
            contract_sha = hashlib.sha256(f.read()).hexdigest()
    except OSError:
        contract_sha = "unavailable"
    print("[gen_textures] contract %s sha256=%s"
          % (os.path.abspath(contract_path), contract_sha))
    for _name, (_mat, _rect) in L.REGIONS.items():
        print("[gen_textures]   region %-11s %-12s rect=%s" % (_name, _mat, _rect))
    print("[gen_textures] atlas %dx%d, seed=0x%016X, padding=%d px, normal=%s"
          % (ATLAS, ATLAS, SEED, L.PADDING, L.NORMAL_CONVENTION))

    H = W = ATLAS
    albedo = np.full((H, W, 3), -1.0, np.float32)
    normal = np.zeros((H, W, 3), np.float32)
    normal[..., 0] = 0.5                      # flat base in encode space,
    normal[..., 1] = 0.5                      # i.e. (128, 128, 255) in bytes
    normal[..., 2] = 1.0
    met = np.full((H, W), -1.0, np.float32)
    rough = np.full((H, W), -1.0, np.float32)
    painted = np.zeros((H, W), bool)

    region_stats = {}
    t_paint = time.perf_counter()
    for name in L.REGIONS:
        mat, rect = L.REGIONS[name]
        x0, y0, x1, y1 = rect
        rg = Region(name)
        painter = PAINTERS.get(name)
        if painter is None:
            painter = MATERIAL_PAINTERS[mat]
            print("[gen_textures] note: region '%s' has no dedicated painter; "
                  "using the %s painter" % (name, mat))
        t0 = time.perf_counter()
        painter(rg)
        dt = time.perf_counter() - t0
        # ---- per-region self checks (still region local) ----
        assert rg.painted.all(), "%s: painter left unpainted pixels" % name
        assert np.isfinite(rg.rgb).all() and np.isfinite(rg.hgt).all(), name
        assert np.isfinite(rg.met).all() and np.isfinite(rg.rough).all(), name
        # stacked stamps may overshoot locally: clamp physical channels
        np.clip(rg.met, 0.0, 1.0, out=rg.met)
        np.clip(rg.rough, 0.02, 0.99, out=rg.rough)
        # ---- normal from the height field ----
        nx, ny, nz = region_normal(rg, NORMAL_STRENGTH.get(name, 1.0))
        nimg = np.empty((rg.h, rg.w, 3), np.float32)
        nimg[..., 0] = (0.5 + 0.5 * nx).astype(np.float32)
        nimg[..., 1] = (0.5 + 0.5 * ny).astype(np.float32)
        nimg[..., 2] = (0.5 + 0.5 * nz).astype(np.float32)
        # ---- compose into the atlas ----
        albedo[y0:y1, x0:x1] = np.clip(rg.rgb, 0.0, 1.0)
        normal[y0:y1, x0:x1] = nimg
        met[y0:y1, x0:x1] = rg.met
        rough[y0:y1, x0:x1] = rg.rough
        painted[y0:y1, x0:x1] = True
        region_stats[name] = {
            "rect": rect, "mat": mat, "px": int(rg.w * rg.h), "secs": dt,
            "hgt_rms_slope": float(np.sqrt(np.mean(
                np.gradient(rg.hgt, axis=1) ** 2 + np.gradient(rg.hgt, axis=0) ** 2))),
        }
        print("[gen_textures] paint %-10s %-12s %4dx%-4d rect=%-22s %7d px  %6.3f s"
              % (name, mat, rg.w, rg.h, str(rect), rg.w * rg.h, dt))
    t_paint = time.perf_counter() - t_paint
    n_regions = len(L.REGIONS)
    print("[gen_textures] painted %d/%d contract regions in %.2f s"
          % (n_regions, n_regions, t_paint))

    # ---- gutters: distance weighted average of the neighbouring edges ---- #
    n_gutter = int((~painted).sum())
    albedo = fill_gutters(albedo, painted, 4)
    normal = fill_gutters(normal, painted, 4)
    # averaging neighbouring normals shortens them: renormalise (the flat base
    # 0.5/0.5/1.0 stays exactly flat under this operation).
    nvec = normal * 2.0 - 1.0
    nlen = np.sqrt((nvec * nvec).sum(axis=2, keepdims=True))
    nvec = nvec / np.maximum(nlen, 1e-6)
    normal = 0.5 + 0.5 * nvec
    met = fill_gutters(met, painted, 4)
    rough = fill_gutters(rough, painted, 4)

    # ---- assemble the three texture images ---- #
    albedo_u8 = to_u8(albedo)
    normal_u8 = to_u8(np.clip(normal, 0.0, 1.0))

    met_c = np.clip(met, 0.0, 1.0)
    rough_c = np.clip(rough, 0.02, 0.99)
    met_u8 = to_u8(met_c)
    rough_u8 = to_u8(rough_c)
    mask_u8 = np.empty((H, W, 4), np.uint8)
    mask_u8[..., 0] = met_u8                 # R = metallic
    mask_u8[..., 1] = rough_u8               # G = roughness
    mask_u8[..., 2] = met_u8                 # B = metallic (glTF packing)
    mask_u8[..., 3] = (255 - rough_u8)       # A = smoothness

    files = [
        ("albedo", os.path.join(TEXTURE_DIR, "atlas_albedo.png"), albedo_u8, True),
        ("normal", os.path.join(TEXTURE_DIR, "atlas_normal.png"), normal_u8, False),
        ("mask", os.path.join(TEXTURE_DIR, "atlas_mask.png"), mask_u8, False),
    ]
    os.makedirs(TEXTURE_DIR, exist_ok=True)
    results = []
    for key, path, arr, srgb in files:
        data = png_bytes(arr, srgb=srgb)
        sha = hashlib.sha256(data).hexdigest()
        prev = None
        if os.path.exists(path):
            with open(path, "rb") as f:
                prev = f.read()
        det = "n/a (no previous file)"
        if prev is not None:
            det = "PASS" if prev == data else "FAIL"
        with open(path, "wb") as f:
            f.write(data)
        size = os.path.getsize(path)
        results.append({"key": key, "path": path, "data": data, "sha256": sha,
                        "size": size, "det": det, "arr": arr})
        print("[gen_textures] wrote %s" % os.path.abspath(path))
        print("[gen_textures]   size %d bytes  sha256=%s  cross-run=%s" % (size, sha, det))

    # ---- self checks on the final images --------------------------------- #
    checks = []
    for r in results:
        img, ct, tags = decode_png(r["data"])
        assert img.shape[0] == ATLAS and img.shape[1] == ATLAS, (r["key"], img.shape)
        assert np.array_equal(img, r["arr"]), "%s: PNG round-trip mismatch" % r["key"]
        assert not any(t in (b"tEXt", b"zTXt", b"iTXt") for t in tags), "text chunk found"
        expected_ct = {"albedo": 2, "normal": 2, "mask": 6}[r["key"]]
        assert ct == expected_ct, (r["key"], ct)
        checks.append("%s %dx%d ct=%d roundtrip + no-metadata" % (r["key"], ATLAS, ATLAS, ct))
    print("[gen_textures] self-check: %s" % "; ".join(checks))

    # region coverage / sentinel / mask range checks
    painted_regions = 0
    for name, (mat, rect) in L.REGIONS.items():
        x0, y0, x1, y1 = rect
        area = (x1 - x0) * (y1 - y0)
        assert int(painted[y0:y1, x0:x1].sum()) == area, "%s not fully painted" % name
        assert float(albedo[y0:y1, x0:x1].min()) >= 0.0, "%s neutral initialiser" % name
        assert float(met[y0:y1, x0:x1].min()) >= 0.0, "%s metallic sentinel" % name
        assert float(rough[y0:y1, x0:x1].min()) >= 0.0, "%s roughness sentinel" % name
        painted_regions += 1
    print("[gen_textures] self-check: regions painted %d/%d, no neutral initialiser, "
          "gutters filled %d px" % (painted_regions, n_regions, n_gutter))
    assert painted_regions == n_regions >= 1
    assert float(met.min()) >= 0.0 and float(rough.min()) >= 0.0
    # float32 storage means the clipped bounds are approximate: compare with
    # a small tolerance instead of the exact float64 literals.
    assert float(rough_c.min()) >= 0.0195 and float(rough_c.max()) <= 0.9905
    assert int(mask_u8[..., 0].min()) >= 0 and int(mask_u8[..., 0].max()) <= 255

    # normal unit length (decode space -> vector) and OpenGL +Z
    nd = normal_u8.astype(np.float64) / 127.5 - 1.0
    nlen = np.sqrt((nd * nd).sum(axis=2))
    bad = int((np.abs(nlen - 1.0) > 0.02).sum())
    assert bad == 0, "%d normal map pixels not unit length" % bad
    assert float(nd[..., 2].min()) > 0.0, "normal map has -Z pixels"
    flat_ok = int(((normal_u8[..., 0] == 128) & (normal_u8[..., 1] == 128)
                   & (normal_u8[..., 2] == 255)).sum())
    print("[gen_textures] self-check: normal unit-length PASS (max err %.4f), "
          "flat-neutral pixels %d/%d" % (float(np.abs(nlen - 1.0).max()), flat_ok, H * W))

    # ---- per region statistics ------------------------------------------- #
    print("[gen_textures] per-region means (albedo 0-255, metallic/roughness 0-1):")
    print("[gen_textures]   %-11s %-12s %-16s %-9s %s"
          % ("region", "material", "albedo RGB", "metallic", "roughness"))
    for name in L.REGIONS:
        x0, y0, x1, y1 = L.REGIONS[name][1]
        sub = albedo_u8[y0:y1, x0:x1].astype(np.float64)
        m = float(met_c[y0:y1, x0:x1].mean())
        r = float(rough_c[y0:y1, x0:x1].mean())
        print("[gen_textures]   %-11s %-12s %3d,%3d,%3d      %.3f      %.3f"
              % (name, L.REGIONS[name][0], int(round(sub[..., 0].mean())),
                 int(round(sub[..., 1].mean())), int(round(sub[..., 2].mean())), m, r))

    # ---- optional QA previews (never part of the deliverable) ------------ #
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
        def save_preview(fname, arr):
            p = os.path.join(out_dir, fname)
            with open(p, "wb") as f:
                f.write(png_bytes(arr, srgb=False))
            print("[gen_textures] preview %s" % p)
        save_preview("preview_atlas.png", box_downscale(albedo_u8, 4))
        save_preview("preview_normal.png", box_downscale(normal_u8, 4))
        save_preview("preview_mask.png", box_downscale(mask_u8[..., :3], 4))
        for name, (mat, rect) in L.REGIONS.items():
            x0, y0, x1, y1 = rect
            sub = albedo_u8[y0:y1, x0:x1]
            f = max(1, int(math.ceil(max(sub.shape[0], sub.shape[1]) / 520.0)))
            save_preview("preview_%s.png" % name, box_downscale(sub, f))

    # ---- summary --------------------------------------------------------- #
    det_all = [r["det"] for r in results]
    if all(d == "PASS" for d in det_all):
        det_line = "PASS (byte-identical to the previous run)"
    elif any(d == "FAIL" for d in det_all):
        det_line = "FAIL (files differ from the previous run)"
    else:
        det_line = "n/a (first run, no previous files)"
    total = time.perf_counter() - t_start
    print("[gen_textures] CROSS-RUN DETERMINISM: %s" % det_line)
    print("[gen_textures] TOTAL RUNTIME: %.2f s" % total)
    if any(d == "FAIL" for d in det_all):
        raise SystemExit("determinism FAIL")
    print("[gen_textures] RESULT: PASS (%d regions, contract sha256=%s)"
          % (n_regions, contract_sha))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.stdout.flush()
        sys.stderr.flush()
        os._exit(1)
