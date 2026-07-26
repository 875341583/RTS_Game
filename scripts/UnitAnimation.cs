using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P1-4: 单位多帧动画系统。
///
/// 设计理念：
/// - 与现有 R3 等距8方向单帧系统**兼容共存**：
///   * 如果单位有动画素材（unit_<name>_<action>_<dir>_<frame>.png）→ 用动画
///   * 如果没有 → 回退到现有单帧等距图（unit_<name>_<dir>.png）
/// - 动作状态机：Idle → Walk → Attack → Idle（Attack 为一次性，播完回 Idle）
/// - 每动作×每方向预加载所有帧到缓存
/// - 帧率独立于游戏帧率，用累计时间驱动换帧
///
/// 动画素材命名规范：
///   res://assets/sprites/units_anim/unit_<name>_<action>_<direction>_<frame>.png
///   例: unit_light_tank_walk_E_0.png, unit_light_tank_walk_E_1.png, ...
///
/// 动作定义：
///   walk  — 8帧/方向, 12 FPS, 循环
///   idle  — 6帧/方向, 6 FPS, 循环
///   attack— 6帧/方向, 15 FPS, 不循环（播完回idle）
/// </summary>
public static class UnitAnimation
{
    /// <summary>动画动作类型。</summary>
    public enum Action
    {
        /// <summary>原地待机（呼吸/微动）。</summary>
        Idle,
        /// <summary>移动中。</summary>
        Walk,
        /// <summary>攻击中（一次性，播完回Idle）。</summary>
        Attack,
    }

    /// <summary>动作配置：帧数、FPS、是否循环。</summary>
    private static readonly Dictionary<Action, (int frames, int fps, bool loop)> ActionConfig = new()
    {
        { Action.Idle,   (6, 6,  true)  },
        { Action.Walk,   (8, 12, true)  },
        { Action.Attack, (6, 15, false) },
    };

    /// <summary>8方向名称（与 IsoCoords.DirNames 一致）。</summary>
    private static readonly string[] DirNames = { "E", "SE", "S", "SW", "W", "NW", "N", "NE" };

    /// <summary>
    /// 动画帧缓存：[unitName][action][direction] = Texture2D[](frames)
    /// dirArray[direction] = Texture2D[frames], null表示该方向无素材
    /// 若某单位无动画素材，则该 unitName 不在字典中（回退到单帧系统）。
    /// </summary>
    private static readonly Dictionary<string, Dictionary<Action, Texture2D?[]?[]?>> _animCache = new();

    /// <summary>已确认无动画素材的单位名集合（避免重复尝试加载）。</summary>
    private static readonly HashSet<string> _noAnimUnits = new();

    /// <summary>是否已初始化。</summary>
    private static bool _initialized = false;

    // ====================================================================
    //                          缓存初始化
    // ====================================================================

    /// <summary>初始化动画缓存：扫描所有单位的动画素材。</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // 遍历所有 UnitType，尝试加载动画素材
        foreach (UnitType t in Enum.GetValues<UnitType>())
        {
            if (t == UnitType.Hero) continue;
            string name = Unit.GetIsoSpriteName(t);
            TryLoadUnitAnimations(name);
        }

        GD.Print($"[P1-4] 单位动画系统初始化完成: {_animCache.Count} 种兵种有动画, {_noAnimUnits.Count} 种无动画(回退单帧)");
    }

    /// <summary>尝试加载某个兵种的所有动画帧。</summary>
    private static void TryLoadUnitAnimations(string unitName)
    {
        var unitDict = new Dictionary<Action, Texture2D?[]?[]?>();
        bool anyActionLoaded = false;

        foreach (var (action, config) in ActionConfig)
        {
            var dirArray = new Texture2D?[]?[8];  // [direction] → Texture2D?[frames]
            bool anyFrameLoaded = false;

            for (int d = 0; d < 8; d++)
            {
                var dirFrames = new Texture2D?[config.frames];
                bool dirLoaded = false;
                for (int f = 0; f < config.frames; f++)
                {
                    string path = $"res://assets/sprites/units_anim/unit_{unitName}_{action.ToString().ToLower()}_{DirNames[d]}_{f}.png";
                    // 先用 ResourceLoader.Exists 检查，避免缺失文件时刷错误日志
                    if (!ResourceLoader.Exists(path))
                        continue;
                    var tex = ResourceLoader.Load<Texture2D>(path);
                    if (tex != null)
                    {
                        dirFrames[f] = tex;
                        dirLoaded = true;
                    }
                }
                dirArray[d] = dirLoaded ? dirFrames : null;
                if (dirLoaded) anyFrameLoaded = true;
            }

            if (anyFrameLoaded)
            {
                unitDict[action] = dirArray;
                anyActionLoaded = true;
            }
            else
            {
                unitDict[action] = null;
            }
        }

        if (anyActionLoaded)
        {
            _animCache[unitName] = unitDict;
        }
        else
        {
            _noAnimUnits.Add(unitName);
        }
    }

    /// <summary>检查指定兵种是否有动画素材。</summary>
    public static bool HasAnimation(string unitName)
    {
        if (!_initialized) Initialize();
        return _animCache.ContainsKey(unitName);
    }

    /// <summary>获取指定兵种某动作某方向某帧的纹理。</summary>
    public static Texture2D? GetFrame(string unitName, Action action, int direction, int frame)
    {
        if (!_animCache.TryGetValue(unitName, out var unitDict)) return null;
        if (!unitDict.TryGetValue(action, out var dirArray) || dirArray == null) return null;
        if (direction < 0 || direction >= 8) return null;
        var frames = dirArray[direction];
        if (frames == null) return null;

        // 帧索引钳制（循环动作用取模，非循环动作不越界）
        if (!ActionConfig.TryGetValue(action, out var config)) return null;
        int idx = config.loop ? frame % config.frames : Math.Min(frame, config.frames - 1);
        if (idx < 0 || idx >= frames.Length) return null;
        return frames[idx];
    }

    /// <summary>获取动作的帧数。</summary>
    public static int GetFrameCount(Action action)
    {
        return ActionConfig.TryGetValue(action, out var c) ? c.frames : 1;
    }

    /// <summary>获取动作的FPS。</summary>
    public static int GetFps(Action action)
    {
        return ActionConfig.TryGetValue(action, out var c) ? c.fps : 10;
    }

    /// <summary>动作是否循环。</summary>
    public static bool IsLooping(Action action)
    {
        return ActionConfig.TryGetValue(action, out var c) && c.loop;
    }

    /// <summary>根据移动状态和攻击状态推断当前动作。</summary>
    public static Action InferAction(bool isMoving, bool isAttacking)
    {
        if (isAttacking) return Action.Attack;
        if (isMoving) return Action.Walk;
        return Action.Idle;
    }
}

/// <summary>
/// P1-4: 单位动画播放器（每个 Unit 实例持有一个）。
/// 管理当前动作状态、方向、帧索引、累计时间。
/// </summary>
public class UnitAnimationPlayer
{
    private Unit _unit;
    private string _unitName = "";
    private bool _hasAnimation = false;

    private UnitAnimation.Action _currentAction = UnitAnimation.Action.Idle;
    private int _currentDir = -1;      // 0-7, -1=未设置
    private int _currentFrame = 0;     // 当前帧索引
    private float _frameTimer = 0f;    // 累计时间（秒）

    /// <summary>攻击动画是否播完（用于通知 Unit 回到 Idle）。</summary>
    public bool AttackFinished { get; private set; } = false;

    // 缓存上次的纹理，避免重复设置
    private Texture2D? _lastAppliedTexture = null;

    /// <summary>初始化动画播放器。</summary>
    public void Setup(Unit unit, UnitType type)
    {
        _unit = unit;
        _unitName = Unit.GetIsoSpriteName(type);
        UnitAnimation.Initialize();
        _hasAnimation = UnitAnimation.HasAnimation(_unitName);
    }

    /// <summary>是否拥有多帧动画。</summary>
    public bool HasAnimation => _hasAnimation;

    // ====================================================================
    //                          每帧更新
    // ====================================================================

    /// <summary>每帧调用：根据单位状态更新动画。
    /// 返回 true 表示已处理精灵纹理（调用方不应再设置 _body.Texture）。</summary>
    public bool Update(float dt, bool isMoving, bool isAttacking, int direction)
    {
        if (!_hasAnimation) return false;

        // 推断目标动作
        var targetAction = UnitAnimation.InferAction(isMoving, isAttacking);

        // 攻击动画播完后回到 Idle
        if (_currentAction == UnitAnimation.Action.Attack && AttackFinished)
        {
            _currentAction = UnitAnimation.Action.Idle;
            _currentFrame = 0;
            _frameTimer = 0;
            AttackFinished = false;
        }

        // 动作切换 → 重置帧
        if (targetAction != _currentAction)
        {
            _currentAction = targetAction;
            _currentFrame = 0;
            _frameTimer = 0;
            AttackFinished = false;
        }

        // 方向切换 → 保持动作但重置帧（方向变化时从头播）
        if (direction != _currentDir && direction >= 0 && direction < 8)
        {
            _currentDir = direction;
            _currentFrame = 0;
            _frameTimer = 0;
        }

        // 累计时间，推进帧
        int fps = UnitAnimation.GetFps(_currentAction);
        bool looping = UnitAnimation.IsLooping(_currentAction);
        int frameCount = UnitAnimation.GetFrameCount(_currentAction);

        _frameTimer += dt;
        float frameDuration = 1f / fps;

        while (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            _currentFrame++;

            if (_currentFrame >= frameCount)
            {
                if (looping)
                {
                    _currentFrame = 0;
                }
                else
                {
                    // 非循环动画播完
                    _currentFrame = frameCount - 1;
                    if (_currentAction == UnitAnimation.Action.Attack)
                        AttackFinished = true;
                    break;
                }
            }
        }

        // 应用纹理
        var tex = UnitAnimation.GetFrame(_unitName, _currentAction, _currentDir, _currentFrame);
        if (tex != null && tex != _lastAppliedTexture)
        {
            _unit.SetAnimationFrame(tex);
            _lastAppliedTexture = tex;
        }

        return true;
    }

    /// <summary>强制切换到攻击动作。</summary>
    public void PlayAttack(int direction)
    {
        if (!_hasAnimation) return;
        _currentAction = UnitAnimation.Action.Attack;
        _currentDir = direction;
        _currentFrame = 0;
        _frameTimer = 0;
        AttackFinished = false;
    }

    /// <summary>获取当前动作。</summary>
    public UnitAnimation.Action CurrentAction => _currentAction;

    /// <summary>获取当前帧索引。</summary>
    public int CurrentFrame => _currentFrame;
}
