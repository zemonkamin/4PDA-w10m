using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Buffer = System.Buffer;

namespace _4PDA.Services
{
    internal sealed class ApiArray
    {
        internal readonly JsonArray Values;
        public ApiArray(JsonArray values) { Values = values ?? new JsonArray(); }
        public int Count { get { return Values.Count; } }
        public int Int(int index)
        {
            return index >= 0 && index < Count && Values[index].ValueType == JsonValueType.Number
                ? (int)Values[index].GetNumber() : 0;
        }
        public string Text(int index)
        {
            return index >= 0 && index < Count && Values[index].ValueType == JsonValueType.String
                ? Values[index].GetString() : string.Empty;
        }
        public ApiArray Array(int index)
        {
            return new ApiArray(index >= 0 && index < Count && Values[index].ValueType == JsonValueType.Array
                ? Values[index].GetArray() : null);
        }
    }

    internal sealed class OfficialApiException : InvalidOperationException
    {
        public int Status { get; private set; }
        public string Command { get; private set; }
        public ApiArray Payload { get; private set; }
        public OfficialApiException(string command, int status, ApiArray data)
            : base("4PDA API (" + command + "): " + (string.IsNullOrWhiteSpace(data.Text(0))
                ? "ошибка " + status : data.Text(0)))
        { Command = command; Status = status; Payload = data; }
    }

    // Protocol recovered from Network.java, Document.java and DocumentManager.java
    // in the user-supplied official Android client 1.9.43. Use its TLS endpoint.
    internal sealed class OfficialApiClient
    {
        private static readonly int MaximumFrameBytes = string.Equals(Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily,
            "Windows.Mobile", StringComparison.OrdinalIgnoreCase) ? 8 * 1024 * 1024 : 32 * 1024 * 1024;
        private static string _endpointHost = "appbk.4pda.to";
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private StreamSocket _socket;
        private DataReader _reader;
        private DataWriter _writer;
        private AppendableStream _compressedInput;
        private DeflateStream _inflater;
        // Android starts ordinary request IDs above the reserved notification IDs.
        private int _nextId = new Random().Next(65536, 73728);
        private int _connectedUserId;
        private string _connectedLoginKey;
        private bool _connectedHidden;
        public int UserId { get; set; }
        public string LoginKey { get; set; }
        public bool Hidden { get; set; }

        public async Task<ApiArray> CallAsync(string command, params object[] arguments)
        {
            return await CallCoreAsync(command, arguments, null);
        }

        public async Task<ApiArray> UploadAsync(string command, object[] arguments, Stream content)
        {
            return await CallCoreAsync(command, arguments, content);
        }

        private async Task<ApiArray> CallCoreAsync(string command, object[] arguments, Stream upload)
        {
            await _gate.WaitAsync();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(upload == null ? 30 : 180)))
            {
                try
                {
                    return await OfficialApiRequestRunner.RunAsync(command, upload != null,
                        () => PrepareConnectionAsync(command, timeout.Token),
                        () => ExchangeAsync(command, arguments, timeout.Token, upload), Disconnect,
                        () => RefreshEndpointAsync(timeout.Token), timeout.Token);
                }
                finally { _gate.Release(); }
            }
        }

        private async Task PrepareConnectionAsync(string command, CancellationToken cancellation)
        {
            if (_socket != null && (_connectedUserId != UserId
                || _connectedLoginKey != LoginKey || _connectedHidden != Hidden)) Disconnect();
            if (_socket == null) await ConnectAsync(cancellation);
            else if (!OfficialApiRequestRunner.IsRead(command))
                await ProbeConnectionAsync(cancellation);
        }

        internal async Task ProbeConnectionAsync(CancellationToken cancellation)
        {
            using (var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                // Android DocumentManager's `le` heartbeat. The reply proves
                // this socket is alive before a non-repeatable request is sent.
                // Leave time in the request deadline to reconnect if a
                // half-open socket never replies to the probe.
                probe.CancelAfter(TimeSpan.FromSeconds(5));
                try { await ExchangeAsync("le", new object[] { UserId }, probe.Token); }
                catch (OfficialApiException)
                {
                    // Android's heartbeat ignores a nonzero application status.
                    // Anonymous le [0] returns 2, but still confirms transport.
                    // Transport exceptions must escape and trigger recovery.
                }
            }
        }

        private async Task ConnectAsync(CancellationToken cancellation)
        {
            string host = _endpointHost;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Trace("TLS connect");
            _socket = await ConnectSocketAsync(host, cancellation).ConfigureAwait(false);
            Trace("TLS ready in " + clock.ElapsedMilliseconds + " ms");
            _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            _writer = new DataWriter(_socket.OutputStream);
            string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string upgrade = "GET /ws/ HTTP/1.1\r\nHost: " + host
                + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key
                + "\r\nSec-WebSocket-Protocol: app\r\nSec-WebSocket-Version: 13\r\n\r\n";
            _writer.WriteBytes(Encoding.UTF8.GetBytes(upgrade));
            await _writer.StoreAsync().AsTask(cancellation);
            var header = new StringBuilder();
            while (header.Length < 16384 && !header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                header.Append((char)(await ReadExactlyAsync(1, cancellation))[0]);
            if (!header.ToString().StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal))
                throw new IOException("Сервер 4PDA не открыл соединение API.");
            Trace("WebSocket ready");

            _compressedInput = new AppendableStream();
            _inflater = new DeflateStream(_compressedInput, CompressionMode.Decompress, true);
            await ExchangeAsync("ah", new object[] { "1.9.43", "", "", 0, UserId }, cancellation);
            if (UserId > 0 && !string.IsNullOrWhiteSpace(LoginKey))
                await ExchangeAsync("ma", new object[] { UserId, LoginKey, Hidden ? 1 : 0 }, cancellation);
            _connectedUserId = UserId;
            _connectedLoginKey = LoginKey;
            _connectedHidden = Hidden;
        }

        private static async Task<StreamSocket> ConnectSocketAsync(string host, CancellationToken cancellation)
        {
            // WinRT can wait 21 seconds on a dead virtual-adapter route before
            // trying the usable one. Race explicit address pairs instead. Keep
            // the normal hostname path for networks which require a proxy.
            using (var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var attempts = new List<Task<StreamSocket>>();
                attempts.Add(ConnectCandidateAsync(host, null, attemptsCancellation.Token));
                try
                {
                    using (var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        dnsTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                        try
                        {
                            var pairs = await DatagramSocket.GetEndpointPairsAsync(new HostName(host), "443")
                                .AsTask(dnsTimeout.Token).ConfigureAwait(false);
                            var unique = new HashSet<string>(StringComparer.Ordinal);
                            foreach (EndpointPair pair in pairs)
                            {
                                if (pair.LocalHostName == null || pair.RemoteHostName == null) continue;
                                string key = pair.LocalHostName.RawName + "/" + pair.RemoteHostName.RawName;
                                if (!unique.Add(key)) continue;
                                attempts.Add(ConnectCandidateAsync(host, pair, attemptsCancellation.Token));
                                if (unique.Count >= 16) break;
                            }
                        }
                        catch { cancellation.ThrowIfCancellationRequested(); }
                    }
                    while (attempts.Count > 0)
                    {
                        Task<StreamSocket> completed = await Task.WhenAny(attempts).ConfigureAwait(false);
                        attempts.Remove(completed);
                        StreamSocket connected = await completed.ConfigureAwait(false);
                        if (connected != null) return connected;
                    }
                    cancellation.ThrowIfCancellationRequested();
                    throw new IOException("Не удалось установить защищённое соединение с API 4PDA.");
                }
                finally
                {
                    attemptsCancellation.Cancel();
                    foreach (Task<StreamSocket> pending in attempts)
                        DisposeCandidateWhenComplete(pending);
                }
            }
        }

        private static async Task<StreamSocket> ConnectCandidateAsync(string host, EndpointPair pair, CancellationToken cancellation)
        {
            var socket = new StreamSocket();
            try
            {
                socket.Control.NoDelay = true;
                socket.Control.KeepAlive = true;
                using (cancellation.Register(socket.Dispose))
                {
                    if (pair == null)
                        await socket.ConnectAsync(new HostName(host), "443", SocketProtectionLevel.Tls12)
                            .AsTask(cancellation).ConfigureAwait(false);
                    else
                    {
                        await socket.ConnectAsync(pair).AsTask(cancellation).ConfigureAwait(false);
                        // No application data is sent before TLS. Validate the
                        // certificate against the hostname, not the numeric IP.
                        await socket.UpgradeToSslAsync(SocketProtectionLevel.Tls12, new HostName(host))
                            .AsTask(cancellation).ConfigureAwait(false);
                    }
                    cancellation.ThrowIfCancellationRequested();
                }
                return socket;
            }
            catch { socket.Dispose(); return null; }
        }

        private static async void DisposeCandidateWhenComplete(Task<StreamSocket> pending)
        {
            try
            {
                StreamSocket socket = await pending.ConfigureAwait(false);
                if (socket != null) socket.Dispose();
            }
            catch { }
        }

        private static async Task RefreshEndpointAsync(CancellationToken cancellation)
        {
            string[] sources = {
                "https://gist.githubusercontent.com/aigilea/152b043823de7cfeacd06f348b78ec25/raw/provision.json",
                "https://provision.app.devapps.ru/"
            };
            foreach (string source in sources)
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    try
                    {
                        using (var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter
                            { CookieUsageBehavior = Windows.Web.Http.Filters.HttpCookieUsageBehavior.NoCookies })
                        using (var client = new Windows.Web.Http.HttpClient(filter))
                        using (var response = await client.GetAsync(new Uri(source)).AsTask(timeout.Token))
                        {
                            response.EnsureSuccessStatusCode();
                            string body = await response.Content.ReadAsStringAsync().AsTask(timeout.Token);
                            string host = JsonObject.Parse(body).GetNamedString("w", string.Empty);
                            if (Uri.CheckHostName(host) == UriHostNameType.Dns
                                && (host.EndsWith(".4pda.to", StringComparison.OrdinalIgnoreCase)
                                    || host.EndsWith(".4pda.ru", StringComparison.OrdinalIgnoreCase)))
                            { _endpointHost = host; return; }
                        }
                    }
                    catch { if (cancellation.IsCancellationRequested) throw; }
                }
            }
        }

        private async Task<ApiArray> ExchangeAsync(string command, object[] arguments, CancellationToken cancellation, Stream upload = null)
        {
            Trace("Request " + command);
            int id = _nextId = (_nextId % 0x0ffffffe) + 1;
            var request = new JsonArray { JsonValue.CreateNumberValue(id), JsonValue.CreateStringValue(command) };
            foreach (object value in arguments)
                request.Add(ToJson(value));
            await SendFrameAsync(1, Encode(request.Stringify()), cancellation, upload == null);
            if (upload != null)
            {
                byte[] chunk = new byte[131072];
                while (upload.Position < upload.Length)
                {
                    int count = await upload.ReadAsync(chunk, 0, chunk.Length, cancellation);
                    if (count == 0) throw new EndOfStreamException("Не удалось прочитать вложение.");
                    byte[] frame = new byte[count]; Buffer.BlockCopy(chunk, 0, frame, 0, count);
                    await SendFrameAsync(0, frame, cancellation, upload.Position == upload.Length);
                }
            }
            while (true)
            {
                byte[] frameHeader = await ReadExactlyAsync(2, cancellation);
                int opcode = frameHeader[0] & 15;
                ulong length = (uint)(frameHeader[1] & 127);
                if (length == 126)
                {
                    byte[] size = await ReadExactlyAsync(2, cancellation);
                    length = (uint)((size[0] << 8) | size[1]);
                }
                else if (length == 127)
                {
                    byte[] size = await ReadExactlyAsync(8, cancellation);
                    length = 0;
                    foreach (byte part in size) length = (length << 8) | part;
                }
                if (length > (ulong)MaximumFrameBytes || (frameHeader[1] & 128) != 0)
                    throw new IOException("Некорректный размер пакета 4PDA.");
                byte[] payload = await ReadExactlyAsync((int)length, cancellation);
                Trace("Frame " + opcode + ", bytes " + length);
                if (opcode == 8) throw new IOException("Сервер 4PDA закрыл соединение.");
                if (opcode == 9) { await SendFrameAsync(10, payload, cancellation); continue; }
                if (opcode == 10) continue;
                if (opcode != 1 && opcode != 2) throw new IOException("Неизвестный пакет 4PDA.");
                bool compressed = (frameHeader[0] & 64) != 0;
                // Large topic documents must not inflate/decode/parse on the XAML thread.
                // The request gate stays held, preserving the shared DEFLATE dictionary.
                var response = await Task.Run(() => new ApiArray(JsonArray.Parse(Decode(compressed ? Inflate(payload) : payload))));
                if (response.Count < 2 || response.Values[0].ValueType != JsonValueType.Number
                    || response.Values[1].ValueType != JsonValueType.Number)
                    throw new IOException("Некорректный заголовок ответа 4PDA API.");
                int responseId = response.Int(0) & 0x0fffffff;
                if (responseId != 0 && responseId != id)
                    continue; // Server notifications have their own request identifiers.
                var data = new JsonArray();
                for (int i = 2; i < response.Count; i++) data.Add(response.Values[i]);
                var result = new ApiArray(data);
                Trace("Reply " + command + ", status " + response.Int(1));
                if (response.Int(1) != 0) throw new OfficialApiException(command, response.Int(1), result);
                return result;
            }
        }

        private async Task SendFrameAsync(int opcode, byte[] payload, CancellationToken cancellation, bool final = true)
        {
            // The official endpoint uses unmasked WebSocket frames, also over WSS.
            var header = new List<byte> { (byte)((final ? 128 : 0) | opcode) };
            if (payload.Length < 126) header.Add((byte)payload.Length);
            else if (payload.Length <= 65535)
            {
                header.Add(126); header.Add((byte)(payload.Length >> 8)); header.Add((byte)payload.Length);
            }
            else
            {
                header.Add(127);
                for (int shift = 56; shift >= 0; shift -= 8) header.Add((byte)((ulong)payload.Length >> shift));
            }
            _writer.WriteBytes(header.ToArray());
            _writer.WriteBytes(payload);
            await _writer.StoreAsync().AsTask(cancellation);
        }

        private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellation)
        {
            byte[] bytes = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                uint loaded = await _reader.LoadAsync((uint)(count - offset)).AsTask(cancellation);
                if (loaded == 0) throw new EndOfStreamException("Соединение с 4PDA прервано.");
                byte[] part = new byte[loaded];
                _reader.ReadBytes(part);
                Buffer.BlockCopy(part, 0, bytes, offset, part.Length);
                offset += part.Length;
            }
            return bytes;
        }

        private byte[] Inflate(byte[] payload)
        {
            if (payload.Length < 4) throw new IOException("Повреждённый сжатый пакет 4PDA.");
            int size = payload[0] | payload[1] << 8 | payload[2] << 16 | payload[3] << 24;
            if (size < 0 || size > MaximumFrameBytes) throw new IOException("Слишком большой ответ 4PDA.");
            _compressedInput.Append(payload, 4, payload.Length - 4);
            _compressedInput.Append(new byte[] { 0, 0, 255, 255 }, 0, 4);
            byte[] output = new byte[size];
            int offset = 0;
            while (offset < size)
            {
                int read = _inflater.Read(output, offset, size - offset);
                if (read == 0) throw new IOException("Не удалось распаковать ответ 4PDA.");
                offset += read;
            }
            return output;
        }

        private static IJsonValue ToJson(object value)
        {
            if (value is string) return JsonValue.CreateStringValue((string)value);
            if (value is ApiArray) return ((ApiArray)value).Values;
            if (value is JsonArray) return (JsonArray)value;
            if (value is int || value is long) return JsonValue.CreateNumberValue(Convert.ToDouble(value));
            if (value is object[])
            {
                var array = new JsonArray();
                foreach (object child in (object[])value) array.Add(ToJson(child));
                return array;
            }
            throw new ArgumentException("Неподдерживаемый аргумент 4PDA API.");
        }

        // CP1251 is part of the wire protocol, independent of the system locale.
        private const string HighCharacters = "ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏђ‘’“”•–—\u0098™љ›њќћџ\u00a0ЎўЈ¤Ґ¦§Ё©Є«¬\u00ad®Ї°±Ііґµ¶·ё№є»јЅѕї";
        internal static string Decode(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length);
            foreach (byte value in bytes)
                text.Append(value < 128 ? (char)value : value >= 192 ? (char)(value + 848) : HighCharacters[value - 128]);
            return text.ToString();
        }
        internal static byte[] Encode(string text)
        {
            var bytes = new List<byte>(text.Length);
            foreach (char value in text)
            {
                if (value < 128) bytes.Add((byte)value);
                else if (value >= 1040 && value < 1104) bytes.Add((byte)(value - 848));
                else
                {
                    int index = HighCharacters.IndexOf(value);
                    if (index >= 0) bytes.Add((byte)(128 + index));
                    else bytes.AddRange(Encoding.UTF8.GetBytes("\\u" + ((int)value).ToString("x4")));
                }
            }
            return bytes.ToArray();
        }

        private void Disconnect()
        {
            if (_reader != null) _reader.Dispose();
            if (_writer != null) _writer.Dispose();
            if (_socket != null) _socket.Dispose();
            if (_inflater != null) _inflater.Dispose();
            _reader = null; _writer = null; _socket = null; _inflater = null; _compressedInput = null;
            _connectedUserId = 0; _connectedLoginKey = null; _connectedHidden = false;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void Trace(string message)
        {
            System.Diagnostics.Debug.WriteLine("[4PDA.API] " + message);
            try
            {
                string path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "api-diagnostics.log");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private sealed class AppendableStream : Stream
        {
            private readonly Queue<byte[]> _chunks = new Queue<byte[]>();
            private int _offset;
            public void Append(byte[] bytes, int offset, int count)
            {
                byte[] chunk = new byte[count]; Buffer.BlockCopy(bytes, offset, chunk, 0, count); _chunks.Enqueue(chunk);
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_chunks.Count == 0) return 0;
                byte[] chunk = _chunks.Peek();
                int copied = Math.Min(count, chunk.Length - _offset);
                Buffer.BlockCopy(chunk, _offset, buffer, offset, copied);
                _offset += copied;
                if (_offset == chunk.Length) { _chunks.Dequeue(); _offset = 0; }
                return copied;
            }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
