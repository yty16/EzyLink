package com.yitongyao.phone;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;

/** WiFi 通道 */
public final class TcpTransport implements Transport {

    private final String host;
    private final int port;
    private Socket socket;

    public TcpTransport(String host, int port) {
        this.host = host;
        this.port = port;
    }

    public String getHost() {
        return host;
    }

    @Override
    public void connect(int timeoutMs) throws Exception {
        Socket s = new Socket();
        s.connect(new InetSocketAddress(host, port), timeoutMs);
        s.setSoTimeout(6000);
        socket = s;
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
        Socket s = socket;
        return s != null && s.isConnected() && !s.isClosed();
    }

    @Override
    public String describe() {
        return host + ":" + port;
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
