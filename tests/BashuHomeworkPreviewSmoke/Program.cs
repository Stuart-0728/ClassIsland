using ClassIsland.ViewModels;
var vm = new BashuHomeworkPreviewViewModel();
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine($"PASS {message}"); }
Check(vm.Cards.Count == 2 && vm.Cards.All(c => !c.Checked && !c.HasNames), "unverified homework never exposes missing names");
vm.Stage = 1;
Check(vm.Cards.Sum(c => c.Missing) == 5 && vm.Cards.All(c => c.Submitted + c.Missing == 46), "checked totals exclude two excused students");
Check(vm.Cards[0].Names.Contains("示例同学甲"), "checked names displayed");
vm.Stage = 2;
Check(vm.Cards.Sum(c => c.Missing) == 4 && !vm.Cards[0].Names.Contains("示例同学甲"), "late submission removes only the confirmed student");
vm.ShowNames = false;
Check(vm.Cards.All(c => !c.HasNames) && vm.Cards.Sum(c => c.Missing) == 4, "hide names preserves counts");
vm.ShowNames = true; vm.Stage = 3;
Check(vm.Cards.All(c => c.Missing == 0 && !c.HasNames && c.Submitted == 46), "all submitted clears missing names");
vm.Next();Check(vm.Stage == 0 && vm.Cards.All(c => !c.Checked), "restart does not retain old clearance state");
vm.Stage = -1; Check(vm.Stage == 0, "invalid selection ignored");
vm.TextSize = 100; Check(vm.TextSize == 22, "text size upper bound");
vm.TextSize = 0; Check(vm.TextSize == 14, "text size lower bound");
vm.Expanded = false; Check(vm.FoldLabel == "展开", "fold control state");
Console.WriteLine("11 homework preview checks passed");
var snapshot = BashuHomeworkSnapshot.Parse("""{"schemaVersion":1,"revision":"first","className":"模拟班级","items":[{"id":"1","subject":"物理","title":"测试作业","checked":false,"total":3,"completed":3,"excused":0,"missingNames":[]}]}""");
vm.ApplySnapshot(snapshot);
Check(vm.IsLive && !vm.IsDemo && vm.Cards.Count == 1 && !vm.Cards[0].Checked && vm.Cards[0].Status == "待清查", "platform snapshot replaces demo and hides unverified result");
snapshot.Items[0].Checked = true; snapshot.Items[0].Completed = 1; snapshot.Items[0].Excused = 1; snapshot.Items[0].MissingNames = ["模拟甲"];
vm.ApplySnapshot(snapshot);
Check(vm.Cards[0].Missing == 1 && vm.Cards[0].Counts.Contains("应交 2 人"), "platform counts exclude excused");
vm.ShowNames = false; Check(!vm.Cards[0].HasNames && vm.Cards[0].Missing == 1, "live privacy toggle preserves counts");
vm.ShowNames = true; snapshot.Items[0].MissingNames.Clear(); snapshot.Items[0].Completed = 2; vm.ApplySnapshot(snapshot);
Check(!vm.Cards[0].HasNames && vm.Cards[0].Status == "已全部交齐", "live late submission clears missing list");
snapshot.Items.Clear(); vm.ApplySnapshot(snapshot); Check(vm.Cards.Count == 0 && vm.Summary == "暂无待展示作业", "empty live snapshot clears archived and deleted work");
try { BashuHomeworkSnapshot.Parse("""{"schemaVersion":2,"revision":"bad","items":[]}"""); throw new Exception("invalid schema accepted"); } catch (FormatException) { Console.WriteLine("PASS incompatible snapshot rejected"); }
var fixture = Environment.GetEnvironmentVariable("HOMEWORK_QA_SNAPSHOT");
if (fixture != null) { vm.ApplySnapshot(BashuHomeworkSnapshot.Parse(File.ReadAllText(fixture))); Check(vm.IsLive && vm.Cards.Count > 0, "actual platform JSON deserializes and renders cards"); }
var ticker = new BashuHomeworkPreviewViewModel { Stage = 1 };
Check(ticker.CompactText.Contains("物理") && ticker.CompactText.Contains("示例同学甲"), "folded ticker includes assignment and checked missing names");
ticker.AdvanceCompactCard(); Check(ticker.CompactText.Contains("数学"), "folded ticker cycles subjects");
ticker.ShowNames = false; Check(!ticker.CompactText.Contains("示例同学"), "folded ticker respects name visibility and resets on update");
ticker.ApplySnapshot(new BashuHomeworkSnapshot { SchemaVersion=1, Revision="empty" }); ticker.AdvanceCompactCard();
Check(ticker.CompactText == "暂无待展示作业", "empty folded ticker clears stale work safely");

var placement = new ClassIsland.Models.BashuHomeworkWindowState { CompactWidth = 640 };
foreach(var scale in new[] {0.75,1.0,1.5})
{
    placement.WindowScale=scale;
    var compact=placement.GetSize(false,1920,1080);
    Check(compact.Width==640 && compact.Height==62*scale,"compact width stays independent at content scale "+scale);
}
placement.WindowScale=1;placement.RightInset=250;placement.TopInset=180;
var beforePosition=placement.GetPosition(640,62,1920,1080);
var restored=System.Text.Json.JsonSerializer.Deserialize<ClassIsland.Models.BashuHomeworkWindowState>(System.Text.Json.JsonSerializer.Serialize(placement))!;
Check(restored.GetPosition(640,62,1920,1080)==beforePosition,"saved placement survives restart");
var widerPosition=placement.GetPosition(800,62,1920,1080);
Check(widerPosition.Left+800==beforePosition.Left+640 && widerPosition.Top==beforePosition.Top,"width adjustment preserves top and right anchor");
placement.TopInset=1000;
var expandedPosition=placement.GetPosition(440,640,1920,1080);
var foldedPosition=placement.GetPosition(640,62,1920,1080);
Check(expandedPosition.Top==440 && foldedPosition.Top==1000,"temporary expanded-screen clamp does not lose folded placement");
placement.RightInset=3000;placement.TopInset=3000;
var constrainedSize=placement.GetSize(false,800,600);
var constrained=placement.GetPosition(constrainedSize.Width,constrainedSize.Height,800,600);
Check(constrained.Left>=0 && constrained.Top+constrainedSize.Height<=600,"changed monitor keeps the whole capsule visible");
placement.WindowScale=double.NaN;placement.CompactWidth=double.PositiveInfinity;placement.RightInset=double.NaN;placement.Normalize();
Check(placement.WindowScale==1 && placement.CompactWidth==520 && placement.RightInset==16,"invalid saved geometry falls back safely");
vm.CompactWidth=720;vm.WindowScale=1.4;
Check(vm.CompactWidth==720,"view model does not scale capsule width with content");
