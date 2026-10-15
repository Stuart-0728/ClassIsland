using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace ClassIsland.Services.SpeechService;

internal static class EdgeSpeechClient
{
    private const string Version = "143.0.3650.75";
    private static double _clockSkewSeconds;
    internal static string SpeechMessage(string text, string voice, string requestId, DateTimeOffset now)
    {
        XNamespace ns="http://www.w3.org/2001/10/synthesis";
        var safeText=new string(text.Where(XmlConvert.IsXmlChar).ToArray());
        var ssml=new XElement(ns+"speak", new XAttribute("version","1.0"),new XAttribute(XNamespace.Xml+"lang","en-US"),
            new XElement(ns+"voice", new XAttribute("name",voice),new XElement(ns+"prosody",new XAttribute("pitch","+0Hz"),new XAttribute("rate","+0%"),new XAttribute("volume","+0%"),safeText)));
        return $"X-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{now.UtcDateTime.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",CultureInfo.InvariantCulture)}Z\r\nPath:ssml\r\n\r\n{ssml.ToString(SaveOptions.DisableFormatting)}";
    }
    internal static async Task<byte[]> SynthesizeAsync(string text,string voice,CancellationToken cancellationToken)
    {
        for(var attempt=0;attempt<2;attempt++)
        {
            using var socket=new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails=true;
            socket.Options.SetRequestHeader("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
            socket.Options.SetRequestHeader("Origin","chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            socket.Options.SetRequestHeader("Pragma","no-cache");
            socket.Options.SetRequestHeader("Cache-Control","no-cache");
            socket.Options.SetRequestHeader("Accept-Language","en-US,en;q=0.9");
            socket.Options.Cookies=new CookieContainer();
            socket.Options.Cookies.SetCookies(new Uri("https://speech.platform.bing.com"),$"muid={Guid.NewGuid():N};");
            var now=DateTimeOffset.UtcNow.AddSeconds(Volatile.Read(ref _clockSkewSeconds));
            var id=Guid.NewGuid().ToString("N");
            var uri=new Uri($"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken=6A5AA1D4EAFF4E9FB37E23D68491D6F4&ConnectionId={id}&Sec-MS-GEC={EdgeTtsToken.Generate(now)}&Sec-MS-GEC-Version=2-{Version}");
            try { await socket.ConnectAsync(uri,cancellationToken); }
            catch(WebSocketException) when(attempt==0 && socket.HttpStatusCode==HttpStatusCode.Forbidden)
            {
                var date=socket.HttpResponseHeaders?.FirstOrDefault(x=>x.Key.Equals("Date",StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
                if(!DateTimeOffset.TryParse(date,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var serverTime)) throw;
                // Correct this provider's clock only; never change the computer's system time.
                Volatile.Write(ref _clockSkewSeconds,(serverTime-DateTimeOffset.UtcNow).TotalSeconds);
                continue;
            }
            const string config="Content-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
            await socket.SendAsync(Encoding.UTF8.GetBytes(config).AsMemory(),WebSocketMessageType.Text,true,cancellationToken);
            await socket.SendAsync(Encoding.UTF8.GetBytes(SpeechMessage(text,voice,id,now)).AsMemory(),WebSocketMessageType.Text,true,cancellationToken);
            using var audio=new MemoryStream();
            var buffer=new byte[32*1024];
            while(socket.State==WebSocketState.Open)
            {
                using var frame=new MemoryStream();ValueWebSocketReceiveResult result;
                do
                {
                    result=await socket.ReceiveAsync(buffer.AsMemory(),cancellationToken);
                    if(result.MessageType==WebSocketMessageType.Close) throw new IOException("语音服务在生成完成前关闭了连接");
                    if(frame.Length+result.Count>1024*1024) throw new IOException("语音响应帧过大");
                    frame.Write(buffer,0,result.Count);
                }while(!result.EndOfMessage);
                var bytes=frame.ToArray();
                if(result.MessageType==WebSocketMessageType.Text)
                {
                    if(Encoding.UTF8.GetString(bytes).Contains("Path:turn.end",StringComparison.Ordinal))
                    {
                        if(audio.Length==0) throw new IOException("语音服务没有返回音频");
                        return audio.ToArray();
                    }
                    continue;
                }
                if(result.MessageType!=WebSocketMessageType.Binary || bytes.Length<2) continue;
                var headerLength=BinaryPrimitives.ReadUInt16BigEndian(bytes);
                if(headerLength+2>bytes.Length) throw new IOException("语音音频帧不完整");
                if(!Encoding.UTF8.GetString(bytes,2,headerLength).Contains("Path:audio",StringComparison.Ordinal)) continue;
                var count=bytes.Length-2-headerLength;
                if(audio.Length+count>8*1024*1024) throw new IOException("语音音频过大");
                audio.Write(bytes,2+headerLength,count);
            }
            throw new IOException("语音服务连接已关闭");
        }
        throw new IOException("语音服务校验失败");
    }
}
