package com.yitongyao.phone;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.os.Bundle;
import android.text.method.ScrollingMovementMethod;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.ScrollView;
import android.widget.TextView;

/** 手机端连接设置：电脑 IP / 端口 / 配对码 / MAC，以及自动连接与自动发现。 */
public class SettingsActivity extends Activity {

    private EditText editIp;
    private EditText editPort;
    private EditText editToken;
    private EditText editMac;
    private CheckBox chkAutoConnect;
    private TextView searchResult;
    private SharedPreferences prefs;

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
        editIp = findViewById(R.id.editIp);
        editPort = findViewById(R.id.editPort);
        editToken = findViewById(R.id.editToken);
        editMac = findViewById(R.id.editMac);
        chkAutoConnect = findViewById(R.id.chkAutoConnect);
        searchResult = findViewById(R.id.searchResult);

        editIp.setText(prefs.getString("ip", ""));
        editPort.setText(prefs.getString("port", "7788"));
        editToken.setText(prefs.getString("token", ""));
        editMac.setText(prefs.getString("mac", ""));
        chkAutoConnect.setChecked(prefs.getBoolean("auto_connect", true));

        Button save = findViewById(R.id.btnSave);
        save.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                prefs.edit()
                        .putString("ip", editIp.getText().toString().trim())
                        .putString("port", editPort.getText().toString().trim())
                        .putString("token", editToken.getText().toString().trim())
                        .putString("mac", editMac.getText().toString().trim())
                        .putBoolean("auto_connect", chkAutoConnect.isChecked())
                        .apply();
                finish();
            }
        });

        Button search = findViewById(R.id.btnSearch);
        search.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                doSearch();
            }
        });
    }

    /** 广播搜索局域网内的电脑（不需要互联网）。 */
    private void doSearch() {
        searchResult.setText("正在搜索…");
        searchResult.setTextColor(Color.parseColor("#B26A00"));
        new Thread(new Runnable() {
            public void run() {
                final java.util.List<String> found =
                        new ConnectionManager(SettingsActivity.this, null).discover();
                runOnUiThread(new Runnable() {
                    public void run() {
                        if (found.isEmpty()) {
                            searchResult.setText("没有搜到。请确认电脑端易通遥已开启服务，" +
                                    "且手机与电脑在同一 WiFi 或已连上电脑的移动热点。");
                            searchResult.setTextColor(Color.parseColor("#B00020"));
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
            tv.setTextColor(Color.BLACK);
            sv.setBackgroundColor(Color.WHITE);
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
