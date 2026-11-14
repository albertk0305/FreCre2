using UnityEngine;

/// <summary>
/// 플레이어의 지면상 이동과 점프 상태를 관리한다.
/// - 입력은 PlayerInputRouter의 공통 입력(MoveVector, ConsumeJumpPressed)만 사용한다.
/// - 지면 좌표(GroundPosition)는 Rigidbody2D로 이동하고 ArenaBounds로 제한한다.
/// - 점프는 JumpHeight로 따로 관리하며 Visual 자식만 위로 이동시킨다. Collider/Rigidbody2D는 지면에 남는다.
/// 애니메이션과 장치별 키 처리는 담당하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInputRouter inputRouter;
    [SerializeField] private ArenaBounds arenaBounds;
    [Tooltip("점프 시 위로 이동시킬 그래픽 자식 (Player/Visual).")]
    [SerializeField] private Transform visual;
    [Tooltip("경계 계산에 사용할 지면 충돌 원. 비우면 같은 오브젝트의 CircleCollider2D를 사용.")]
    [SerializeField] private CircleCollider2D groundCollider;

    [Header("Move")]
    [Min(0f)] [SerializeField] private float moveSpeed = 1.33f;

    [Header("Jump")]
    [Min(0f)] [SerializeField] private float jumpPower = 2.50f;
    [Min(0.01f)] [SerializeField] private float gravity = 6.67f;
    [Tooltip("점프 시작 시점부터 다음 점프까지 필요한 최소 간격(초).")]
    [Min(0f)] [SerializeField] private float jumpCooldown = 1.15f;

    [Header("Start")]
    [SerializeField] private bool controlEnabledOnStart = true;

    private static readonly Vector2 DefaultFacing = Vector2.down; // 정면(South) 스프라이트 기준

    private Rigidbody2D body;
    private Vector3 visualBaseLocalPosition;
    private Vector2 spawnPosition;
    private bool initialized;

    // 고정 스텝마다 누적되는 플레이어 자체 시계. 쿨다운 계산에 사용한다.
    private float simulationTime;
    private float previousJumpHeight;

    // ---- 외부 읽기 전용 상태 ----
    public Vector2 GroundPosition { get; private set; }
    /// <summary>지면에서 떠 있는 높이 (unit). Stage 4 PlayerHitDetector가 참조한다.</summary>
    public float JumpHeight { get; private set; }
    /// <summary>Visual에 마지막으로 적용한(프레임 보간된) 표시 높이. 그림자 등 연출이 Visual과 같은 높이를 쓰도록 읽기 전용으로 제공.</summary>
    public float VisualJumpHeight { get; private set; }
    public float VerticalVelocity { get; private set; }
    public bool IsGrounded { get; private set; } = true;
    /// <summary>마지막 점프 시작 시각 (SimulationTime 기준). 시작/리셋 시 -Infinity → 즉시 점프 가능.</summary>
    public float LastJumpTime { get; private set; } = float.NegativeInfinity;
    public Vector2 MoveDirection { get; private set; }
    public Vector2 FacingDirection { get; private set; } = DefaultFacing;
    public bool CanControl { get; private set; }
    public bool IsDead { get; private set; }
    public float SimulationTime => simulationTime;

    public float MoveSpeed => moveSpeed;
    public float JumpPower => jumpPower;
    public float Gravity => gravity;
    public float JumpCooldown => jumpCooldown;

    /// <summary>쿨다운 남은 시간(초). 0이면 쿨다운 완료.</summary>
    public float JumpCooldownRemaining => Mathf.Max(0f, jumpCooldown - (simulationTime - LastJumpTime));

    /// <summary>ArenaBounds 제한에 사용하는 월드 단위 충돌 반지름.</summary>
    public float PlayerRadius
    {
        get
        {
            if (groundCollider == null)
            {
                return 0f;
            }
            Vector3 s = groundCollider.transform.lossyScale;
            return groundCollider.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
        }
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void FixedUpdate()
    {
        Tick(Time.fixedDeltaTime);
    }

    private void LateUpdate()
    {
        // 고정 스텝 사이를 보간해 높은 주사율에서도 점프가 매끄럽게 보이게 한다.
        float alpha = Time.fixedDeltaTime > 0f
            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
            : 1f;
        ApplyVisualHeight(Mathf.Lerp(previousJumpHeight, JumpHeight, alpha));
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        jumpPower = Mathf.Max(0f, jumpPower);
        gravity = Mathf.Max(0.01f, gravity);
        jumpCooldown = Mathf.Max(0f, jumpCooldown);
    }

    /// <summary>
    /// 고정 스텝 1회 분량의 시뮬레이션. FixedUpdate에서 호출되며 테스트에서 직접 호출할 수도 있다.
    /// 순서: 시계 진행 → 이동 → 점프 요청 처리 → 점프 높이 갱신.
    /// </summary>
    public void Tick(float deltaTime)
    {
        EnsureInitialized();
        if (deltaTime <= 0f || float.IsNaN(deltaTime))
        {
            return;
        }

        simulationTime += deltaTime;
        previousJumpHeight = JumpHeight;

        Vector2 moveInput = inputRouter != null ? inputRouter.MoveVector : Vector2.zero;
        // 점프 요청은 매 스텝 소비한다. 점프할 수 없는 상태에서 누른 입력이 쌓였다가 나중에 실행되지 않도록.
        bool jumpRequested = inputRouter != null && inputRouter.ConsumeJumpPressed();

        Move(moveInput, deltaTime);
        if (jumpRequested)
        {
            TryJump();
        }
        UpdateJump(deltaTime);
    }

    /// <summary>
    /// 지면 이동. 입력 크기를 최대 1로 제한(대각선 정규화)하고 ArenaBounds로 보정한다.
    /// 점프 상태와 관계없이 지면 좌표 기준으로 이동한다.
    /// </summary>
    public void Move(Vector2 input, float deltaTime)
    {
        EnsureInitialized();

        if (!CanControl || IsDead)
        {
            MoveDirection = Vector2.zero;
            return;
        }

        Vector2 direction = PlayerInputRouter.NormalizeMove(input);
        MoveDirection = direction;
        if (direction.sqrMagnitude > 0.0001f)
        {
            FacingDirection = direction.normalized;
        }

        Vector2 target = GroundPosition + direction * (moveSpeed * Mathf.Max(0f, deltaTime));
        SetGroundPosition(target, teleport: false);
    }

    /// <summary>점프 시작을 시도한다. 지면에 있고 쿨다운이 끝났을 때만 성공한다.</summary>
    public bool TryJump()
    {
        EnsureInitialized();

        if (!CanControl || IsDead || !IsGrounded)
        {
            return false;
        }
        if (simulationTime - LastJumpTime < jumpCooldown)
        {
            return false;
        }

        IsGrounded = false;
        VerticalVelocity = jumpPower;
        LastJumpTime = simulationTime;
        return true;
    }

    /// <summary>점프 높이와 수직 속도를 갱신한다. 등가속도 정확해를 사용해 스텝 크기와 무관한 궤적을 만든다.</summary>
    public void UpdateJump(float deltaTime)
    {
        EnsureInitialized();

        if (IsGrounded || deltaTime <= 0f)
        {
            return;
        }

        float height = JumpHeight + VerticalVelocity * deltaTime - 0.5f * gravity * deltaTime * deltaTime;
        float velocity = VerticalVelocity - gravity * deltaTime;

        if (height <= 0f)
        {
            Land();
        }
        else
        {
            JumpHeight = height;
            VerticalVelocity = velocity;
        }

        ApplyVisualHeight(JumpHeight);
    }

    /// <summary>조작 활성화/비활성화.</summary>
    public void SetControlEnabled(bool enabled)
    {
        CanControl = enabled && !IsDead;
        if (!CanControl)
        {
            MoveDirection = Vector2.zero;
        }
    }

    /// <summary>사망 상태로 전환한다. 이동/점프 입력을 막는다. 공중이었다면 중력에 따라 착지한다.</summary>
    public void Die()
    {
        IsDead = true;
        CanControl = false;
        MoveDirection = Vector2.zero;
    }

    /// <summary>시작 위치에서 플레이어 상태를 초기화한다.</summary>
    public void ResetPlayer()
    {
        EnsureInitialized();
        ResetPlayer(spawnPosition);
    }

    /// <summary>지정한 지면 위치에서 플레이어 상태를 초기화한다. 리셋 직후에는 바로 점프할 수 있다.</summary>
    public void ResetPlayer(Vector2 position)
    {
        EnsureInitialized();

        IsDead = false;
        CanControl = controlEnabledOnStart;
        JumpHeight = 0f;
        previousJumpHeight = 0f;
        VerticalVelocity = 0f;
        IsGrounded = true;
        LastJumpTime = float.NegativeInfinity;
        MoveDirection = Vector2.zero;
        FacingDirection = DefaultFacing;

        SetGroundPosition(position, teleport: true);
        ApplyVisualHeight(0f);
    }

    private void Land()
    {
        JumpHeight = 0f;
        VerticalVelocity = 0f;
        IsGrounded = true;
    }

    private void SetGroundPosition(Vector2 target, bool teleport)
    {
        GroundPosition = arenaBounds != null
            ? arenaBounds.ClampPosition(target, PlayerRadius)
            : target;

        if (teleport)
        {
            body.position = GroundPosition;
            transform.position = new Vector3(GroundPosition.x, GroundPosition.y, transform.position.z);
        }
        else
        {
            body.MovePosition(GroundPosition);
        }
    }

    private void ApplyVisualHeight(float height)
    {
        VisualJumpHeight = height;
        if (visual != null)
        {
            visual.localPosition = visualBaseLocalPosition + new Vector3(0f, height, 0f);
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;

        body = GetComponent<Rigidbody2D>();
        if (inputRouter == null)
        {
            inputRouter = GetComponent<PlayerInputRouter>();
        }
        if (groundCollider == null)
        {
            groundCollider = GetComponent<CircleCollider2D>();
        }
        if (visual == null)
        {
            visual = transform.Find("Visual");
        }

        if (inputRouter == null)
        {
            Debug.LogWarning($"{name}: PlayerInputRouter가 연결되지 않아 입력을 받을 수 없습니다.", this);
        }
        if (arenaBounds == null)
        {
            Debug.LogWarning($"{name}: ArenaBounds가 연결되지 않아 경기장 경계 제한이 적용되지 않습니다.", this);
        }
        if (visual == null)
        {
            Debug.LogWarning($"{name}: Visual이 연결되지 않아 점프 높이가 표시되지 않습니다.", this);
        }

        visualBaseLocalPosition = visual != null ? visual.localPosition : Vector3.zero;

        Vector2 start = body.position;
        spawnPosition = arenaBounds != null ? arenaBounds.ClampPosition(start, PlayerRadius) : start;
        ResetPlayer(spawnPosition);
    }
}
