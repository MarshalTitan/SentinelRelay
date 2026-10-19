using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SentinelRelay.Remote;
using SentinelRelay.RemoteInput;

var count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL " + name);
    count++;
    Console.WriteLine("PASS " + name);
}
var held = new ControllerFrame(1, -1, 0.5, -0.5, 1, 0.5, 0x1201);
var vector = ViiperWire.Encode(held);
Check(Convert.ToHexString(vector) == "01120000FF80FF7F0080004000C0000000000000", "documented 20-byte Xbox packet matches golden vector");
Check(ViiperWire.Encode(ControllerFrame.Neutral).All(b => b == 0), "neutral packet releases every control");
try { ViiperWire.Encode(held with { RightX = double.NaN }); Check(false, "invalid frame denied"); }
catch (InvalidDataException) { Check(true, "invalid frame never reaches backend encoding"); }
var packet = WorkerPacket.Create(1, held);
using (var stream = new MemoryStream())
{
    await WorkerWire.Write(stream, packet);
    stream.Position = 0;
    var read = await WorkerWire.Read(stream, CancellationToken.None);
    Check(read == packet && WorkerWire.Accept(read, 0, Stopwatch.GetTimestamp()), "strict bounded worker packet round trip");
    Check(!WorkerWire.Accept(read, 1, Stopwatch.GetTimestamp()), "worker replay rejected");
}
var now = Stopwatch.GetTimestamp();
Check(!WorkerWire.Accept(packet with { IssuedAt = now + 1 }, 0, now), "future worker timestamp rejected");
Check(!WorkerWire.Accept(packet with { IssuedAt = now - Stopwatch.Frequency }, 0, now), "queued stale frame cannot renew worker timeout");
Check(!WorkerWire.Accept(packet with { Version = 2 }, 0, now), "worker protocol mismatch denied");
Check(!WorkerWire.Accept(packet with { Sequence = 0 }, 0, now), "worker sequence must be positive");
Check(!WorkerWire.Accept(packet with { Buttons = 0x0400 }, 0, now), "worker excludes system Guide and reserved buttons");
foreach (var length in new[] { -1, 0, WorkerWire.MaxBytes + 1, int.MaxValue })
{
    using var stream = new MemoryStream();
    var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, length); stream.Write(header); stream.Position = 0;
    try { await WorkerWire.Read(stream, CancellationToken.None); Check(false, "oversized frame denied"); }
    catch (InvalidDataException) { Check(true, "worker frame size rejected before allocation"); }
}
foreach (var json in new[] { "{broken", "{\"version\":1,\"sequence\":1,\"shell\":\"unused\"}", "{}" })
{
    using var stream = new MemoryStream();
    var bytes = Encoding.UTF8.GetBytes(json); var header = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length); stream.Write(header); stream.Write(bytes); stream.Position = 0;
    try { await WorkerWire.Read(stream, CancellationToken.None); Check(false, "malformed worker packet denied"); }
    catch (System.Text.Json.JsonException) { Check(true, "malformed or extended worker schema rejected"); }
}
foreach (var oversized in new[] { false, true })
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    try
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            var stream = accepted.GetStream(); var received = new List<byte>();
            while (true) { var b = stream.ReadByte(); if (b == 0) break; if (b < 0) throw new EndOfStreamException(); received.Add((byte)b); }
            Check(Encoding.ASCII.GetString(received.ToArray()) == "ping", "local management request has null terminator");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(oversized ? new string('x', 4097) : "{\"server\":\"VIIPER\",\"version\":\"0.9.0\"}"));
        });
        try
        {
            using var response = await ViiperWire.Request(port, "ping", CancellationToken.None);
            Check(!oversized && response.RootElement.GetProperty("server").GetString() == "VIIPER", "bounded loopback management response");
        }
        catch (InvalidDataException) { Check(oversized, "oversized backend response rejected"); }
        await server;
    }
    finally { listener.Stop(); }
}
var temp = Path.Combine(Path.GetTempPath(), "SentinelInputHash-" + Guid.NewGuid().ToString("N"));
File.WriteAllText(temp, "This is not executable code.");
try
{
    try { using var rejected = await ViiperBackend.Start(temp, false); Check(false, "unpinned backend denied"); }
    catch (InvalidDataException) { Check(true, "unpinned backend cannot execute"); }
}
finally { File.Delete(temp); }
if (args.Length == 3 && args[0] is "--native" or "--protocol")
    await NativeProof.Run(args[1], args[2], args[0] == "--native", Check);
else if (args.Length != 0) throw new ArgumentException("invalid test arguments");
Console.WriteLine(count + " input backend checks passed.");
