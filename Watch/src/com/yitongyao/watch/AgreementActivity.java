package com.yitongyao.watch;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.view.MotionEvent;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;

/**
 * 首次使用协议与免责声明网关（手表端）：必须勾选同意后方可进入主界面；
 * 选择「不同意并退出」则直接关闭应用。同意后写入 SharedPreferences。
 * 长文本支持表冠旋转滚动。
 */
public class AgreementActivity extends Activity {

    private SharedPreferences prefs;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_agreement);

        prefs = getSharedPreferences("yity", MODE_PRIVATE);

        final CheckBox check = findViewById(R.id.agreeCheck);
        final Button accept = findViewById(R.id.agreeAccept);
        final Button decline = findViewById(R.id.agreeDecline);

        check.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                accept.setEnabled(isChecked);
            }
        });

        accept.setOnClickListener(new android.view.View.OnClickListener() {
            @Override
            public void onClick(android.view.View v) {
                prefs.edit().putBoolean("eula_agreed", true).apply();
                startActivity(new Intent(AgreementActivity.this, MainActivity.class));
                finish();
            }
        });

        decline.setOnClickListener(new android.view.View.OnClickListener() {
            @Override
            public void onClick(android.view.View v) {
                finishAffinity();
            }
        });
    }

    /** 表冠旋转 → 协议文本纵向滚动。 */
    @Override
    public boolean onGenericMotionEvent(MotionEvent event) {
        if (Rotary.scroll(this, event, R.id.scroll, 100f)) return true;
        return super.onGenericMotionEvent(event);
    }

    /** 强制表态：返回键等同退出，不允许绕过。 */
    @Override
    public void onBackPressed() {
        finishAffinity();
    }
}
