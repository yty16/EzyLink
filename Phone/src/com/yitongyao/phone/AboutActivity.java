package com.yitongyao.phone;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.widget.TextView;

/** 关于页面。 */
public class AboutActivity extends Activity {

    /** 开源仓库地址。 */
    private static final String REPO_URL = "https://github.com/yty16/EzyLink";
    /** 开发者主页。 */
    private static final String DEV_URL = "https://github.com/yty16";
    /** 隐私政策地址。 */
    private static final String PRIVACY_URL = "https://github.com/yty16/EzyLink/blob/main/PRIVACY.md";
    /** 第三方组件声明。 */
    private static final String NOTICES_URL = "https://github.com/yty16/EzyLink/blob/main/THIRD_PARTY_NOTICES.md";

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
        bindLink(R.id.aboutThirdParty, NOTICES_URL);
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
}
