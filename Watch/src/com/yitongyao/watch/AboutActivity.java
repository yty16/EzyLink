package com.yitongyao.watch;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.view.MotionEvent;
import android.widget.TextView;

/** 关于页面。*/
public class AboutActivity extends Activity {

    /** 开源仓库。*/
    private static final String REPO_URL = "https://github.com/yty16/EzyLink";
    /** 开发者主页。 */
    private static final String DEV_URL = "https://github.com/yty16";
    /** 隐私政策。*/
    private static final String PRIVACY_URL = "https://github.com/yty16/EzyLink/blob/main/PRIVACY.md";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_about);

        TextView ver = findViewById(R.id.aboutVersion);
        try {
            String v = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
            if (v != null && !v.isEmpty()) ver.setText("版本 " + v);
        } catch (Exception ignored) {
            ver.setText("");
        }

        bindLink(R.id.aboutRepo, REPO_URL);
        bindLink(R.id.aboutDeveloper, DEV_URL);
        bindLink(R.id.aboutPrivacy, PRIVACY_URL);
    }

    private void bindLink(int id, final String url) {
        android.view.View v = findViewById(id);
        if (v == null) return;
        v.setOnClickListener(new android.view.View.OnClickListener() {
            @Override
            public void onClick(android.view.View view) {
                openUrl(url);
            }
        });
    }

    private void openUrl(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (Exception ignored) {
        }
    }

    /** 表冠旋转 → 关于内容纵向滚动。 */
    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (Rotary.scroll(this, event, R.id.scroll, 100f)) return true;
        return super.onGenericMotionEvent(event);
    }
}
