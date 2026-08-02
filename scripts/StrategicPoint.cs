using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 战略要地：单位停留 4 秒可占领，占领后每秒提供 $5 被动收入。
/// 只有战斗单位（AttackDamage > 0）能占领。
/// 支持 8 阵营（teamId 0-7）动态占领与染色：仅当唯一一方战斗单位在场时推进占领，
/// 颜色取自 GameData.GetTeamColor，名称取自 FactionManager（回退 "阵营{teamId}"）。
/// </summary>
public partial class StrategicPoint : Area2D
{
    /// <summary>支持的最大阵营数（teamId 0..MaxTeams-1）。</summary>
    private const int MaxTeams = 8;

    public int OwningTeam { get; private set; } = -1; // -1 = neutral

    private Sprite2D? _visual;
    private Label? _label;
    /// <summary>各阵营当前在场战斗单位计数，索引=teamId。</summary>
    private readonly int[] _teamCounts = new int[MaxTeams];
    /// <summary>当前正在推进占领的阵营（-1=无/对峙中）。</summary>
    private int _capturingTeam = -1;
    private float _captureProgress; // 0-100
    private float _incomeTimer;
    private const float CaptureSpeed = 25f;   // 4 seconds to capture
    private const float IncomePerSecond = 5f;

    private static ImageTexture? _neutralTex;
    /// <summary>阵营色纹理缓存，key=teamId。颜色由 GameData.GetTeamColor 动态生成。</summary>
    private static readonly Dictionary<int, ImageTexture> _teamTexCache = new();

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 2; // monitor Units layer
        Monitoring = true;

        // Collision shape
        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D();
        rect.Size = new Vector2(120, 120);
        shape.Shape = rect;
        AddChild(shape);

        // Visual
        EnsureNeutralTexture();
        _visual = new Sprite2D();
        _visual!.Texture = _neutralTex;
        AddChild(_visual!);

        // Label
        _label = new Label();
        _label!.OffsetLeft = -50;
        _label!.OffsetTop = -55;
        _label!.OffsetRight = 50;
        _label!.OffsetBottom = -35;
        _label!.HorizontalAlignment = HorizontalAlignment.Center;
        _label!.Text = TrManager.Tr("strategic_point.name");
        AddChild(_label!);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;

        GameLog.Debug($"[StrategicPoint] Created at {GlobalPosition}");
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Unit u && u.AttackDamage > 0f && u.TeamId >= 0 && u.TeamId < MaxTeams)
        {
            _teamCounts[u.TeamId]++;
        }
    }

    private void OnBodyExited(Node body)
    {
        if (body is Unit u && u.AttackDamage > 0f && u.TeamId >= 0 && u.TeamId < MaxTeams)
        {
            if (_teamCounts[u.TeamId] > 0)
                _teamCounts[u.TeamId]--;
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        // 统计在场战斗阵营：仅当唯一一方有战斗单位时推进占领。
        int soleTeam = -1;
        int teamsPresent = 0;
        for (int i = 0; i < MaxTeams; i++)
        {
            if (_teamCounts[i] > 0)
            {
                teamsPresent++;
                soleTeam = i;
                if (teamsPresent > 1) break;
            }
        }

        if (teamsPresent == 1 && soleTeam != OwningTeam)
        {
            // 切换推进方时重置进度（例如从蓝切到红）
            if (_capturingTeam != soleTeam)
            {
                _capturingTeam = soleTeam;
                _captureProgress = 0f;
            }
            _captureProgress += dt * CaptureSpeed;
            if (_captureProgress >= 100f)
            {
                OwningTeam = soleTeam;
                _capturingTeam = -1;
                _captureProgress = 0f;
                _visual!.Texture = GetTeamTexture(soleTeam);
                _label!.Text = $"{GetTeamDisplayName(soleTeam)}{TrManager.Tr("strategic_point.controlled")}";
                GameLog.Debug($"[StrategicPoint] Team {soleTeam} ({GetTeamDisplayName(soleTeam)}) captured!");
            }
        }
        else if (teamsPresent == 0)
        {
            // 无人在场：进度衰减
            _captureProgress = Mathf.Max(0f, _captureProgress - dt * CaptureSpeed * 0.5f);
            if (_captureProgress <= 0f)
                _capturingTeam = -1;
        }
        else
        {
            // 多方对峙：进度暂停，等待局势明朗
            _capturingTeam = -1;
        }

        // Income（受难度开关控制）
        if (OwningTeam >= 0 && GetParent()?.GetParent() is Main main2 && main2.StrategicPointIncomeEnabled)
        {
            _incomeTimer += dt;
            if (_incomeTimer >= 1f)
            {
                _incomeTimer -= 1f;
                main2.AddResourceForTeam(OwningTeam, (int)IncomePerSecond);
            }
        }

        // Update label with capture progress
        if (_captureProgress > 0f && OwningTeam == -1 && _capturingTeam >= 0)
        {
            _label!.Text = $"{TrManager.Tr("strategic_point.capturing")} {GetTeamDisplayName(_capturingTeam)} {(int)_captureProgress}%";
        }
    }

    private static void EnsureNeutralTexture()
    {
        if (_neutralTex != null) return;
        _neutralTex = CreatePointTexture(new Color(0.8f, 0.75f, 0.3f, 0.5f), new Color(0.6f, 0.55f, 0.2f, 0.9f));
    }

    /// <summary>获取指定阵营的战略点纹理（缓存）。颜色由 GameData.GetTeamColor 动态生成。</summary>
    private static ImageTexture GetTeamTexture(int teamId)
    {
        if (_teamTexCache.TryGetValue(teamId, out var cached))
            return cached;

        var baseColor = GameData.GetTeamColor(teamId);
        var fill = new Color(baseColor.R, baseColor.G, baseColor.B, 0.5f);
        var border = new Color(
            Mathf.Max(0f, baseColor.R - 0.15f),
            Mathf.Max(0f, baseColor.G - 0.15f),
            Mathf.Max(0f, baseColor.B - 0.15f),
            0.9f);
        var tex = CreatePointTexture(fill, border);
        _teamTexCache[teamId] = tex;
        return tex;
    }

    /// <summary>
    /// 获取 teamId 的显示名。优先走 FactionManager（即 GameData 阵营体系），
    /// 不可用时回退到 "阵营{teamId}"。
    /// 注：GameData 暂未提供 GetTeamName，此处直接使用其底层的 FactionManager。
    /// </summary>
    private static string GetTeamDisplayName(int teamId)
    {
        try
        {
            if (FactionManager.IsLoaded && teamId >= 0 && teamId < FactionManager.Count)
            {
                var name = FactionManager.GetFactionForTeam(teamId).Name;
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
        }
        catch
        {
            // FactionManager 异常时降级
        }
        return $"{TrManager.Tr("common.faction")}{teamId}";
    }

    private static ImageTexture CreatePointTexture(Color fill, Color border)
    {
        var img = Image.CreateEmpty(100, 100, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
            {
                float dx = x - 50, dy = y - 50;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist < 42)
                    img.SetPixel(x, y, fill);
                else if (dist < 48)
                    img.SetPixel(x, y, border);
            }
        // Star marker
        for (float a = 0; a < Mathf.Tau; a += 0.6f)
        {
            float r = (a % 1.2f < 0.6f) ? 28 : 14;
            for (int i = 0; i < (int)r; i++)
            {
                int cx = (int)(50 + i * Mathf.Cos(a));
                int cy = (int)(50 + i * Mathf.Sin(a));
                if (cx >= 0 && cx < 100 && cy >= 0 && cy < 100)
                    img.SetPixel(cx, cy, border.Lightened(0.2f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ==================== P0-2: 存档/读档 访问器 ====================

    /// <summary>获取当前所有者阵营ID（-1=中立）。</summary>
    public int GetOwningTeam() => OwningTeam;

    /// <summary>P0-2 读档：直接设置所有者阵营并刷新视觉。支持任意 teamId。</summary>
    public void SetOwningTeam(int teamId)
    {
        OwningTeam = teamId;
        _captureProgress = 0f;
        _capturingTeam = -1;
        EnsureNeutralTexture();
        if (teamId >= 0 && teamId < MaxTeams)
        {
            _visual!.Texture = GetTeamTexture(teamId);
            _label!.Text = $"{GetTeamDisplayName(teamId)}{TrManager.Tr("strategic_point.controlled")}";
        }
        else
        {
            _visual!.Texture = _neutralTex;
            _label!.Text = TrManager.Tr("strategic_point.name");
        }
    }
}
