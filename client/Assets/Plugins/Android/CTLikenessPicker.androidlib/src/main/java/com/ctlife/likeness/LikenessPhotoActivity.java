package com.ctlife.likeness;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.media.ExifInterface;
import java.io.ByteArrayInputStream;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

/** System photo picker: no media-library permission, one selected URI, bounded decode, private normalized JPEG. */
public final class LikenessPhotoActivity extends Activity {
    public interface Result { void complete(String path, String error); }
    private static Result receiver;
    private static final int REQUEST = 4701;
    public static synchronized void pick(Activity activity, Result callback) {
        if (receiver != null) { callback.complete("", "busy"); return; }
        receiver = callback;
        activity.runOnUiThread(() -> {
            try { activity.startActivity(new Intent(activity, LikenessPhotoActivity.class)); }
            catch (Exception e) { deliver("", "unavailable"); }
        });
    }
    private static synchronized void deliver(String path, String error) {
        Result callback = receiver; receiver = null;
        if (callback != null) callback.complete(path, error);
    }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        if (state != null) return;
        Intent intent = new Intent(Build.VERSION.SDK_INT >= 33 ? MediaStore.ACTION_PICK_IMAGES : Intent.ACTION_OPEN_DOCUMENT);
        intent.setType("image/*");
        if (Build.VERSION.SDK_INT < 33) intent.addCategory(Intent.CATEGORY_OPENABLE);
        try { startActivityForResult(intent, REQUEST); }
        catch (Exception e) { deliver("", "unavailable"); finish(); }
    }
    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != REQUEST) return;
        if (result != RESULT_OK || data == null || data.getData() == null) { deliver("", "cancelled"); finish(); return; }
        final Uri uri = data.getData();
        new Thread(() -> {
            File file = null;
            try {
                byte[] source;
                try (InputStream in = getContentResolver().openInputStream(uri); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
                    byte[] chunk = new byte[32768]; int count;
                    while ((count = in.read(chunk)) != -1) {
                        if (out.size() + count > 8 * 1024 * 1024) throw new Exception("size");
                        out.write(chunk, 0, count);
                    }
                    source = out.toByteArray();
                }
                BitmapFactory.Options opts = new BitmapFactory.Options(); opts.inJustDecodeBounds = true;
                BitmapFactory.decodeByteArray(source, 0, source.length, opts);
                if (opts.outWidth < 128 || opts.outHeight < 128 || opts.outWidth > 16000 || opts.outHeight > 16000 || (long)opts.outWidth * opts.outHeight > 64000000L) throw new Exception("dimensions");
                opts.inSampleSize = 1;
                while (Math.max(opts.outWidth, opts.outHeight) / opts.inSampleSize > 2048) opts.inSampleSize *= 2;
                opts.inJustDecodeBounds = false;
                Bitmap bitmap = BitmapFactory.decodeByteArray(source, 0, source.length, opts);
                if (bitmap == null) throw new Exception("decode");
                // Normalize camera orientation before stripping metadata; the chosen photo preview and upload match.
                int orientation = new ExifInterface(new ByteArrayInputStream(source)).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL);
                Matrix transform = new Matrix();
                switch (orientation) {
                    case ExifInterface.ORIENTATION_FLIP_HORIZONTAL: transform.setScale(-1, 1); break;
                    case ExifInterface.ORIENTATION_ROTATE_180: transform.setRotate(180); break;
                    case ExifInterface.ORIENTATION_FLIP_VERTICAL: transform.setScale(1, -1); break;
                    case ExifInterface.ORIENTATION_TRANSPOSE: transform.setRotate(90); transform.postScale(-1, 1); break;
                    case ExifInterface.ORIENTATION_ROTATE_90: transform.setRotate(90); break;
                    case ExifInterface.ORIENTATION_TRANSVERSE: transform.setRotate(270); transform.postScale(-1, 1); break;
                    case ExifInterface.ORIENTATION_ROTATE_270: transform.setRotate(270); break;
                    default: break;
                }
                if (!transform.isIdentity()) { Bitmap oriented = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), transform, true); if (oriented != bitmap) { bitmap.recycle(); bitmap = oriented; } }
                file = File.createTempFile("ctlife-face-", ".jpg", getCacheDir());
                try (FileOutputStream out = new FileOutputStream(file)) { if (!bitmap.compress(Bitmap.CompressFormat.JPEG, 92, out)) throw new Exception("encode"); }
                finally { bitmap.recycle(); }
                deliver(file.getAbsolutePath(), "");
            } catch (Exception e) { if (file != null) file.delete(); deliver("", "invalid_photo"); }
            runOnUiThread(this::finish);
        }, "CTLifePhoto").start();
    }
}
