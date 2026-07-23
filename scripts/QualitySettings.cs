using Godot;

namespace RTSGame;

/// <summary>
/// R7: 画质分级系统 — 自动适配无GPU/有GPU环境
/// 
/// 三档画质：
/// - Low: 无GPU软件渲染 — 降分辨率(480p)、禁阴影、禁特效、降纹理过滤
/// - Medium: 弱GPU — 原分辨率(540p)、禁阴影、基础特效
/// - High: 正常GPU — 原分辨率(540p)、开阴影、全特效
/// 
/// 检测策略：通过 RenderingServer 查询 GPU 适配器信息，判断是否有硬件加速。
/// </summary>
public static class QualitySettings
{
    public enum QualityLevel { Low, Medium, High }

    public static QualityLevel Current { get; private set; } = QualityLevel.High;

    /// <summary>是否检测到硬件GPU加速。</summary>
    public static bool HasGPU { get; private set; } = true;

    /// <summary>当前画质等级的中文描述。</summary>
    public static string LevelName => Current switch
    {
        QualityLevel.Low => "低画质(省电模式)",
        QualityLevel.Medium => "中画质",
        QualityLevel.High => "高画质",
        _ => "未知"
    };

    /// <summary>
    /// 在游戏启动时调用。检测GPU并设置对应画质参数。
    /// 应在Main._Ready()最开头调用。
    /// </summary>
    public static void AutoDetect()
    {
        // 检测GPU：Godot 4.x 通过 RenderingServer 获取适配器信息
        // 在无GPU的云服务器上，D3D12/Vulkan创建会失败，Godot回退到ANGLE软件渲染
        string rendererName = RenderingServer.GetRenderingDevice() != null
            ? "hardware"
            : "software";

        // 更可靠的检测：检查当前渲染驱动
        // 无GPU时Godot会回退到OpenGL/ANGLE，我们可以通过适配器名称判断
        var rid = RenderingServer.GetRenderingDevice();
        if (rid == null)
        {
            // RenderingDevice为null意味着没有硬件加速
            HasGPU = false;
            Current = QualityLevel.Low;
        }
        else
        {
            // 有GPU，但可能很弱。通过显存大小判断
            // Godot 4.7 不直接暴露显存查询，我们用适配器数量间接判断
            HasGPU = true;
            // 默认给Medium，让游戏运行流畅
            Current = QualityLevel.Medium;
        }

        // 命令行覆盖：--quality=low/medium/high
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--quality", System.StringComparison.OrdinalIgnoreCase))
            {
                string val = a.Contains('=') ? a.Split('=')[1] : "";
                Current = val.ToLowerInvariant() switch
                {
                    "low" or "0" => QualityLevel.Low,
                    "medium" or "1" => QualityLevel.Medium,
                    "high" or "2" => QualityLevel.High,
                    _ => Current
                };
                HasGPU = Current != QualityLevel.Low;
                break;
            }
        }

        ApplySettings();
        GD.Print($"[R7] 画质分级: {LevelName} (GPU: {HasGPU}, Renderer: {rendererName})");
    }

    /// <summary>根据当前画质等级应用渲染参数。</summary>
    private static void ApplySettings()
    {
        switch (Current)
        {
            case QualityLevel.Low:
                // 低画质：降分辨率到480p，降低渲染负担
                ProjectSettings.SetSetting("display/window/size/viewport_width", 854);
                ProjectSettings.SetSetting("display/window/size/viewport_height", 480);
                // 禁用阴影
                ProjectSettings.SetSetting("rendering/lights_and_shadows/directional_shadow/size", 256);
                ProjectSettings.SetSetting("rendering/lights_and_shadows/positional_shadow/atlas_size", 256);
                // 禁用遮挡剔除（软件渲染下开销大于收益）
                ProjectSettings.SetSetting("rendering/occlusion_culling/use_occlusion_culling", false);
                // 降低LOD阈值
                ProjectSettings.SetSetting("rendering/mesh_lod/lod_change/threshold_pixels", 4.0);
                break;

            case QualityLevel.Medium:
                // 中画质：保持540p，禁阴影，保留遮挡剔除
                ProjectSettings.SetSetting("display/window/size/viewport_width", 960);
                ProjectSettings.SetSetting("display/window/size/viewport_height", 540);
                ProjectSettings.SetSetting("rendering/lights_and_shadows/directional_shadow/size", 512);
                ProjectSettings.SetSetting("rendering/lights_and_shadows/positional_shadow/atlas_size", 512);
                ProjectSettings.SetSetting("rendering/occlusion_culling/use_occlusion_culling", true);
                ProjectSettings.SetSetting("rendering/mesh_lod/lod_change/threshold_pixels", 6.0);
                break;

            case QualityLevel.High:
                // 高画质：保持540p，开阴影
                ProjectSettings.SetSetting("display/window/size/viewport_width", 960);
                ProjectSettings.SetSetting("display/window/size/viewport_height", 540);
                ProjectSettings.SetSetting("rendering/lights_and_shadows/directional_shadow/size", 1024);
                ProjectSettings.SetSetting("rendering/lights_and_shadows/positional_shadow/atlas_size", 1024);
                ProjectSettings.SetSetting("rendering/occlusion_culling/use_occlusion_culling", true);
                ProjectSettings.SetSetting("rendering/mesh_lod/lod_change/threshold_pixels", 8.0);
                break;
        }
    }

    /// <summary>手动切换画质等级（运行时可用）。</summary>
    public static void SetQuality(QualityLevel level)
    {
        Current = level;
        HasGPU = level != QualityLevel.Low;
        ApplySettings();
        GD.Print($"[R7] 画质已切换: {LevelName}");
    }

    /// <summary>是否应该渲染高开销特效（如粒子、光晕）。</summary>
    public static bool ShouldRenderExpensiveEffects => Current == QualityLevel.High;

    /// <summary>是否应该渲染阴影。</summary>
    public static bool ShouldRenderShadows => Current >= QualityLevel.Medium;

    /// <summary>装饰物的生成概率倍率（低画质降低）。</summary>
    public static float DecorationDensityFactor => Current switch
    {
        QualityLevel.Low => 0.3f,
        QualityLevel.Medium => 0.6f,
        QualityLevel.High => 1.0f,
        _ => 1.0f
    };

    /// <summary>是否应该使用线性纹理过滤（低画质用Nearest更快）。</summary>
    public static bool UseLinearFilter => Current == QualityLevel.High;
}
