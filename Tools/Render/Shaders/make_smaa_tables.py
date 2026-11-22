"""Converts the SMAA reference lookup tables (AreaTex.h, SearchTex.h from github.com/iryoku/smaa, MIT licence, see
SMAA-LICENSE.txt) from C arrays into the binary files embedded in Render.dll.
usage: python make_smaa_tables.py <AreaTex.h> <SearchTex.h>"""
import re
import sys


def convert(header, out, expected):
    text = open(header, encoding='utf8').read()
    body = text[text.index('{', text.index('=')):]
    nums = [int(x, 16) for x in re.findall(r'0x([0-9A-Fa-f]{2})', body)]
    assert len(nums) == expected, '%s: %d bytes, expected %d' % (header, len(nums), expected)
    open(out, 'wb').write(bytes(nums))
    print(out, len(nums), 'bytes')


convert(sys.argv[1], 'AreaTex.bin', 160 * 560 * 2)
convert(sys.argv[2], 'SearchTex.bin', 64 * 16)
