using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 입력 장치별 입력을 공통 형식(MoveVector, 점프 요청)으로 변환한다.
/// PlayerController는 이 클래스의 출력만 사용하며 어떤 장치에서 입력되었는지 알 필요가 없다.
///
/// Stage 2: PC 키보드(WASD / 방향키 / Space)만 구현.
/// Stage 10: 모바일 입력은 SubmitInput()으로 같은 경로에 공급하면 PlayerController 수정이 필요 없다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputRouter : MonoBehaviour
{
    [Tooltip("시작 시 ControlSettings에 저장된 조작 방식을 읽는다. 끄면 아래 Control Mode 값을 사용한다.")]
    [SerializeField] private bool loadModeFromSettings = true;

    [SerializeField] private ControlMode controlMode = ControlMode.PC;

    [SerializeField] private bool inputEnabled = true;

    [Tooltip("Mobile 모드에서만 활성화할 모바일 조작 UI 루트(UICanvas/MobileControls). 비우면 UI 표시를 바꾸지 않는다.")]
    [SerializeField] private GameObject mobileControlsRoot;

    private Vector2 moveVector;
    private bool jumpRequested;

    /// <summary>현재 조작 방식.</summary>
    public ControlMode CurrentControlMode => controlMode;

    /// <summary>현재 이동 입력. 크기는 최대 1 (대각선 정규화).</summary>
    public Vector2 MoveVector => moveVector;

    /// <summary>소비되지 않은 점프 요청이 있는지 (소비하지 않고 확인만).</summary>
    public bool JumpPressed => jumpRequested;

    public bool InputEnabled => inputEnabled;

    private void Awake()
    {
        // loadModeFromSettings가 꺼져 있어도 Inspector 값으로 모바일 UI 표시를 맞춘다.
        Initialize(loadModeFromSettings ? ControlSettings.GetControlMode() : controlMode);
    }

    private void Update()
    {
        UpdateInput();
    }

    /// <summary>조작 방식을 설정하고, Mobile일 때만 MobileControlsRoot를 활성화한다.</summary>
    public void Initialize(ControlMode mode)
    {
        controlMode = mode;
        // Stage 9: 모바일 조작 UI 표시/숨김만 처리. 조이스틱/점프 버튼 입력 연결은 Stage 10.
        if (mobileControlsRoot != null)
        {
            mobileControlsRoot.SetActive(mode == ControlMode.Mobile);
        }
    }

    /// <summary>현재 조작 방식의 입력을 읽는다. Update에서 매 프레임 호출된다.</summary>
    public void UpdateInput()
    {
        if (!inputEnabled)
        {
            return;
        }

        switch (controlMode)
        {
            case ControlMode.PC:
            default:
                // Stage 10 전까지 Mobile 모드도 키보드 입력으로 대체한다.
                ReadKeyboard(Keyboard.current);
                break;
        }
    }

    /// <summary>
    /// 장치와 무관한 공통 입력 경로. 모든 입력 소스(키보드, 향후 모바일)는 이 메서드로 값을 공급한다.
    /// jumpPressedThisFrame=true이면 점프 요청을 버퍼에 저장하며, ConsumeJumpPressed()로 소비될 때까지 유지된다.
    /// </summary>
    public void SubmitInput(Vector2 rawMove, bool jumpPressedThisFrame)
    {
        if (!inputEnabled)
        {
            return;
        }

        moveVector = NormalizeMove(rawMove);
        if (jumpPressedThisFrame)
        {
            jumpRequested = true;
        }
    }

    /// <summary>점프 요청을 반환하고 소비한다. 한 번의 입력은 정확히 한 번만 true를 반환한다.</summary>
    public bool ConsumeJumpPressed()
    {
        bool requested = jumpRequested;
        jumpRequested = false;
        return requested;
    }

    /// <summary>입력 활성화/비활성화. 비활성화 시 이동 입력과 대기 중인 점프 요청을 비운다.</summary>
    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            moveVector = Vector2.zero;
            jumpRequested = false;
        }
    }

    private void ReadKeyboard(Keyboard keyboard)
    {
        if (keyboard == null)
        {
            SubmitInput(Vector2.zero, false);
            return;
        }

        Vector2 raw = ComputeMoveVector(
            left: keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
            right: keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed,
            down: keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
            up: keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed);

        // wasPressedThisFrame: 누르고 있어도 누른 순간 한 번만 true → 자동 재점프 방지.
        SubmitInput(raw, keyboard.spaceKey.wasPressedThisFrame);
    }

    /// <summary>방향 키 상태로 8방향 이동 벡터를 만든다. 반대 방향 동시 입력은 상쇄된다.</summary>
    public static Vector2 ComputeMoveVector(bool left, bool right, bool down, bool up)
    {
        float x = (right ? 1f : 0f) - (left ? 1f : 0f);
        float y = (up ? 1f : 0f) - (down ? 1f : 0f);
        return NormalizeMove(new Vector2(x, y));
    }

    /// <summary>크기를 최대 1로 제한한다. 대각선(√2)은 1로 정규화되고, 아날로그 입력의 1 미만 크기는 유지된다.</summary>
    public static Vector2 NormalizeMove(Vector2 raw)
    {
        if (float.IsNaN(raw.x) || float.IsNaN(raw.y))
        {
            return Vector2.zero;
        }
        return Vector2.ClampMagnitude(raw, 1f);
    }
}
