package com.yitongyao.watch;

import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothSocket;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.UUID;

/** 蓝牙 RFCOMM 通道 */
public final class BtTransport implements Transport {

    public static final UUID YTY_UUID = UUID.fromString("6E8F3A21-5C7B-4D9E-A1F2-0B3C4D5E6F70");
    public static final UUID SPP_UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB");

    private final BluetoothDevice device;
    private final UUID uuid;
    private final boolean insecure;
    private BluetoothSocket socket;

    private BtTransport(BluetoothDevice device, UUID uuid, boolean insecure) {
        this.device = device;
        this.uuid = uuid;
        this.insecure = insecure;
    }

    public static Transport create(String mac) {
        return build(mac, YTY_UUID, false);
    }

    public static Transport create(String mac, boolean useSpp) {
        return build(mac, useSpp ? SPP_UUID : YTY_UUID, false);
    }

    public static Transport createInsecure(String mac) {
        return build(mac, YTY_UUID, true);
    }

    public static Transport createSppInsecure(String mac) {
        return build(mac, SPP_UUID, true);
    }

    private static Transport build(String mac, UUID uuid, boolean insecure) {
        if (!isMac(mac)) return null;
        try {
            BluetoothAdapter adapter = BluetoothAdapter.getDefaultAdapter();
            if (adapter == null || !adapter.isEnabled()) return null;
            BluetoothDevice dev = adapter.getRemoteDevice(mac.trim().toUpperCase());
            return dev == null ? null : new BtTransport(dev, uuid, insecure);
        } catch (Throwable ignored) {
            return null;
        }
    }

    /** 地址校验 */
    public static boolean isMac(String s) {
        if (s == null) return false;
        String t = s.trim();
        if (t.length() != 17) return false;
        for (int i = 0; i < t.length(); i++) {
            char c = t.charAt(i);
            if (i % 3 == 2) {
                if (c != ':' && c != '-') return false;
            } else if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) {
                return false;
            }
        }
        return true;
    }

    @Override
    public void connect(int timeoutMs) throws Exception {
        final BluetoothSocket s = insecure
                ? device.createInsecureRfcommSocketToServiceRecord(uuid)
                : device.createRfcommSocketToServiceRecord(uuid);
        socket = s;
        final Exception[] failure = new Exception[1];
        Thread t = new Thread(new Runnable() {
            public void run() {
                try { s.connect(); } catch (Exception e) { failure[0] = e; }
            }
        }, "yty-bt-connect");
        t.setDaemon(true);
        t.start();
        t.join(Math.max(timeoutMs, 8000));
        if (t.isAlive()) {
            try { s.close(); } catch (IOException ignored) { }
            socket = null;
            throw new IOException("蓝牙连接超时");
        }
        if (failure[0] != null || !s.isConnected()) {
            try { s.close(); } catch (IOException ignored) { }
            socket = null;
            if (failure[0] != null) throw failure[0];
            throw new IOException("蓝牙连接失败");
        }
    }

    @Override
    public InputStream getInputStream() throws IOException {
        return socket.getInputStream();
    }

    @Override
    public OutputStream getOutputStream() throws IOException {
        return socket.getOutputStream();
    }

    @Override
    public boolean isConnected() {
        BluetoothSocket s = socket;
        return s != null && s.isConnected();
    }

    @Override
    public String describe() {
        return "蓝牙 " + (device != null ? device.getAddress() : "?");
    }

    @Override
    public void close() throws IOException {
        try {
            if (socket != null) socket.close();
        } finally {
            socket = null;
        }
    }
}
