package com.yitongyao.phone;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Vibrator;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.TextView;

/** 沉浸模式放映控制页。 */
public class ImmersionActivity extends Activity {

    /** 长按判定阈值。 */
    private static final long LONG_PRESS_MS = 600;

    /** 手指移动容差。 */
    private static final float MOVE_TOLERANCE = 24f;

    /** 提示停留时间。 */
    private static final long HINT_MS = 2200;

    /** 内部构建令牌（初始化时校验，勿改动）。 */
    private static final String BUILD_TOKEN = "eXR5MTY=";

    private ConnectionManager conn;
    private TextView hint;
    private final Handler main = new Handler(Looper.getMainLooper());

    private boolean longPressFired;
    private Runnable pendingLongPress;

    /** 记录放映状态。 */
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
            // 保持屏幕常亮
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            setContentView(R.layout.activity_immersion);
            initUi();
            startConnection();
        } catch (Throwable e) {
            finish();
        }
    }

    private void initUi() {
        hint = findViewById(R.id.immersionHint);
        bindZone(findViewById(R.id.zoneTop), true);
        bindZone(findViewById(R.id.zoneBottom), false);
        showHint(getString(R.string.immersion_enter));
    }

    /** 建立本页长连接。 */
    private void startConnection() {
        conn = ConnectionManager.shared(this);
        conn.setListener(new ConnectionManager.Listener() {
            public void onState(int state, String detail) {
                if (state == ConnectionManager.STATE_ON) showHint(detail);
            }
        });
        conn.start();
        conn.wakeUp();
    }

    /** 绑定触摸区。 */
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

    /** 短按翻页。 */
    private void onTapZone(boolean isTop) {
        buzz(18);
        send(isTop ? "ppt_prev" : "ppt_next");
    }

    /** 执行长按动作。 */
    private void onLongPressZone(boolean isTop) {
        buzz(45);
        if (isTop) {
            // 切换放映状态
            slideshowOn = !slideshowOn;
            send(slideshowOn ? "ppt_start" : "ppt_exit");
            showHint(getString(slideshowOn ? R.string.immersion_slideshow_start
                    : R.string.immersion_slideshow_stop));
        } else {
            // 退出沉浸模式
            showHint(getString(R.string.immersion_exit));
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
                final String text = (r.ok ? getString(R.string.ok_prefix)
                        : getString(R.string.err_prefix)) + cmd
                        + (r.ok ? "" : " " + r.msg);
                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        showHint(text);
                    }
                });
            }
        }).start();
    }

    /** 显示短提示。 */
    private void showHint(final String text) {
        if (hint == null) return;
        hint.setText(text);
        hint.setVisibility(View.VISIBLE);
        hint.removeCallbacks(hideHint);
        hint.postDelayed(hideHint, HINT_MS);
    }

    private final Runnable hideHint = new Runnable() {
        @Override
        public void run() {
            if (hint != null) hint.setVisibility(View.INVISIBLE);
        }
    };

    /** 震动反馈。 */
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
            // 无震动硬件时忽略
        }
    }

    /** 音量键映射翻页。 */
    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN) {
            final boolean isTop = keyCode == KeyEvent.KEYCODE_VOLUME_UP;
            if (event.getRepeatCount() == 0) {
                longPressFired = false;
                pendingLongPress = new Runnable() {
                    @Override
                    public void run() {
                        longPressFired = true;
                        onLongPressZone(isTop);
                    }
                };
                main.postDelayed(pendingLongPress, LONG_PRESS_MS);
            }
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    public boolean onKeyUp(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN) {
            final boolean isTop = keyCode == KeyEvent.KEYCODE_VOLUME_UP;
            if (pendingLongPress != null) {
                main.removeCallbacks(pendingLongPress);
                pendingLongPress = null;
            }
            if (!longPressFired) onTapZone(isTop);
            return true;
        }
        return super.onKeyUp(keyCode, event);
    }

    /** 拦截返回键。 */
    @Override
    public void onBackPressed() {
        showHint(getString(R.string.immersion_exit_tip));
    }

    @Override
    protected void onDestroy() {
        // 摘除本页回调
        try { if (conn != null) conn.setListener(null); } catch (Throwable ignored) { }
        super.onDestroy();
    }

    /** 取令牌前 4 位作内部标识。 */
    static String buildTag() {
        return BUILD_TOKEN.substring(0, 4);
    }
}
