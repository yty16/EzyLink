using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace YiTongYao.Core;

/// <summary>单实例互斥 + 进程间命令（SHOW / RESTART）。</summary>
internal static class SingleInstance
{
    private const string MutexName = "EzyLink_SingleInstance_yty16";
    private const string PipeName = "EzyLink_SingleInstance_Pipe_yty16";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _cts;

    /// <summary>尝试成为主实例；true = 本实例为主。</summary>
    public static bool TryBecomePrimary()
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // 互斥体存在但无所有权（极端权限情况），保守认为已有实例
            createdNew = false;
        }
        catch (Exception)
        {
            createdNew = false;
        }

        if (createdNew) return true;

        try { _mutex?.Close(); } catch { }
        _mutex = null;
        return false;
    }

    /// <summary>主实例启动 IPC 监听（回调在后台线程）。</summary>
    public static void StartServer(Action onShow, Action onRestart)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var cmd = (await reader.ReadLineAsync().ConfigureAwait(false))?.Trim();
                    if (cmd == "SHOW") onShow();
                    else if (cmd == "RESTART") onRestart();
                }
                catch (OperationCanceledException) { break; }
                catch { /* 连接/创建异常，下一轮重试 */ }
                finally { try { server?.Dispose(); } catch { } }

                try { await Task.Delay(120, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    /// <summary>向主实例发送命令，返回是否成功。</summary>
    public static bool Signal(string command, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var w = new StreamWriter(client);
            w.WriteLine(command);
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    /// <summary>主实例退出时释放互斥体并停止监听。</summary>
    public static void Release()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;

        try { _mutex?.Close(); } catch { }
        _mutex = null;
    }
}
