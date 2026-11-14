package com.yitongyao.watch;

import android.app.Activity;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.widget.ScrollView;

/** 表冠旋转滚动辅助。*/
public final class Rotary {

    private Rotary() {
    }

    /** 表冠输入转 ScrollView 滚动。*/
    public static boolean scroll(Activity activity, MotionEvent event, int scrollViewId,
                                 float pxPerNotch) {
        if (event.getAction() == MotionEvent.ACTION_SCROLL
                && event.isFromSource(InputDevice.SOURCE_ROTARY_ENCODER)) {
            float delta = event.getAxisValue(MotionEvent.AXIS_SCROLL);
            ScrollView sv = activity.findViewById(scrollViewId);
            if (sv != null) {
                sv.scrollBy(0, Math.round(-delta * pxPerNotch));
                return true;
            }
        }
        return false;
    }
}
