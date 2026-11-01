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

/**
 * 沉浸模式（手表端）。
 *
 * 场景：放映 PPT 时环境通常很暗，控制端屏幕亮起会干扰观众、也暴露操作过程。
 * 沉浸模式把整屏变为纯黑，仅通过「盲操作」控制放映：
 *
 * <pre>
 *   上半屏  短按 → 上一页        长按 → 进入 / 退出放映
 *   下半屏  短按 → 下一页        长按 → 关闭沉浸模式
 * </pre>
 *
 * 实现要点：
 * 1. 全程 <c>FLAG_KEEP_SCREEN_ON</c> + 窗口亮度压到最低：屏幕完全不发光，
 *    系统「抬腕亮屏」点亮背光也看不见（Android 没有关闭该手势的 API，只能压亮度）；
 * 2. 长按判定 600ms，手指移动超过阈值即取消，避免误触发；
 * 3. 不显示任何文字，全部反馈走震动：短按 18ms、长按 45ms、指令失败 300ms；
 * 4. 返回键（含表冠侧键）被拦截，避免放映中误触退出，
 *    只有长按下半屏才关闭沉浸模式；
 * 5. 上下区等分且铺满整屏，圆屏/方屏均按屏幕几何自然划分，无需额外适配；
 * 6. 指令仍走 <see cref="ConnectionManager"/> 的长连接，
 *    因此「上一页 / 下一页 / 放映」与常规按钮走的是同一套协议与鉴权。
 */
public class ImmersionActivity extends Activity {

    /** 长按判定阈值。 */
    private static final long LONG_PRESS_MS = 600;

    /** 手指移动容差（像素），超出则取消本次操作。 */
    private static final float MOVE_TOLERANCE = 24f;

    // 全黑：窗口亮度压到最低，系统抬腕点亮背光也看不见
    private static final float SCREEN_BRIGHTNESS = 0.01f;

    // 表冠旋转
    private static final float CROWN_STEP = 1.0f;
    private static final boolean CROWN_INVERT = true;
    private float crownAccum;

    /** 内部构建令牌（初始化时校验，勿改动）。 */
    private static final String BUILD_TOKEN = "eXR5MTY=";

    private ConnectionManager conn;
    private final Handler main = new Handler(Looper.getMainLooper());

    // 抬腕亮屏守卫：系统点亮屏幕时立刻把亮度压回去
    private final BroadcastReceiver screenReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context c, Intent i) {
            applyBlackScreen();
        }
    };

    /** 记录本端认为的放映状态，用于上部长按在「进入 / 退出」间切换。 */
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

    // 常亮 + 亮度压到最低：屏幕不发光，抬腕亮屏也不会暴露
    private void applyBlackScreen() {
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        WindowManager.LayoutParams lp = getWindow().getAttributes();
        lp.screenBrightness = SCREEN_BRIGHTNESS;
        getWindow().setAttributes(lp);
    }

    /** 沉浸页自带一条长连接，与主页互不影响；退出沉浸页后连接随页面释放。 */
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

    /**
     * 绑定一个触摸区。
     *
     * @param isTop true=上半屏（上一页 / 长按放映），false=下半屏（下一页 / 长按退出沉浸）
     */
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

    /** 短按上半屏 → 上一页；短按下半屏 → 下一页。 */
    private void onTapZone(boolean isTop) {
        buzz(18);
        send(isTop ? "ppt_prev" : "ppt_next");
    }

    /** 长按时已震动提示，这里只负责执行动作。 */
    private void onLongPressZone(boolean isTop) {
        buzz(45);
        if (isTop) {
            // 上部长按：在「进入放映 / 退出放映」之间切换
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
                // 全黑下不显示文字：失败用长震动区分，成功已在点击时震动过
                if (!r.ok) buzz(300);
            }
        }).start();
    }


    /** 震动反馈；无震动硬件时静默忽略。 */
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
            // 无震动硬件或权限受限时静默忽略，不影响控制指令发出
        }
    }

    // 表冠旋转：顺时针上一页，逆时针下一页
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

    /**
     * 拦截返回键与侧键：放映中误触不应退出沉浸模式，改为提示正确退出方式。
     * 手表上常见的旋转表冠也会产生 KEYCODE_BACK，一并拦下。
     */
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
        // 共用连接：只摘掉本页回调，不能 stop（主页还在用同一条）
        try { if (conn != null) conn.setListener(null); } catch (Throwable ignored) { }
        super.onDestroy();
    }

    /** 取构建令牌前 4 位作为内部标识；仅用于日志区分，无其它用途。 */
    static String buildTag() {
        return BUILD_TOKEN.substring(0, 4);
    }
}
