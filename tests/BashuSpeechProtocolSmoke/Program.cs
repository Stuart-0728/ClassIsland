using ClassIsland.Services.SpeechService;
using System.Xml.Linq;
static void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS "+label); }
var vectors=new Dictionary<long,string> {
 [0]="52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6",
 [1710000000]="CBBF0F1C7124D6BF-6F19AC2CF6833F05E96712BA658B22198DA66BEBAFD9DD711E2DF256B625A022",
 [1792022400]="F3FD83535256D08E-CF2FA3106FF93AB92502C05F6EF45B92187ED35987A62A77464E342750F61216",
 [1792022699]="F3FD83535256D08E-CF2FA3106FF93AB92502C05F6EF45B92187ED35987A62A77464E342750F61216",
 [1792022700]="BC7569B4AA9A3A86-5299C23F1D621C77C10E9D7C28D5798AD7EBFFF02E6EC21899E3774660824207"
};
foreach(var (timestamp,expected) in vectors)Check(EdgeTtsToken.Generate(DateTimeOffset.FromUnixTimeSeconds(timestamp))==expected,$"v2 token matches upstream reference at {timestamp}");
var now=DateTimeOffset.FromUnixTimeSeconds(1792022400);
Check(EdgeTtsToken.Generate(now.ToOffset(TimeSpan.FromHours(8)))==EdgeTtsToken.Generate(now),"token is independent of local timezone");
var input="物理作业：A < B & C > D，不交<break time='10s'/>不是标记";
var message=EdgeSpeechClient.SpeechMessage(input,"zh-CN-XiaoxiaoNeural","test",now);
var xml=XDocument.Parse(message[(message.IndexOf("\r\n\r\n",StringComparison.Ordinal)+4)..]);
Check(xml.Root!.Value==input,"notification punctuation and markup are escaped as text");
Check(!xml.Descendants().Any(x=>x.Name.LocalName=="break"),"notification text cannot inject SSML commands");
using var cancelled=new CancellationTokenSource();cancelled.Cancel();
try { await EdgeSpeechClient.SynthesizeAsync("取消","zh-CN-XiaoxiaoNeural",cancelled.Token);throw new Exception("Cancelled synthesis should not connect"); }
catch(OperationCanceledException) { Console.WriteLine("PASS cancelled speech request is aborted"); }
Console.WriteLine("All speech protocol checks passed.");
