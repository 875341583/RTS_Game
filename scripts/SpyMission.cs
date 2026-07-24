using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G7: 间谍深化 — 文明6风格间谍任务系统
///
/// 间谍（UnitType.Spy）可以执行5种任务，选中间谍右键敌方建筑触发：
/// - 窃取科技：渗透科技中心，窃取敌方已研究科技
/// - 破坏电网：渗透电站，使其断电8秒
/// - 窃取资金：渗透基地，偷取$500
/// - 瘫痪生产：渗透兵营/车厂，暂停生产10秒
/// - 侦察：渗透任意建筑，揭示敌方信息
///
/// 机制：
/// - 渗透需4秒倒计时，期间间谍不可移动
/// - 成功率80%，失败间谍死亡(20%)
/// - N键查看间谍任务面板
/// </summary>
public static class SpyMission
{
    /// <summary>间谍任务类型。</summary>
    public enum MissionType
    {
        StealTech,      // 窃取科技 — 科技中心
        SabotagePower,  // 破坏电网 — 电站
        StealMoney,     // 窃取资金 — 基地
        SabotageProd,   // 瘫痪生产 — 兵营/车厂
        Recon,          // 侦察 — 任意建筑
    }

    /// <summary>任务成功率（0.8 = 80%）。</summary>
    public const float SuccessRate = 0.80f;
    /// <summary>渗透倒计时（秒）。</summary>
    public const float InfiltrateTime = 4f;
    /// <summary>破坏电网持续秒数。</summary>
    public const float SabotagePowerDuration = 8f;
    /// <summary>瘫痪生产持续秒数。</summary>
    public const float SabotageProdDuration = 10f;
    /// <summary>窃取资金量。</summary>
    public const int StealMoneyAmount = 500;

    /// <summary>根据目标建筑类型自动选择最优任务。</summary>
    public static MissionType ChooseMission(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.TechCenter => MissionType.StealTech,
            BuildingType.PowerPlant => MissionType.SabotagePower,
            BuildingType.Base => MissionType.StealMoney,
            BuildingType.Barracks => MissionType.SabotageProd,
            BuildingType.WarFactory => MissionType.SabotageProd,
            _ => MissionType.Recon,
        };
    }

    /// <summary>获取任务的中文名称。</summary>
    public static string MissionName(MissionType type) => type switch
    {
        MissionType.StealTech => "窃取科技",
        MissionType.SabotagePower => "破坏电网",
        MissionType.StealMoney => "窃取资金",
        MissionType.SabotageProd => "瘫痪生产",
        MissionType.Recon => "侦察",
        _ => "未知",
    };

    /// <summary>获取任务描述。</summary>
    public static string MissionDesc(MissionType type) => type switch
    {
        MissionType.StealTech => "窃取1个敌方已研究科技(免费完成)",
        MissionType.SabotagePower => $"使电站断电{(int)SabotagePowerDuration}秒",
        MissionType.StealMoney => $"偷取${StealMoneyAmount}",
        MissionType.SabotageProd => $"暂停生产{(int)SabotageProdDuration}秒",
        MissionType.Recon => "揭示敌方建筑/单位信息5秒",
        _ => "",
    };
}
