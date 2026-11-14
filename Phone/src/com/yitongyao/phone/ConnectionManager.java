package com.yitongyao.phone;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.ArrayList;
import java.util.List;

/** 自动连接管理：WiFi / 蓝牙双通道，全 App 共用一条长连接 */
public final class ConnectionManager {

    public static final int STATE_OFF = 0;
    public static final int STATE_CONNECTING = 1;
    public static final int STATE_ON = 2;

    public interface Listener {
        void onState(int state, String detail);
    }

    public static class Result {
        public final boolean ok;
        public final String msg;

        public Result(boolean ok, String msg) {
            this.ok = ok;
            this.msg = msg;
        }
    }

    private static final int DISCOVERY_PORT = 7789;
    private static final String DISCOVERY_REQ = "YTY_DISCOVER";
    private static final int CONNECT_TIMEOUT = 2500;
    private static final long RETRY_INTERVAL = 3000;
    private static final long PING_INTERVAL = 15000;

    private final SharedPreferences prefs;
    private volatile Listener listener;
    private final Handler main = new Handler(Looper.getMainLooper());

    private volatile boolean running;
    private volatile Transport transport;
    private volatile BufferedWriter writer;
    private volatile BufferedReader reader;
    private volatile String currentTarget = "";
    private Thread worker;

    public ConnectionManager(Context ctx, Listener listener) {
        this.prefs = ctx.getSharedPreferences("yity", Context.MODE_PRIVATE);
        this.listener = listener;
    }

    /** 共用实例 */
    private static ConnectionManager sShared;

    public static ConnectionManager shared(Context ctx) {
        if (sShared == null) {
            synchronized (ConnectionManager.class) {
                if (sShared == null) {
                    sShared = new ConnectionManager(ctx.getApplicationContext(), null);
                }
            }
        }
        return sShared;
    }

    public void setListener(Listener l) {
        this.listener = l;
    }

    public boolean isConnected() {
        Transport t = transport;
        return t != null && t.isConnected();
    }

    public String getTarget() {
        return currentTarget;
    }

    public void start() {
        if (running) return;
        running = true;
        worker = new Thread(new Runnable() {
            public void run() {
                loop();
            }
        }, "yty-connection");
        worker.setDaemon(true);
        worker.start();
    }

    public void stop() {
        running = false;
        closeSocket();
        Thread t = worker;
        if (t != null) t.interrupt();
    }

    public void wakeUp() {
        Thread t = worker;
        if (t != null) t.interrupt();
    }

    public Result send(String cmd) {
        if (!isConnected()) {
            wakeUp();
            return new Result(false, "未连接电脑，正在自动重连…");
        }
        return sendInternal(cmd);
    }

    // ---------- 工作线程 ----------

    private void loop() {
        while (running) {
            if (!prefs.getBoolean("auto_connect", true)) {
                setState(STATE_OFF, "自动连接已关闭（可在设置中开启）");
                sleepQuietly(5000);
                continue;
            }

            if (isConnected()) {
                sleepQuietly(PING_INTERVAL);
                if (!isConnected()) continue;
                Result r = sendInternal("ping");
                if (!r.ok) {
                    closeSocket();
                    setState(STATE_CONNECTING, "连接已断开，正在重连…");
                }
                continue;
            }

            setState(STATE_CONNECTING, "正在连接电脑…");
            String mode = getMode();
            String btMac = prefs.getString("bt_mac", "");

            List<Transport> fast = new ArrayList<Transport>();
            if (!"bt".equals(mode)) {
                String saved = prefs.getString("ip", "");
                if (saved != null && !saved.trim().isEmpty()) {
                    fast.add(new TcpTransport(saved.trim(), getPort()));
                }
            }
            if (!"wifi".equals(mode)) addBluetooth(fast, btMac);

            boolean ok = false;
            for (Transport t : fast) {
                if (tryConnect(t)) { ok = true; break; }
            }

            if (!ok) {
                List<Transport> slow = new ArrayList<Transport>();
                if (!"bt".equals(mode)) {
                    for (String ip : discover()) {
                        if (ip != null && !ip.trim().isEmpty()) {
                            slow.add(new TcpTransport(ip.trim(), getPort()));
                        }
                    }
                }
                String btMac2 = prefs.getString("bt_mac", "");
                if (!"wifi".equals(mode) && !btMac2.equals(btMac)) addBluetooth(slow, btMac2);
                for (Transport t : slow) {
                    if (tryConnect(t)) { ok = true; break; }
                }
            }

            if (ok) {
                String token = prefs.getString("token", "");
                boolean needAuth = token != null && !token.trim().isEmpty();
                if (needAuth) {
                    Result ar = sendInternal("auth");
                    if (!ar.ok) {
                        closeSocket();
                        setState(STATE_CONNECTING, "配对码校验失败，请到设置中检查配对码");
                        sleepQuietly(RETRY_INTERVAL);
                        continue;
                    }
                }
                Transport t = transport;
                if (t instanceof TcpTransport) {
                    String ip = ((TcpTransport) t).getHost();
                    if (!ip.equals(prefs.getString("ip", ""))) {
                        prefs.edit().putString("ip", ip).apply();
                    }
                }
                setState(STATE_ON, "已连接 " + currentTarget);
            } else {
                setState(STATE_CONNECTING, "还没找到电脑，会自动重试。请确认：① 电脑端已启动服务；" +
                        "② WiFi 在同一网络 / 已连电脑热点，或已与电脑完成蓝牙配对；③ 防火墙已放行");
                sleepQuietly(RETRY_INTERVAL);
            }
        }
    }

    /** 蓝牙候选 */
    private void addBluetooth(List<Transport> out, String mac) {
        if (!BtTransport.isMac(mac)) return;
        Transport a = BtTransport.create(mac);
        Transport b = BtTransport.createInsecure(mac);
        if (a != null) out.add(a);
        if (b != null) out.add(b);
    }

    private String getMode() {
        String m = prefs.getString("conn_mode", "auto");
        if (m == null || m.isEmpty()) return "auto";
        return m;
    }

    /** UDP 广播发现；同时带回电脑端蓝牙 MAC */
    public List<String> discover() {
        List<String> out = new ArrayList<String>();
        DatagramSocket ds = null;
        try {
            ds = new DatagramSocket();
            ds.setBroadcast(true);
            ds.setSoTimeout(1200);
            byte[] buf = DISCOVERY_REQ.getBytes("UTF-8");
            ds.send(new DatagramPacket(buf, buf.length,
                    InetAddress.getByName("255.255.255.255"), DISCOVERY_PORT));

            long end = System.currentTimeMillis() + 1500;
            byte[] rb = new byte[2048];
            while (System.currentTimeMillis() < end) {
                try {
                    DatagramPacket p = new DatagramPacket(rb, rb.length);
                    ds.receive(p);
                    String s = new String(p.getData(), 0, p.getLength(), "UTF-8");
                    JSONObject o = new JSONObject(s);
                    if (!"yty_here".equals(o.optString("type"))) continue;

                    int port = o.optInt("port", getPort());
                    JSONArray ips = o.optJSONArray("ips");
                    if (ips != null) {
                        for (int i = 0; i < ips.length(); i++) {
                            String ip = ips.optString(i, "");
                            if (!ip.isEmpty()) out.add(ip);
                        }
                    }
                    if (port > 0 && port != getPort()) {
                        prefs.edit().putString("port", String.valueOf(port)).apply();
                    }
                    String bm = o.optString("bt_mac", "");
                    if (BtTransport.isMac(bm)) {
                        prefs.edit().putString("bt_mac", bm).apply();
                    }
                    String bu = o.optString("bt_uuid", "");
                    if (!bu.isEmpty()) prefs.edit().putString("bt_uuid", bu).apply();
                } catch (Exception ignored) {
                    break;
                }
            }
        } catch (Exception ignored) {
        } finally {
            if (ds != null) {
                try { ds.close(); } catch (Exception ignored) { }
            }
        }
        return out;
    }

    private boolean tryConnect(Transport t) {
        try {
            t.connect(CONNECT_TIMEOUT);
            transport = t;
            writer = new BufferedWriter(new OutputStreamWriter(t.getOutputStream(), "UTF-8"));
            reader = new BufferedReader(new InputStreamReader(t.getInputStream(), "UTF-8"));
            currentTarget = t.describe();
            return true;
        } catch (Exception e) {
            try { t.close(); } catch (Exception ignored) { }
            closeSocket();
            return false;
        }
    }

    private Result sendInternal(String cmd) {
        try {
            BufferedWriter w = writer;
            BufferedReader r = reader;
            if (!isConnected() || w == null || r == null) {
                return new Result(false, "未连接");
            }
            JSONObject req = new JSONObject();
            req.put("cmd", cmd);
            String token = prefs.getString("token", "");
            if (token != null && !token.isEmpty()) req.put("token", token);

            w.write(req.toString() + "\n");
            w.flush();
            String resp = r.readLine();
            if (resp == null) return new Result(false, "无响应");
            JSONObject o = new JSONObject(resp);
            String msg = o.optString("msg", "");
            if (msg.isEmpty()) msg = o.optString("err", "");
            return new Result(o.optBoolean("ok", false), msg);
        } catch (Exception e) {
            closeSocket();
            return new Result(false, e.getMessage());
        }
    }

    private int getPort() {
        try {
            String p = prefs.getString("port", "7788");
            int v = Integer.parseInt(p == null ? "7788" : p.trim());
            if (v > 0 && v < 65536) return v;
        } catch (Exception ignored) {
        }
        return 7788;
    }

    private void closeSocket() {
        try { if (writer != null) writer.close(); } catch (Exception ignored) { }
        try { if (reader != null) reader.close(); } catch (Exception ignored) { }
        try { if (transport != null) transport.close(); } catch (Exception ignored) { }
        writer = null;
        reader = null;
        transport = null;
    }

    private void sleepQuietly(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException ignored) {
        }
    }

    private void setState(final int state, final String detail) {
        if (listener == null) return;
        main.post(new Runnable() {
            public void run() {
                listener.onState(state, detail);
            }
        });
    }
}
