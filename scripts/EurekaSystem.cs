using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G5: 尤里卡时刻 — 文明6风格科技跳跃
/// 
/// 特定游戏事件触发"尤里卡"，即时获得科技或加速研究：
/// - 击杀N个敌方单位 → 军事分支随机科技尤里卡
/// - 采集N资金 → 经济分支随机科技尤里卡
/// - 建造N个建筑 → 防御分支随机科技尤里卡
/// - 击毁敌方建筑 → 随机分支尤里卡
/// 
/// 尤里卡效果：如果该科技未研究则直接完成；如果正在研究则立即完成；
/// 如果已完成则获得资金补偿。
/// 
/// 按H键查看尤里卡进度。
/// </summary>
public class EurekaSystem
{
    // ===== 尤里卡触发阈值 =====
    public const int KillThreshold = 5;         // 击杀5个敌方单位
    public const int MoneyThreshold = 2000;     // 累计采集2000资金
    public const int BuildThreshold = 3;        // 建造3个建筑
    public const int DestroyThreshold = 2;      // 击毁2个敌方建筑

    // ===== 尤里卡跟踪 =====
    public class TeamEureka
    {
        public int KillCounter;
        public int MoneyAccumulated;
        public int BuildCounter;
        public int DestroyCounter;

        /// <summary>记录击杀，返回是否触发尤里卡。</summary>
        public bool OnKill()
        {
            KillCounter++;
            if (KillCounter >= KillThreshold) { KillCounter = 0; return true; }
            return false;
        }

        /// <summary>记录资金采集，返回触发次数。</summary>
        public int OnMoneyGained(int amount)
        {
            MoneyAccumulated += amount;
            int triggers = 0;
            while (MoneyAccumulated >= MoneyThreshold)
            {
                MoneyAccumulated -= MoneyThreshold;
                triggers++;
            }
            return triggers;
        }

        /// <summary>记录建造，返回是否触发尤里卡。</summary>
        public bool OnBuild()
        {
            BuildCounter++;
            if (BuildCounter >= BuildThreshold) { BuildCounter = 0; return true; }
            return false;
        }

        /// <summary>记录击毁建筑，返回是否触发尤里卡。</summary>
        public bool OnDestroy()
        {
            DestroyCounter++;
            if (DestroyCounter >= DestroyThreshold) { DestroyCounter = 0; return true; }
            return false;
        }
    }

    /// <summary>获取某分支中第一个未研究的科技。</summary>
    public static TechTree.TechId? GetUnresearchedInBranch(
        HashSet<TechTree.TechId> completed, TechTree.TechId? currentlyResearching, string branch)
    {
        for (int tier = 1; tier <= 4; tier++)
        {
            var node = TechTree.GetByBranchTier(branch, tier);
            if (node == null) continue;
            if (!completed.Contains(node.Id) && node.Id != currentlyResearching)
            {
                // 检查前置是否满足
                bool prereqOk = true;
                foreach (var pre in node.Prerequisites)
                    if (!completed.Contains(pre)) { prereqOk = false; break; }
                if (prereqOk) return node.Id;
            }
        }
        return null;
    }
}
