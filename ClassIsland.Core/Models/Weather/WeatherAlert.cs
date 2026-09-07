using System.Text.Json.Serialization;
using Avalonia.Media;

namespace ClassIsland.Core.Models.Weather;

public class WeatherAlert
{
    [JsonPropertyName("locationKey")] public string LocationKey { get; set; } = "";
    [JsonPropertyName("alertId")] public string AlertId { get; set; } = "";

    [JsonPropertyName("pubTime")] public DateTime PubTime { get; set; } = DateTime.Now;
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    [JsonPropertyName("images")] public Dictionary<string, string> Images { get; set; } = new();

    [JsonIgnore]
    public int IsDefaultIcon
    {
        get
        {
            if (Images == null || !Images.TryGetValue("icon", out var icon) || string.IsNullOrWhiteSpace(icon))
            {
                return 1;
            }

            var clean = icon.Trim();
            return clean.Contains("0ac110d2ee20a454ab44f5df30f9fa6ff650e0b72", StringComparison.OrdinalIgnoreCase) // 蓝色预警默认图标
                || clean.Contains("072013febeb1944da85649e5e547ec5a8284816a2", StringComparison.OrdinalIgnoreCase) // 黄色预警默认图标
                || clean.Contains("06db501333e6d4075a3364a66cdf23ba5733111b3", StringComparison.OrdinalIgnoreCase) // 橙色预警默认图标
                || clean.Contains("03e3e096d3d9e485fa33bbf833fc3b3c96c23d014", StringComparison.OrdinalIgnoreCase) // 红色预警默认图标
                ? 1 : 0;
        }
    }

    [JsonIgnore]
    public string SafeIconSource
    {
        get
        {
            if (Images != null && Images.TryGetValue("icon", out var icon) && !string.IsNullOrWhiteSpace(icon))
            {
                var clean = icon.Trim();

                // 官方预警图片特征码映射到本地内置资源，0延迟0流量且绝对免拦截
                if (clean.Contains("0ac110d2ee20a454ab44f5df30f9fa6ff650e0b72", StringComparison.OrdinalIgnoreCase))
                    return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_blue.png";
                if (clean.Contains("072013febeb1944da85649e5e547ec5a8284816a2", StringComparison.OrdinalIgnoreCase))
                    return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_yellow.png";
                if (clean.Contains("06db501333e6d4075a3364a66cdf23ba5733111b3", StringComparison.OrdinalIgnoreCase))
                    return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_orange.png";
                if (clean.Contains("03e3e096d3d9e485fa33bbf833fc3b3c96c23d014", StringComparison.OrdinalIgnoreCase))
                    return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_red.png";
                if (clean.Contains("05e4cfab2c2164836a8025ab9f539173f58ac05d0", StringComparison.OrdinalIgnoreCase))
                    return "avares://ClassIsland/Assets/WeatherAlerts/alert_heat.png";

                // 若是第三方未知的远程图片链接，将明文 http 转换为 https
                if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    return "https://" + clean.Substring(7);
                }

                return clean;
            }

            return ResolveBuiltinIconByLevel();
        }
    }

    [JsonIgnore]
    public string AlertColorHex
    {
        get
        {
            var text = (Level ?? "") + (Title ?? "");
            if (text.Contains('红')) return "#E53935"; // 红色预警
            if (text.Contains('橙')) return "#FB8C00"; // 橙色预警
            if (text.Contains('黄')) return "#FBC02D"; // 黄色预警
            if (text.Contains('蓝')) return "#1E88E5"; // 蓝色预警
            return "#FB8C00"; // 默认橙色警示
        }
    }

    [JsonIgnore]
    public SolidColorBrush AlertColorBrush => new(Color.Parse(AlertColorHex));

    [JsonIgnore]
    public string AlertLucideGlyph
    {
        get
        {
            var target = ((Type ?? "") + (Title ?? "")).ToLowerInvariant();
            if (target.Contains("火") || target.Contains("林") || target.Contains("草"))
                return "\ue0d6"; // flame 火焰
            if (target.Contains("高温") || target.Contains("热") || target.Contains("暑"))
                return "\ue188"; // thermometer-sun 高温
            if (target.Contains("暴雨") || target.Contains("雨") || target.Contains("降水"))
                return "\ue092"; // cloud-rain 降雨
            if (target.Contains("雷") || target.Contains("闪电"))
                return "\ue1e9"; // zap 闪电
            if (target.Contains("风") || target.Contains("台风"))
                return "\ue1cd"; // wind 大风
            if (target.Contains("雪") || target.Contains("结冰") || target.Contains("寒潮") || target.Contains("霜冻") || target.Contains("低温"))
                return "\ue166"; // snowflake 雪花/结冰
            if (target.Contains("雾"))
                return "\ue08c"; // cloud-fog 大雾
            if (target.Contains("霾") || target.Contains("沙") || target.Contains("尘"))
                return "\ue0f4"; // haze 霾/沙尘
            if (target.Contains("雹"))
                return "\ue090"; // cloud-lightning 冰雹
            return "\ue193"; // triangle-alert 警报
        }
    }

    private string ResolveBuiltinIconByLevel()
    {
        var text = (Level ?? "") + (Title ?? "");
        if (text.Contains('红')) return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_red.png";
        if (text.Contains('橙')) return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_orange.png";
        if (text.Contains('黄')) return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_yellow.png";
        if (text.Contains('蓝')) return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_blue.png";
        return "avares://ClassIsland/Assets/WeatherAlerts/alert_default_orange.png";
    }
}