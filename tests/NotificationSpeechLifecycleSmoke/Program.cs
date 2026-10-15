using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Services;
using Microsoft.Extensions.Logging.Abstractions;
static void Check(bool c,string n) {if(!c) throw new Exception(n); Console.WriteLine("PASS "+n);}
var speech=new Speech(); var settings=new SettingsService();
var worker=new NotificationWorkerService(speech,new Audio(),settings,new Time(),new Lessons(),NullLogger<NotificationWorkerService>.Instance);
speech.EnqueueSpeechQueue("external fullscreen speech");
var silent=new NotificationRequest { MaskContent=new NotificationContent {IsSpeechEnabled=false} };
await worker.CreateTicket(silent).ProcessMask();
Check(speech.Clears==0,"silent notification completion preserves fullscreen and test speech");
var silentCancel=worker.CreateTicket(new NotificationRequest {MaskContent=new NotificationContent {IsSpeechEnabled=false,Duration=TimeSpan.FromSeconds(1)}});
var cancelTask=silentCancel.ProcessMask();silentCancel.CancellationTokenSource.Cancel();
try {await cancelTask;} catch(OperationCanceledException) {}
Check(speech.Clears==0,"silent notification interruption preserves unrelated speech");
var voiced=new NotificationRequest {MaskContent=new NotificationContent(),OverlayContent=new NotificationContent {IsSpeechEnabled=false}};
var ticket=worker.CreateTicket(voiced);await ticket.ProcessMask();
Check(speech.Clears==0,"voiced mask continues through overlay transition");await ticket.ProcessOverlay();
Check(speech.Clears==1,"voiced mask is stopped when its own complete notification ends");
var pausedRequest=new NotificationRequest {MaskContent=new NotificationContent {Duration=TimeSpan.FromSeconds(1)}};
var paused=worker.CreateTicket(pausedRequest);var pausedTask=paused.ProcessMask();paused.CancellationTokenSource.Cancel();
try {await pausedTask;} catch(OperationCanceledException) {}
Check(speech.Clears==2,"preemption still stops the interrupted ticket speech once");
var resumed=worker.CreateTicket(pausedRequest);var before=speech.Spoken;var resumedTask=resumed.ProcessMask();resumed.CancellationTokenSource.Cancel();
try {await resumedTask;} catch(OperationCanceledException) {}
Check(speech.Spoken==before+1 && speech.Clears==3,"resumed notification restarts and remains cancellable");
var clearsBeforeRepeated = speech.Clears;
var spokenBeforeRepeated = speech.Spoken;
for (var i=0;i<3;i++)
{
    var repeated = worker.CreateTicket(new NotificationRequest
    {
        MaskContent = new NotificationContent { IsSpeechEnabled=false },
        OverlayContent = new NotificationContent()
    });
    await repeated.ProcessMask();
    await repeated.ProcessOverlay();
}
Check(speech.Spoken==spokenBeforeRepeated+3 && speech.Clears==clearsBeforeRepeated+3,
    "successive normal notifications each start and finish their speech");
var clearsBeforeMuted=speech.Clears;
settings.Settings.AllowNotificationSpeech=false;
await worker.CreateTicket(new NotificationRequest()).ProcessMask();
Check(speech.Clears==clearsBeforeMuted,"globally muted notification never cancels another speech queue");
class Speech:ISpeechService { public int Clears,Spoken; public void EnqueueSpeechQueue(string t)=>Spoken++; public void ClearSpeechQueue()=>Clears++; }
class Audio:IAudioService { public Task PlayAudioAsync(Stream s,float v,CancellationToken t)=>Task.CompletedTask; public Task PlayAudioAsync(string p,float v,CancellationToken t)=>Task.CompletedTask; }
class Time:IExactTimeService { public DateTime GetCurrentLocalDateTime()=>DateTime.Now; }
class Lessons:ILessonsService { public event EventHandler? PostMainTimerTicked; }
