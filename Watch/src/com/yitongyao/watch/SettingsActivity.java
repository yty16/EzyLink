package com.yitongyao.watch;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.DialogInterface;
import android.graphics.Color;
import android.os.Bundle;
import android.text.method.ScrollingMovementMethod;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.TextView;

/** 手机端连接设置：电脑 IP / 端口 / 配对码 / MAC，以及自动连接与自动发现。 */
public class SettingsActivity extends Activity {

    private EditText editIp;
    private EditText editPort;
    private EditText editToken;
    private EditText editMac;
    private EditText editBtMac;
    private RadioGroup radioMode;
    private CheckBox chkAutoConnect;
    private TextView searchResult;
    private SharedPreferences prefs;
    private DeviceStore store;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread t, Throwable e) {
                showError(e);
            }
        });
        try {
            super.onCreate(savedInstanceState);
            initUi();
        } catch (Throwable e) {
            showError(e);
        }
    }

    private void initUi() {
        setContentView(R.layout.activity_settings);

        prefs = getSharedPreferences("yity", Context.MODE_PRIVATE);
        store = new DeviceStore(prefs);
        editIp = findViewById(R.id.editIp);
        editPort = findViewById(R.id.editPort);
        editToken = findViewById(R.id.editToken);
        editMac = findViewById(R.id.editMac);
        editBtMac = findViewById(R.id.editBtMac);
        radioMode = findViewById(R.id.radioMode);
        chkAutoConnect = findViewById(R.id.chkAutoConnect);
        searchResult = findViewById(R.id.searchResult);

        editIp.setText(prefs.getString("ip", ""));
        editPort.setText(prefs.getString("port", "7788"));
        editToken.setText(prefs.getString("token", ""));
        editMac.setText(prefs.getString("mac", ""));
        editBtMac.setText(prefs.getString("bt_mac", ""));
        String mode = prefs.getString("conn_mode", "auto");
        if ("wifi".equals(mode)) radioMode.check(R.id.modeWifi);
        else if ("bt".equals(mode)) radioMode.check(R.id.modeBt);
        else radioMode.check(R.id.modeAuto);
        chkAutoConnect.setChecked(prefs.getBoolean("auto_connect", true));

        Button save = findViewById(R.id.btnSave);
        save.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                prefs.edit()
                        .putString("ip", editIp.getText().toString().trim())
                        .putString("port", editPort.getText().toString().trim())
                        .putString("token", editToken.getText().toString().trim())
                        .putString("mac", editMac.getText().toString().trim())
                        .putString("bt_mac", editBtMac.getText().toString().trim())
                        .putString("conn_mode", currentMode())
                        .putBoolean("auto_connect", chkAutoConnect.isChecked())
                        .apply();
                // 把当前连接设置写回设备列表中的「当前设备」，避免切换 / 重装后丢失
                store.syncCurrentFromPrefs();
                finish();
            }
        });

        Button search = findViewById(R.id.btnSearch);
        search.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                doSearch();
            }
        });

        Button scanAdd = findViewById(R.id.btnScanAdd);
        scanAdd.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                startScanAdd();
            }
        });

        Button delete = findViewById(R.id.btnDelete);
        delete.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                confirmDeleteCurrent();
            }
        });

        Button about = findViewById(R.id.btnAbout);
        about.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                startActivity(new Intent(SettingsActivity.this, AboutActivity.class));
            }
        });
    }

    /** 连接方式 */
    private String currentMode() {
        int id = radioMode.getCheckedRadioButtonId();
        if (id == R.id.modeWifi) return "wifi";
        if (id == R.id.modeBt) return "bt";
        return "auto";
    }

    /** 广播搜索局域网内的电脑（不需要互联网）。 */
    private void doSearch() {
        searchResult.setText("正在搜索…");
        searchResult.setTextColor(Color.parseColor("#FFB74D"));
        new Thread(new Runnable() {
            public void run() {
                final java.util.List<String> found =
                        new ConnectionManager(SettingsActivity.this, null).discover();
                runOnUiThread(new Runnable() {
                    public void run() {
                        if (found.isEmpty()) {
                            searchResult.setText("没有搜到。请确认电脑端易通遥已开启服务，" +
                                    "且手机与电脑在同一 WiFi 或已连上电脑的移动热点。");
                            searchResult.setTextColor(Color.parseColor("#FF8A80"));
                            return;
                        }
                        String ip = found.get(0);
                        editIp.setText(ip);
                        editPort.setText(prefs.getString("port", "7788"));
                        searchResult.setText("已找到电脑：" + ip + "（已填入上方，点保存即可）");
                        searchResult.setTextColor(Color.parseColor("#0a8a8f"));
                    }
                });
            }
        }).start();
    }

    /** 扫码把一台新电脑加入设备列表（同 IP+端口则合并）。 */
    private static final int REQ_SCAN_ADD = 9301;

    private void startScanAdd() {
        if (android.os.Build.VERSION.SDK_INT >= 23
                && checkSelfPermission(android.Manifest.permission.CAMERA)
                != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{android.Manifest.permission.CAMERA}, REQ_SCAN_ADD);
            return;
        }
        try {
            startActivityForResult(new Intent(this, QrScannerActivity.class), REQ_SCAN_ADD);
        } catch (Throwable e) {
            android.widget.Toast.makeText(this, getString(R.string.scan_no_camera),
                    android.widget.Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                           int[] grantResults) {
        if (requestCode != REQ_SCAN_ADD) return;
        if (grantResults != null && grantResults.length > 0
                && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
            startScanAdd();
        } else {
            android.widget.Toast.makeText(this, getString(R.string.scan_need_perm),
                    android.widget.Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_SCAN_ADD || resultCode != RESULT_OK || data == null) return;
        String text = data.getStringExtra(QrScannerActivity.EXTRA_QR_TEXT);
        Device d = Device.fromQr(text);
        if (d == null) {
            android.widget.Toast.makeText(this, getString(R.string.scan_bad_code),
                    android.widget.Toast.LENGTH_LONG).show();
            return;
        }
        Device saved = store.upsertFromQr(d);
        store.activate(saved.id);
        // 重新加载表单以反映刚加入 / 切换的设备
        recreate();
    }

    /** 删除当前设备；删除后自动切到另一台或退回。 */
    private void confirmDeleteCurrent() {
        final Device cur = store.current();
        if (cur == null) {
            finish();
            return;
        }
        new android.app.AlertDialog.Builder(this)
                .setTitle(R.string.devices_delete_title)
                .setMessage(getString(R.string.devices_delete_msg) + " " + cur.ip)
                .setPositiveButton(android.R.string.ok, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        store.remove(cur.id);
                        if (store.list().isEmpty()) finish();
                        else recreate();
                    }
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    /** 表冠（旋转侧键 / 表圈）旋转 → 设置页纵向滚动。 */
    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (Rotary.scroll(this, event, R.id.scroll, 100f)) return true;
        return super.onGenericMotionEvent(event);
    }

    private void showError(Throwable e) {
        try {
            setTheme(android.R.style.Theme_Material_Light_NoActionBar);
            StringBuilder sb = new StringBuilder();
            sb.append("易通遥 设置页异常（请反馈给开发者）：\n\n");
            if (e != null) {
                sb.append(e.getClass().getName()).append(": ").append(e.getMessage()).append("\n");
                for (StackTraceElement st : e.getStackTrace()) {
                    sb.append("    at ").append(st.toString()).append("\n");
                }
            }
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
        }
    }
}
