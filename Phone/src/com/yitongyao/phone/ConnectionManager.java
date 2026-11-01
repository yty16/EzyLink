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
import java.net.InetSocketAddress;
import java.net.Socket;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/**
 * 自动连接管理器（纯 Android 框架层实现）。
 *
 * 目标：一次配置后，打开 App 即自动连上电脑，之后点按钮立刻生效，
 * 不需要每次手填 IP，也不需要互联网 —— 只要手机与电脑在同一网络
 * （同一个 WiFi，或手机连着电脑开的移动热点）。
 *
 * 工作方式：
 * 1. 优先用设置里已保存的 IP 直连；
 * 2. 连不上时向局域网广播 UDP 发现请求（端口 7789），电脑端会回复自己的地址；
 * 3. 仍连不上则按固定间隔自动重试，直到连上为止（App 在前台期间持续进行）；
 * 4. 连上后保持长连接：先完成配对码鉴权，之后每 15 秒 ping 保活，
 *    断开自动重连。长连接让点按钮即时生效，也避免每次都重新建连。
 */
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
    private static final int IO_TIMEOUT = 6000;
    private static final long RETRY_INTERVAL = 3000;
    private static final long PING_INTERVAL = 15000;

    private final SharedPreferences prefs;
    private volatile Listener listener;
    private final Handler main = new Handler(Looper.getMainLooper());

    private volatile boolean running;
    private volatile Socket socket;
    private volatile BufferedWriter writer;
    private volatile BufferedReader reader;
    private volatile String currentTarget = "";
    private Thread worker;

    public ConnectionManager(Context ctx, Listener listener) {
        this.prefs = ctx.getSharedPreferences("yity", Context.MODE_PRIVATE);
        this.listener = listener;
    }

    // 全 App 共用一条长连接：主页与沉浸页复用同一条，
    // 避免沉浸页另起连接还要重新 UDP 广播发现（约 1.5s）才能用，
    // 也就不会出现「刚进沉浸页点一下提示未连接电脑」
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

    /** 切换接收状态回调的页面（共用连接时各页面各自设置；离开页面传 null）。 */
    public void setListener(Listener l) {
        this.listener = l;
    }

    public boolean isConnected() {
        Socket s = socket;
        return s != null && s.isConnected() && !s.isClosed();
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

    /** 唤醒工作线程立即重试一次（用户点了按钮但还没连上时调用）。 */
    public void wakeUp() {
        Thread t = worker;
        if (t != null) t.interrupt();
    }

    /**
     * 发送一条指令。调用方应在后台线程执行。
     * 未连接时不静默失败：返回明确提示并触发立即重连。
     */
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
                // 保活：定期 ping，失败则断开进入重连
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
            boolean ok = false;
            // 先直连已保存的地址（约百毫秒）；失败再做 UDP 广播发现（约 1.5s）
            String saved = prefs.getString("ip", "");
            if (saved != null && !saved.trim().isEmpty()
                    && tryConnect(saved.trim(), getPort())) {
                ok = true;
            }
            if (!ok) {
                for (String ip : discover()) {
                    if (ip == null || ip.trim().isEmpty()) continue;
                    if (tryConnect(ip.trim(), getPort())) {
                        ok = true;
                        break;
                    }
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
                // 记住这次成功的地址，下次优先直连
                String host = currentTarget;
                int idx = host.lastIndexOf(':');
                if (idx > 0) {
                    String ip = host.substring(0, idx);
                    if (!ip.equals(prefs.getString("ip", ""))) {
                        prefs.edit().putString("ip", ip).apply();
                    }
                }
                setState(STATE_ON, "已连接 " + currentTarget);
            } else {
                setState(STATE_CONNECTING, "还没找到电脑，会自动重试。请确认：① 电脑端易通遥已启动服务；" +
                        "② 手机与电脑在同一 WiFi，或已连上电脑的移动热点；③ 电脑防火墙已放行");
                sleepQuietly(RETRY_INTERVAL);
            }
        }
    }

    /** UDP 广播发现：向 255.255.255.255:7789 发请求，收集电脑端回复的地址。 */
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
                    // 端口以电脑端实际监听的为准，自动纠正手填错误
                    if (port > 0 && port != getPort()) {
                        prefs.edit().putString("port", String.valueOf(port)).apply();
                    }
                } catch (Exception ignored) {
                    break;   // 超时或解析失败即结束收集
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

    private boolean tryConnect(String ip, int port) {
        try {
            Socket s = new Socket();
            s.connect(new InetSocketAddress(ip, port), CONNECT_TIMEOUT);
            s.setSoTimeout(IO_TIMEOUT);
            socket = s;
            writer = new BufferedWriter(new OutputStreamWriter(s.getOutputStream(), "UTF-8"));
            reader = new BufferedReader(new InputStreamReader(s.getInputStream(), "UTF-8"));
            currentTarget = ip + ":" + port;
            return true;
        } catch (Exception e) {
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
        try { if (socket != null) socket.close(); } catch (Exception ignored) { }
        writer = null;
        reader = null;
        socket = null;
    }

    private void sleepQuietly(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException ignored) {
            // wakeUp() 打断睡眠以立即重试
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
