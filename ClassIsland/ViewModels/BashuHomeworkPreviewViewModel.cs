using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels;

public sealed class BashuHomeworkPreviewViewModel : ObservableObject
{
    private int _stage;
    private BashuHomeworkSnapshot? _snapshot;
    public bool IsLive { get; private set; }
    public bool IsDemo => !IsLive;
    public string Subtitle => IsLive ? $"{_snapshot?.ClassName ?? "班级作业"} · 平台自动同步" : "演示测试版 · 模拟班级 · 不连接真实数据";
    public string SyncHint { get; private set; } = "等待平台作业";
    public void ApplySnapshot(BashuHomeworkSnapshot snapshot)
    {
        IsLive = true; _snapshot = snapshot;
        SyncHint = $"{DateTime.Now:HH:mm:ss} 已同步 · 清查和补交结果自动更新";
        Refresh(); OnPropertyChanged(nameof(IsLive)); OnPropertyChanged(nameof(IsDemo));
        OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(SyncHint));
    }

    private bool _expanded = true;
    private bool _showNames = true;
    private double _textSize = 16;
    public string[] Stages { get; } = ["1 · 布置作业", "2 · 清查完成", "3 · 学生补交", "4 · 全部交齐"];
    public ObservableCollection<BashuHomeworkPreviewCard> Cards { get; } = new();
    public int Stage
    {
        get => _stage;
        set { if (value is < 0 or > 3 || !SetProperty(ref _stage, value)) return; Refresh(); }
    }
    public bool Expanded { get => _expanded; set { if (SetProperty(ref _expanded, value)) OnPropertyChanged(nameof(FoldLabel)); } }
    public string FoldLabel => Expanded ? "收起" : "展开";
    public bool ShowNames { get => _showNames; set { if (SetProperty(ref _showNames, value)) Refresh(); } }
    public double TextSize { get => _textSize; set => SetProperty(ref _textSize, Math.Clamp(value, 14, 22)); }
    public string Summary => IsLive ? LiveSummary : Stage switch { 0 => "2 项作业 · 等待清查", 1 => "2 项作业 · 待补交 5 人次", 2 => "2 项作业 · 待补交 4 人次", _ => "2 项作业 · 已全部交齐" };
    public string Hint => Stage switch { 0 => "布置后自动展示；清查前不推断未交名单。", 1 => "模拟老师确认清查：请假、免交不计入待补交。", 2 => "模拟收到补交确认：示例同学甲已从物理名单移除。", _ => "所有作业已交齐。可收起悬浮窗，或重新演示。" };
    public BashuHomeworkPreviewViewModel() => Refresh();
    public void Next() => Stage = (Stage + 1) % Stages.Length;
    private string LiveSummary => _snapshot == null || _snapshot.Items.Count == 0 ? "暂无待展示作业" :
        $"{_snapshot.Items.Count} 项作业 · 待补交 {_snapshot.Items.Where(x => x.Checked).Sum(x => x.MissingNames.Count)} 人次" +
        (_snapshot.Items.Any(x => !x.Checked) ? " · 部分待清查" : "");
    private void Refresh()
    {
        Cards.Clear();
        if (IsLive)
        {
            foreach (var item in _snapshot?.Items ?? [])
            {
                var missing = item.Checked ? item.MissingNames.Count : 0;
                var expected = Math.Max(0, item.Total - item.Excused);
                Cards.Add(new(item.Subject, item.Title, item.Content, item.Deadline,
                    !item.Checked ? "待清查" : item.Total == 0 ? "暂无学生记录" : missing > 0 ? $"待补交 {missing} 人" : "已全部交齐",
                    item.Checked ? $"应交 {expected} 人 · 已交 {item.Completed} 人 · 请假/免交 {item.Excused} 人" : "收交情况待核对",
                    item.Checked && ShowNames ? string.Join("、", item.MissingNames) : "",
                    missing > 0 ? "#AA6100" : "#007E73", missing > 0 ? "#FFF5DF" : "#E8F5F1",
                    item.Completed, missing, item.Checked));
            }
            OnPropertyChanged(nameof(Summary)); return;
        }
        Cards.Add(Create("物理", "运动的描述 · 巩固练习", "教材 P32 第 1–4 题；写出分析过程。", "明天早读前交", Stage switch { 1 => 3, 2 => 2, _ => 0 }, Stage == 1 ? "示例同学甲、示例同学乙、示例同学丙" : "示例同学乙、示例同学丙"));
        Cards.Add(Create("数学", "整式的乘法 · 基础练习", "练习册 P18；选做题按课堂要求完成。", "明天第一节课前交", Stage is 1 or 2 ? 2 : 0, "示例同学丁、示例同学戊"));
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Hint));
    }
    private BashuHomeworkPreviewCard Create(string subject, string title, string content, string deadline, int missing, string names)
    {
        var checkedAlready = Stage > 0;
        return new(subject, title, content, deadline,
            checkedAlready ? (missing > 0 ? $"待补交 {missing} 人" : "已全部交齐") : "待清查",
            checkedAlready ? $"应交 46 人 · 已交 {46 - missing} 人 · 请假/免交 2 人" : "班级 48 人 · 收交情况待核对",
            checkedAlready && missing > 0 && ShowNames ? names : "",
            checkedAlready && missing > 0 ? "#AA6100" : "#007E73",
            checkedAlready && missing > 0 ? "#FFF5DF" : "#E8F5F1",
            checkedAlready ? 46 - missing : 0, missing, checkedAlready);
    }
}

public sealed record BashuHomeworkPreviewCard(string Subject, string Title, string Content, string Deadline,
    string Status, string Counts, string Names, string StatusColor, string StatusBackground, int Submitted, int Missing, bool Checked)
{
    public bool HasNames => Names.Length > 0;
}

public sealed class BashuHomeworkSnapshot
{
    public int SchemaVersion { get; set; }
    public string Revision { get; set; } = "";
    public string ClassName { get; set; } = "";
    public List<BashuHomeworkItem> Items { get; set; } = [];
    public static BashuHomeworkSnapshot Parse(string json)
    {
        var snapshot = JsonSerializer.Deserialize<BashuHomeworkSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException("作业响应为空");
        if (snapshot.SchemaVersion != 1 || string.IsNullOrWhiteSpace(snapshot.Revision) || snapshot.Items == null ||
            snapshot.Items.Any(x => x == null || x.MissingNames == null || x.Total < 0 || x.Completed < 0 || x.Excused < 0))
            throw new FormatException("作业响应格式不受支持");
        return snapshot;
    }
}
public sealed class BashuHomeworkItem
{
    public string Id { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Deadline { get; set; } = "";
    public bool Checked { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Excused { get; set; }
    public List<string> MissingNames { get; set; } = [];
}
