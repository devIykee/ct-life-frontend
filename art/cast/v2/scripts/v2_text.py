"""Bitmap-font labels and JPEG save for contact sheets (no FreeType in Blender's image IO)."""
import sys, numpy as np, bpy
sys.path.insert(0, '/Users/mac/Documents/codes/projects/ct-life/art/cast/scripts')
import cast_tex as T

def label(img, text, y0, h, color=(0.92, 0.94, 0.97), scale_h=None):
    H, W, _ = img.shape
    th = scale_h or max(10, int(h * 0.55))
    tw = min(int(th * 6 / 7 * len(text) * 0.86), W - 8)
    m = T.text_mask(text, th, max(tw, 6))
    x0 = max(4, (W - m.shape[1]) // 2)
    r0 = y0 + (h - th) // 2
    for i in range(th):
        row = r0 + (th - 1 - i)
        if 0 <= row < H:
            sel = m[i] > 0.5
            img[row, x0:x0 + m.shape[1]][sel] = color

def save_jpg(arr, path):
    h, w, _ = arr.shape
    im = bpy.data.images.new('sheet', width=w, height=h, alpha=False, float_buffer=False)
    im.colorspace_settings.name = 'Non-Color'
    px = np.ones((h, w, 4), dtype=np.float32); px[:, :, :3] = np.clip(arr, 0, 1)
    im.pixels.foreach_set(px.ravel())
    bpy.context.scene.render.image_settings.file_format = 'JPEG'
    bpy.context.scene.render.image_settings.quality = 90
    im.filepath_raw = str(path); im.file_format = 'JPEG'; im.save()
    bpy.data.images.remove(im)
