using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class PlayerMovement : MonoBehaviour
{
    public PlayerMovementStats moveStats;
    [SerializeField] private Collider2D feetCol;
    [SerializeField] private Collider2D bodyCol;

    private Rigidbody2D rb;

    //移动参数
    public float horizontalVelocity { get; private set; }
    private bool isFacingRight;

    //碰撞检测参数
    private RaycastHit2D groundHit;
    private RaycastHit2D headHit;
    private RaycastHit2D wallHit;
    private RaycastHit2D lastWallHit;
    private bool isGrounded;
    private bool bumpedHead;
    private bool isTouchingWall;

    //跳跃参数
    public float VerticalVelocity { get; private set; } // 当前竖直方向速度（向上为正，向下为负，外部仅可读，内部修改）
    private bool isJumping;                // 是否处于起跳上升阶段
    private bool isFastFalling;           // 是否处于快速下落状态（松开跳跃键加速下坠）
    private bool isFalling;                // 是否整体处于下落阶段（过顶点后）
    private float fastFallTime;            // 快速下坠生效的计时
    private float fastFallReleaseSpeed;    // 松开跳跃键瞬间记录的竖直速度，用于计算短跳加速
    private int numberOfJumpsUsed;         // 已使用的跳跃次数（用来限制二段跳/多段跳）

    //跳跃顶点
    private float apexPoint;               // 跳跃顶点的竖直高度
    private float timePastApexThreshold;    // 到达顶点阈值后持续的滞空计时
    private bool isPastApexThreshold;      // 是否已经越过跳跃最高点阈值（进入顶点滞空区间）

    //跳跃缓冲
    private float jumpBufferTime;          // 跳跃缓冲剩余计时（预存提前按下的跳跃指令）
    private bool jumpReleasedDuringBuffer; // 缓冲窗口内是否松开过跳跃键，用于区分长短跳

    private float coyoteTimer;             // 土狼时间剩余计时（离开平台后仍可起跳的窗口期）

    // 滑墙相关状态变量
    private bool isWallSliding;              // 是否处于贴墙下滑状态
    private bool isWallSlideFalling;         // 滑墙阶段是否进入下坠阶段

    // 墙跳相关状态变量
    private bool useWallJumpMoveStats;       // 是否启用墙跳专属移动参数
    private bool isWallJumping;              // 是否正在执行墙跳上升阶段
    private float wallJumpTime;              // 墙跳持续上升计时
    private bool isWallJumpFastFalling;      // 墙跳后是否开启快速下坠
    private bool isWallJumpFalling;          // 墙跳完成后是否进入下落阶段
    private float wallJumpFastFallTime;      // 墙跳快速下坠生效时长
    private float wallJumpFastFallReleaseSpeed; // 松开跳跃键瞬间记录的墙跳竖直速度

    private float wallJumpPostBufferTimer;   // 墙跳缓冲后置计时器

    private float wallJumpApexPoint;         // 墙跳最高点竖直坐标
    private float timePastWallJumpApexThreshold; // 越过墙跳顶点阈值后的累计计时
    private bool isPastWallJumpApexThreshold; // 是否已经越过墙跳最高点阈值（进入顶点滞空区间）

    // 冲刺相关状态变量
    private bool isDashing;                  // 是否正在冲刺
    private bool isAirDashing;                // 是否为空中冲刺
    private float dashTimer;                 // 单次冲刺剩余持续计时
    private float dashOnGroundTimer;         // 地面连续冲刺冷却计时
    private int numberOfDashesUsed;          // 已使用的冲刺次数（限制连续冲刺上限）
    private Vector2 dashDirection;           // 当前冲刺的移动方向向量
    private bool isDashFastFalling;          // 冲刺结束后是否开启快速下坠
    private float dashFastFallTime;          // 冲刺快速下坠生效时长
    private float dashFastFallReleaseSpeed;  // 松开冲刺/跳跃时记录的冲刺竖直速度

    void Awake()
    {
        isFacingRight = true;
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        CountTimers();
        JumpChecks();
        LandCheck();
        WallSlideCheck();
        WallJumpCheck();
    }

    void FixedUpdate()
    {
        CollisionChecks();
        
        Jump();

        Fall();

        WallSlide();
        
        WallJump();

        if(isGrounded)
        {
            Move(moveStats.GroundAcceleration, moveStats.GroundDeceleration, InputManager.Movement);
        }
        else
        {
            if(useWallJumpMoveStats)
            {
                Move(moveStats.WallJumpMoveAcceleration, moveStats.WallJumpMoveDeceleration, InputManager.Movement);
            }
            else
            {
                Move(moveStats.AirAcceleration, moveStats.AirDeceleration, InputManager.Movement);
            }
        }

        ApplyVelocity();
    }

    private void ApplyVelocity()
    {
        //限制最大下落速度
        if(!isDashing)
        {
            VerticalVelocity = Mathf.Clamp(VerticalVelocity, -moveStats.MaxFallSpeed, 50f);
        }    
        else
        {
            VerticalVelocity = Mathf.Clamp(VerticalVelocity, -50f, 50f);
        }


        rb.linearVelocity = new Vector2(horizontalVelocity, VerticalVelocity);
    }

    #region 移动
    /// <summary>
    /// 移动逻辑
    /// </summary>
    /// <param name="acceleration">加速度</param>
    /// <param name="deceleration">减速度</param>
    /// <param name="moveInput">移动向量</param>
    private void Move(float acceleration, float deceleration, Vector2 moveInput)
    {
        if(!isDashing)
        {
            if(Mathf.Abs(moveInput.x) >= moveStats.MoveThreshold)
            {
                //检查角色是否需要转向
                TurnCheck(moveInput);

                float targetVelocity = 0f;
                if(InputManager.RunIsHeld)
                {
                    targetVelocity = moveInput.x * moveStats.MaxRunSpeed;
                }
                else
                {
                    targetVelocity = moveInput.x * moveStats.MaxWalkSpeed;
                }

                horizontalVelocity = Mathf.Lerp(horizontalVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
            }

            else if (Mathf.Abs(moveInput.x) < moveStats.MoveThreshold)
            {
                horizontalVelocity = Mathf.Lerp(horizontalVelocity, 0f, deceleration * Time.fixedDeltaTime);
            }
        }
    }

    private void TurnCheck(Vector2 moveInput)
    {
        if(isFacingRight && moveInput.x < 0)
        {
            Turn(false);
        }
        else if(!isFacingRight && moveInput.x > 0)
        {
            Turn(true);
        }
    }

    private void Turn(bool turnRight)
    {
        if(turnRight)
        {
            isFacingRight = true;
            transform.Rotate(0f, 180f, 0f);
        }
        else
        {
            isFacingRight = false;
            transform.Rotate(0f, -180f, 0f);
        }
    }
    #endregion

    #region 着陆/下降

    private void LandCheck()
    {
        //着陆状态
        if((isJumping || isFalling || isWallJumpFalling || isWallJumping || isWallSlideFalling || isWallSliding || isDashFastFalling) && isGrounded && VerticalVelocity <= 0)
        {
            ResetJumpValues();
            StopWallSlide();
            ResetWallJumpValues();
            ResetDashes();

            numberOfJumpsUsed = 0;

            VerticalVelocity = Physics2D.gravity.y;

            if(isDashFastFalling && isGrounded)
            {
                ResetDashValues();
                return;
            }

            ResetDashValues();
        }
    }

    private void Fall()
    {
        //自然掉落时的重力
        if(!isGrounded && !isJumping && !isWallSliding && !isWallJumping && !isDashing && !isDashFastFalling)
        {
            if(!isFalling)
            {
                isFalling = true;
            }

            VerticalVelocity += moveStats.Gravity * Time.fixedDeltaTime;
        }
    }

    #endregion

    #region 跳跃
    private void ResetJumpValues()
    {
        isJumping = false;
        isFalling = false;
        isFastFalling = false;
        fastFallTime = 0f;
        isPastApexThreshold = false;
    }

    private void JumpChecks()
    {
        //如果按下跳跃键
        if(InputManager.JumpWasPressed)
        {
            if(isWallSlideFalling && wallJumpPostBufferTimer >= 0f)
            {
                return;
            }
            else if(isWallSliding || (isTouchingWall && !isGrounded))
            {
                return;
            }

            jumpBufferTime = moveStats.JumpBufferTime;
            jumpReleasedDuringBuffer = false;
        }

        //如果松开跳跃键
        if(InputManager.JumpWasReleased)
        {
            //如果还有缓冲时间
            if(jumpBufferTime > 0f)
            {
                jumpReleasedDuringBuffer = true;
            }

            //如果在跳跃中，并且加速度是向上的
            if(isJumping && VerticalVelocity > 0f)
            {
                //是否到达顶点阈值
                if(isPastApexThreshold)
                {
                    isPastApexThreshold = false;
                    isFastFalling = true;
                    fastFallTime = moveStats.TimeForUpwardsCancel;
                    VerticalVelocity = 0f;
                }
                else
                {
                    isFastFalling = true;
                    fastFallReleaseSpeed = VerticalVelocity;
                }
            }
        }

        //如果在跳跃缓冲时间内并且不在跳跃状态，并且在地面或者土狼时间内
        if(jumpBufferTime > 0f && !isJumping && (isGrounded || coyoteTimer > 0f))
        {
            InitiateJump(1);

            //如果松开了跳跃键 (小跳)
            if(jumpReleasedDuringBuffer)
            {
                isFastFalling = true;
                fastFallReleaseSpeed = VerticalVelocity;
            }
        }
        //二段跳
        else if(jumpBufferTime > 0f && (isJumping || isWallJumping || isWallSlideFalling || isAirDashing || isDashFastFalling) && !isTouchingWall && numberOfJumpsUsed < moveStats.NumberOfJumpsAllowed)
        {
            isFastFalling = false;
            InitiateJump(1);

            if(isDashFastFalling)
            {
                isDashFastFalling = false;
            }
        }
        //土狼时间的跳跃
        else if(jumpBufferTime > 0f && isFalling && !isWallSlideFalling && numberOfJumpsUsed < moveStats.NumberOfJumpsAllowed - 1)
        {
            InitiateJump(2);
            isFastFalling = false;
        }
    }

    /// <summary>
    /// 实现跳跃
    /// </summary>
    /// <param name="numberOfJumpsUsed">已跳跃次数</param>
    private void InitiateJump(int numberOfJumpsUsed)
    {
        if(!isJumping)
        {
            isJumping = true;
        }

        ResetWallJumpValues();

        jumpBufferTime = 0f;
        this.numberOfJumpsUsed += numberOfJumpsUsed;
        VerticalVelocity = moveStats.InitialJumpVelocity;
    }

    private void Jump()
    {
        //在跳跃状态下施加重力
        if(isJumping)
        {
            //检测角色头部碰撞情况
            if(bumpedHead)
            {
                //触发快速下落
                isFastFalling = true;
            }

            //上升状态的重力处理
            //处理顶点控制
            if(VerticalVelocity >= 0f)
            {
                apexPoint = Mathf.InverseLerp(moveStats.InitialJumpVelocity, 0f, VerticalVelocity);

                //到达顶点后的处理
                if(apexPoint > moveStats.ApexThreshold)
                {
                    if(!isPastApexThreshold)
                    {
                        isPastApexThreshold = true;
                        timePastApexThreshold = 0; //重置置空时间
                    }

                    if(isPastApexThreshold)
                    {
                        timePastApexThreshold += Time.fixedDeltaTime;

                        if(timePastApexThreshold < moveStats.ApexHangTime)
                        {
                            VerticalVelocity = 0f;
                        }
                        else
                        {
                            VerticalVelocity = -0.01f;
                        }
                    }
                }
                //没有达到顶点的重力处理
                else if(!isFastFalling)
                {
                    VerticalVelocity += moveStats.Gravity * Time.fixedDeltaTime;
                
                    if(isPastApexThreshold)
                    {
                        isPastApexThreshold = false;
                    }
                }
            }
            //下降状态的重力处理
            else if(!isFastFalling)
            {
                VerticalVelocity += moveStats.Gravity * moveStats.GravityOnReleaseMultiplier * Time.fixedDeltaTime;
            }
            else if(VerticalVelocity < 0f)
            {
                if(!isFalling)
                {
                    isFalling = true;
                }
            }           
        }

        //处理跳跃中断
        if(isFastFalling)
        {
            if(fastFallTime >= moveStats.TimeForUpwardsCancel)
            {
                VerticalVelocity += moveStats.Gravity * moveStats.GravityOnReleaseMultiplier * Time.fixedDeltaTime;
            }
            else if(fastFallTime < moveStats.TimeForUpwardsCancel)
            {
                VerticalVelocity = Mathf.Lerp(fastFallReleaseSpeed, 0f, (fastFallTime / moveStats.TimeForUpwardsCancel));
            }

            fastFallTime += Time.fixedDeltaTime;
        }
    }

    #endregion

    #region 墙壁滑行
    
    /// <summary>
    /// 滑墙状态检测逻辑，判断是否进入/退出贴墙下滑
    /// </summary>
    private void WallSlideCheck()
    {
        // 条件：贴墙、不在地面、不在冲刺状态
        if (isTouchingWall && !isGrounded && !isDashing)
        {
            // 竖直速度向下，且当前未处于滑墙状态 → 开启滑墙
            if (VerticalVelocity < 0f && !isWallSliding)
            {
                ResetJumpValues();
                ResetWallJumpValues();
                ResetDashValues();

                if(moveStats.ResetDashOnWallSlide)
                {
                    ResetDashes();
                }

                isWallSlideFalling = false;
                isWallSliding = true;
            }

            // 配置开启滑墙重置跳跃次数时，清空已使用跳跃计数
            if (moveStats.ResetJumpsOnWallSlide)
            {
                numberOfJumpsUsed = 0;
            }
        }
        // 滑墙中、脱离墙壁、未落地、还未进入滑墙下坠阶段
        else if (isWallSliding && !isTouchingWall && !isGrounded && !isWallSlideFalling)
        {
            // 标记滑墙结束，进入自由下坠
            isWallSlideFalling = true;
            StopWallSlide();
        }
        // 其他所有情况，直接终止滑墙状态
        else
        {
            StopWallSlide();
        }
    }

    /// <summary>
    /// 终止滑墙状态，重置滑墙标记并消耗一次跳跃额度
    /// </summary>
    private void StopWallSlide()
    {
        if (isWallSliding)
        {
            // 滑墙结束后消耗一次跳跃次数
            numberOfJumpsUsed++;
            // 关闭滑墙状态标记
            isWallSliding = false;
        }
    }

    /// <summary>
    /// 滑墙物理运动逻辑（空白待填充）
    /// </summary>
    private void WallSlide()
    {
        if(isWallSliding)
        {
            VerticalVelocity = Mathf.Lerp(VerticalVelocity, -moveStats.WallSlideSpeed, moveStats.WallSlideDecelerationSpeed * Time.fixedDeltaTime);
        }
    }

    #endregion

    #region 蹬墙跳

    private void WallJumpCheck()
    {
        // 判断是否需要开启墙跳后置缓冲窗口
        if (ShouldApplyPostWallJumpBuffer())
        {
            // 重置墙跳后置缓冲计时器
            wallJumpPostBufferTimer = moveStats.WallJumpPostBufferTime;
        }

        // ========== 墙跳松开快速下坠逻辑 ==========
        // 条件：松开跳跃键、不在滑墙、不贴墙、正处于墙跳上升阶段
        if (InputManager.JumpWasReleased && !isWallSliding && !isTouchingWall && isWallJumping)
        {
            // 当前竖直速度向上（还在上升过程）
            if (VerticalVelocity > 0f)
            {
                // 已经越过墙跳顶点阈值（接近最高点）
                if (isPastWallJumpApexThreshold)
                {
                    isPastWallJumpApexThreshold = false;
                    isWallJumpFastFalling = true;
                    wallJumpFastFallTime = moveStats.TimeForUpwardsCancel;
                    // 直接清零向上速度，立刻下坠
                    VerticalVelocity = 0f;
                }
                else
                {
                    // 还没到顶点，开启快速下坠，记录松开瞬间的上升速度
                    isWallJumpFastFalling = true;
                    wallJumpFastFallReleaseSpeed = VerticalVelocity;
                }
            }
        }

        // ========== 墙跳后置缓冲触发墙跳 ==========
        // 按下跳跃键 且 墙跳后置缓冲计时器仍有剩余时间，执行墙跳
        if (InputManager.JumpWasPressed && wallJumpPostBufferTimer > 0f)
        {
            InitiateWallJump();
        }
    }

    private void InitiateWallJump()
    {
        if(!isWallJumping)
        {
            isWallJumping = true;
            useWallJumpMoveStats = true;
        }

        StopWallSlide();
        ResetJumpValues();
        wallJumpTime = 0f;

        VerticalVelocity = moveStats.InitialJumpVelocity;

        int dirMultiplier;

        Vector2 hitPoint = lastWallHit.collider.ClosestPoint(bodyCol.bounds.center);

        if(hitPoint.x > transform.position.x)
        {
            dirMultiplier = -1;
        }
        else
        {
            dirMultiplier = 1;
        }

        horizontalVelocity = Mathf.Abs(moveStats.WallJumpDirection.x) * dirMultiplier;
    }

    private void WallJump()
    {
        if(isWallJumping)
        {
            wallJumpTime += Time.deltaTime;
            if(wallJumpTime >= moveStats.TimeTillJumpApex)
            {
                useWallJumpMoveStats = false;
            }

            if(bumpedHead)
            {
                isWallJumpFastFalling = true;
                useWallJumpMoveStats = false;
            }

            // 上升阶段重力与顶点滞空控制逻辑
            if (VerticalVelocity >= 0f)
            {
                // 顶点滞空控制
                // 将当前竖直速度反向映射为0~1区间的顶点进度值，1代表完全到达最高点
                wallJumpApexPoint = Mathf.InverseLerp(moveStats.WallJumpDirection.y, 0f, VerticalVelocity);

                // 若顶点进度超过阈值，判定进入顶点滞空区间
                if (wallJumpApexPoint > moveStats.ApexThreshold)
                {
                    // 首次进入顶点区间，标记状态并重置滞空计时器
                    if (!isPastWallJumpApexThreshold)
                    {
                        isPastWallJumpApexThreshold = true;
                        timePastWallJumpApexThreshold = 0f;
                    }

                    // 已进入顶点区间时，累计滞空计时
                    if (isPastWallJumpApexThreshold)
                    {
                        timePastWallJumpApexThreshold += Time.fixedDeltaTime;
                        // 滞空时间未达到设定值，锁定竖直速度实现悬浮效果
                        if (timePastWallJumpApexThreshold < moveStats.ApexHangTime)
                        {
                            VerticalVelocity = 0f;
                        }
                        // 滞空时长结束，赋予微小向下速度，开始正常下落
                        else
                        {
                            VerticalVelocity = -0.01f;
                        }
                    }
                }
                else if(!isWallJumpFastFalling)
                {
                    VerticalVelocity += moveStats.WallJumpGravity * Time.fixedDeltaTime;
                    
                    if(isPastWallJumpApexThreshold)
                    {
                        isPastWallJumpApexThreshold = false;
                    }
                }
            }
            else if(!isWallJumpFastFalling)
            {
                VerticalVelocity += moveStats.WallJumpGravity * Time.fixedDeltaTime;
            }
            else if(VerticalVelocity < 0f)
            {
                if(!isWallJumpFalling)
                    isWallJumpFalling = true;
            }
        }

        if(isWallJumpFastFalling)
        {
            if(wallJumpFastFallTime >= moveStats.TimeForUpwardsCancel)
            {
                VerticalVelocity += moveStats.WallJumpGravity * moveStats.WallJumpGravityOnReleaseMultiplier * Time.fixedDeltaTime;
            }
            else if(wallJumpFastFallTime < moveStats.TimeForUpwardsCancel)
            {
                VerticalVelocity = Mathf.Lerp(wallJumpFastFallReleaseSpeed, 0f, (wallJumpFastFallTime / moveStats.TimeForUpwardsCancel));
            }

            wallJumpFastFallTime += Time.fixedDeltaTime;
        }
    }

    private bool ShouldApplyPostWallJumpBuffer()
    {
        if(!isGrounded && (isTouchingWall || isWallSliding))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private void ResetWallJumpValues()
    {
        isWallSlideFalling = false;
        isWallJumping = false;
        useWallJumpMoveStats = false;
        isWallJumpFastFalling = false;
        isWallJumpFalling = false;
        isPastWallJumpApexThreshold = false;

        wallJumpFastFallTime = 0f;
        wallJumpTime = 0f;
    }

    #endregion

    #region 冲刺
    private void ResetDashValues()
    {
        isDashFastFalling = false;
        dashOnGroundTimer = -0.01f;
    }

    private void ResetDashes()
    {
        numberOfDashesUsed = 0;
    }

    #endregion

    #region 碰撞检测

    /// <summary>
    /// 检测角色是否站在地面上，给isGrounded布尔变量赋值
    /// 使用BoxCast盒形投射，比单射线检测更稳定，斜坡、窄平台不会误判浮空
    /// </summary>
    private void IsGrounded()
    {
        // 1. 计算盒形投射的中心点：X取脚部碰撞体中心，Y取脚部碰撞体最底部（脚底位置）
        Vector2 boxCastOrigin = new Vector2(feetCol.bounds.center.x, feetCol.bounds.min.y);

        // 2. 盒形投射尺寸：宽度等于脚部碰撞体宽度，高度为配置里的地面检测射线长度
        Vector2 boxCastSize = new Vector2(feetCol.bounds.size.x, moveStats.GroundDetectionRayLength);

        // 3. 发射向下的2D盒形射线，只检测GroundLayer层级物体
        // 参数说明：起点、盒大小、旋转角度、射线方向、射线长度、检测层级
        groundHit = Physics2D.BoxCast(boxCastOrigin, boxCastSize, 0f, Vector2.down, moveStats.GroundDetectionRayLength, moveStats.GroundLayer);

        // 4. 判断射线是否碰到地面碰撞体
        if (groundHit.collider != null)
        {
            // 有碰撞 = 角色踩在地面
            isGrounded = true;
        }
        else
        {
            // 无碰撞 = 角色在空中、跳跃、掉落
            isGrounded = false;
        }

        // 5. 如果开启调试绘制，在Scene窗口画出地面检测框，方便调试碰撞范围
        if (moveStats.DebugShowIsGroundedBox)
        {
            Color rayColor;
            // 落地显示绿色，浮空显示红色，直观区分状态
            if (isGrounded)
            {
                rayColor = Color.green;
            }
            else
            {
                rayColor = Color.red;
            }

            // 绘制检测盒左边界竖线（从脚底向下）
            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x / 2, boxCastOrigin.y), Vector2.down * moveStats.GroundDetectionRayLength, rayColor);
            // 绘制检测盒右边界竖线（从脚底向下）
            Debug.DrawRay(new Vector2(boxCastOrigin.x + boxCastSize.x / 2, boxCastOrigin.y), Vector2.down * moveStats.GroundDetectionRayLength, rayColor);
            // 绘制检测盒底部横线（连接左右两条竖线底部，组成完整方框底边）
            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x / 2, boxCastOrigin.y - moveStats.GroundDetectionRayLength), Vector2.right * boxCastSize.x, rayColor);
        }
    }

    private void BumpedHead()
    {
        // 1. 计算盒形投射的中心点：X取脚部碰撞体中心，Y取脚部碰撞体最顶部
        Vector2 boxCastOrigin = new Vector2(feetCol.bounds.center.x, bodyCol.bounds.max.y);

        // 2. 盒形投射尺寸：宽度等于脚部碰撞体宽度，高度为配置里的地面检测射线长度
        Vector2 boxCastSize = new Vector2(feetCol.bounds.size.x * moveStats.HeadWidth, moveStats.HeadDetectionRayLength);

        // 3. 发射向下的2D盒形射线，只检测GroundLayer层级物体
        // 参数说明：起点、盒大小、旋转角度、射线方向、射线长度、检测层级
        headHit = Physics2D.BoxCast(boxCastOrigin, boxCastSize, 0f, Vector2.up, moveStats.HeadDetectionRayLength, moveStats.GroundLayer);

        // 4. 判断射线是否碰到地面碰撞体
        if (headHit.collider != null)
        {
            // 有碰撞 = 角色头部撞到天花板
            bumpedHead = true;
        }
        else
        {
            // 无碰撞 = 角色未触碰天花板
            bumpedHead = false;
        }

        // 5. 如果开启调试绘制，在Scene窗口画出头顶检测框，方便调试碰撞范围
        if (moveStats.DebugShowHeadBumpBox)
        {
            float headWidth = moveStats.HeadWidth;

            Color rayColor;
            // 落地显示绿色，浮空显示红色，直观区分状态
            if (bumpedHead)
            {
                rayColor = Color.green;
            }
            else
            {
                rayColor = Color.red;
            }

            // 绘制检测盒左边界竖线（从头顶向上）
            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x / 2 * headWidth, boxCastOrigin.y), Vector2.up * moveStats.HeadDetectionRayLength, rayColor);
            // 绘制检测盒右边界竖线（从头顶向上）
            Debug.DrawRay(new Vector2(boxCastOrigin.x + (boxCastSize.x / 2) * headWidth, boxCastOrigin.y), Vector2.up * moveStats.HeadDetectionRayLength, rayColor);
            // 绘制检测盒底部横线（连接左右两条竖线底部，组成完整方框底边）
            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x / 2 * headWidth, boxCastOrigin.y + moveStats.HeadDetectionRayLength), Vector2.right * boxCastSize.x * headWidth, rayColor);
        }
    }

    /// <summary>
    /// 检测角色左右两侧是否接触墙壁，用于滑墙、墙跳判定
    /// </summary>
    private void IsTouchingWall()
    {
        // 投射起点X坐标：面朝右取碰撞体最右侧，面朝左取碰撞体最左侧
        float originEndPoint = 0f;
        if (isFacingRight)
        {
            originEndPoint = bodyCol.bounds.max.x;
        }
        else
        {
            originEndPoint = bodyCol.bounds.min.x;
        }

        // 碰撞盒高度：身体碰撞体高度 × 墙壁检测高度缩放系数
        float adjustedHeight = bodyCol.bounds.size.y * moveStats.WallDetectionRayHeightMultiplier;

        // BoxCast中心点：X为角色左右侧边，Y为身体碰撞体竖直中心
        Vector2 boxCastOrigin = new Vector2(originEndPoint, bodyCol.bounds.center.y);
        // 碰撞盒尺寸：宽度=墙壁检测距离，高度=缩放后的身体高度
        Vector2 boxCastSize = new Vector2(moveStats.WallDetectionRayLength, adjustedHeight);

        // 向角色面朝方向发射盒形投射，检测墙壁层级碰撞
        wallHit = Physics2D.BoxCast(boxCastOrigin, boxCastSize, 0f, transform.right, moveStats.WallDetectionRayLength, moveStats.GroundLayer);
        if (wallHit.collider != null)
        {
            lastWallHit = wallHit; // 保存本次墙壁碰撞信息
            isTouchingWall = true; // 标记角色贴墙
        }
        else
        {
            isTouchingWall = false; // 角色未接触墙壁
        }

        // 开启墙壁检测框调试绘制
        if (moveStats.DebugShowWallHitBox)
        {
            Color rayColor;
            // 贴墙显示绿色，未贴墙显示红色
            if (isTouchingWall)
            {
                rayColor = Color.green;
            }
            else
            {
                rayColor = Color.red;
            }

            // 计算检测盒四个顶点坐标
            Vector2 boxBottomLeft = new Vector2(boxCastOrigin.x - boxCastSize.x / 2, boxCastOrigin.y - boxCastSize.y / 2);
            Vector2 boxBottomRight = new Vector2(boxCastOrigin.x + boxCastSize.x / 2, boxCastOrigin.y - boxCastSize.y / 2);
            Vector2 boxTopLeft = new Vector2(boxCastOrigin.x - boxCastSize.x / 2, boxCastOrigin.y + boxCastSize.y / 2);
            Vector2 boxTopRight = new Vector2(boxCastOrigin.x + boxCastSize.x / 2, boxCastOrigin.y + boxCastSize.y / 2);

            // 绘制矩形四条边线，组成完整检测框
            Debug.DrawLine(boxBottomLeft, boxBottomRight, rayColor);
            Debug.DrawLine(boxBottomRight, boxTopRight, rayColor);
            Debug.DrawLine(boxTopRight, boxTopLeft, rayColor);
            Debug.DrawLine(boxTopLeft, boxBottomLeft, rayColor);
        }
    }

    private void CollisionChecks()
    {
        IsGrounded();
        BumpedHead();
        IsTouchingWall();
    }

    #endregion

    #region 计时器
    private void CountTimers()
    {
        jumpBufferTime -= Time.deltaTime;

        //判断是否进入土狼时间
        if(!isGrounded)
        {
            coyoteTimer -= Time.deltaTime;
        }
        else
        {
            coyoteTimer = moveStats.JumpCoyoteTime;
        }

        if(!ShouldApplyPostWallJumpBuffer())
        {
            wallJumpPostBufferTimer -= Time.deltaTime;
        }
    }

    #endregion

    void OnDrawGizmos()
    {
        if(moveStats.ShowWalkJumpArc)
            DrawJumpArc(moveStats.MaxWalkSpeed, Color.white);

        if(moveStats.ShowRunJumpArc)
            DrawJumpArc(moveStats.MaxRunSpeed, Color.red);
    }

    /// <summary>
    /// 绘制跳跃轨迹预览弧线，模拟角色完整跳跃抛物线（上升→顶点滞空→下落三段）
    /// </summary>
    /// <param name="moveSpeed">本次模拟使用的水平移动速度（行走/奔跑速度）</param>
    /// <param name="gizmoColor">轨迹线条绘制颜色</param>
    private void DrawJumpArc(float moveSpeed, Color gizmoColor)
    {
        // 轨迹模拟起点：角色脚部碰撞体底部中心位置
        Vector2 startPosition = new Vector2(feetCol.bounds.center.x, feetCol.bounds.min.y);
        // 上一个模拟点坐标，用于连线绘制线段
        Vector2 previousPosition = startPosition;
        // 水平模拟速度，根据配置选择向左/向右绘制轨迹
        float speed = 0f;

        if (moveStats.DrawRight)
        {
            speed = moveSpeed;
        }
        else
        {
            speed = -moveSpeed;
        }

        // 初始速度向量：X=水平移动速度，Y=起跳初始竖直速度
        Vector2 velocity = new Vector2(speed, moveStats.InitialJumpVelocity);
        // 设置Gizmos绘制线条颜色
        Gizmos.color = gizmoColor;

        // 单步模拟时间增量：总上升时间 ÷ 曲线分段精度，控制轨迹平滑度
        float timeStep = 2 * moveStats.TimeTillJumpApex / moveStats.ArcResolution;
        //float totalTime = (2 * moveStats.TimeTillJumpApex) + moveStats.ApexHangTime; // 完整跳跃总时长（上升+顶点滞空+下落）

        // 循环模拟多步物理，生成整条跳跃轨迹点
        for (int i = 0; i < moveStats.VisualizationSteps; i++)
        {
            // 当前模拟累计时间
            float simulationTime = i * timeStep;
            // 存储当前时间下角色的整体偏移坐标（相对起跳起点）
            Vector2 displacement;
            // 当前轨迹点世界坐标
            Vector2 drawPoint;

            // 阶段1：上升阶段（从起跳至到达最高点前）
            if (simulationTime < moveStats.TimeTillJumpApex)
            {
                // 匀变速位移公式：初速度×时间 + 0.5×重力×时间²
                displacement = velocity * simulationTime + 0.5f * new Vector2(0, moveStats.Gravity) * simulationTime * simulationTime;
            }
            // 阶段2：顶点滞空阶段（到达最高点后，短暂悬浮无竖直下落）
            else if (simulationTime < moveStats.TimeTillJumpApex + moveStats.ApexHangTime)
            {
                // 计算已经度过的滞空时长
                float apexTime = simulationTime - moveStats.TimeTillJumpApex;
                // 先计算完整上升阶段的总位移
                displacement = velocity * moveStats.TimeTillJumpApex + 0.5f * new Vector2(0, moveStats.Gravity) * moveStats.TimeTillJumpApex * moveStats.TimeTillJumpApex;
                // 滞空阶段仅水平移动，竖直高度不变
                displacement += new Vector2(speed, 0) * apexTime;
            }
            // 阶段3：下落阶段（滞空结束后开始下坠）
            else
            {
                // 计算已经度过的下落时长
                float descendTime = simulationTime - (moveStats.TimeTillJumpApex + moveStats.ApexHangTime);
                // 先算出上升+滞空阶段的总位移
                displacement = velocity * moveStats.TimeTillJumpApex + 0.5f * new Vector2(0, moveStats.Gravity) * moveStats.TimeTillJumpApex * moveStats.TimeTillJumpApex;
                displacement += new Vector2(speed, 0) * moveStats.ApexHangTime;
                // 叠加下落阶段的位移（持续受重力下坠）
                displacement += new Vector2(speed, 0) * descendTime + 0.5f * new Vector2(0, moveStats.Gravity) * descendTime * descendTime;
            }

            // 用起跳起点 + 累计偏移，得到当前轨迹点实际世界坐标
            drawPoint = startPosition + displacement;

            // 如果开启碰撞截断，检测轨迹线段是否撞墙/地面
            if (moveStats.StopOnCollision)
            {
                // 射线检测上一个轨迹点到当前轨迹点之间是否碰撞地面层级物体
                RaycastHit2D hit = Physics2D.Raycast(previousPosition, drawPoint - previousPosition, Vector2.Distance(previousPosition, drawPoint), moveStats.GroundLayer);
                if (hit.collider != null)
                {
                    // 检测到碰撞，只绘制到碰撞点，终止后续轨迹绘制
                    Gizmos.DrawLine(previousPosition, hit.point);
                    break;
                }
            }

            // 绘制上一个点到当前点的连线，拼接成完整弧线
            Gizmos.DrawLine(previousPosition, drawPoint);
            // 更新上一个点，供下一轮循环连线使用
            previousPosition = drawPoint;
        }
    }
}
