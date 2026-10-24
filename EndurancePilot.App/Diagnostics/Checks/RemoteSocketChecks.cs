using System.Net.WebSockets;
using System.Text;

namespace EndurancePilot.App;

internal static class RemoteSocketChecks
{
    internal static IEnumerable<StartupCheck> Run()
    {
        bool Read(byte[] bytes, WebSocketMessageType type, bool expected)
        {
            using var socket = new Frames(bytes, type);
            try
            {
                var result = DiscordRemote.ReceiveCommandAsync(socket, default).GetAwaiter().GetResult();
                return expected && result.SequenceEqual(bytes);
            }
            catch (InvalidDataException) { return !expected; }
        }
        foreach (var item in new[] {
            ("WebSocket reassembles fragmented command frames", Read(Encoding.UTF8.GetBytes(new string('x', 20000)), WebSocketMessageType.Text, true)),
            ("WebSocket rejects oversized commands", Read(new byte[200001], WebSocketMessageType.Text, false)),
            ("WebSocket rejects binary commands", Read(new byte[8], WebSocketMessageType.Binary, false)),
            ("WebSocket close ends command reception", Read([], WebSocketMessageType.Close, false)) })
            yield return new(item.Item1, item.Item2, item.Item2 ? "Passed" : "Failed");
    }

    private sealed class Frames(byte[] bytes, WebSocketMessageType type) : WebSocket
    {
        private int offset;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken token) => throw new NotSupportedException();
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            int count = Math.Min(3000, Math.Min(buffer.Count, bytes.Length - offset));
            bytes.AsSpan(offset, count).CopyTo(buffer.AsSpan());
            offset += count;
            return Task.FromResult(new WebSocketReceiveResult(count, type, offset == bytes.Length));
        }
    }
}
