"""Numbers for 'did the anti-aliasing help, and what did it cost in texture detail?' from two aligned screenshots (game paused).

  python Measure-AA.py off.png aa.png

lawn_sharpness : mean absolute Laplacian over a plain lawn crop (texture detail). Ratio aa/off; below ~0.9 means the filter blurs textures.
edge_hardness  : (a second argument "whole" measures both over the whole world area instead) number of pixels on the fence/wall silhouettes whose neighbour differs by more than 60 in luma (stair-step edges).
                 Ratio aa/off; a good filter lowers it (more intermediate tones along the edges).
"""
import sys
from PIL import Image
import numpy as np

LAWN = (900, 700, 1500, 1000)      # plain grass, lower right of the Newbie lot view (1920x1080)
FENCE = (1090, 330, 1860, 560)     # picket fences and brick wall edges


def luma(path, box):
    im = Image.open(path).convert("L").crop(box)
    return np.asarray(im, dtype=np.float32)


def sharp(a):
    lap = np.abs(4 * a[1:-1, 1:-1] - a[:-2, 1:-1] - a[2:, 1:-1] - a[1:-1, :-2] - a[1:-1, 2:])
    return float(lap.mean())


def hard(a):
    dx = np.abs(a[:, 1:] - a[:, :-1]) > 60
    dy = np.abs(a[1:, :] - a[:-1, :]) > 60
    return int(dx.sum() + dy.sum())


off, aa = sys.argv[1], sys.argv[2]
if len(sys.argv) > 3 and sys.argv[3] == "whole":    # whole world area, for use in any camera mode (the UI strip at the bottom is cut off)
    LAWN = FENCE = (330, 0, 1920, 880)
ls = sharp(luma(aa, LAWN)) / sharp(luma(off, LAWN))
eh = hard(luma(aa, FENCE)) / max(1, hard(luma(off, FENCE)))
print("lawn_sharpness ratio %.3f   edge_hardness ratio %.3f" % (ls, eh))
