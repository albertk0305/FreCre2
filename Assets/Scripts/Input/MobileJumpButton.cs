using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 모바일 점프 버튼. 누르는 순간(OnPointerDown) 한 번만 점프 요청을 등록한다.
/// - 길게 눌러도 새 요청이 생기지 않는다(자동 재점프 없음). 다시 점프하려면 떼고 다시 눌러야 한다.
/// - 점프 가능 여부(지면/쿨다운/사망)는 판단하지 않는다. PlayerController의 기존 조건이 그대로 적용된다.
/// - 같은 오브젝트의 Button은 눌림 색상 표시용으로만 사용한다(OnClick은 비워 둔다).
/// PlayerInputRouter가 ConsumeJumpPressed()로 요청을 가져간다.
/// </summary>
[DisallowMultipleComponent]
public class MobileJumpButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private bool jumpRequested;
    private int activePointerId;

    /// <summary>소비되지 않은 점프 요청이 있는지.</summary>
    public bool JumpRequested => jumpRequested;

    /// <summary>버튼을 누르고 있는지.</summary>
    public bool IsPressed { get; private set; }

    private void OnDisable()
    {
        ResetInput();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            ResetInput();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // PointerDown은 누르는 순간 한 번만 오므로 길게 눌러도 요청이 반복되지 않는다.
        // 새 탭(다른 손가락 포함)은 의도한 입력이므로 항상 새 요청으로 받는다(Up 누락 시 버튼이 막히지 않게).
        IsPressed = true;
        activePointerId = eventData.pointerId;
        jumpRequested = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsPressed && eventData.pointerId == activePointerId)
        {
            IsPressed = false;
        }
    }

    /// <summary>점프 요청을 반환하고 초기화한다. 한 번의 누름은 정확히 한 번만 true.</summary>
    public bool ConsumeJumpPressed()
    {
        bool requested = jumpRequested;
        jumpRequested = false;
        return requested;
    }

    /// <summary>누름 상태와 대기 중인 요청을 모두 지운다.</summary>
    public void ResetInput()
    {
        IsPressed = false;
        jumpRequested = false;
    }
}
