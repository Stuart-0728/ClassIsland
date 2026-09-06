using ClassIsland.Services.Management;
using Concentus;

static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
using var decoder = OpusCodecFactory.CreateDecoder(48000, 1);
var decoded = new float[5760];
var count = decoder.Decode(new byte[]{0xf8,0xff,0xfe}.AsSpan(), decoded.AsSpan(), decoded.Length, false);
Check(count == 960, "real Opus silence packet decodes to 20 ms / 960 samples");
Check(decoded.Take(count).All(float.IsFinite), "decoded audio contains finite samples");
using var buffer = new BashuRtcAudioBuffer();
var mono = Enumerable.Repeat(0.25f,960).ToArray();
var output = new float[1920];
buffer.Push(mono); buffer.ReadBytes(output);
Check(output.All(x => x == 0), "jitter cushion waits for sufficient audio");
buffer.Push(mono); buffer.Push(mono); buffer.ReadBytes(output);
Check(output.All(x => x == 0.25f), "mono speech expands to stereo without pitch change");
for(var i=0;i<100;i++) buffer.Push(mono);
buffer.Push(Enumerable.Repeat(0.75f,960).ToArray());
var drain = new float[24000]; buffer.ReadBytes(drain);
Check(drain.Count(x => x != 0) <= 11520, "bursty network audio never exceeds 120 ms backlog");
Check(drain.Contains(0.75f), "latest audio is retained");
buffer.Enabled = false; buffer.Push(mono); buffer.ReadBytes(output);
Check(output.All(x=>x==0), "preempted live audio is muted and cleared");
buffer.Enabled = true; buffer.ReadBytes(output);
Check(output.All(x=>x==0), "resumption does not replay stale speech");
buffer.Dispose(); Check(buffer.ReadBytes(output)==0, "disposed receiver stops audio");

// Optional fixture captured from the real browser -> TURN -> Go receiver test.
if (args.Length > 0)
{
    var packets = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[0]))!;
    using var browserDecoder = OpusCodecFactory.CreateDecoder(48000, 1);
    double energy = 0; long samples = 0;
    foreach (var packet in packets)
    {
        var size = browserDecoder.Decode(Convert.FromBase64String(packet).AsSpan(), decoded.AsSpan(), decoded.Length, false);
        Check(size > 0 && decoded.Take(size).All(float.IsFinite), "browser Opus frame decodes");
        foreach (var sample in decoded.Take(size)) energy += sample * sample;
        samples += size;
    }
    Check(packets.Length >= 10 && samples > 9600, "browser stream contains continuous audio");
    Check(Math.Sqrt(energy / samples) > 0.01, "browser audio remains non-silent after TURN and Concentus decoding");
    Console.WriteLine($"Browser audio RMS: {Math.Sqrt(energy / samples):F4}; samples: {samples}");
}
