package com.yitongyao.watch;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Rect;
import android.hardware.Camera;
import android.os.Build;
import android.os.Bundle;
import android.view.Gravity;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.TextView;

import com.google.zxing.BinaryBitmap;
import com.google.zxing.DecodeHintType;
import com.google.zxing.LuminanceSource;
import com.google.zxing.NotFoundException;
import com.google.zxing.PlanarYUVLuminanceSource;
import com.google.zxing.Result;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeReader;

import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;

/** 扫码连接页面：扫描配对二维码并回传原文。*/
public class QrScannerActivity extends Activity
        implements SurfaceHolder.Callback, Camera.PreviewCallback {

    /** 结果 Intent 中承载二维码原文的键名。 */
    public static final String EXTRA_QR_TEXT = "qr_text";

    private static final int REQ_CAMERA = 1001;

    // 构建令牌，勿改动。
    private static final String BUILD_TOKEN = "eXR5MTY=";

    /** 预览最长边。*/
    private int previewMaxDim = 640;

    private SurfaceView surface;
    private Camera camera;
    private byte[] buffer;
    private int cameraId = -1;
    private int previewWidth;
    private int previewHeight;

    private final Object lock = new Object();
    private final AtomicBoolean decoding = new AtomicBoolean(false);
    private final AtomicBoolean finished = new AtomicBoolean(false);
    private volatile boolean hasSurface = false;

    private TextView hint;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread t, Throwable e) {
                fail(getString(R.string.scan_no_camera) + " " + e.getMessage());
            }
        });
        super.onCreate(savedInstanceState);

        getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);

        surface = new SurfaceView(this);
        surface.getHolder().addCallback(this);
        root.addView(surface, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        View overlay = new ScanOverlay(this);
        root.addView(overlay, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        hint = new TextView(this);
        hint.setText(getString(R.string.scan_hint));
        hint.setTextColor(Color.parseColor("#B9D9FF"));
        hint.setTextSize(13f);
        hint.setGravity(Gravity.CENTER);
        hint.setPadding(24, 12, 24, 12);
        hint.setBackgroundColor(Color.parseColor("#66000000"));
        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.BOTTOM);
        root.addView(hint, lp);

        setContentView(root);

        if (!hasCameraPermission()) {
            if (Build.VERSION.SDK_INT >= 23) {
                requestPermissions(new String[]{android.Manifest.permission.CAMERA}, REQ_CAMERA);
            } else {
                fail(getString(R.string.scan_need_perm));
            }
        }
    }

    private boolean hasCameraPermission() {
        if (Build.VERSION.SDK_INT < 23) return true;
        return checkSelfPermission(android.Manifest.permission.CAMERA)
                == PackageManager.PERMISSION_GRANTED;
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                           int[] grantResults) {
        if (requestCode != REQ_CAMERA) return;
        if (grantResults == null || grantResults.length == 0
                || grantResults[0] != PackageManager.PERMISSION_GRANTED) {
            fail(getString(R.string.scan_need_perm));
            return;
        }
        if (hasSurface) openCamera(surface.getHolder());
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (hasCameraPermission() && hasSurface) openCamera(surface.getHolder());
    }

    @Override
    protected void onPause() {
        closeCamera();
        super.onPause();
    }

    // ---- Surface

    public void surfaceCreated(SurfaceHolder holder) {
        hasSurface = true;
        if (hasCameraPermission()) openCamera(holder);
    }

    public void surfaceChanged(SurfaceHolder holder, int format, int w, int h) {
        if (camera == null) return;
        try {
            camera.stopPreview();
            camera.setPreviewDisplay(holder);
            applyDisplayOrientation();
            startPreview();
        } catch (Throwable ignored) {
        }
    }

    public void surfaceDestroyed(SurfaceHolder holder) {
        hasSurface = false;
        closeCamera();
    }

    // ---- Camera

    private void openCamera(SurfaceHolder holder) {
        if (camera != null) return;
        try {
            int count = Camera.getNumberOfCameras();
            int target = -1;
            for (int i = 0; i < count; i++) {
                Camera.CameraInfo info = new Camera.CameraInfo();
                Camera.getCameraInfo(i, info);
                if (info.facing == Camera.CameraInfo.CAMERA_FACING_BACK) {
                    target = i;
                    break;
                }
            }
            if (target < 0) target = 0;
            cameraId = target;
            camera = Camera.open(target);
            if (camera == null) {
                fail(getString(R.string.scan_no_camera));
                return;
            }
            camera.setPreviewDisplay(holder);
            Camera.Parameters p = camera.getParameters();
            Camera.Size size = pickPreviewSize(p.getSupportedPreviewSizes());
            if (size != null) {
                previewWidth = size.width;
                previewHeight = size.height;
                p.setPreviewSize(size.width, size.height);
            } else {
                previewWidth = p.getPreviewSize().width;
                previewHeight = p.getPreviewSize().height;
            }
            p.setPreviewFormat(android.graphics.ImageFormat.NV21);
            trySetFocus(p);
            camera.setParameters(p);
            applyDisplayOrientation();
            startPreview();
        } catch (Throwable e) {
            closeCamera();
            fail(getString(R.string.scan_no_camera));
        }
    }

    private void startPreview() {
        if (camera == null) return;
        try {
            int bits = previewWidth * previewHeight
                    * android.graphics.ImageFormat.getBitsPerPixel(
                    android.graphics.ImageFormat.NV21) / 8;
            buffer = new byte[bits];
            camera.addCallbackBuffer(buffer);
            camera.setPreviewCallbackWithBuffer(this);
            camera.startPreview();
        } catch (Throwable e) {
            fail(getString(R.string.scan_no_camera));
        }
    }

    private void closeCamera() {
        synchronized (lock) {
            if (camera != null) {
                try {
                    camera.setPreviewCallbackWithBuffer(null);
                    camera.stopPreview();
                } catch (Throwable ignored) {
                }
                try {
                    camera.release();
                } catch (Throwable ignored) {
                }
                camera = null;
                cameraId = -1;
            }
        }
    }

    private void applyDisplayOrientation() {
        if (camera == null) return;
        try {
            Camera.CameraInfo info = new Camera.CameraInfo();
            Camera.getCameraInfo(cameraId >= 0 ? cameraId : 0, info);
            int degrees = 0;
            int rot = getWindowManager().getDefaultDisplay().getRotation();
            if (rot == Surface.ROTATION_90) degrees = 90;
            else if (rot == Surface.ROTATION_180) degrees = 180;
            else if (rot == Surface.ROTATION_270) degrees = 270;
            int result;
            if (info.facing == Camera.CameraInfo.CAMERA_FACING_FRONT) {
                result = (info.orientation + degrees) % 360;
                result = (360 - result) % 360;
            } else {
                result = (info.orientation - degrees + 360) % 360;
            }
            camera.setDisplayOrientation(result);
        } catch (Throwable ignored) {
        }
    }

    private Camera.Size pickPreviewSize(List<Camera.Size> sizes) {
        if (sizes == null || sizes.isEmpty()) return null;
        Camera.Size best = null;
        int bestScore = Integer.MAX_VALUE;
        for (Camera.Size s : sizes) {
            // 优先 16:9 且最长边接近目标
            int longSide = Math.max(s.width, s.height);
            int score = Math.abs(longSide - previewMaxDim);
            float ratio = (float) Math.max(s.width, s.height)
                    / Math.min(s.width, s.height);
            if (Math.abs(ratio - 16f / 9f) > 0.15f) score += 400;
            if (score < bestScore) {
                bestScore = score;
                best = s;
            }
        }
        return best;
    }

    private void trySetFocus(Camera.Parameters p) {
        try {
            List<String> modes = p.getSupportedFocusModes();
            if (modes != null) {
                if (modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE)) {
                    p.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
                } else if (modes.contains(Camera.Parameters.FOCUS_MODE_AUTO)) {
                    p.setFocusMode(Camera.Parameters.FOCUS_MODE_AUTO);
                }
            }
        } catch (Throwable ignored) {
        }
    }

    // ---- Decode

    @Override
    public void onPreviewFrame(byte[] data, Camera cam) {
        if (data == null) return;
        if (!decoding.compareAndSet(false, true)) {
            // 复用缓冲继续出帧。
            if (cam != null) {
                try {
                    cam.addCallbackBuffer(data);
                } catch (Throwable ignored) {
                }
            }
            return;
        }
        final byte[] frame = data;
        final int w = previewWidth;
        final int h = previewHeight;
        final Camera c = cam;
        new Thread(new Runnable() {
            @Override
            public void run() {
                String text = null;
                try {
                    text = decodeFrame(frame, w, h);
                } catch (Throwable ignored) {
                }
                if (text != null && text.length() > 0) {
                    succeed(text);
                    return;
                }
                decoding.set(false);
                if (c != null) {
                    try {
                        c.addCallbackBuffer(frame);
                    } catch (Throwable ignored) {
                    }
                }
            }
        }).start();
    }

    /** 解码帧，失败时旋转 90° 重试。*/
    private String decodeFrame(byte[] data, int w, int h) {
        if (data == null || w <= 0 || h <= 0) return null;
        String r = decodeLuma(data, w, h);
        if (r != null) return r;
        byte[] rot = rotate90(data, w, h);
        return decodeLuma(rot, h, w);
    }

    private String decodeLuma(byte[] y, int w, int h) {
        try {
            LuminanceSource src = new PlanarYUVLuminanceSource(y, w, h, 0, 0, w, h, false);
            BinaryBitmap bmp = new BinaryBitmap(new HybridBinarizer(src));
            Map<DecodeHintType, Object> hints = new EnumMap<DecodeHintType, Object>(DecodeHintType.class);
            hints.put(DecodeHintType.TRY_HARDER, Boolean.TRUE);
            hints.put(DecodeHintType.CHARACTER_SET, "UTF-8");
            QRCodeReader reader = new QRCodeReader();
            Result res = reader.decode(bmp, hints);
            return res == null ? null : res.getText();
        } catch (NotFoundException e) {
            return null;
        } catch (Throwable e) {
            return null;
        }
    }

    /** 旋转亮度平面 90°。*/
    private static byte[] rotate90(byte[] src, int w, int h) {
        byte[] out = new byte[w * h];
        int idx = 0;
        for (int x = 0; x < w; x++) {
            for (int y = h - 1; y >= 0; y--) {
                out[idx++] = src[y * w + x];
            }
        }
        return out;
    }

    private void succeed(String text) {
        if (finished.compareAndSet(false, true)) {
            try {
                View v = getWindow().getDecorView();
                v.performHapticFeedback(android.view.HapticFeedbackConstants.LONG_PRESS);
            } catch (Throwable ignored) {
            }
            Intent data = new Intent();
            data.putExtra(EXTRA_QR_TEXT, text);
            setResult(RESULT_OK, data);
            finish();
        }
    }

    private void fail(String msg) {
        if (finished.compareAndSet(false, true)) {
            setResult(RESULT_CANCELED);
            try {
                android.widget.Toast.makeText(this, msg, android.widget.Toast.LENGTH_LONG).show();
            } catch (Throwable ignored) {
            }
            finish();
        }
    }

    /** 扫码取景框遮罩。*/
    private static class ScanOverlay extends View {

        private final Paint frame = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint mask = new Paint();

        ScanOverlay(android.content.Context ctx) {
            super(ctx);
            frame.setColor(Color.parseColor("#36E2E8"));
            frame.setStrokeWidth(4f);
            frame.setStyle(Paint.Style.STROKE);
            mask.setColor(Color.parseColor("#78000000"));
        }

        @Override
        protected void onDraw(Canvas canvas) {
            int cw = getWidth();
            int ch = getHeight();
            int side = Math.min(cw, ch) * 3 / 5;
            int left = (cw - side) / 2;
            int top = (ch - side) / 2;
            Rect r = new Rect(left, top, left + side, top + side);

            canvas.drawRect(0, 0, cw, top, mask);
            canvas.drawRect(0, r.bottom, cw, ch, mask);
            canvas.drawRect(0, top, left, r.bottom, mask);
            canvas.drawRect(r.right, top, cw, r.bottom, mask);

            canvas.drawRect(r, frame);

            int corner = side / 6;
            frame.setStrokeWidth(8f);
            canvas.drawLine(r.left, r.top + corner, r.left, r.top, frame);
            canvas.drawLine(r.left, r.top, r.left + corner, r.top, frame);
            canvas.drawLine(r.right - corner, r.top, r.right, r.top, frame);
            canvas.drawLine(r.right, r.top, r.right, r.top + corner, frame);
            canvas.drawLine(r.left, r.bottom - corner, r.left, r.bottom, frame);
            canvas.drawLine(r.left, r.bottom, r.left + corner, r.bottom, frame);
            canvas.drawLine(r.right - corner, r.bottom, r.right, r.bottom, frame);
            canvas.drawLine(r.right, r.bottom, r.right - corner, r.bottom, frame);
        }
    }
}
