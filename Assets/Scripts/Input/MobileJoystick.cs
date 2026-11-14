using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 모바일 가상 조이스틱. 터치(또는 Editor 마우스) 드래그를 8방향 단위 이동 벡터(Direction)로 변환한다.
/// - 터치 영역: background(RectTransform)의 Graphic이 Raycast를 받고, 이벤트는 부모인 이 오브젝트로 전달된다.
/// - 방향: 드래그 위치를 가장 가까운 45도 단위 8방향으로 스냅. Dead Zone(반경의 20%) 밖에서는 크기 1(대각선 포함)로 일정.
/// - 손잡이(handle)는 실제 드래그 위치를 최대 반경 내로 제한해 표시만 한다. 손잡이는 Raycast를 받지 않아야 한다.
/// - 멀티터치: 처음 누른 pointerId 하나만 추적한다. 다른 손가락(점프 버튼 등)의 이벤트는 무시한다.
/// - 터치 종료/비활성화/포커스 상실 시 Direction = Vector2.zero, 손잡이는 원래 위치로 복귀.
/// PlayerController를 직접 참조하지 않는다. PlayerInputRouter가 Direction을 읽는다.
/// </summary>
[DisallowMultipleComponent]
public class MobileJoystick : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    [Tooltip("조이스틱 원형 영역 (MobileJoystick/Background). 중심과 기본 반경의 기준이며 이 Image가 터치를 받는다.")]
    [SerializeField] private RectTransform background;

    [Tooltip("움직이는 손잡이 (MobileJoystick/JoystickButton). 시각 표시 전용.")]
    [SerializeField] private RectTransform handle;

    [Tooltip("손잡이 최대 이동 반경(Background 로컬 단위). 0 이하면 Background 너비의 절반.")]
    [Min(0f)] [SerializeField] private float movementRadius = 0f;

    [Tooltip("Dead Zone 비율 (최대 이동 반경 대비). v0.5 규격 초기값 0.2.")]
    [Range(0f, 0.95f)] [SerializeField] private float deadZoneRatio = 0.2f;

    private static readonly float Diagonal = Mathf.Sqrt(0.5f);

    /// <summary>0°(오른쪽)부터 반시계 방향 45° 간격의 8방향 단위 벡터.</summary>
    private static readonly Vector2[] Directions =
    {
        new Vector2(1f, 0f), new Vector2(Diagonal, Diagonal), new Vector2(0f, 1f), new Vector2(-Diagonal, Diagonal),
        new Vector2(-1f, 0f), new Vector2(-Diagonal, -Diagonal), new Vector2(0f, -1f), new Vector2(Diagonal, -Diagonal),
    };

    private bool initialized;
    private Vector3 handleRestLocalPosition;
    private int activePointerId;

    /// <summary>현재 이동 방향. 8방향 단위 벡터 또는 Vector2.zero.</summary>
    public Vector2 Direction { get; private set; }

    /// <summary>조이스틱을 누르고 있는지.</summary>
    public bool IsPressed { get; private set; }

    /// <summary>추적 중인 pointerId (IsPressed일 때만 의미 있음).</summary>
    public int ActivePointerId => activePointerId;

    /// <summary>적용 중인 최대 이동 반경(Background 로컬 단위).</summary>
    public float MovementRadius
    {
        get
        {
            if (movementRadius > 0f)
            {
                return movementRadius;
            }
            return background != null ? background.rect.width * 0.5f : 0f;
        }
    }

    public float DeadZoneRatio => deadZoneRatio;

    private void Awake()
    {
        EnsureInitialized();
        WarnIfMisconfigured();
    }

    private void OnDisable()
    {
        ResetInput(); // MobileControls 비활성화, 씬 전환 등으로 이벤트가 끊겨도 이동이 남지 않게
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            ResetInput();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            ResetInput();
        }
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        // 누른 직후부터 드래그를 바로 반영한다(드래그 임계값 무시).
        eventData.useDragThreshold = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsPressed)
        {
            return; // 이미 다른 터치가 조이스틱을 사용 중
        }
        EnsureInitialized();
        IsPressed = true;
        activePointerId = eventData.pointerId;
        UpdateFromScreenPoint(eventData.position, eventData.pressEventCamera);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!IsPressed || eventData.pointerId != activePointerId)
        {
            return;
        }
        UpdateFromScreenPoint(eventData.position, eventData.pressEventCamera);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!IsPressed || eventData.pointerId != activePointerId)
        {
            return;
        }
        ResetInput();
    }

    /// <summary>입력을 초기화하고 손잡이를 원래 위치로 되돌린다.</summary>
    public void ResetInput()
    {
        IsPressed = false;
        Direction = Vector2.zero;
        if (initialized && handle != null)
        {
            handle.localPosition = handleRestLocalPosition;
        }
    }

    private void UpdateFromScreenPoint(Vector2 screenPoint, Camera eventCamera)
    {
        if (background == null)
        {
            Direction = Vector2.zero;
            return;
        }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, screenPoint, eventCamera, out Vector2 local))
        {
            return;
        }

        Vector2 offset = local - background.rect.center; // Background 중심 기준
        float radius = MovementRadius;
        Direction = ComputeDirection(offset, radius, deadZoneRatio);
        MoveHandle(ClampHandleOffset(offset, radius));
    }

    private void MoveHandle(Vector2 backgroundLocalOffset)
    {
        if (handle == null)
        {
            return;
        }
        // Background 로컬 오프셋 → 월드 → 손잡이 부모 로컬. 배치/스케일이 달라도 같은 화면 거리만큼 움직인다.
        Vector3 worldDelta = background.TransformVector(backgroundLocalOffset);
        Vector3 localDelta = handle.parent != null ? handle.parent.InverseTransformVector(worldDelta) : worldDelta;
        handle.localPosition = handleRestLocalPosition + localDelta;
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        if (handle != null)
        {
            handleRestLocalPosition = handle.localPosition; // 기존 배치를 중앙(휴지) 위치로 사용
        }
    }

    private void WarnIfMisconfigured()
    {
        if (background == null)
        {
            Debug.LogWarning($"{name}: MobileJoystick의 Background가 연결되지 않아 입력을 받을 수 없습니다.", this);
            return;
        }
        var bgGraphic = background.GetComponent<UnityEngine.UI.Graphic>();
        if (bgGraphic == null || !bgGraphic.raycastTarget)
        {
            Debug.LogWarning($"{name}: {background.name}의 Image Raycast Target이 꺼져 있어 터치를 받을 수 없습니다.", this);
        }
        if (handle != null)
        {
            var selectable = handle.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable != null && selectable.enabled)
            {
                Debug.LogWarning($"{name}: {handle.name}의 Button이 터치 이벤트를 가로챕니다. Button을 제거(또는 비활성화)하세요.", this);
            }
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>
    /// Background 중심 기준 오프셋을 8방향 단위 벡터로 변환한다.
    /// 반경이 0 이하이거나 오프셋이 Dead Zone(반경 × 비율) 이내이면 Vector2.zero.
    /// </summary>
    public static Vector2 ComputeDirection(Vector2 offset, float radius, float deadZoneRatio)
    {
        if (radius <= 0f || float.IsNaN(offset.x) || float.IsNaN(offset.y))
        {
            return Vector2.zero;
        }
        float deadZone = radius * Mathf.Clamp01(deadZoneRatio);
        if (offset.sqrMagnitude <= deadZone * deadZone || offset.sqrMagnitude <= 0f)
        {
            return Vector2.zero;
        }

        float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg; // -180 ~ 180
        int sector = Mathf.RoundToInt(angle / 45f);
        sector = ((sector % 8) + 8) % 8;
        return Directions[sector];
    }

    /// <summary>손잡이 표시 오프셋: 실제 드래그 위치를 최대 반경 내로 제한한다.</summary>
    public static Vector2 ClampHandleOffset(Vector2 offset, float radius)
    {
        return Vector2.ClampMagnitude(offset, Mathf.Max(0f, radius));
    }
}
