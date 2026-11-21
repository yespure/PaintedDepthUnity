using System;
using System.Data.SqlTypes;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.AppUI.Core;

public sealed class ComfyPreviewSession
{
    private readonly ClientWebSocket socket = new ClientWebSocket();

    private CancellationTokenSource receiveCancellation;
    private Task receiveTask;

    private byte[] latestImage;
    private string latestWarning;

    private bool disposed;

    public async Task ConnectAsync(
        Uri serverUri,
        string clientId,
        CancellationToken token)
    {
        receiveCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        var address = new UriBuilder(new Uri(serverUri, "ws"))
        {
            Scheme = serverUri.Scheme == "https" ? "wss" : "ws",
            Query = "clientId=" + Uri.EscapeDataString(clientId)
        };

        // 为建立连接单独设置超时。
        using var connectionTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        connectionTimeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await socket.ConnectAsync(
                address.Uri,
                connectionTimeout.Token);
        }
        catch (OperationCanceledException)
            when (!token.IsCancellationRequested)
        {
            SetWarning("预览连接超时，生成任务将继续提交。");
            return;
        }
        catch (WebSocketException exception)
        {
            SetWarning(
                "预览连接失败，生成任务将继续提交：" +
                exception.Message);
            return;
        }

        // 保存接收任务，结束时可以等待它退出。
        receiveTask = ReceiveAsync(receiveCancellation.Token);
    }

    private async Task ReceiveAsync(CancellationToken token)
    {
        byte[] buffer = new byte[8192];

        try
        {
            while (!token.IsCancellationRequested)
            {
                using var message = new MemoryStream();

                WebSocketReceiveResult result;

                // 一条 WebSocket 消息可能分成多个片段。
                do
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (!token.IsCancellationRequested)
                            SetWarning("ComfyUI 关闭了预览连接。");

                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Binary)
                    continue;

                byte[] image = ExtractImage(message.ToArray());

                if (image != null && !token.IsCancellationRequested)
                {
                    // 新图片替换上一张尚未显示的图片。
                    Interlocked.Exchange(ref latestImage, image);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消接收。
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                SetWarning(
                    "预览接收中断：" + exception.Message);
            }
        }
    }

    private static byte[] ExtractImage(byte[] packet)
    {
        if (packet.Length < 8)
            return null;

        uint eventType = ReadUInt32(packet, 0);

        int imageOffset;

        if (eventType == 1)
        {
            // 普通预览：
            // 4 字节事件类型 + 4 字节图片格式 + 图片内容。
            uint imageType = ReadUInt32(packet, 4);

            if (imageType != 1 && imageType != 2)
                return null;

            imageOffset = 8;
        }
        else if (eventType == 4)
        {
            // 带 metadata 的预览：
            // 4 字节事件类型 + 4 字节 metadata 长度
            // + metadata 内容 + 图片内容。
            uint metadataLength = ReadUInt32(packet, 4);

            if (metadataLength > (uint)(packet.Length - 8))
                return null;

            imageOffset = 8 + (int)metadataLength;
        }
        else
        {
            return null;
        }

        int imageLength = packet.Length - imageOffset;

        if (imageLength <= 0)
            return null;

        byte[] image = new byte[imageLength];

        Buffer.BlockCopy(
            packet,
            imageOffset,
            image,
            0,
            imageLength
        );

        return image;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return
        ((uint)bytes[offset]<<24)|
        ((uint)bytes[offset + 1]<<16)|
        ((uint)bytes[offset + 2]<<8)|
        bytes[offset + 3];
    }

    public byte[] TakeLatestImage()
    {
        return Interlocked.Exchange(ref latestImage, null);
    }

    public string TakeWarning()
    {
        return Interlocked.Exchange(ref latestWarning, null);
    }

    private void SetWarning(string message)
    {
        Interlocked.Exchange(ref latestWarning, message);
    }

    public void Abort()
    {
        if(disposed)
        return;

        receiveCancellation?.Cancel();
        socket.Abort();
    }

    public async Task StopAsync()
    {
        if(disposed)
        return;

        Abort();

        if(receiveTask != null)
        await receiveTask;

        socket.Dispose();
        receiveCancellation?.Dispose();

        disposed = true;

        Interlocked.Exchange(ref latestImage, null);
    }
}