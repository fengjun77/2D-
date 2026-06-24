using System.IO;
using UnityEngine;

[CreateAssetMenu(menuName = "Player Movement")]
public class PlayerMovementStats : ScriptableObject
{
    [Header("移动")]
    [Range(1f, 100f)] public float MaxWalkSpeed = 12.5f; //行走状态最大水平速度
    [Range(0.25f, 50f)] public float GroundAcceleration = 5f; //地面加速倍率
    [Range(0.25f, 50f)] public float GroundDeceleration = 5f; //地面减速倍率
    [Range(0.25f, 50f)] public float AirAcceleration = 5f; //空中加速力度
    [Range(0.25f, 50f)] public float AirDeceleration = 5f; //空中减速力度

    [Header("奔跑")]
    [Range(1f, 100f)] public float MaxRunSpeed = 20f; //按住奔跑键时的最大速度上限

    [Header("跳跃")]
    [Range(1f, 100f)] public float JumpHeight = 6.5f;                  // 标准跳跃最高高度
    [Range(1f, 1.1f)] public float JumpHeightCompensationFactor = 1.054f; // 松开跳跃键时高度补偿系数，轻微加高跳跃峰值
    public float TimeTillJumpApex = 0.35f;                             // 从起跳到达最高点的时间
    [Range(0.01f, 5f)] public float GravityOnReleaseMultiplier = 2f;   // 松开跳跃键后重力倍增系数，实现短跳效果
    public float MaxFallSpeed = 26f;                                   // 下落最大速度上限，防止下坠过快
    [Range(1, 5)] public int NumberOfJumpsAllowed = 2;                 // 允许连续跳跃次数（2=二段跳）

    [Header("跳切")]
    [Range(0.02f, 0.3f)] public float TimeForUpwardsCancel = 0.027f;   // 起跳后一小段时间内松开跳跃键可立刻切断上升，实现小跳

    [Header("跳转")]
    [Range(0.5f, 1f)] public float ApexThreshold = 0.97f;              // 到达跳跃顶点的速度阈值（接近0即判定为顶点）
    [Range(0.01f, 1f)] public float ApexHangTime = 0.075f;             // 顶点滞空时间，最高点轻微悬浮，手感更柔和

    [Header("跳跃缓冲")]
    [Range(0f, 1f)] public float JumpBufferTime = 0.125f;              // 预输入缓冲：落地前提前按跳，落地自动触发跳跃

    [Header("跳跃土狼时间")]
    [Range(0f, 1f)] public float JumpCoyoteTime = 0.1f;                // 土狼时间：离开平台后一小段时间内仍可起跳

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
    public bool DebugShowIsGroundedBox; //是否可视化检测地面碰撞
    public bool DebugShowHeadBumpBox; //是否可视化检测头顶碰撞

    public float Gravity { get; private set; }
    public float InitialJumpVelocity { get; private set; }
    public float AdjustedJumpHeight { get; private set; }

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
    }
}
