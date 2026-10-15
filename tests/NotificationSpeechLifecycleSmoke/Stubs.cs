namespace Avalonia.Threading { class Unused {} }
namespace Avalonia.Platform { public static class AssetLoader { public static Stream Open(Uri u)=>new MemoryStream(); } }
namespace ClassIsland.Shared.Abstraction.Models { class Unused {} }
namespace ClassIsland.Shared.Interfaces { public interface INotificationSettings { bool IsSpeechEnabled {get;} bool IsNotificationSoundEnabled {get;} string NotificationSoundPath {get;} } }
namespace ClassIsland.Shared.Models.Notification { public class NotificationSettings:ClassIsland.Shared.Interfaces.INotificationSettings { public bool IsSettingsEnabled {get;set;} public bool IsSpeechEnabled {get;set;}=true; public bool IsNotificationSoundEnabled {get;set;} public string NotificationSoundPath {get;set;}=""; } }
namespace ClassIsland.Core.Enums.Notification { public enum NotificationState {None,Playing,Completed,Cancelled,Paused} }
namespace ClassIsland.Core.Abstractions.Services.SpeechService { public interface ISpeechService { void EnqueueSpeechQueue(string text); void ClearSpeechQueue(); } }
namespace ClassIsland.Core.Abstractions.Services {
 public interface INotificationWorkerService {} public interface INotificationProvider { public static Uri DefaultNotificationSoundUri {get;}=new("file:///unused"); }
 public interface IAudioService { Task PlayAudioAsync(Stream s,float v,CancellationToken t); Task PlayAudioAsync(string p,float v,CancellationToken t); }
 public interface IExactTimeService { DateTime GetCurrentLocalDateTime(); } public interface ILessonsService { event EventHandler PostMainTimerTicked; }
}
namespace ClassIsland.Services {
 public class SettingsService { public Settings Settings {get;}=new(); }
 public class Settings:ClassIsland.Shared.Models.Notification.NotificationSettings { public bool AllowNotificationSpeech {get;set;}=true; public bool AllowNotificationSound {get;set;} public double NotificationSoundVolume {get;set;}=1; }
}
namespace ClassIsland.Core.Models.Notification {
 using ClassIsland.Core.Enums.Notification;
 using ClassIsland.Shared.Models.Notification;
 public class NotificationContent { public TimeSpan Duration {get;set;}=TimeSpan.FromMilliseconds(20); public bool IsSpeechEnabled {get;set;}=true; public string SpeechContent {get;set;}="test"; public bool IsTimingInit {get;set;} public DateTime? EndTime {get;set;} }
 public class NotificationPlayingSessionInfo { public bool IsExplicitEndTime {get;set;} public DateTime SessionStartTime {get;set;}=DateTime.MinValue; public TimeSpan SessionPlayedTime {get;set;} public System.Diagnostics.Stopwatch TimingStopwatch {get;}=new(); public DateTime CurrentTicketStartTime {get;set;} public bool HasSoundsPlayed {get;set;} public bool IsCompleted {get;set;} }
 public class NotificationRequest { public CancellationTokenSource CancellationTokenSource {get;}=new(); public CancellationToken CancellationToken=>CancellationTokenSource.Token; public NotificationState State {get;set;} public double LeftProgress {get;set;} public NotificationSettings? ChannelSettings {get;set;} public NotificationSettings? ProviderSettings {get;set;} public NotificationSettings RequestNotificationSettings {get;}=new(); public NotificationContent MaskContent {get;set;}=new(); public NotificationContent? OverlayContent {get;set;} public NotificationPlayingSessionInfo MaskSession {get;}=new(); public NotificationPlayingSessionInfo OverlaySession {get;}=new(); public CancellationTokenSource CompletedTokenSource {get;}=new(); }
 public class NotificationPlayingTicket { public Func<Task> ProcessMask {get;set;}=null!; public Func<Task> ProcessOverlay {get;set;}=null!; public NotificationRequest Request {get;set;}=null!; public ClassIsland.Shared.Interfaces.INotificationSettings Settings {get;set;}=null!; public CancellationTokenSource CancellationTokenSource {get;set;}=null!; public TaskCompletionSource CancellationCompletedCompletionSource {get;set;}=null!; }
}
