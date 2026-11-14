package com.yitongyao.phone;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;

/** 首次使用协议网关。 */
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

    /** 拦截返回键。 */
    @Override
    public void onBackPressed() {
        finishAffinity();
    }
}
