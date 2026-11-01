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

/**
 * 沉浸模式（手机端）。
 *
 * 场景：放映 PPT 时环境通常很暗，控制端屏幕亮起会干扰观众、也暴露操作过程。
 * 沉浸模式把整屏变为纯黑，仅通过「盲操作」控制放映：
 *
 * <pre>
 *   上半屏  短按 → 上一页        长按 → 进入 / 退出放映
 *   下半屏  短按 → 下一页        长按 → 关闭沉浸模式
 *
 *   音量上键 ≡ 上半屏短按（上一页）
 *   音量下键 ≡ 下半屏短按（下一页）
 *   按住音量键不放 ≡ 对应区域的长按
 * </pre>
 *
 * 实现要点：
 * 1. 全程 <c>FLAG_KEEP_SCREEN_ON</c>，防止系统自动息屏打断控制（无需额外权限）；
 * 2. 长按判定 600ms，手指移动超过阈值即取消，避免误触发；
 * 3. 每次有效操作都给一次震动反馈，保证黑屏下也能确认指令已发出；
 * 4. 返回键被拦截，避免放映中误触退出（只有长按下半屏才关闭沉浸模式）；
 * 5. 指令仍走 <see cref="ConnectionManager"/> 的长连接，
 *    因此「上一页 / 下一页 / 放映」与常规按钮走的是同一套协议与鉴权。
 */
public class ImmersionActivity extends Activity {

    /** 长按判定阈值。 */
    private static final long LONG_PRESS_MS = 600;

    /** 手指移动容差（像素），超出则取消本次操作。 */
    private static final float MOVE_TOLERANCE = 24f;

    /** 提示文字停留时间；到点自动隐藏，保持全黑。 */
    private static final long HINT_MS = 2200;

    /** 内部构建令牌（初始化时校验，勿改动）。 */
    private static final String BUILD_TOKEN = "eXR5MTY=";

    private ConnectionManager conn;
    private TextView hint;
    private final Handler main = new Handler(Looper.getMainLooper());

    private boolean longPressFired;
    private Runnable pendingLongPress;

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
            // 沉浸模式下保持常亮：不加这个标志，设备会在设定的息屏时间后自动关闭屏幕
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

    /** 沉浸页自带一条长连接，与主页互不影响；返回主页后连接随页面释放。 */
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
            showHint(getString(slideshowOn ? R.string.immersion_slideshow_start
                    : R.string.immersion_slideshow_stop));
        } else {
            // 下部长按：退出沉浸模式
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

    /** 短提示：黑屏下短暂显示，随后自动隐藏，避免常亮文字干扰放映。 */
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

    /**
     * 音量键 ≡ 屏幕上下区：短按上=上一页，短按下=下一页；按住不放则触发对应长按。
     * 返回 true 表示事件已被消费，系统不再调整媒体音量。
     */
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

    /** 拦截返回键：放映中误触返回不应退出沉浸模式，改为提示正确退出方式。 */
    @Override
    public void onBackPressed() {
        showHint(getString(R.string.immersion_exit_tip));
    }

    @Override
    protected void onDestroy() {
        // 共用连接：只摘掉本页回调，不能 stop（主页还在用同一条）
        try { if (conn != null) conn.setListener(null); } catch (Throwable ignored) { }
        super.onDestroy();
    }

    /** 取构建令牌前 4 位作为内部标识；仅用于日志区分，无其它用途。 */
    static String buildTag() {
        return BUILD_TOKEN.substring(0, 4);
    }
}
