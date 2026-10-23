#!/usr/bin/env python3
"""Renders the Windows app icon: windows/src/Recess.App/Assets/Recess.ico and icon.png.

Runs on macOS (it uses scripts/make_icon.swift and sips). The .ico holds PNG images at the
sizes Windows asks for, from the 16 px tray icon up to 256 px.

Usage: python3 scripts/make_windows_icon.py
"""

import os
import struct
import subprocess
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "windows", "src", "Recess.App", "Assets")
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]


def render(work):
    big = os.path.join(work, "icon_1024.png")
    subprocess.run(["swift", os.path.join(ROOT, "scripts", "make_icon.swift"), big, "--windows"], check=True)
    images = []
    for size in SIZES:
        path = os.path.join(work, f"icon_{size}.png")
        subprocess.run(["sips", "-z", str(size), str(size), big, "--out", path], check=True, capture_output=True)
        with open(path, "rb") as f:
            images.append((size, f.read()))
    # A 256 px PNG for toast notifications, which need the icon as a file.
    png = os.path.join(ASSETS, "icon.png")
    subprocess.run(["sips", "-z", "256", "256", big, "--out", png], check=True, capture_output=True)
    return images


def pack(images):
    # ICONDIR, then one ICONDIRENTRY per image (0 means 256), then the PNG data.
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries, data = b"", b""
    for size, png in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset + len(data))
        data += png
    return header + entries + data


def main():
    os.makedirs(ASSETS, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        ico = pack(render(work))
    with open(os.path.join(ASSETS, "Recess.ico"), "wb") as f:
        f.write(ico)
    print(f"Wrote {os.path.join(ASSETS, 'Recess.ico')} ({len(ico)} bytes) and icon.png")


if __name__ == "__main__":
    main()
