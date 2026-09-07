using System.Text.Json;
using ClassIsland.Services.Management;
using ClassIsland.Shared.Models.Profile;

static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
using var data = JsonDocument.Parse("""
[
 {"weekday":1,"subject_name":"语文","teacher_name":"张老师","starts_at":"08:00:00","ends_at":"08:40:00"},
 {"weekday":1,"subject_name":"数学","teacher_name":null,"starts_at":"08:50:00","ends_at":"09:30:00"},
 {"weekday":5,"subject_name":"英语","starts_at":"14:00:00","ends_at":"14:35:00"}
]
""");
var profile = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(new Profile()))!;
profile.TempClassPlanId = Guid.Parse("bb001122-3344-5566-7788-99aabbccdde1");
BashuScheduleMapper.Apply(profile, data.RootElement);
Check(profile.TempClassPlanId == null, "legacy temporary plan is removed");
Check(profile.ClassPlans.Count == 7, "seven distinct weekday plans");
var monday = profile.ClassPlans.Values.Single(x => x.TimeRule.WeekDay == 1);
var friday = profile.ClassPlans.Values.Single(x => x.TimeRule.WeekDay == 5);
Check(monday.Classes.Count == 2 && friday.Classes.Count == 1, "Monday and Friday are not copies");
Check(profile.TimeLayouts[friday.TimeLayoutId].Layouts.Last().EndTime == TimeSpan.FromMinutes(875), "custom Friday end time preserved");
Check(profile.TimeLayouts[monday.TimeLayoutId].Layouts.Count == 3, "break does not offset subject indices");
Check(profile.Subjects[monday.Classes[0].SubjectId].TeacherName == "张老师", "teacher identity preserved");
var subjects = profile.Subjects.Count;
BashuScheduleMapper.Apply(profile, data.RootElement);
Check(profile.Subjects.Count == subjects && profile.ClassPlans.Count == 7, "repeat sync is idempotent");
using var empty = JsonDocument.Parse("[]");
BashuScheduleMapper.Apply(profile, empty.RootElement);
Check(profile.ClassPlans.Values.All(x => x.Classes.Count == 0), "empty schedule clears previous lessons");
var shortDuration = BashuNotificationTiming.Duration("请准备课本", "张老师", 1);
var longDuration = BashuNotificationTiming.Duration(new string('课', 300), "张老师", 1);
Check(longDuration > shortDuration && longDuration.TotalSeconds > 100, "long notification gets enough reading time");
Check(BashuNotificationTiming.Duration("请准备课本", "张老师", 3) == shortDuration * 3, "repeat count scales scrolling duration");

// Weather alert double-assurance smoke tests
var orangeForestFireAlert = new ClassIsland.Core.Models.Weather.WeatherAlert
{
    Type = "森林（草原）火险",
    Level = "橙色",
    Title = "重庆市发布森林（草原）火险橙色预警",
    Images = new Dictionary<string, string>
    {
        { "icon", "http://f5.market.xiaomi.com/download/Weather/06db501333e6d4075a3364a66cdf23ba5733111b3/a.webp" }
    }
};
Check(orangeForestFireAlert.AlertColorHex == "#FB8C00", "forest fire orange alert color hex is correct");
Check(orangeForestFireAlert.AlertLucideGlyph == "\ue0d6", "forest fire lucide glyph is flame");
Check(orangeForestFireAlert.SafeIconSource == "avares://ClassIsland/Assets/WeatherAlerts/alert_default_orange.png", "orange default icon redirects to local asset");
Check(orangeForestFireAlert.IsDefaultIcon == 1, "forest fire is identified as default icon capsule");

var heatAlert = new ClassIsland.Core.Models.Weather.WeatherAlert
{
    Type = "高温",
    Level = "红色",
    Title = "高温红色预警",
    Images = new Dictionary<string, string>
    {
        { "icon", "http://f3.market.mi-img.com/download/Weather/05e4cfab2c2164836a8025ab9f539173f58ac05d0/a.webp" }
    }
};
Check(heatAlert.AlertColorHex == "#E53935", "heat red alert color hex is red");
Check(heatAlert.AlertLucideGlyph == "\ue188", "heat lucide glyph is thermometer-sun");
Check(heatAlert.SafeIconSource == "avares://ClassIsland/Assets/WeatherAlerts/alert_heat.png", "heat icon redirects to local asset");

var emptyIconAlert = new ClassIsland.Core.Models.Weather.WeatherAlert
{
    Type = "雷电",
    Level = "黄色"
};
Check(emptyIconAlert.IsDefaultIcon == 1, "empty icon alert safely defaults to 1 without throwing");
Check(emptyIconAlert.AlertColorHex == "#FBC02D", "yellow thunder alert color is yellow");
Check(emptyIconAlert.AlertLucideGlyph == "\ue1e9", "thunder lucide glyph is zap");
Check(emptyIconAlert.SafeIconSource == "avares://ClassIsland/Assets/WeatherAlerts/alert_default_yellow.png", "empty icon resolves to level default");

var httpCustomAlert = new ClassIsland.Core.Models.Weather.WeatherAlert
{
    Type = "暴雨",
    Level = "蓝色",
    Images = new Dictionary<string, string>
    {
        { "icon", "http://example.com/custom/rain.png" }
    }
};
Check(httpCustomAlert.SafeIconSource == "https://example.com/custom/rain.png", "http custom icon is upgraded to https");
Check(httpCustomAlert.AlertLucideGlyph == "\ue092", "rain lucide glyph is cloud-rain");
