using UnityEngine;
using UnityEngine.Rendering;

public class PlayerMovement : MonoBehaviour
{
    public PlayerMovementStats moveStats;
    [SerializeField] private Collider2D feetCol;
    [SerializeField] private Collider2D bodyCol;

    private Rigidbody2D rb;

    //移动参数
    private Vector2 moveVelocity;
    private bool isFacingRight;

    //碰撞检测参数
    private RaycastHit2D groundHit;
    private RaycastHit2D headHit;
    private bool isGrounded;
    private bool bumpedHead;

    //跳跃参数
    public float VerticalVelocity { get; private set; } // 当前竖直方向速度（向上为正，向下为负，外部仅可读，内部修改）
    private bool isJumping;                // 是否处于起跳上升阶段
    private bool isFastFalliing;           // 是否处于快速下落状态（松开跳跃键加速下坠）
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

    void Awake()
    {
        isFacingRight = true;
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        CountTimers();
        JumpChecks();
    }

    void FixedUpdate()
    {
        CollisionChecks();
        
        Jump();

        if(isGrounded)
        {
            Move(moveStats.GroundAcceleration, moveStats.GroundDeceleration, InputManager.Movement);
        }
        else
        {
            Move(moveStats.AirAcceleration, moveStats.AirDeceleration, InputManager.Movement);
        }
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
        if(moveInput != Vector2.zero)
        {
            //检查角色是否需要转向
            TurnCheck(moveInput);

            Vector2 targetVelocity = Vector2.zero;
            if(InputManager.RunIsHeld)
            {
                targetVelocity = new Vector2(moveInput.x, 0f) * moveStats.MaxRunSpeed;
            }
            else
            {
                targetVelocity = new Vector2(moveInput.x, 0f) * moveStats.MaxWalkSpeed;
            }

            moveVelocity = Vector2.Lerp(moveVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        
            rb.linearVelocity = new Vector2(moveVelocity.x, rb.linearVelocity.y);
        }

        else if (moveInput == Vector2.zero)
        {
            moveVelocity = Vector2.Lerp(moveVelocity, Vector2.zero, deceleration * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector2(moveVelocity.x, rb.linearVelocity.y);
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

    #region 跳跃
    private void JumpChecks()
    {
        //如果按下跳跃键
        if(InputManager.JumpWasPressed)
        {
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
                    isFastFalliing = true;
                    fastFallTime = moveStats.TimeForUpwardsCancel;
                    VerticalVelocity = 0f;
                }
                else
                {
                    isFastFalliing = true;
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
                isFastFalliing = true;
                fastFallReleaseSpeed = VerticalVelocity;
            }
        }
        //二段跳
        else if(jumpBufferTime > 0f && isJumping && numberOfJumpsUsed < moveStats.NumberOfJumpsAllowed)
        {
            isFastFalliing = false;
            InitiateJump(1);
        }
        //土狼时间的跳跃
        else if(jumpBufferTime > 0f && isFalling && numberOfJumpsUsed < moveStats.NumberOfJumpsAllowed - 1)
        {
            InitiateJump(2);
            isFastFalliing = false;
        }

        //着陆状态
        if((isJumping || isFalling) && isGrounded && VerticalVelocity <= 0)
        {
            isJumping = false;
            isFalling = false;
            isFastFalliing = false;
            fastFallTime = 0f;
            isPastApexThreshold = false;
            numberOfJumpsUsed = 0;

            VerticalVelocity = Physics2D.gravity.y;
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
                isFastFalliing = true;
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
                else
                {
                    VerticalVelocity += moveStats.Gravity * Time.fixedDeltaTime;
                
                    if(isPastApexThreshold)
                    {
                        isPastApexThreshold = false;
                    }
                }
            }
            //下降状态的重力处理
            else if(!isFastFalliing)
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
        if(isFastFalliing)
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

        //自然掉落时的重力
        if(!isGrounded && !isJumping)
        {
            if(!isFalling)
            {
                isFalling = true;
            }

            VerticalVelocity += moveStats.Gravity * Time.fixedDeltaTime;
        }

        //限制最大下落速度
        VerticalVelocity = Mathf.Clamp(VerticalVelocity, -moveStats.MaxFallSpeed, 50f);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, VerticalVelocity);
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

    private void CollisionChecks()
    {
        IsGrounded();
        BumpedHead();
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
