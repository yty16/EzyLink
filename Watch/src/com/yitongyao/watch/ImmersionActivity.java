package com.yitongyao.watch;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Vibrator;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;

/** 沉浸模式：全屏黑屏下盲操作控制放映。*/
public class ImmersionActivity extends Activity {

    /** 长按判定阈值。 */
    private static final long LONG_PRESS_MS = 600;

    /** 手指移动容差。*/
    private static final float MOVE_TOLERANCE = 24f;

    // 屏幕亮度。
    private static final float SCREEN_BRIGHTNESS = 0.01f;

    // 表冠旋转
    private static final float CROWN_STEP = 1.0f;
    private static final boolean CROWN_INVERT = true;
    private float crownAccum;

    /** 构建令牌，勿改动。*/
    private static final String BUILD_TOKEN = "eXR5MTY=";

    private ConnectionManager conn;
    private final Handler main = new Handler(Looper.getMainLooper());

    // 抬腕亮屏守卫。
    private final BroadcastReceiver screenReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context c, Intent i) {
            applyBlackScreen();
        }
    };

    /** 放映状态。*/
    private boolean slideshowOn;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread t, Throwable e) {
                finish();
            }
        });
        try {
            super.onCreate(savedInstanceState);
            setContentView(R.layout.activity_immersion);
            applyBlackScreen();
            try {
                registerReceiver(screenReceiver, new IntentFilter(Intent.ACTION_SCREEN_ON));
            } catch (Throwable ignored) { }
            initUi();
            startConnection();
        } catch (Throwable e) {
            finish();
        }
    }

    private void initUi() {
        bindZone(findViewById(R.id.zoneTop), true);
        bindZone(findViewById(R.id.zoneBottom), false);
        buzz(60);
    }

    // 保持常亮并压低亮度。
    private void applyBlackScreen() {
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        WindowManager.LayoutParams lp = getWindow().getAttributes();
        lp.screenBrightness = SCREEN_BRIGHTNESS;
        getWindow().setAttributes(lp);
    }

    /** 建立长连接。*/
    private void startConnection() {
        conn = ConnectionManager.shared(this);
        conn.setListener(new ConnectionManager.Listener() {
            public void onState(int state, String detail) {
                if (state == ConnectionManager.STATE_ON) buzz(60);
            }
        });
        conn.start();
        conn.wakeUp();
    }

    /** 绑定触摸区。*/
    private void bindZone(View v, final boolean isTop) {
        v.setOnTouchListener(new View.OnTouchListener() {
            private boolean fired;
            private Runnable task;
            private float startX, startY;

            @Override
            public boolean onTouch(View view, MotionEvent e) {
                switch (e.getActionMasked()) {
                    case MotionEvent.ACTION_DOWN:
                        fired = false;
                        startX = e.getX();
                        startY = e.getY();
                        task = new Runnable() {
                            @Override
                            public void run() {
                                fired = true;
                                onLongPressZone(isTop);
                            }
                        };
                        main.postDelayed(task, LONG_PRESS_MS);
                        view.setPressed(true);
                        return true;

                    case MotionEvent.ACTION_MOVE:
                        if (task != null
                                && (Math.abs(e.getX() - startX) > MOVE_TOLERANCE
                                || Math.abs(e.getY() - startY) > MOVE_TOLERANCE)) {
                            main.removeCallbacks(task);
                            task = null;
                            view.setPressed(false);
                        }
                        return true;

                    case MotionEvent.ACTION_UP:
                        view.setPressed(false);
                        if (task != null) {
                            main.removeCallbacks(task);
                            task = null;
                        }
                        if (!fired) onTapZone(isTop);
                        return true;

                    default:
                        view.setPressed(false);
                        if (task != null) {
                            main.removeCallbacks(task);
                            task = null;
                        }
                        return true;
                }
            }
        });
    }

    /** 短按翻页。*/
    private void onTapZone(boolean isTop) {
        buzz(18);
        send(isTop ? "ppt_prev" : "ppt_next");
    }

    /** 长按动作。*/
    private void onLongPressZone(boolean isTop) {
        buzz(45);
        if (isTop) {
            // 切换放映。
            slideshowOn = !slideshowOn;
            send(slideshowOn ? "ppt_start" : "ppt_exit");
        } else {
            // 下部长按：退出沉浸模式
            main.postDelayed(new Runnable() {
                @Override
                public void run() {
                    finish();
                }
            }, 260);
        }
    }

    private void send(final String cmd) {
        if (conn == null) return;
        new Thread(new Runnable() {
            @Override
            public void run() {
                ConnectionManager.Result r = conn.send(cmd);
                // 失败时长震动提示。
                if (!r.ok) buzz(300);
            }
        }).start();
    }


    /** 震动反馈。*/
    private void buzz(int ms) {
        try {
            Vibrator v = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);
            if (v != null) {
                if (android.os.Build.VERSION.SDK_INT >= 26) {
                    v.vibrate(android.os.VibrationEffect.createOneShot(
                            ms, android.os.VibrationEffect.DEFAULT_AMPLITUDE));
                } else {
                    v.vibrate(ms);
                }
            }
        } catch (Throwable ignored) {
        }
    }

    // 表冠旋转翻页。
    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (event.getAction() == MotionEvent.ACTION_SCROLL) {
            float v = event.getAxisValue(MotionEvent.AXIS_SCROLL);
            if (CROWN_INVERT) v = -v;
            if (v * crownAccum < 0f) crownAccum = 0f;
            crownAccum += v;
            if (crownAccum >= CROWN_STEP) {
                crownAccum = 0f;
                onTapZone(true);
            } else if (crownAccum <= -CROWN_STEP) {
                crownAccum = 0f;
                onTapZone(false);
            }
            return true;
        }
        return super.onGenericMotionEvent(event);
    }

    /** 拦截返回键与侧键。*/
    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK
                || keyCode == KeyEvent.KEYCODE_NAVIGATE_PREVIOUS
                || keyCode == KeyEvent.KEYCODE_STEM_PRIMARY) {
            buzz(80);
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    public void onBackPressed() {
        buzz(80);
    }

    @Override
    protected void onResume() {
        super.onResume();
        applyBlackScreen();
    }

    @Override
    protected void onDestroy() {
        try { unregisterReceiver(screenReceiver); } catch (Throwable ignored) { }
        // 摘除本页回调。
        try { if (conn != null) conn.setListener(null); } catch (Throwable ignored) { }
        super.onDestroy();
    }

    /** 取构建令牌前 4 位作标识。*/
    static String buildTag() {
        return BUILD_TOKEN.substring(0, 4);
    }
}
