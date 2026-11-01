package com.yitongyao.phone;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.Socket;

/**
 * 易通遥 远端控制客户端（纯 Android 框架层实现，无第三方依赖）。
 * 与 Windows 端共用文本 JSON、按行分隔的指令协议（详见项目根 Protocol.md）。
 */
public final class RemoteClient {

    private RemoteClient() {
    }

    public static class Result {
        public final boolean ok;
        public final String msg;

        public Result(boolean ok, String msg) {
            this.ok = ok;
            this.msg = msg;
        }
    }

    /** 发送单条指令并读取一行 JSON 响应。 */
    public static Result send(String ip, int port, String token, String cmd) {
        try (Socket socket = new Socket(ip, port)) {
            socket.setSoTimeout(5000);
            OutputStream os = socket.getOutputStream();
            JSONObject req = new JSONObject();
            req.put("cmd", cmd);
            req.put("token", token == null ? "" : token);
            os.write((req.toString() + "\n").getBytes("UTF-8"));
            os.flush();

            BufferedReader reader = new BufferedReader(
                    new InputStreamReader(socket.getInputStream(), "UTF-8"));
            String resp = reader.readLine();
            if (resp == null) {
                return new Result(false, "无响应");
            }
            JSONObject obj = new JSONObject(resp);
            boolean ok = obj.optBoolean("ok", false);
            String message = obj.optString("msg", "");
            return new Result(ok, message);
        } catch (Exception e) {
            return new Result(false, e.getMessage());
        }
    }

    /** 发送 Wake-on-LAN 魔术包以唤醒关机状态的电脑。 */
    public static void wakeOnLan(String mac) throws Exception {
        String clean = mac.replace(":", "").replace("-", "").replace(" ", "").trim();
        if (clean.length() != 12) {
            throw new IllegalArgumentException("MAC 格式错误");
        }
        byte[] macBytes = new byte[6];
        for (int i = 0; i < 6; i++) {
            macBytes[i] = (byte) Integer.parseInt(clean.substring(i * 2, i * 2 + 2), 16);
        }
        byte[] packet = new byte[102];
        for (int i = 0; i < 6; i++) {
            packet[i] = (byte) 0xFF;
        }
        for (int i = 6; i < packet.length; i++) {
            packet[i] = macBytes[(i - 6) % 6];
        }
        try (DatagramSocket ds = new DatagramSocket()) {
            ds.setBroadcast(true);
            DatagramPacket p = new DatagramPacket(packet, packet.length,
                    InetAddress.getByName("255.255.255.255"), 9);
            ds.send(p);
        }
    }
}
