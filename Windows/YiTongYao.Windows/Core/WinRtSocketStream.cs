using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace YiTongYao.Core;

/// <summary>WinRT 蓝牙流适配：把 StreamSocket 包装成同步 Stream，供会话层复用。</summary>
internal sealed class WinRtSocketStream : Stream
{
    private readonly StreamSocket _socket;
    private readonly DataReader _reader;
    private readonly DataWriter _writer;
    private readonly BlockingCollection<byte[]> _chunks = new(new ConcurrentQueue<byte[]>());
    private readonly CancellationTokenSource _cts = new();
    private byte[]? _current;
    private int _pos;
    private int _timeout = SessionRunner.ReadTimeoutMs;
    private volatile bool _closed;

    public WinRtSocketStream(StreamSocket socket)
    {
        _socket = socket;
        _reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        _writer = new DataWriter(socket.OutputStream);
        _ = Task.Run(PumpAsync);
    }

    /// <summary>接收泵</summary>
    private async Task PumpAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                uint n = await _reader.LoadAsync(1024);
                if (n == 0) break;
                var buf = new byte[n];
                _reader.ReadBytes(buf);
                _chunks.Add(buf, _cts.Token);
            }
        }
        catch { }
        finally { try { _chunks.CompleteAdding(); } catch { } }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_current == null || _pos >= _current.Length)
        {
            if (_chunks.IsCompleted && _chunks.Count == 0) return 0;
            try
            {
                if (!_chunks.TryTake(out _current, _timeout, _cts.Token))
                {
                    if (_closed) return 0;
                    throw new IOException("bluetooth read timeout",
                        new SocketException((int)System.Net.Sockets.SocketError.TimedOut));
                }
            }
            catch (OperationCanceledException) { return 0; }
            catch (ArgumentException) { return 0; }
            if (_current == null) return 0;
            _pos = 0;
        }
        int n = Math.Min(count, _current.Length - _pos);
        Array.Copy(_current, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var data = new byte[count];
        Array.Copy(buffer, offset, data, 0, count);
        _writer.WriteBytes(data);
        try { _writer.StoreAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception ex) { throw new IOException("bluetooth write failed", ex); }
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override bool CanTimeout => true;
    public override int ReadTimeout { get => _timeout; set => _timeout = value; }
    public override int WriteTimeout { get => _timeout; set => _timeout = value; }
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_closed) { base.Dispose(disposing); return; }
        _closed = true;
        _cts.Cancel();
        try { _chunks.CompleteAdding(); } catch { }
        try { _writer.DetachStream(); } catch { }
        try { _writer.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _socket.Dispose(); } catch { }
        try { _chunks.Dispose(); } catch { }
        _cts.Dispose();
        base.Dispose(disposing);
    }
}
