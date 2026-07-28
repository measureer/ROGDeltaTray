using System.Net.WebSockets;
using System.Text;

// WsProbe - probe the local ASUS ArmourySocketServer / asus_framework WebSockets,
// mimicking the official HomePage UI connection flow (from View/a701/index.js):
//   1. ws://127.0.0.1:1042/?role=home&deviceType=4&pid=a701   (main device socket)
//   2. ws://127.0.0.1:9013                                     (alert socket, receives ui_pid XML)
//
// Usage: wsprobe [seconds]

int seconds = args.Length > 0 ? int.Parse(args[0]) : 60;

async Task<ClientWebSocket?> Connect(string url)
{
    var ws = new ClientWebSocket();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    try
    {
        await ws.ConnectAsync(new Uri(url), cts.Token);
        Console.WriteLine($"[connected] {url}");
        return ws;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[connect failed] {url}: {ex.Message}");
        ws.Dispose();
        return null;
    }
}

async Task Listen(string name, ClientWebSocket ws, DateTime end)
{
    var buf = new byte[256 * 1024];
    while (DateTime.UtcNow < end)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var result = await ws.ReceiveAsync(buf, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine($"[{name}] CLOSED status={ws.CloseStatus} desc={ws.CloseStatusDescription}");
                return;
            }
            var msg = Encoding.UTF8.GetString(buf, 0, result.Count);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{name}] {(result.MessageType == WebSocketMessageType.Text ? "TXT" : "BIN")} {msg}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[{name}] error: {ex.Message} (state={ws.State})");
            return;
        }
    }
}

var main = await Connect("ws://127.0.0.1:1042/?role=home&deviceType=4&pid=a701");
var alert = await Connect("ws://127.0.0.1:9013/");

if (alert != null)
{
    string xml = "<xml><command>ui_pid</command><device>a701</device></xml>";
    await alert.SendAsync(Encoding.UTF8.GetBytes(xml), WebSocketMessageType.Text, true, CancellationToken.None);
    Console.WriteLine($"[alert] sent: {xml}");
}

var end = DateTime.UtcNow.AddSeconds(seconds);
var tasks = new List<Task>();
if (main != null) tasks.Add(Listen("main", main, end));
if (alert != null) tasks.Add(Listen("alert", alert, end));
await Task.WhenAll(tasks);
Console.WriteLine("done.");
