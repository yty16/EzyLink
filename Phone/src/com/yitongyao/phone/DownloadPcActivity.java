package com.yitongyao.phone;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Button;

/** 首次打开提醒下载电脑版。 */
public class DownloadPcActivity extends Activity {

    private SharedPreferences prefs;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_download_pc);
        prefs = getSharedPreferences("yity", MODE_PRIVATE);

        findViewById(R.id.dlPcGithub).setOnClickListener(new android.view.View.OnClickListener() {
            public void onClick(android.view.View v) {
                openLink(getString(R.string.dl_pc_github));
            }
        });
        findViewById(R.id.dlPcPan).setOnClickListener(new android.view.View.OnClickListener() {
            public void onClick(android.view.View v) {
                openLink(getString(R.string.dl_pc_pan));
            }
        });
        findViewById(R.id.dlPcDismiss).setOnClickListener(new android.view.View.OnClickListener() {
            public void onClick(android.view.View v) {
                dismissToMain();
            }
        });
    }

    private void openUrl(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (Throwable ignored) {
        }
    }

    private void openLink(String url) {
        prefs.edit().putBoolean("dl_pc_shown", true).apply();
        openUrl(url);
        finish();
    }

    private void dismissToMain() {
        prefs.edit().putBoolean("dl_pc_shown", true).apply();
        startActivity(new Intent(DownloadPcActivity.this, MainActivity.class));
        finish();
    }

    @Override
    public void onBackPressed() {
        dismissToMain();
    }
}
