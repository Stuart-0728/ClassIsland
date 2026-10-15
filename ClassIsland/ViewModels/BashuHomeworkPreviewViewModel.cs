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
    public string Subtitle => IsLive
        ? (_snapshot != null ? $"{_snapshot.ClassName} · 平台实时同步" : "平台实时同步 · 正在获取作业数据")
        : "演示测试版 · 模拟班级 · 不连接真实数据";
    public string ContextLabel => IsLive ? (_snapshot?.ClassName ?? "班级作业") : "示例班级";
    public bool IsEmpty => Cards.Count == 0;
    public string CompactSummary => Summary;
    private int _compactIndex;
    public string CompactText => Cards.Count == 0 ? Summary : FormatCompact(Cards[_compactIndex % Cards.Count]);
    private static string FormatCompact(BashuHomeworkPreviewCard card) => string.Join(" · ",
        new[] { card.Subject, card.Title, card.Content, card.Deadline, card.Status, card.Names }
            .Where(x => !string.IsNullOrWhiteSpace(x))).Replace("\r", " ").Replace("\n", " ");

    private double _windowScale = 1.0;
    public double WindowScale
    {
        get => _windowScale;
        set
        {
            if (!double.IsFinite(value)) return;
            var clamped = Math.Clamp(Math.Round(value, 2), 0.75, 1.5);
            if (Math.Abs(clamped - _windowScale) < 0.001) return;
            if (SetProperty(ref _windowScale, clamped))
            {
                OnPropertyChanged(nameof(WindowScalePercentText));
            }
        }
    }
    private double _compactWidth = 520;
    public double CompactWidth
    {
        get => _compactWidth;
        set
        {
            if (!double.IsFinite(value)) return;
            if (SetProperty(ref _compactWidth, Math.Clamp(Math.Round(value), 320, 960)))
                OnPropertyChanged(nameof(CompactWidthText));
        }
    }
    private bool _isPinned = true;
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!SetProperty(ref _isPinned, value)) return;
            OnPropertyChanged(nameof(PinAngle));
            OnPropertyChanged(nameof(PinActionText));
        }
    }
    public double PinAngle => IsPinned ? 0 : -40;
    public string PinActionText => IsPinned ? "取消置顶" : "置顶显示";

    // Draft values keep the popup and its sliders stationary until the panel closes.
    private double _draftWindowScale = 1;
    public double DraftWindowScale
    {
        get => _draftWindowScale;
        set
        {
            if (double.IsFinite(value) && SetProperty(ref _draftWindowScale, Math.Clamp(Math.Round(value, 2), 0.75, 1.5)))
                OnPropertyChanged(nameof(DraftWindowScaleText));
        }
    }
    private double _draftCompactWidth = 520;
    public double DraftCompactWidth
    {
        get => _draftCompactWidth;
        set
        {
            if (double.IsFinite(value) && SetProperty(ref _draftCompactWidth, Math.Clamp(Math.Round(value), 320, 960)))
                OnPropertyChanged(nameof(DraftCompactWidthText));
        }
    }
    public string DraftWindowScaleText => $"{Math.Round(DraftWindowScale * 100)}%";
    public string DraftCompactWidthText => $"{DraftCompactWidth:0}";

    public string CompactWidthText => $"{CompactWidth:0}";
    public string WindowScalePercentText => $"{Math.Round(WindowScale * 100)}%";

    public BashuHomeworkPreviewCard? CurrentCard => Cards.Count == 0 ? null : Cards[_compactIndex % Cards.Count];
    public string CurrentSubject => CurrentCard?.Subject ?? "";
    public string CurrentSubjectColor => CurrentCard?.SubjectColor ?? "#007E73";
    public string CurrentSubjectBackground => CurrentCard?.SubjectBackground ?? "#E8F5F1";
    public string CurrentStatus => CurrentCard?.Status ?? (Cards.Count == 0 ? (IsLive ? "暂无作业" : "等待清查") : "");
    public string CurrentStatusColor => CurrentCard?.StatusColor ?? "#007E73";
    public string CurrentStatusBackground => CurrentCard?.StatusBackground ?? "#E8F5F1";
    public string CurrentStatusBorder => CurrentCard?.StatusBorder ?? "#C5E3D8";

    public string CompactSubjectTitle => Cards.Count == 0 ? (IsLive ? "班级作业" : "示例作业") : (CurrentCard?.Subject ?? "作业");

    public void AdvanceCompactCard()
    {
        _compactIndex = Cards.Count == 0 ? 0 : (_compactIndex + 1) % Cards.Count;
        OnPropertyChanged(nameof(CompactSubjectTitle));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(CurrentSubject));
        OnPropertyChanged(nameof(CurrentSubjectColor));
        OnPropertyChanged(nameof(CurrentSubjectBackground));
        OnPropertyChanged(nameof(CurrentStatus));
        OnPropertyChanged(nameof(CurrentStatusColor));
        OnPropertyChanged(nameof(CurrentStatusBackground));
        OnPropertyChanged(nameof(CurrentStatusBorder));
    }
    public string SyncHint { get; private set; } = "等待平台作业";

    public void EnterLiveMode(string? className = null)
    {
        IsLive = true;
        _snapshot = null;
        SyncHint = "正在连接平台作业接口...";
        Cards.Clear();
        NotifyCardsUpdated();
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsDemo));
        OnPropertyChanged(nameof(ContextLabel));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SyncHint));
    }

    public void ApplySnapshot(BashuHomeworkSnapshot snapshot)
    {
        IsLive = true; _snapshot = snapshot;
        SyncHint = $"{DateTime.Now:HH:mm:ss} 已同步 · 清查和补交结果自动更新";
        Refresh(); OnPropertyChanged(nameof(IsLive)); OnPropertyChanged(nameof(IsDemo));
        OnPropertyChanged(nameof(ContextLabel)); OnPropertyChanged(nameof(Subtitle)); OnPropertyChanged(nameof(SyncHint));
    }

    private bool _expanded = false;
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

    public static (string textColor, string bgColor, string tagBorder) GetSubjectStyle(string subject) => subject switch
    {
        "语文" => ("#A03E1B", "#FDF1EB", "#F7D8CB"),
        "数学" => ("#0D6F8A", "#E9F6FA", "#C8E8F2"),
        "英语" => ("#1D599A", "#EEF4FC", "#CCE0F5"),
        "物理" => ("#13785B", "#EAF6F2", "#C8EADB"),
        "化学" => ("#633A8C", "#F5EEFA", "#E3D2F3"),
        "生物" => ("#2C7A3E", "#ECF7EE", "#C7EACD"),
        "历史" => ("#8A5314", "#FAF2E8", "#EDDAC2"),
        "道法" or "政治" => ("#A82E2E", "#FDF0F0", "#F7D2D2"),
        "地理" => ("#207373", "#EBF7F7", "#C9ECEC"),
        _ => ("#007E73", "#E8F5F1", "#CCE5DE")
    };

    private void Refresh()
    {
        _compactIndex = 0;
        Cards.Clear();
        if (IsLive)
        {
            foreach (var item in _snapshot?.Items ?? [])
            {
                var missing = item.Checked ? item.MissingNames.Count : 0;
                var expected = Math.Max(0, item.Total - item.Excused);
                var statusText = !item.Checked ? "待清查" : item.Total == 0 ? "暂无学生记录" : missing > 0 ? $"待补交 {missing} 人" : "已全部交齐";
                var statusColor = !item.Checked ? "#5B6E68" : missing > 0 ? "#AA6100" : "#007E73";
                var statusBg = !item.Checked ? "#EFF4F2" : missing > 0 ? "#FFF5DF" : "#E8F5F1";
                var statusBorder = !item.Checked ? "#D8E2DF" : missing > 0 ? "#FADFA8" : "#C5E3D8";
                var (sColor, sBg, _) = GetSubjectStyle(item.Subject);

                Cards.Add(new(item.Subject, item.Title, item.Content, item.Deadline,
                    statusText,
                    item.Checked ? $"应交 {expected} 人 · 已交 {item.Completed} 人 · 请假/免交 {item.Excused} 人" : "收交情况待核对",
                    item.Checked && ShowNames ? string.Join("、", item.MissingNames) : "",
                    statusColor, statusBg,
                    item.Completed, missing, item.Checked,
                    sColor, sBg, statusBorder));
            }
            NotifyCardsUpdated();
            return;
        }
        Cards.Add(Create("物理", "运动的描述 · 巩固练习", "教材 P32 第 1–4 题；写出分析过程。", "明天早读前交", Stage switch { 1 => 3, 2 => 2, _ => 0 }, Stage == 1 ? "示例同学甲、示例同学乙、示例同学丙" : "示例同学乙、示例同学丙"));
        Cards.Add(Create("数学", "整式的乘法 · 基础练习", "练习册 P18；选做题按课堂要求完成。", "明天第一节课前交", Stage is 1 or 2 ? 2 : 0, "示例同学丁、示例同学戊"));
        NotifyCardsUpdated();
        OnPropertyChanged(nameof(Hint));
    }

    private void NotifyCardsUpdated()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CompactSummary));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(CompactSubjectTitle));
        OnPropertyChanged(nameof(CurrentSubject));
        OnPropertyChanged(nameof(CurrentSubjectColor));
        OnPropertyChanged(nameof(CurrentSubjectBackground));
        OnPropertyChanged(nameof(CurrentStatus));
        OnPropertyChanged(nameof(CurrentStatusColor));
        OnPropertyChanged(nameof(CurrentStatusBackground));
        OnPropertyChanged(nameof(CurrentStatusBorder));
    }

    private BashuHomeworkPreviewCard Create(string subject, string title, string content, string deadline, int missing, string names)
    {
        var checkedAlready = Stage > 0;
        var statusText = checkedAlready ? (missing > 0 ? $"待补交 {missing} 人" : "已全部交齐") : "待清查";
        var statusColor = checkedAlready && missing > 0 ? "#AA6100" : (checkedAlready ? "#007E73" : "#5B6E68");
        var statusBg = checkedAlready && missing > 0 ? "#FFF5DF" : (checkedAlready ? "#E8F5F1" : "#EFF4F2");
        var statusBorder = checkedAlready && missing > 0 ? "#FADFA8" : (checkedAlready ? "#C5E3D8" : "#D8E2DF");
        var (sColor, sBg, _) = GetSubjectStyle(subject);

        return new(subject, title, content, deadline,
            statusText,
            checkedAlready ? $"应交 46 人 · 已交 {46 - missing} 人 · 请假/免交 2 人" : "班级 48 人 · 收交情况待核对",
            checkedAlready && missing > 0 && ShowNames ? names : "",
            statusColor,
            statusBg,
            checkedAlready ? 46 - missing : 0, missing, checkedAlready,
            sColor, sBg, statusBorder);
    }
}

public sealed record BashuHomeworkPreviewCard(
    string Subject,
    string Title,
    string Content,
    string Deadline,
    string Status,
    string Counts,
    string Names,
    string StatusColor,
    string StatusBackground,
    int Submitted,
    int Missing,
    bool Checked,
    string SubjectColor = "#007E73",
    string SubjectBackground = "#E8F5F1",
    string StatusBorder = "#C5E3D8"
)
{
    public bool HasNames => Names.Length > 0;
    public string MissingCountLabel => Missing > 0 ? $"待补交学生名单（{Missing}人）" : "待补交学生名单";
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
