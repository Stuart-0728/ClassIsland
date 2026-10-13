using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels;

public sealed class BashuHomeworkPreviewViewModel : ObservableObject
{
    private int _stage;
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
    public string Summary => Stage switch { 0 => "2 项作业 · 等待清查", 1 => "2 项作业 · 待补交 5 人次", 2 => "2 项作业 · 待补交 4 人次", _ => "2 项作业 · 已全部交齐" };
    public string Hint => Stage switch { 0 => "布置后自动展示；清查前不推断未交名单。", 1 => "模拟老师确认清查：请假、免交不计入待补交。", 2 => "模拟收到补交确认：示例同学甲已从物理名单移除。", _ => "所有作业已交齐。可收起悬浮窗，或重新演示。" };
    public BashuHomeworkPreviewViewModel() => Refresh();
    public void Next() => Stage = (Stage + 1) % Stages.Length;
    private void Refresh()
    {
        Cards.Clear();
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
