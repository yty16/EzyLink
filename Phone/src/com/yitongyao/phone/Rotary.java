package com.yitongyao.phone;

import android.app.Activity;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.widget.ScrollView;

/**
 * 表冠（旋转侧键 / 旋转表圈）滚动辅助：把 WearOS 的 rotary 输入转成 ScrollView 的垂直滚动。
 * 与 {@link ImmersionActivity} 中翻页所用的表冠事件同属 {@link MotionEvent#ACTION_SCROLL} 一族。
 */
public final class Rotary {

    private Rotary() {
    }

    /**
     * 在 Activity 的 {@link Activity#onGenericMotionEvent(MotionEvent)} 中调用：
     * 若事件来自旋转编码器，则滚动指定 ScrollView 并返回 true。
     *
     * @param pxPerNotch 每「格」旋转滚动的像素，圆屏手表取 ~100 手感较顺。
     */
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
