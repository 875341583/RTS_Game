using System.Runtime.CompilerServices;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// P2-4: 测试装配初始化器 — 在所有测试运行前预加载硬编码数据。
/// 因为Godot原生IO在单元测试进程中不可用（AccessViolationException），
/// 所有数据驱动类需要用forceFallback=true提前初始化。
/// 
/// SetAlwaysFallback确保即使在并行测试中，数据驱动类的懒加载getter
/// 也不会尝试Godot IO（直接走硬编码fallback路径）。
/// </summary>
public class TestAssemblyInitializer
{
    /// <summary>在测试启动时调用一次，设置安全模式并预加载所有数据驱动类的fallback数据</summary>
    public static void EnsureFallbackDataLoaded()
    {
        // 防止懒加载getter触发Godot IO崩溃
        TechTree.SetAlwaysFallback(true);
        TacticalCards.SetAlwaysFallback(true);
        EraSystem.SetAlwaysFallback(true);
        SpyMission.SetAlwaysFallback(true);
        DifficultyConfig.SetAlwaysFallback(true);
        TerrainModifiers.SetAlwaysFallback(true);

        // 预加载所有fallback数据
        TechTree.LoadFromJson(forceFallback: true);
        TacticalCards.LoadFromJson(forceFallback: true);
        EraSystem.LoadFromJson(forceFallback: true);
        SpyMission.LoadFromJson(forceFallback: true);
        DifficultyConfig.LoadFromJson(forceFallback: true);
        TerrainModifiers.LoadFromJson(forceFallback: true);
    }
}
