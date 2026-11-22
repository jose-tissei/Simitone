"""Builds the world-sprite effect with directional shading from the game's own HLSL source.

The engine already ships the code for it, commented out: psZDepthSpriteDirLight shades a sprite pixel by the angle between its
surface normal (rebuilt from the depth sprite through screen-space derivatives) and the direction of the light that reaches that
spot (the engine's direction light map). This script re-enables it as a third pass, "dirLighting", next to the two the release
has. The engine picks passes[DirPassOffset] and already clamps it to the pass count, so the release works unchanged with the
old file; the Render library moves DirPassOffset to 2 when directional shading is on.

  python Make-WorldEffect.py <ContentSrc/Effects dir> <out dir>      then Build-Effects.ps1 -Fx <out>/2DWorldBatch.fx -Template <game 2DWorldBatch.xnb> -Out ...
"""
import os
import shutil
import sys

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
for name in ("LightingCommon.fx", "2DWorldBatch.fx"):
    shutil.copyfile(os.path.join(src, name), os.path.join(out, name))

path = os.path.join(out, "2DWorldBatch.fx")
text = open(path, encoding="utf-8-sig", newline="").read()
nl = "\r\n" if "\r\n" in text else "\n"
text = text.replace("\r\n", "\n")

# 1. the pixel shader: remove its comment wrapper (the function is the first block comment that contains it)
head = "/*\nvoid psZDepthSpriteDirLight("
i = text.index(head)
text = text[:i] + text[i + 3:]                       # drop the opening /*
j = text.index("}*/", i)
text = text[:j + 1] + text[j + 3:]                   # drop the closing */

# 2. the pass: same wrapper trick around "pass dirLighting"
k = text.index("\t/*\n\tpass dirLighting {")
text = text[:k] + text[k + 4:]
m = text.index("}*/", k)
text = text[:m + 1] + text[m + 3:]

# 3. the pass sets depth states of its own; the other passes leave them to the engine, so do the same
text = text.replace("\t\tZEnable = true; ZWriteEnable = true;\n\t\tCullMode = CCW;\n\n", "")

open(path, "w", encoding="utf-8", newline="").write(text.replace("\n", nl))
print("wrote", path)
