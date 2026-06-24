using UnityEngine;

public class InputManager : MonoBehaviour
{
    public PlayerInput input { get; private set; }
    
    public static Vector2 Movement;
    public static bool JumpWasPressed; //跳跃按下
    public static bool JumpIsHeld; //跳跃长按
    public static bool JumpWasReleased; //跳跃松开
    public static bool RunIsHeld; //奔跑长按

    private void Awake()
    {
        input = new PlayerInput();
    }

    private void OnEnable()
    {
        // 仅启用输入，不读取任何数值
        input.Enable();
    }

    private void OnDisable()
    {
        // 关闭输入，防止后台持续占用
        input.Disable();
    }

    private void Update()
    {
        // 每一帧实时刷新所有输入状态
        ReadAllInput();
    }

    /// <summary>
    /// 统一读取所有玩家输入，每帧执行
    /// </summary>
    private void ReadAllInput()
    {
        // 移动方向向量
        Movement = input.Player.Move.ReadValue<Vector2>();

        // 跳跃相关状态
        JumpWasPressed = input.Player.Jump.WasPressedThisFrame();   // 本帧刚按下（仅1帧true）
        JumpIsHeld = input.Player.Jump.IsPressed();                 // 当前持续按住
        JumpWasReleased = input.Player.Jump.WasReleasedThisFrame(); // 本帧刚松开（仅1帧true）

        // 奔跑长按
        RunIsHeld = input.Player.Run.IsPressed();
    }
}
