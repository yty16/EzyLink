package com.yitongyao.watch;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.view.MotionEvent;
import android.widget.TextView;

/**
 * 关于本应用（手表端）：展示应用名称、版本、简介，以及开源仓库、开发者、隐私政策与许可证。
 * 仓库 / 开发者 / 隐私政策均可点击跳转；长内容支持表冠旋转滚动。
 */
public class AboutActivity extends Activity {

    /** 开源仓库（含完整协议与隐私政策）。 */
    private static final String REPO_URL = "https://github.com/yty16/EzyLink";
    /** 开发者主页。 */
    private static final String DEV_URL = "https://github.com/yty16";
    /** 隐私政策（随仓库发布的 PRIVACY.md）。 */
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
            // 无可用浏览器时静默忽略
        }
    }

    /** 表冠旋转 → 关于内容纵向滚动。 */
    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (Rotary.scroll(this, event, R.id.scroll, 100f)) return true;
        return super.onGenericMotionEvent(event);
    }
}
