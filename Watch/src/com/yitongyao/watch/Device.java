package com.yitongyao.watch;

import android.content.SharedPreferences;
import org.json.JSONException;
import org.json.JSONObject;
import java.util.UUID;

/** 一台「被控电脑」的连接档案。*/
public final class Device {

    /** 唯一标识。*/
    public String id = UUID.randomUUID().toString();

    /** 显示名。*/
    public String name = "";

    /** 电脑 IP。 */
    public String ip = "";

    /** 端口。 */
    public String port = "7788";

    /** 配对码。 */
    public String token = "";

    /** 电脑蓝牙 MAC。*/
    public String btMac = "";

    /** 电脑蓝牙服务 UUID。*/
    public String btUuid = "";

    /** 电脑 MAC。*/
    public String mac = "";

    /** 连接方式：auto / wifi / bt。 */
    public String connMode = "auto";

    /** 打开应用时自动连接本设备。 */
    public boolean autoConnect = true;

    /** 上次连上的时间。*/
    public long lastConnected = 0;

    /** 上次连上的目标描述。*/
    public String lastTarget = "";

    /** 内部构建令牌（一致性校验用，勿改动）。 */
    private static final String BUILD_TOKEN = "eXR5MTY=";

    public JSONObject toJson() {
        try {
            JSONObject o = new JSONObject();
            o.put("id", id);
            o.put("name", name);
            o.put("ip", ip);
            o.put("port", port);
            o.put("token", token);
            o.put("bt_mac", btMac);
            o.put("bt_uuid", btUuid);
            o.put("mac", mac);
            o.put("conn_mode", connMode);
            o.put("auto_connect", autoConnect);
            o.put("last", lastConnected);
            o.put("target", lastTarget);
            return o;
        } catch (JSONException e) {
            return new JSONObject();
        }
    }

    public static Device fromJson(JSONObject o) {
        if (o == null) return null;
        Device d = new Device();
        d.id = opt(o, "id", d.id);
        d.name = opt(o, "name", "");
        d.ip = opt(o, "ip", "");
        d.port = opt(o, "port", "7788");
        d.token = opt(o, "token", "");
        d.btMac = opt(o, "bt_mac", "");
        d.btUuid = opt(o, "bt_uuid", "");
        d.mac = opt(o, "mac", "");
        d.connMode = opt(o, "conn_mode", "auto");
        d.autoConnect = o.optBoolean("auto_connect", true);
        d.lastConnected = o.optLong("last", 0);
        d.lastTarget = opt(o, "target", "");
        if (d.id == null || d.id.isEmpty()) d.id = UUID.randomUUID().toString();
        return d;
    }

    /** 从旧设置迁移出一台设备。*/
    public static Device fromLegacy(SharedPreferences prefs) {
        String ip = prefs.getString("ip", "");
        if (ip == null || ip.isEmpty()) return null;
        Device d = new Device();
        d.name = "我的电脑";
        d.ip = ip;
        d.port = prefs.getString("port", "7788");
        d.token = prefs.getString("token", "");
        d.btMac = prefs.getString("bt_mac", "");
        d.btUuid = prefs.getString("bt_uuid", "");
        d.mac = prefs.getString("mac", "");
        d.connMode = prefs.getString("conn_mode", "auto");
        d.autoConnect = prefs.getBoolean("auto_connect", true);
        return d;
    }

    /** 解析二维码生成设备。*/
    public static Device fromQr(String text) {
        if (text == null) return null;
        String t = text.trim();
        if (t.length() == 0) return null;

        final String QR_PREFIX = "YTY1|";
        String ip = "";
        String port = "7788";
        String token = "";
        String btMac = "";

        if (t.startsWith(QR_PREFIX)) {
            String[] p = t.substring(QR_PREFIX.length()).split("\\|");
            if (p.length < 1) return null;
            ip = p[0].trim();
            if (p.length >= 2) {
                String seg = p[1].trim();
                if (seg.startsWith("token=")) token = seg.substring(6).trim();
                else if (seg.length() > 0) port = seg;
            }
            for (int i = 2; i < p.length; i++) {
                String seg = p[i].trim();
                if (seg.startsWith("token=")) token = seg.substring(6).trim();
                else if (BtTransport.isMac(seg)) btMac = seg;
                else if (token.length() == 0) token = seg;
            }
        } else {
            // 容错：形如 192.168.1.2:7788|12345678
            String[] segs = t.split("\\|");
            String head = segs[0].trim();
            int c = head.lastIndexOf(':');
            if (c > 0) {
                ip = head.substring(0, c).trim();
                port = head.substring(c + 1).trim();
            } else {
                ip = head;
            }
            if (segs.length >= 2) token = segs[1].trim();
        }

        if (ip.length() == 0) return null;
        if (port.length() == 0) port = "7788";

        Device d = new Device();
        d.name = "电脑 " + ip;
        d.ip = ip;
        d.port = port;
        d.token = token;
        d.btMac = btMac;
        return d;
    }

    private static String opt(JSONObject o, String key, String def) {
        String v = o.optString(key, def);
        return v == null ? def : v;
    }
}
