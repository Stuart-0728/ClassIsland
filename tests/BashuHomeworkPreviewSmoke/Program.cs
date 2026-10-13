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
