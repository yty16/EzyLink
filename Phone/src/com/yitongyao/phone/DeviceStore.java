package com.yitongyao.phone;

import android.content.SharedPreferences;
import org.json.JSONArray;
import org.json.JSONException;
import java.util.ArrayList;
import java.util.List;

/** 多设备仓库。 */
public final class DeviceStore {

    private static final String KEY_DEVICES = "yty_devices";
    private static final String KEY_CURRENT = "yty_current_device";

    private final SharedPreferences prefs;

    public DeviceStore(SharedPreferences prefs) {
        this.prefs = prefs;
    }

    public List<Device> list() {
        List<Device> out = new ArrayList<>();
        try {
            JSONArray arr = new JSONArray(prefs.getString(KEY_DEVICES, "[]"));
            for (int i = 0; i < arr.length(); i++) {
                Device d = Device.fromJson(arr.optJSONObject(i));
                if (d != null) out.add(d);
            }
        } catch (JSONException ignored) {
        }
        return out;
    }

    /** 取当前设备。 */
    public Device current() {
        List<Device> all = list();
        if (all.isEmpty()) {
            Device legacy = Device.fromLegacy(prefs);
            if (legacy != null) {
                all.add(legacy);
                save(all);
                prefs.edit().putString(KEY_CURRENT, legacy.id).apply();
            }
        }
        String cur = prefs.getString(KEY_CURRENT, "");
        for (Device d : all) {
            if (d.id.equals(cur)) return d;
        }
        if (!all.isEmpty()) {
            prefs.edit().putString(KEY_CURRENT, all.get(0).id).apply();
            return all.get(0);
        }
        return null;
    }

    public Device find(String id) {
        for (Device d : list()) {
            if (d.id.equals(id)) return d;
        }
        return null;
    }

    /** 切换当前设备。 */
    public void activate(String id) {
        Device d = find(id);
        if (d == null) return;
        d.lastConnected = System.currentTimeMillis();
        SharedPreferences.Editor ed = prefs.edit();
        ed.putString("ip", d.ip == null ? "" : d.ip);
        ed.putString("port", d.port == null ? "7788" : d.port);
        ed.putString("token", d.token == null ? "" : d.token);
        ed.putString("bt_mac", d.btMac == null ? "" : d.btMac);
        ed.putString("bt_uuid", d.btUuid == null ? "" : d.btUuid);
        ed.putString("mac", d.mac == null ? "" : d.mac);
        ed.putString("conn_mode", d.connMode == null ? "auto" : d.connMode);
        ed.putBoolean("auto_connect", d.autoConnect);
        ed.putString(KEY_CURRENT, d.id);
        ed.apply();
        List<Device> all = list();
        boolean changed = false;
        for (int i = 0; i < all.size(); i++) {
            if (all.get(i).id.equals(d.id)) {
                all.set(i, d);
                changed = true;
                break;
            }
        }
        if (changed) save(all);
    }

    /** 新增或更新设备。 */
    public Device upsert(Device d) {
        List<Device> all = list();
        boolean found = false;
        for (int i = 0; i < all.size(); i++) {
            if (all.get(i).id.equals(d.id)) {
                all.set(i, d);
                found = true;
                break;
            }
        }
        if (!found) all.add(d);
        save(all);
        return d;
    }

    /** 写入扫码得到的设备。 */
    public Device upsertFromQr(Device scanned) {
        List<Device> all = list();
        Device match = null;
        for (Device d : all) {
            if (sameHost(d, scanned)) {
                match = d;
                break;
            }
        }
        if (match != null) {
            match.ip = scanned.ip;
            match.port = scanned.port;
            if (scanned.token != null && !scanned.token.isEmpty()) match.token = scanned.token;
            if (scanned.btMac != null && !scanned.btMac.isEmpty()) match.btMac = scanned.btMac;
            match.autoConnect = true;
            upsert(match);
            return match;
        }
        upsert(scanned);
        return scanned;
    }

    /** 删除设备。 */
    public void remove(String id) {
        List<Device> all = list();
        List<Device> out = new ArrayList<>();
        for (Device d : all) {
            if (!d.id.equals(id)) out.add(d);
        }
        save(out);
        String cur = prefs.getString(KEY_CURRENT, "");
        if (cur.equals(id)) {
            if (!out.isEmpty()) activate(out.get(0).id);
            else prefs.edit().remove(KEY_CURRENT).apply();
        }
    }

    /** 写回当前设备对象。 */
    public void syncCurrentFromPrefs() {
        Device cur = current();
        if (cur == null) {
            cur = new Device();
            cur.name = "我的电脑";
        }
        cur.ip = prefs.getString("ip", cur.ip);
        cur.port = prefs.getString("port", cur.port);
        cur.token = prefs.getString("token", cur.token);
        cur.btMac = prefs.getString("bt_mac", cur.btMac);
        cur.btUuid = prefs.getString("bt_uuid", cur.btUuid);
        cur.mac = prefs.getString("mac", cur.mac);
        cur.connMode = prefs.getString("conn_mode", cur.connMode);
        cur.autoConnect = prefs.getBoolean("auto_connect", cur.autoConnect);
        upsert(cur);
    }

    private static boolean sameHost(Device a, Device b) {
        if (a.ip == null || b.ip == null) return false;
        if (!a.ip.equals(b.ip)) return false;
        String pa = a.port == null ? "7788" : a.port;
        String pb = b.port == null ? "7788" : b.port;
        return pa.equals(pb);
    }

    private void save(List<Device> all) {
        try {
            JSONArray arr = new JSONArray();
            for (Device d : all) arr.put(d.toJson());
            prefs.edit().putString(KEY_DEVICES, arr.toString()).apply();
        } catch (Exception ignored) {
        }
    }
}
