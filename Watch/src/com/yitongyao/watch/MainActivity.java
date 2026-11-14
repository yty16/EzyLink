package com.yitongyao.watch;

import android.app.Activity;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.text.method.ScrollingMovementMethod;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

/**
 * 手表端主控制界面（圆屏 / 方屏自适应）。
 * 通过 WiFi / 电脑移动热点直连电脑，控制 PPT 放映与锁屏/睡眠/关机/重启/开机(WoL)。
 *
 * 打开即自动连接：由 ConnectionManager 在后台完成「直连已保存地址 → 广播发现 → 自动重试」，
 * 连上后保持长连接，点按钮即时生效；未连接时会明确提示并立即重试，不再静默失败。
 *
 * 内置「崩溃自报」：任何未捕获异常都会以安全主题在屏幕上打印堆栈，
 * 便于在无电脑/无 adb 时直接看到真正报错。
 */
public class MainActivity extends Activity {

    /** 扫码请求码。 */
    private static final int REQ_SCAN = 9101;
    /** 相机权限请求码。 */
    private static final int REQ_CAM_PERM = 9102;
    /** 蓝牙权限请求码。 */
    private static final int REQ_BT_PERM = 9103;
    /** 设备列表页请求码。 */
    private static final int REQ_DEVICES = 9104;
    private static final String BT_PERM = "android.permission.BLUETOOTH_CONNECT";
    /** 电脑端二维码固定前缀：YTY1|IP|端口|配对码[|蓝牙MAC]。 */
    private static final String QR_PREFIX = "YTY1|";

    private SharedPreferences prefs;
    private TextView status;
    private TextView connState;
    private ConnectionManager conn;
    private DeviceStore deviceStore;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        // 全局兜底：任何主线程未捕获异常都显示到屏幕上
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread t, Throwable e) {
                showError(e);
            }
        });
        try {
            super.onCreate(savedInstanceState);
            // 首次使用须同意《用户协议》与《免责声明》，未同意则进入协议网关
            prefs = getSharedPreferences("yity", Context.MODE_PRIVATE);
            if (!prefs.getBoolean("eula_agreed", false)) {
                startActivity(new Intent(MainActivity.this, AgreementActivity.class));
                finish();
                return;
            }
            initUi();
        } catch (Throwable e) {
            showError(e);
        }
    }

    private void initUi() {
        setContentView(R.layout.activity_main);

        prefs = getSharedPreferences("yity", Context.MODE_PRIVATE);
        deviceStore = new DeviceStore(prefs);
        deviceStore.current();   // 首次启动把旧版单一连接迁移为「我的电脑」
        status = findViewById(R.id.status);
        connState = findViewById(R.id.connState);

        bind(R.id.btnStart, "ppt_start");
        bind(R.id.btnNext, "ppt_next");
        bind(R.id.btnPrev, "ppt_prev");
        bind(R.id.btnExit, "ppt_exit");
        bind(R.id.btnBlack, "ppt_black");
        bind(R.id.btnWhite, "ppt_white");
        bind(R.id.btnLock, "lock");
        bind(R.id.btnSleep, "sleep");
        bind(R.id.btnWol, "wol");

        findViewById(R.id.btnShutdown).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                confirm("shutdown", "确认要关闭电脑吗？");
            }
        });
        findViewById(R.id.btnRestart).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                confirm("restart", "确认要重启电脑吗？");
            }
        });
        findViewById(R.id.btnSettings).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                startActivity(new Intent(MainActivity.this, SettingsActivity.class));
            }
        });
        findViewById(R.id.btnDevices).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                startActivityForResult(new Intent(MainActivity.this, DeviceListActivity.class),
                        REQ_DEVICES);
            }
        });
        findViewById(R.id.btnScan).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                startScan();
            }
        });
        findViewById(R.id.btnImmersion).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                startActivity(new Intent(MainActivity.this, ImmersionActivity.class));
            }
        });

        conn = ConnectionManager.shared(this);
        conn.setListener(new ConnectionManager.Listener() {
            public void onState(int state, String detail) {
                applyConnState(state, detail);
            }
        });
        conn.start();
        // 共用连接可能已经连好（例如从沉浸页返回），按真实状态回填
        if (conn.isConnected()) {
            applyConnState(ConnectionManager.STATE_ON, "已连接 " + conn.getTarget());
        } else {
            applyConnState(ConnectionManager.STATE_CONNECTING, "正在连接电脑…");
        }
    }

    /** 蓝牙权限 */
    private void ensureBluetoothPermission() {
        if (Build.VERSION.SDK_INT >= 31
                && checkSelfPermission(BT_PERM) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{BT_PERM}, REQ_BT_PERM);
        }
    }

    /** 启动扫码页；先确保已获得相机权限。 */
    private void startScan() {
        if (Build.VERSION.SDK_INT >= 23
                && checkSelfPermission(android.Manifest.permission.CAMERA)
                != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{android.Manifest.permission.CAMERA}, REQ_CAM_PERM);
            return;
        }
        try {
            startActivityForResult(new Intent(this, QrScannerActivity.class), REQ_SCAN);
        } catch (Throwable e) {
            Toast.makeText(this, getString(R.string.scan_no_camera), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                           int[] grantResults) {
        if (requestCode != REQ_CAM_PERM) return;
        if (grantResults != null && grantResults.length > 0
                && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
            startScan();
        } else {
            Toast.makeText(this, getString(R.string.scan_need_perm), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_SCAN || resultCode != RESULT_OK || data == null) return;
        String text = data.getStringExtra(QrScannerActivity.EXTRA_QR_TEXT);
        if (!applyQrPayload(text)) {
            Toast.makeText(this, getString(R.string.scan_bad_code), Toast.LENGTH_LONG).show();
        }
    }

    /**
     * 解析电脑端二维码：把扫码得到的电脑作为一台设备写入仓库（同 IP+端口则合并），
     * 并立即切换为当前设备触发重连。支持标准格式 YTY1|IP|端口|配对码[|蓝牙MAC] 及容错写法。
     */
    private boolean applyQrPayload(String text) {
        Device d = Device.fromQr(text);
        if (d == null) return false;
        Device saved = deviceStore.upsertFromQr(d);
        deviceStore.activate(saved.id);
        String label = saved.name != null && !saved.name.isEmpty() ? saved.name : saved.ip;
        status.setText("已添加/切换：" + label + " " + saved.ip + ":" + saved.port + " …");
        if (conn != null) conn.wakeUp();
        return true;
    }

    @Override
    protected void onResume() {
        super.onResume();
        // 从设置页 / 设备列表返回后立刻重试（可能刚切换了设备 / 改了 IP / 配对码）
        if (conn != null) conn.wakeUp();
    }

    /** 表冠（旋转侧键 / 表圈）旋转 → 主页面纵向滚动。 */
    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (Rotary.scroll(this, event, R.id.scroll, 100f)) return true;
        return super.onGenericMotionEvent(event);
    }

    @Override
    protected void onDestroy() {
        if (conn != null) conn.stop();
        super.onDestroy();
    }

    private void applyConnState(int state, String detail) {
        if (connState == null) return;
        connState.setText(detail);
        if (state == ConnectionManager.STATE_ON) {
            connState.setTextColor(Color.parseColor("#0a8a8f"));   // 已连接：青绿
        } else if (state == ConnectionManager.STATE_CONNECTING) {
            connState.setTextColor(Color.parseColor("#FFB74D"));   // 连接中：橙
        } else {
            connState.setTextColor(Color.parseColor("#9E9E9E"));   // 未连接：灰
        }
    }

    private void bind(int id, final String cmd) {
        findViewById(id).setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                send(cmd);
            }
        });
    }

    private void confirm(final String cmd, String message) {
        new android.app.AlertDialog.Builder(this)
                .setTitle(cmd.equals("shutdown") ? "关机" : "重启")
                .setMessage(message)
                .setPositiveButton("确认", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        send(cmd);
                    }
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private void send(final String cmd) {
        // 网络开机不需要先连接电脑：直接发 WoL 魔术包
        if ("wol".equals(cmd)) {
            sendWol();
            return;
        }

        status.setText("执行中…");
        new Thread(new Runnable() {
            public void run() {
                ConnectionManager.Result r = conn.send(cmd);
                final String text = (r.ok ? "OK " : "ERR ") + cmd
                        + (r.ok ? "" : " " + r.msg);
                runOnUiThread(new Runnable() {
                    public void run() {
                        status.setText(text);
                    }
                });
            }
        }).start();
    }

    private void sendWol() {
        final String mac = prefs.getString("mac", "");
        if (mac == null || mac.trim().isEmpty()) {
            status.setText("请先在设置中填写电脑 MAC");
            return;
        }
        status.setText("发送开机信号…");
        new Thread(new Runnable() {
            public void run() {
                try {
                    RemoteClient.wakeOnLan(mac);
                    setStatus("已发送开机信号 (WoL)");
                } catch (final Exception e) {
                    setStatus("WoL 失败: " + e.getMessage());
                }
            }
        }).start();
    }

    private void setStatus(final String text) {
        runOnUiThread(new Runnable() {
            public void run() {
                status.setText(text);
            }
        });
    }

    /** 以安全主题用代码构建错误界面，避免再次加载可能出问题的 XML/style。 */
    private void showError(Throwable e) {
        try {
            setTheme(android.R.style.Theme_Material_Light_NoActionBar);
            StringBuilder sb = new StringBuilder();
            sb.append("易通遥 启动异常（请将下方内容反馈给开发者）：\n\n");
            appendThrowable(sb, e);
            ScrollView sv = new ScrollView(this);
            TextView tv = new TextView(this);
            tv.setText(sb.toString());
            tv.setTextSize(12);
            tv.setTextColor(Color.WHITE);
            sv.setBackgroundColor(Color.parseColor("#1b1136"));
            tv.setPadding(24, 24, 24, 24);
            tv.setMovementMethod(new ScrollingMovementMethod());
            sv.addView(tv, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            try {
                super.setContentView(sv);
            } catch (Throwable ignored) {
            }
        } catch (Throwable ignored) {
            // 连错误界面都无法构建时静默退出，避免死循环
        }
    }

    private static void appendThrowable(StringBuilder sb, Throwable e) {
        if (e == null) {
            return;
        }
        sb.append(e.getClass().getName()).append(": ").append(e.getMessage()).append("\n");
        for (StackTraceElement st : e.getStackTrace()) {
            sb.append("    at ").append(st.toString()).append("\n");
        }
        Throwable c = e.getCause();
        while (c != null) {
            sb.append("\nCaused by: ").append(c.getClass().getName()).append(": ")
                    .append(c.getMessage()).append("\n");
            for (StackTraceElement st : c.getStackTrace()) {
                sb.append("    at ").append(st.toString()).append("\n");
            }
            c = c.getCause();
        }
    }
}
