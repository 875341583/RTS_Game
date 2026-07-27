using System.Threading.Tasks;
using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// P2-4: 测试装配初始化器 — 在所有测试运行前预加载硬编码数据。
/// 因为Godot原生IO在单元测试进程中不可用（AccessViolationException），
/// 所有数据驱动类需要用forceFallback=true提前初始化。
/// </summary>
public class TestAssemblyInitializer
{
    /// <summary>在测试启动时调用一次，预加载所有数据驱动类的fallback数据</summary>
    public static void EnsureFallbackDataLoaded()
    {
        TechTree.LoadFromJson(forceFallback: true);
    }
}
