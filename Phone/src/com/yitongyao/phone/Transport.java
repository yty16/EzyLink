package com.yitongyao.phone;

import java.io.Closeable;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/** 传输通道抽象 */
public interface Transport extends Closeable {
    void connect(int timeoutMs) throws Exception;
    InputStream getInputStream() throws IOException;
    OutputStream getOutputStream() throws IOException;
    boolean isConnected();
    String describe();
}
