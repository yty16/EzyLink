package com.yitongyao.phone;

import android.app.Activity;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.os.Bundle;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import java.util.List;
import java.util.UUID;

/** 设备列表页。 */
public class DeviceListActivity extends Activity {

    private static final int REQ_ADD_SCAN = 9201;
    private SharedPreferences prefs;
    private DeviceStore store;
    private LinearLayout listContainer;
    private TextView emptyHint;

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
            setContentView(R.layout.activity_devicelist);
            prefs = getSharedPreferences("yity", Context.MODE_PRIVATE);
            store = new DeviceStore(prefs);
            listContainer = findViewById(R.id.listContainer);
            emptyHint = findViewById(R.id.emptyHint);

            findViewById(R.id.btnAddScan).setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    startScan();
                }
            });
            findViewById(R.id.btnAddManual).setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    addManual();
                }
            });

            // 迁移旧连接设置
            store.current();
            buildList();
        } catch (Throwable e) {
            finish();
        }
    }

    private void buildList() {
        listContainer.removeAllViews();
        List<Device> devices = store.list();
        Device cur = store.current();
        String curId = cur == null ? "" : cur.id;
        emptyHint.setVisibility(devices.isEmpty() ? View.VISIBLE : View.GONE);

        for (final Device d : devices) {
            Button row = new Button(this);
            row.setText((d.id.equals(curId) ? "● " : "   ")
                    + (d.name != null && !d.name.isEmpty() ? d.name : d.ip)
                    + "\n" + d.ip + ":" + d.port);
            row.setTextSize(15f);
            row.setMinHeight(56);
            row.setGravity(android.view.Gravity.START | android.view.Gravity.CENTER_VERTICAL);
            row.setPadding(16, 8, 16, 8);
            if (d.id.equals(curId)) {
                row.setBackgroundColor(getResources().getColor(R.color.control_bg));
                row.setTextColor(getResources().getColor(R.color.accent));
            } else {
                row.setBackgroundColor(getResources().getColor(R.color.surface));
                row.setTextColor(getResources().getColor(R.color.fg));
            }
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.setMargins(0, 0, 0, 8);
            row.setLayoutParams(lp);

            row.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    switchTo(d.id);
                }
            });
            row.setOnLongClickListener(new View.OnLongClickListener() {
                public boolean onLongClick(View v) {
                    confirmDelete(d);
                    return true;
                }
            });
            listContainer.addView(row);
        }
    }

    private void switchTo(String id) {
        store.activate(id);
        Toast.makeText(this, getString(R.string.devices_switched), Toast.LENGTH_SHORT).show();
        setResult(RESULT_OK);
        finish();
    }

    private void confirmDelete(final Device d) {
        new android.app.AlertDialog.Builder(this)
                .setTitle(R.string.devices_delete_title)
                .setMessage(getString(R.string.devices_delete_msg) + " " + d.ip)
                .setPositiveButton(android.R.string.ok, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        store.remove(d.id);
                        buildList();
                    }
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    private void addManual() {
        Device d = new Device();
        d.id = UUID.randomUUID().toString();
        d.name = "新电脑";
        d.ip = "";
        d.port = "7788";
        store.upsert(d);
        store.activate(d.id);
        // 打开设置页填写新设备
        startActivity(new Intent(this, SettingsActivity.class));
        finish();
    }

    private void startScan() {
        if (android.os.Build.VERSION.SDK_INT >= 23
                && checkSelfPermission(android.Manifest.permission.CAMERA)
                != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{android.Manifest.permission.CAMERA}, REQ_ADD_SCAN);
            return;
        }
        try {
            startActivityForResult(new Intent(this, QrScannerActivity.class), REQ_ADD_SCAN);
        } catch (Throwable e) {
            Toast.makeText(this, getString(R.string.scan_no_camera), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions,
                                           int[] grantResults) {
        if (requestCode != REQ_ADD_SCAN) return;
        if (grantResults != null && grantResults.length > 0
                && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
            startScan();
        } else {
            Toast.makeText(this, getString(R.string.scan_need_perm), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_ADD_SCAN || resultCode != RESULT_OK || data == null) return;
        String text = data.getStringExtra(QrScannerActivity.EXTRA_QR_TEXT);
        Device d = Device.fromQr(text);
        if (d == null) {
            Toast.makeText(this, getString(R.string.scan_bad_code), Toast.LENGTH_LONG).show();
            return;
        }
        Device saved = store.upsertFromQr(d);
        store.activate(saved.id);
        Toast.makeText(this, getString(R.string.devices_switched), Toast.LENGTH_SHORT).show();
        buildList();
    }

    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (Rotary.scroll(this, event, R.id.scroll, 100f)) return true;
        return super.onGenericMotionEvent(event);
    }
}
