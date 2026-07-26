using System.Collections.Generic;
using Godot;

namespace RTSGame;

// ============================================================================
// P1-5 第2步：核心接口层
// 基于红警2 Y-Sort + OpenRA IRenderable 智慧，定义 2D/3D 统一行为契约。
// 当前阶段（第2步）仅定义接口并让 2D 的 Unit/Building/PathFinder 声明实现，
// 不改变任何现有方法实现。3D 的 Unit3D/Building3D/NavigationRegion3D 将在第5步适配。
// ============================================================================

/// <summary>
/// 渲染对象标准接口。对应红警2 "一切可见物皆参与Y-Sort" + OpenRA IRenderable 抽象。
/// 任何可被绘制、可被选中、可隐藏的实体（单位/建筑/特效）都应实现此接口。
/// </summary>
public interface IRenderable
{
    /// <summary>实体当前世界坐标（2D 为屏幕等距坐标，3D 适配时投影到 XZ 平面）。</summary>
    Vector2 GetPosition();

    /// <summary>Y-Sort 排序键：脚底纵坐标（等距Y）。值越大越靠前绘制。红警2画家算法核心。</summary>
    float GetSortY();

    /// <summary>设置选择框/高亮显示状态。</summary>
    void SetSelected(bool selected);

    /// <summary>是否已死亡/被摧毁，应从渲染队列移除。</summary>
    bool IsDead { get; }
}

/// <summary>
/// 单位行为接口。提取 2D Unit.cs / 3D Unit3D.cs 的公开命令与状态契约。
/// 坐标统一用 Vector2（3D 第5步适配时做 Vector3↔Vector2 转换）。
/// </summary>
public interface IUnitEntity : IRenderable
{
    /// <summary>单位类型。</summary>
    UnitType Type { get; }

    /// <summary>所属阵营ID。</summary>
    int TeamId { get; set; }

    /// <summary>最大生命值。</summary>
    float MaxHealth { get; }

    /// <summary>当前生命值。</summary>
    float Health { get; }

    /// <summary>下达移动命令（2D Vector2 世界坐标）。</summary>
    void CommandMove(Vector2 target);

    /// <summary>下达攻击移动命令（到指定位置，遇敌自动攻击）。</summary>
    void CommandAttackMove(Vector2 target);

    /// <summary>停止当前所有命令。</summary>
    void CommandStop();

    /// <summary>承受伤害。</summary>
    void TakeDamage(float damage);

    /// <summary>治疗/修复（维修厂/工程师）。</summary>
    void Heal(float amount);

    /// <summary>是否正在移动。</summary>
    bool HasMoveTarget();
}

/// <summary>
/// 建筑行为接口。提取 2D Building.cs / 3D Building3D.cs 的公开命令与状态契约。
/// </summary>
public interface IBuildingEntity : IRenderable
{
    /// <summary>建筑类型。</summary>
    BuildingType Type { get; }

    /// <summary>所属阵营ID。</summary>
    int TeamId { get; set; }

    /// <summary>最大生命值。</summary>
    float MaxHealth { get; }

    /// <summary>当前生命值。</summary>
    float Health { get; }

    /// <summary>是否提供电力（>0）。</summary>
    int PowerProvided { get; }

    /// <summary>是否消耗电力。</summary>
    int PowerConsumed { get; }

    /// <summary>是否为防御建筑（自动攻击敌方单位）。</summary>
    bool IsDefensive { get; }

    /// <summary>是否为维修厂。</summary>
    bool IsRepairStation { get; }

    /// <summary>加入生产队列。</summary>
    void EnqueueProduction(ProductionType type, int count = 1);

    /// <summary>承受伤害。</summary>
    void TakeDamage(float damage);

    /// <summary>是否正在生产。</summary>
    bool IsProducing { get; }

    /// <summary>是否有足电力运营（当前血量>0 且 非低电状态）。</summary>
    bool IsOperational();
}

/// <summary>
/// 寻路接口。统一 2D 栅格 A* (PathFinder) 与 3D NavigationRegion3D 的路径查询契约。
/// 坐标用 Vector2（3D 适配时取 XZ 投影）。障碍管理方法保留2D特有，
/// 3D 若需实现 IPathFinder 可抛 NotSupportedException 或用适配器包装。
/// </summary>
public interface IPathFinder
{
    /// <summary>计算从起点到终点的路径点列表（世界坐标）。返回空表表示不可达。</summary>
    List<Vector2> FindPath(Vector2 startWorld, Vector2 endWorld);

    /// <summary>指定世界坐标是否可通行。</summary>
    bool IsWalkable(Vector2 worldPos);

    /// <summary>标记建筑占地区域为障碍（中心格 + radius 范围）。</summary>
    void AddBuilding(int centerGx, int centerGy, int radius);

    /// <summary>取消建筑障碍标记（引用计数安全）。</summary>
    void RemoveBuilding(int centerGx, int centerGy, int radius);
}
