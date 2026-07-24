using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G8: 占领强化 — 文明6风格占领战略收益
///
/// 占领敌方建筑后获得额外战略收益：
/// - 占领即获资源：占领瞬间获得$300
/// - 缴获加速：被占领建筑首次生产速度+30%（持续60秒）
/// - 连锁占领：占领建筑80px内友方工程师占领速度+50%
/// - 叛变风险：被占领建筑30秒内有15%概率叛变回原阵营
/// - K键查看占领面板
/// </summary>
public static class CaptureBonus
{
    /// <summary>占领瞬间获得的资金。</summary>
    public const int CaptureMoneyReward = 300;
    /// <summary>缴获生产加速倍数（1.3=+30%）。</summary>
    public const float CapturedProduceSpeedMul = 1.3f;
    /// <summary>缴获加速持续时间（秒）。</summary>
    public const float CapturedProduceDuration = 60f;
    /// <summary>连锁占领范围（px）。</summary>
    public const float ChainRange = 80f;
    /// <summary>连锁占领速度加成（1.5=+50%）。</summary>
    public const float ChainCaptureSpeedMul = 1.5f;
    /// <summary>叛变风险持续时间（秒）。</summary>
    public const float DefectionRiskDuration = 30f;
    /// <summary>叛变概率（0.15=15%）。</summary>
    public const float DefectionChance = 0.15f;
}
