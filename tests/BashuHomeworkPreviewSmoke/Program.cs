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
