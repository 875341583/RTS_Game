using System;

namespace RTSGame;

/// <summary>
/// 确定性随机数生成器 — 解决联机模式下GD.Randf/GD.RandRange导致的不同步问题。
/// Host使用种子驱动的System.Random生成随机数，客户端不调用（由Host权威逻辑驱动）。
/// 客户端跳过的逻辑中如果包含随机数，客户端不需要执行（isClientLogic会跳过）。
/// 纯视觉特效（非游戏逻辑）中的随机数仍可用GD.Randf，因为不影响游戏状态。
/// </summary>
public static class DeterministicRng
{
    private static System.Random? _shared;

    /// <summary>用种子初始化RNG（游戏开局时由Host调用，种子通过StartGame同步给客户端）。</summary>
    public static void Initialize(ulong seed)
    {
        _shared = new System.Random((int)(seed & 0x7FFFFFFF));
    }

    /// <summary>获取RNG实例，如果未初始化则用默认种子。</summary>
    private static System.Random Rng => _shared ??= new System.Random();

    /// <summary>返回 [0, 1) 的随机浮点数（等价于GD.Randf）。</summary>
    public static float Randf() => (float)Rng.NextDouble();

    /// <summary>返回 [min, max] 的随机整数（等价于GD.RandRange(min, max)）。</summary>
    public static int RandRangeInt(int min, int max)
    {
        if (max < min) return min;
        return Rng.Next(min, max + 1);
    }

    /// <summary>返回 [min, max] 的随机浮点数（等价于GD.RandRange(min, max)返回double）。</summary>
    public static float RandRangeFloat(float min, float max)
    {
        return min + (float)Rng.NextDouble() * (max - min);
    }

    /// <summary>返回 [0, max] 的随机整数（兼容GD.RandRange(0, n-1)调用模式）。</summary>
    public static int RandRange(int min, int max) => RandRangeInt(min, max);

    /// <summary>从列表中随机选取一个元素。</summary>
    public static T Choice<T>(System.Collections.Generic.IList<T> list)
    {
        if (list == null || list.Count == 0)
            throw new ArgumentException("list is null or empty");
        return list[RandRangeInt(0, list.Count - 1)];
    }
}
