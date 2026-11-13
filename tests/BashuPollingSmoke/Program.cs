using System.Net;
using System.Net.Http.Headers;
using ClassIsland.Services.Management;
using ClassIsland.Shared.Models.Management;

void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
var now=DateTimeOffset.Parse("2026-11-13T00:00:00Z");
var handler=new ScriptedHandler();
using var client=new HttpClient(handler);
var settings=new ManagementSettings{BashuServerUrl="https://example.test",BashuDeviceToken=new string('a',64)};
var connection=new BashuPlatformConnection(settings,Guid.NewGuid(),client,()=>now,()=>0);
HttpResponseMessage Reply(HttpStatusCode status,string body="{}",int? retry=null,string? etag=null){var r=new HttpResponseMessage(status){Content=new StringContent(body)};if(retry.HasValue)r.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(retry.Value));if(etag!=null)r.Headers.ETag=new EntityTagHeaderValue(etag);return r;}
handler.Next=()=>Reply(HttpStatusCode.OK);
Check(await connection.PollAsync(default,true)=="{}","initial poll succeeds");
Check(handler.Paths.Last().EndsWith("?schedule=full"),"startup/manual request explicitly asks for a full schedule");
var requests=handler.Paths.Count;
for(var i=0;i<20;i++)await connection.PollAsync();
Check(handler.Paths.Count==requests,"repeated timer/manual calls do not create premature requests");
Check(connection.PollSchedule.Interval.TotalSeconds is >=5.25 and <6,"successful poll stays at five seconds instead of reverting to one");
now+=connection.PollSchedule.Interval;
handler.Next=()=>Reply(HttpStatusCode.TooManyRequests,retry:37);
Check(await connection.PollAsync()==null && !connection.PairingInvalid,"rate limiting is not mistaken for unpairing");
Check(connection.PollSchedule.Interval.TotalSeconds>=37.25,"Retry-After seconds is honored");
now+=TimeSpan.FromSeconds(36);await connection.PollAsync();
Check(handler.Paths.Count==requests+1,"429 cooldown suppresses requests until the server allows them");
now+=TimeSpan.FromSeconds(2);
handler.Next=()=>{var r=Reply(HttpStatusCode.ServiceUnavailable);r.Headers.RetryAfter=new RetryConditionHeaderValue(now+TimeSpan.FromSeconds(45));return r;};
await connection.PollAsync();
Check(connection.PollSchedule.Interval.TotalSeconds>=45.25,"HTTP-date Retry-After is honored too");
now+=connection.PollSchedule.Interval;
handler.Next=()=>{var r=Reply(HttpStatusCode.OK);r.Headers.Add("X-Poll-Interval-Seconds","12");return r;};
await connection.PollAsync();
Check(connection.PollSchedule.Interval.TotalSeconds>=12.25,"server success hint controls the next poll");
now+=connection.PollSchedule.Interval;
handler.Next=()=>Reply(HttpStatusCode.OK);await connection.PollAsync();
Check(connection.PollSchedule.Interval.TotalSeconds>=12.25,"a missing hint does not erase the learned server minimum");

handler.Next=()=>Reply(HttpStatusCode.OK,"snapshot-1",etag:"\"v1\"");
Check(await connection.GetHomeworkAsync(default)=="snapshot-1","homework snapshot is received");
Check(connection.HomeworkSchedule.Interval.TotalSeconds>=15,"homework idle requests run at fifteen seconds");
now+=connection.HomeworkSchedule.Interval;
handler.Next=()=>Reply(HttpStatusCode.NotModified);
Check(await connection.GetHomeworkAsync(default)==null && connection.HomeworkNotModified,"304 confirms that the previous homework remains current");
Check(handler.IfNoneMatch.Last()=="\"v1\"","unchanged homework uses a conditional request");
now+=connection.HomeworkSchedule.Interval;
handler.Next=()=>throw new HttpRequestException("offline");
await connection.GetHomeworkAsync(default);
var firstDelay=connection.HomeworkSchedule.Interval;
now+=firstDelay;await connection.GetHomeworkAsync(default);
Check(connection.HomeworkSchedule.Interval>firstDelay,"repeated network errors back off");
for(var i=0;i<6;i++){now+=connection.HomeworkSchedule.Interval;await connection.GetHomeworkAsync(default);}
Check(connection.HomeworkSchedule.Interval.TotalSeconds is >=60 and <=61,"network backoff has a one minute bound");
now+=connection.HomeworkSchedule.Interval;handler.Next=()=>Reply(HttpStatusCode.OK,"snapshot-2",etag:"\"v2\"");
Check(await connection.GetHomeworkAsync(default)=="snapshot-2" && connection.HomeworkSchedule.Interval.TotalSeconds<16,"reconnection returns to normal cadence and gets updated homework");
now+=connection.HomeworkSchedule.Interval;handler.Next=()=>Reply(HttpStatusCode.Unauthorized);
await connection.GetHomeworkAsync(default);Check(connection.PairingInvalid,"actual revoked pairing is distinguished from temporary errors");
connection.UpdateToken(new string('b',64));handler.Next=()=>Reply(HttpStatusCode.OK,"other-class");
Check(await connection.GetHomeworkAsync(default)=="other-class" && handler.IfNoneMatch.Last()=="" && !connection.PairingInvalid,"new pairing immediately fetches fresh data without the previous class ETag");
now+=connection.PollSchedule.Interval;handler.Next=()=>Reply(HttpStatusCode.Forbidden);
await connection.PollAsync();Check(!connection.PairingInvalid,"a proxy 403 does not erase an otherwise valid pairing");
connection.UpdateToken(new string('c',64));
handler.Next=()=>throw new OperationCanceledException();
using var canceled=new CancellationTokenSource();canceled.Cancel();
try{await connection.PollAsync(canceled.Token);throw new Exception("shutdown cancellation swallowed");}catch(OperationCanceledException){Console.WriteLine("PASS shutdown cancellation propagates");}

public sealed class ScriptedHandler:HttpMessageHandler
{
    public Func<HttpResponseMessage> Next=()=>new(HttpStatusCode.OK);
    public List<string> Paths=new();public List<string> IfNoneMatch=new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Paths.Add(request.RequestUri!.PathAndQuery);IfNoneMatch.Add(request.Headers.IfNoneMatch.ToString());return Task.FromResult(Next());}
}
