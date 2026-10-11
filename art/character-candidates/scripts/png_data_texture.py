"""Write linear RGBA16 data channels without treating alpha as opacity.

Blender float-image PNG saving unpremultiplies RGB by alpha. That is wrong for
metallic RGB + smoothness A. This writer preserves each channel independently.
Input follows Blender's bottom-to-top pixel order; PNG rows are top-to-bottom.
"""
import struct
import zlib
import numpy as np


def write_rgba16(path, pixels):
    data = np.clip(np.rint(np.asarray(pixels) * 65535), 0, 65535).astype('>u2')
    height, width, channels = data.shape
    assert channels == 4

    def chunk(kind, payload):
        return (struct.pack('>I', len(payload)) + kind + payload
                + struct.pack('>I', zlib.crc32(kind + payload) & 0xffffffff))

    rows = b''.join(b'\x00' + row.tobytes() for row in data[::-1])
    path.write_bytes(b'\x89PNG\r\n\x1a\n'
                     + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 16, 6, 0, 0, 0))
                     + chunk(b'gAMA', struct.pack('>I', 100000))
                     + chunk(b'IDAT', zlib.compress(rows, 9))
                     + chunk(b'IEND', b''))
    return data.astype(np.float32) / 65535
