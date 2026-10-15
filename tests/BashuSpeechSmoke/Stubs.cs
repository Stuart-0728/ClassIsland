namespace ClassIsland.Core { public static class CommonDirectories { public static string AppCacheFolderPath { get; } = Path.Combine(Path.GetTempPath(), "BashuSpeechSmoke-" + Guid.NewGuid()); } }
namespace ClassIsland.Core.Abstractions.Services { public interface IAudioService { Task PlayAudioAsync(string path, float volume, CancellationToken cancellationToken); } }
namespace ClassIsland.Core.Abstractions.Services.SpeechService { public interface ISpeechService { void EnqueueSpeechQueue(string text); void ClearSpeechQueue(); } }
namespace ClassIsland.Core.Attributes { public class SpeechProviderInfoAttribute(string id, string name) : Attribute { public string Id { get; } = id; public string Name { get; } = name; } }
namespace ClassIsland.Shared.Abstraction.Services { public class SettingsService { public SpeechSettings Settings { get; } = new(); } public class SpeechSettings { public string EdgeTtsVoiceName { get; set; } = "test"; public double SpeechVolume { get; set; } = 1; } }
namespace Edge_tts_sharp.Model { public class eVoice { public string ShortName { get; set; } = "test"; } public class PlayOption { public string Text { get; set; } = ""; } }
namespace Edge_tts_sharp {
 public static class EdgeTts {
  public static Func<Model.PlayOption,Action<List<byte>>,Task> Invoke { get; set; } = (_,_)=>Task.FromException(new Exception("403 Forbidden"));
  public static List<Model.eVoice> GetVoice()=>[new()];
  public static Task InvokeAsync(Model.PlayOption option, Model.eVoice voice, Action<List<byte>> callback)=>Invoke(option,callback);
 }
}
namespace ClassIsland.Services.SpeechService {
 public class SystemSpeechService : FakeFallback {}
 public class MacSpeechService : FakeFallback {}
 public class FakeFallback : ClassIsland.Core.Abstractions.Services.SpeechService.ISpeechService {
  public static List<string> Spoken { get; } = new(); public static int Clears; public static bool Throw;
  public void EnqueueSpeechQueue(string text) { if(Throw) throw new Exception("No installed voice"); Spoken.Add(text); }
  public void ClearSpeechQueue() { Clears++; }
 }
}
namespace SoundFlow.Backends.MiniAudio { class Unused {} }
namespace SoundFlow.Components { class Unused {} }
namespace SoundFlow.Enums { class Unused {} }
namespace SoundFlow.Providers { class Unused {} }
namespace SoundFlow.Structs { class Unused {} }
