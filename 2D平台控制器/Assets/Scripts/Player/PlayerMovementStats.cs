using System.IO;
using UnityEngine;

[CreateAssetMenu(menuName = "Player Movement")]
public class PlayerMovementStats : ScriptableObject
{
    [Header("移动")]
    [Range(0, 1f)] public float MoveThreshold = 0.25f; //移动参数阈值
    [Range(1f, 100f)] public float MaxWalkSpeed = 12.5f; //行走状态最大水平速度
    [Range(0.25f, 50f)] public float GroundAcceleration = 5f; //地面加速倍率
    [Range(0.25f, 50f)] public float GroundDeceleration = 5f; //地面减速倍率
    [Range(0.25f, 50f)] public float AirAcceleration = 5f; //空中加速力度
    [Range(0.25f, 50f)] public float AirDeceleration = 5f; //空中减速力度
    [Range(0.25f, 50f)] public float WallJumpMoveAcceleration = 5f; //蹬墙跳上升力度
    [Range(0.25f, 50f)] public float WallJumpMoveDeceleration = 5f; //蹬墙跳下降力度

    [Header("奔跑")]
    [Range(1f, 100f)] public float MaxRunSpeed = 20f; //按住奔跑键时的最大速度上限

    [Header("跳跃")]
    [Range(1f, 100f)] public float JumpHeight = 6.5f;                  // 标准跳跃最高高度
    [Range(1f, 1.1f)] public float JumpHeightCompensationFactor = 1.054f; // 松开跳跃键时高度补偿系数，轻微加高跳跃峰值
    public float TimeTillJumpApex = 0.35f;                             // 从起跳到达最高点的时间
    [Range(0.01f, 5f)] public float GravityOnReleaseMultiplier = 2f;   // 松开跳跃键后重力倍增系数，实现短跳效果
    public float MaxFallSpeed = 26f;                                   // 下落最大速度上限，防止下坠过快
    [Range(1, 5)] public int NumberOfJumpsAllowed = 2;                 // 允许连续跳跃次数（2=二段跳）
    public bool ResetJumpsOnWallSlide = true; //是否允许贴墙时重置跳跃次数

    [Header("跳切")]
    [Range(0.02f, 0.3f)] public float TimeForUpwardsCancel = 0.027f;   // 起跳后一小段时间内松开跳跃键可立刻切断上升，实现小跳

    [Header("跳转")]
    [Range(0.5f, 1f)] public float ApexThreshold = 0.97f;              // 到达跳跃顶点的速度阈值（接近0即判定为顶点）
    [Range(0.01f, 1f)] public float ApexHangTime = 0.075f;             // 顶点滞空时间，最高点轻微悬浮，手感更柔和

    [Header("跳跃缓冲")]
    [Range(0f, 1f)] public float JumpBufferTime = 0.125f;              // 预输入缓冲：落地前提前按跳，落地自动触发跳跃

    [Header("跳跃土狼时间")]
    [Range(0f, 1f)] public float JumpCoyoteTime = 0.1f;                // 土狼时间：离开平台后一小段时间内仍可起跳
    
    [Header("墙壁滑行")]
    [Min(0.01f)] public float WallSlideSpeed = 5f; //贴墙壁时的速度
    [Range(0.25f, 50f)] public float WallSlideDecelerationSpeed = 50f; //墙壁滑行下落速度

    [Header("蹬墙跳")]
    public Vector2 WallJumpDirection = new Vector2(-20f, 6.5f); //跳跃方向向量
    [Range(0f, 1f)] public float WallJumpPostBufferTime = 0.125f; // 蹬墙跳缓冲时间
    [Range(0.01f, 5f)] public float WallJumpGravityOnReleaseMultiplier = 1f; //蹬墙跳重力参数

    [Header("冲刺配置")]
    [Range(0f, 1f)] public float DashTime = 0.11f;               // 单次冲刺持续时长
    [Range(1f, 200f)] public float DashSpeed = 40f;             // 冲刺瞬间最大速度
    [Range(0f, 1f)] public float TimeBtwDashesOnGround = 0.225f;// 地面连续冲刺的冷却间隔
    public bool ResetDashOnWallSlide = true;                     // 贴墙滑墙时是否重置冲刺次数
    [Range(0, 5)] public int NumberOfDashes = 2;                // 空中/地面可连续冲刺最大次数
    [Range(0f, 0.5f)] public float DashDiagonallyBias = 0.4f;   // 斜向冲刺竖直方向力度偏移系数

    [Header("冲刺取消参数")]
    [Range(0.01f, 5f)] public float DashGravityOnReleaseMultiplier = 1f; // 松开冲刺后重力倍增系数
    [Range(0.02f, 0.3f)] public float DashTimeForUpwardsCancel = 0.027f; // 冲刺上升阶段可切断向上速度的窗口期

    [Header("跳跃可视化工具")]
    public bool ShowWalkJumpArc = false;                               // 是否绘制行走状态跳跃轨迹预览线
    public bool ShowRunJumpArc = false;                                 // 是否绘制奔跑状态跳跃轨迹预览线
    public bool StopOnCollision = true;                                // 轨迹碰到地面/墙体时停止继续绘制
    public bool DrawRight = true;                                      // 预览轨迹默认向右绘制（方便观察）
    [Range(5, 100)] public int ArcResolution = 20;                     // 轨迹曲线分段精度，数值越高线条越平滑
    [Range(0, 500)] public int VisualizationSteps = 90;                // 轨迹模拟步数，控制预览线显示的总长度

    [Header("地面/墙体检测")]
    public LayerMask GroundLayer; //判定地面的层级（地面/平台层）
    public float GroundDetectionRayLength = 0.02f; //脚底地面检测射线长度
    public float HeadDetectionRayLength = 0.02f; //头顶天花板检测射线长度
    [Range(0f, 1f)] public float HeadWidth = 0.75f; //头顶射线左右横向宽度比例
    public float WallDetectionRayLength = 0.125f; //墙壁检测射线长度
    [Range(0.01f, 2f)]public float WallDetectionRayHeightMultiplier = 0.9f; //墙壁检测区域范围

    public bool DebugShowIsGroundedBox; //是否可视化检测地面碰撞
    public bool DebugShowHeadBumpBox; //是否可视化检测头顶碰撞
    public bool DebugShowWallHitBox; //是否可视化墙面检测区域

    public readonly Vector2[] DashDirections = new Vector2[]
    {
        new Vector2(0, 0),    // 无输入
        new Vector2(1, 0),    // 向右
        new Vector2(1, 1).normalized,    // 右上
        new Vector2(0, 1),    // 向上
        new Vector2(-1, 1).normalized,   // 左上
        new Vector2(-1, 0),   // 向左
        new Vector2(-1, -1).normalized,  // 左下
        new Vector2(0, -1),   // 向下
        new Vector2(1, -1).normalized,   // 右下
    };

    //跳跃
    public float Gravity { get; private set; }
    public float InitialJumpVelocity { get; private set; }
    public float AdjustedJumpHeight { get; private set; }

    //蹬墙跳
    public float WallJumpGravity { get; private set; }
    public float InitialWallJumpVelocity { get; private set; }
    public float AdjustedWallJumpHeight { get; private set; }

    private void OnValidate()
    {
        CalculateValues();
    }

    private void OnEnable()
    {
        CalculateValues();
    }

    private void CalculateValues()
    {
        AdjustedJumpHeight = JumpHeight * JumpHeightCompensationFactor; //添加高度补偿后的跳跃高度
        Gravity = -(2f * AdjustedJumpHeight) / Mathf.Pow(TimeTillJumpApex,2f);
        InitialJumpVelocity = Mathf.Abs(Gravity) * TimeTillJumpApex;
    
        AdjustedWallJumpHeight = WallJumpDirection.y * JumpHeightCompensationFactor;
        WallJumpGravity = -(2f * AdjustedJumpHeight) / Mathf.Pow(TimeTillJumpApex,2f);
        InitialWallJumpVelocity = Mathf.Abs(WallJumpGravity) * TimeTillJumpApex;
    }
}
