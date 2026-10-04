using System.Globalization;
using System.Text;

namespace MemoryWidgetProvider;

internal enum MemoryLevel
{
    Normal,
    Warning,
    Critical
}

/// <summary>
/// 生成小组件中的内存占用进度条 SVG。
/// Widget 面板的 WinUI3 Adaptive Card 渲染器没有进度条控件，因此用 SVG data URI 图片绘制；
/// SVG 按声明尺寸栅格化，这里按 2 倍有效像素输出。
/// </summary>
internal static class WidgetVisuals
{
    public const int WarningThreshold = 75;
    public const int CriticalThreshold = 90;

    // 小、中尺寸卡片同宽（约 268 有效像素），小尺寸只是高度减半
    private const int CanvasWidth = 536;
    private const int ProgressHeight = 12;

    private static readonly string[] NormalGradient = ["#00B7C3", "#0078D4", "#8764B8", "#E3008C"];
    private static readonly string[] WarningGradient = ["#FFB900", "#F7630C"];
    private static readonly string[] CriticalGradient = ["#F7630C", "#E81123", "#C50F1F"];

    public static MemoryLevel GetLevel(int usedPercentage)
    {
        if (usedPercentage >= CriticalThreshold)
        {
            return MemoryLevel.Critical;
        }

        return usedPercentage >= WarningThreshold ? MemoryLevel.Warning : MemoryLevel.Normal;
    }

    public static string GetTextColor(MemoryLevel level)
    {
        return level switch
        {
            MemoryLevel.Warning => "warning",
            MemoryLevel.Critical => "attention",
            _ => "default"
        };
    }

    public static string CreateProgressBar(int usedPercentage, MemoryLevel level, bool isLightTheme)
    {
        const int width = CanvasWidth;
        var percentage = Math.Clamp(usedPercentage, 0, 100);
        var fillWidth = percentage == 0 ? 0 : Math.Max(ProgressHeight, width * percentage / 100d);
        var stops = level switch
        {
            MemoryLevel.Warning => WarningGradient,
            MemoryLevel.Critical => CriticalGradient,
            _ => NormalGradient
        };

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{ProgressHeight}\" viewBox=\"0 0 {width} {ProgressHeight}\">");
        // 正常档的色带固定铺在 0 ~ 警告阈值区间，占用多少露出多少；警告/报警档色带跟随已填充部分
        svg.Append(level == MemoryLevel.Normal
            ? string.Create(CultureInfo.InvariantCulture, $"<defs><linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"{width * WarningThreshold / 100d:0.#}\" y2=\"0\">")
            : "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\">");
        for (var i = 0; i < stops.Length; i++)
        {
            var offset = stops.Length == 1 ? 0 : (double)i / (stops.Length - 1);
            svg.Append(CultureInfo.InvariantCulture, $"<stop offset=\"{offset:0.###}\" stop-color=\"{stops[i]}\"/>");
        }

        svg.Append("</linearGradient></defs>");
        var (trackColor, trackOpacity) = isLightTheme ? ("#000000", 0.1) : ("#FFFFFF", 0.16);
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{width}\" height=\"{ProgressHeight}\" rx=\"{ProgressHeight / 2}\" fill=\"{trackColor}\" fill-opacity=\"{trackOpacity}\"/>");
        if (fillWidth > 0)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{fillWidth:0.#}\" height=\"{ProgressHeight}\" rx=\"{ProgressHeight / 2}\" fill=\"url(#g)\"/>");
        }

        svg.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }
}
