using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Services.SpeechService;
using ClassIsland.Shared.Abstraction.Services;
using Edge_tts_sharp;
using Microsoft.Extensions.Logging.Abstractions;

static void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
static EdgeTtsService Create(FakeAudio audio, string voice="test") { var settings=new SettingsService(); settings.Settings.EdgeTtsVoiceName=voice; return new EdgeTtsService(audio,NullLogger<EdgeTtsService>.Instance,settings); }
static async Task Until(Func<bool> condition) { for(var i=0;i<100&&!condition();i++) await Task.Delay(10); Check(condition(),"asynchronous speech operation completes"); }
static void Reset() { FakeFallback.Spoken.Clear(); FakeFallback.Clears=0; FakeFallback.Throw=false; }
var audio=new FakeAudio();
try {
 Reset(); EdgeTts.Invoke=(_,_)=>Task.FromException(new Exception("403 Forbidden"));
 var service=Create(audio);service.EnqueueSpeechQueue("全屏通知测试");await Until(()=>FakeFallback.Spoken.Count==1);
 Check(FakeFallback.Spoken[0]=="全屏通知测试","403 rejection falls back with original notification text");
 service.ClearSpeechQueue();Check(FakeFallback.Clears==1,"confirmation also stops system fallback speech");
 Reset();EdgeTts.Invoke=(_,callback)=>{callback([1,2,3]);return Task.CompletedTask;};
 audio.Fail=false;var success=Create(audio);success.EnqueueSpeechQueue("在线成功");await Until(()=>audio.Count==1);Check(FakeFallback.Spoken.Count==0,"successful online audio is not spoken twice");
 Reset();audio.Fail=true;var failedAudio=Create(audio);failedAudio.EnqueueSpeechQueue("解码失败");await Until(()=>FakeFallback.Spoken.Count==1);Check(FakeFallback.Spoken[0]=="解码失败","audio playback failure falls back");
 Reset();audio.Fail=false;var invalidVoice=Create(audio,"missing");invalidVoice.EnqueueSpeechQueue("无效音色");await Until(()=>FakeFallback.Spoken.Count==1);Check(FakeFallback.Spoken[0]=="无效音色","missing configured online voice falls back");
 Reset();var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);EdgeTts.Invoke=(_,_)=>pending.Task;
 var cancelled=Create(audio);cancelled.EnqueueSpeechQueue("已确认的通知不应补播");cancelled.ClearSpeechQueue();pending.SetException(new Exception("403 Forbidden"));await Task.Delay(100);Check(FakeFallback.Spoken.Count==0,"cancelled download never starts fallback speech");
 Reset();var queueGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);EdgeTts.Invoke=(_,_)=>queueGate.Task;
 var queued=Create(audio);queued.EnqueueSpeechQueue("第一条");queued.EnqueueSpeechQueue("第二条");queueGate.SetException(new Exception("403 Forbidden"));await Until(()=>FakeFallback.Spoken.Count==2);Check(FakeFallback.Spoken.SequenceEqual(new[]{"第一条","第二条"}),"failed online speech preserves fallback queue order");
 Reset();FakeFallback.Throw=true;EdgeTts.Invoke=(_,_)=>Task.FromException(new Exception("403 Forbidden"));var noVoice=Create(audio);noVoice.EnqueueSpeechQueue("无系统语音包");await Task.Delay(50);noVoice.ClearSpeechQueue();Check(FakeFallback.Clears==1,"unavailable system voice does not crash notification processing");
 Console.WriteLine("All speech fallback checks passed.");
} finally { Directory.Delete(ClassIsland.Core.CommonDirectories.AppCacheFolderPath,true); }
class FakeAudio : IAudioService { public bool Fail; public int Count; public Task PlayAudioAsync(string path,float volume,CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested();Count++;return Fail?Task.FromException(new Exception("Decoder failed")):Task.CompletedTask; } }
