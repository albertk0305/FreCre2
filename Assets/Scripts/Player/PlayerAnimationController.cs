using System;
using UnityEngine;

/// <summary>
/// PlayerController의 상태를 읽어 Visual의 Animator State를 코드로 직접 전환한다(Transition/Parameter 미사용).
/// - 상태 우선순위: IsDead → Death, 공중 상승 → Jump, 공중 하강 → Fall, 지면 이동 → Run, 그 외 → Idle
/// - 방향: FacingDirection을 가장 가까운 45° 8방향으로 변환(Missile.GetDirectionIndex와 같은 규칙). Death는 방향 없음.
/// - 목표 State가 현재 재생 중인 State와 같으면 Animator.Play()를 호출하지 않는다(매 프레임 재시작 방지).
/// - Death State 재생이 끝나면(normalizedTime ≥ 1) OnDeathFinished를 한 번 발생시킨다.
/// - 시작 연출: PlayStretching() 요청 후 Idle일 동안 Stretching을 1회 재생하고, 끝나면(normalizedTime ≥ 1) Idle로 돌아간다.
///   이동/점프/사망 상태가 되면 Stretching을 즉시 끝내고 해당 상태를 재생한다.
///   Animator/State가 없으면 즉시, 진행이 확인되지 않으면 deathFallbackDuration 후에 발생시켜 대기가 끝나지 않는 일을 막는다.
/// Visual의 Transform(점프 높이), Animator.speed, Root Motion, SpriteRenderer는 건드리지 않는다.
/// PlayerController는 이 컴포넌트의 존재를 모른다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationController : MonoBehaviour
{
    public enum AnimState
    {
        Idle = 0,
        Run = 1,
        Jump = 2,
        Fall = 3,
        Death = 4,
        /// <summary>시작 연출(카운트다운). PlayStretching() 요청 시에만, Idle 대신 1회 재생.</summary>
        Stretching = 5,
    }

    public const string LayerName = "Base Layer";
    /// <summary>Visual.controller에 등록된 사망 State 이름. (요청서의 DeathSouth에 해당, 정면 1방향)</summary>
    public const string DeathStateName = "Death";
    /// <summary>Visual.controller에 등록된 시작 연출 State 이름 (정면 1방향, Loop 없음).</summary>
    public const string StretchingStateName = "Stretching";
    public static readonly int StretchingStateHash = Animator.StringToHash(LayerName + "." + StretchingStateName);

    /// <summary>Missile.Direction8 인덱스 순서(0 S, 1 SE, 2 E, 3 NE, 4 N, 5 NW, 6 W, 7 SW)의 State 이름 접미사.</summary>
    private static readonly string[] DirectionSuffixes =
    {
        "South", "SouthEast", "East", "NorthEast", "North", "NorthWest", "West", "SouthWest",
    };

    private static readonly string[] LoopStatePrefixes = { "Idle", "Run", "Jump", "Fall" };

    // [state, direction] → Animator full path hash ("Base Layer.RunNorthEast")
    private static readonly int[,] StateHashes = BuildStateHashes();
    private static readonly int DeathStateHash = Animator.StringToHash(LayerName + "." + DeathStateName);

    [Header("References")]
    [Tooltip("상태를 읽을 플레이어. 비우면 부모에서 찾는다.")]
    [SerializeField] private PlayerController playerController;
    [Tooltip("State를 재생할 Animator. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private Animator animator;

    [Header("Thresholds")]
    [Tooltip("이 크기 이상 이동 입력이 있을 때 Run. PlayerController의 방향 갱신 기준(0.01)과 같다.")]
    [Min(0f)] [SerializeField] private float runThreshold = 0.01f;
    [Tooltip("공중에서 수직 속도가 이 값보다 클 때 Jump, 아니면 Fall.")]
    [Min(0f)] [SerializeField] private float jumpVelocityThreshold = 0.001f;
    [Tooltip("8방향 경계(22.5°) 근처에서 방향이 떨리지 않도록 현재 방향을 유지하는 여유 각도(°).")]
    [Range(0f, 20f)] [SerializeField] private float directionHysteresis = 5f;

    [Header("Death")]
    [Tooltip("Death 재생 완료가 확인되지 않을 때 OnDeathFinished를 강제로 발생시키는 시간(초). Death 클립 길이(1초)보다 길게.")]
    [Min(0.1f)] [SerializeField] private float deathFallbackDuration = 2f;

    [Header("Stretching (시작 연출)")]
    [Tooltip("Stretching 재생 완료가 확인되지 않을 때 Idle로 넘어가는 시간(초). Stretching 클립 길이(2.125초)보다 길게.")]
    [Min(0.1f)] [SerializeField] private float stretchingFallbackDuration = 3f;

    private int currentStateHash;
    private int stretchingPlayFrame = -1;
    private float stretchingElapsed;
    private int deathPlayFrame = -1;
    private float deathElapsed;
    private bool warnedMissingAnimator;

    /// <summary>사망 애니메이션이 끝났을 때(또는 보호 장치로 종료 처리될 때) 사망 1회당 한 번 발생한다.</summary>
    public event Action OnDeathFinished;

    public PlayerController PlayerController => playerController;
    public Animator Animator => animator;
    public AnimState CurrentState { get; private set; } = AnimState.Idle;
    public Missile.Direction8 CurrentDirection { get; private set; } = Missile.Direction8.South;
    /// <summary>마지막으로 요청한 State 이름 (예: "RunNorthEast", "Death"). 아직 없으면 빈 문자열.</summary>
    public string CurrentStateName { get; private set; } = string.Empty;
    /// <summary>Animator.Play()를 요청한 횟수(같은 State 재요청이 없는지 확인용).</summary>
    public int PlayRequestCount { get; private set; }
    public bool IsDeathFinished { get; private set; }
    /// <summary>Stretching 시작 연출이 진행 중인지. 끝나면(또는 Idle이 아닌 상태가 되면) false.</summary>
    public bool IsPlayingStretching { get; private set; }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        // Animator는 비활성화되면 기본 State로 돌아가므로 다시 켜질 때 현재 State를 새로 재생하게 한다.
        currentStateHash = 0;
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>상태/방향을 결정해 필요할 때만 State를 전환하고, Death 완료를 확인한다. 테스트에서 직접 호출할 수 있다.</summary>
    public void Tick(float deltaTime)
    {
        ResolveReferences();
        if (playerController == null)
        {
            return;
        }

        AnimState state = ResolveState(
            playerController.IsDead,
            playerController.IsGrounded,
            playerController.VerticalVelocity,
            playerController.MoveDirection,
            runThreshold,
            jumpVelocityThreshold);

        if (state != AnimState.Death)
        {
            CurrentDirection = (Missile.Direction8)ResolveDirectionIndex(
                playerController.FacingDirection, (int)CurrentDirection, directionHysteresis);

            if (CurrentState == AnimState.Death)
            {
                ResetDeathTracking(); // 재시작(ResetPlayer)으로 사망 해제: 이전 사망의 완료 이벤트는 더 이상 발생하지 않음
            }
        }

        // 시작 연출: 서 있는(Idle) 동안만 Stretching을 유지한다. 이동/점프/사망이 생기면 즉시 원래 상태에 양보한다.
        if (IsPlayingStretching)
        {
            if (state == AnimState.Idle && TickStretching(deltaTime))
            {
                return;
            }
            IsPlayingStretching = false;
        }

        CurrentState = state;
        PlayIfChanged(state == AnimState.Death ? DeathStateHash : StateHashes[(int)state, (int)CurrentDirection],
            GetStateName(state, CurrentDirection));

        if (state == AnimState.Death)
        {
            UpdateDeath(deltaTime);
        }
    }

    /// <summary>
    /// Animation Event용(선택). Death 클립 마지막 프레임에 이 함수를 등록하면 그 시점에 완료 처리한다.
    /// 등록하지 않아도 Animator 재생 상태 확인으로 완료를 감지한다. 중복 호출되어도 이벤트는 한 번만 발생한다.
    /// </summary>
    public void NotifyDeathAnimationFinished()
    {
        if (CurrentState == AnimState.Death)
        {
            FinishDeath();
        }
    }

    /// <summary>
    /// 시작 연출 Stretching을 처음부터 1회 재생하도록 요청한다(카운트다운 시작 시). 끝나면 자동으로 Idle로 돌아간다.
    /// 사망 상태이면 무시한다. 이미 재생 중이면 다시 시작하지 않는다.
    /// </summary>
    public void PlayStretching()
    {
        ResolveReferences();
        if (IsPlayingStretching || (playerController != null && playerController.IsDead))
        {
            return;
        }
        IsPlayingStretching = true;
        stretchingElapsed = 0f;
        stretchingPlayFrame = -1;
    }

    /// <summary>Stretching을 재생/유지한다. 계속 재생 중이면 true, 끝났으면(또는 재생 불가) false.</summary>
    private bool TickStretching(float deltaTime)
    {
        CurrentState = AnimState.Stretching;
        PlayIfChanged(StretchingStateHash, StretchingStateName);

        if (!CanPlay(StretchingStateHash))
        {
            return false; // State가 없거나 재생할 수 없으면 기다리지 않고 Idle로
        }

        stretchingElapsed += Mathf.Max(0f, deltaTime);
        // Play() 요청은 다음 Animator 평가에 반영되므로, 요청한 다음 프레임부터 실제 State를 확인한다.
        if (Time.frameCount > stretchingPlayFrame)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.fullPathHash == StretchingStateHash && info.normalizedTime >= 1f)
            {
                return false; // 1회 재생 완료 → Idle
            }
        }

        if (stretchingElapsed >= stretchingFallbackDuration)
        {
            Debug.LogWarning($"{name}: Stretching 완료를 확인하지 못해 {stretchingFallbackDuration:0.##}초 후 Idle로 전환합니다.", this);
            return false;
        }
        return true;
    }

    private void PlayIfChanged(int stateHash, string stateName)
    {
        if (stateHash == currentStateHash)
        {
            return; // 같은 State: 이어서 재생
        }

        currentStateHash = stateHash;
        CurrentStateName = stateName;
        PlayRequestCount++;

        if (stateHash == DeathStateHash)
        {
            deathPlayFrame = Time.frameCount;
            deathElapsed = 0f;
        }
        else if (stateHash == StretchingStateHash)
        {
            stretchingPlayFrame = Time.frameCount;
        }

        if (CanPlay(stateHash))
        {
            animator.Play(stateHash, 0, 0f);
        }
    }

    private void UpdateDeath(float deltaTime)
    {
        if (IsDeathFinished)
        {
            return;
        }

        if (!CanPlay(DeathStateHash))
        {
            FinishDeath(); // 재생할 수 없으면 기다릴 것이 없다
            return;
        }

        deathElapsed += Mathf.Max(0f, deltaTime);

        // Play() 요청은 다음 Animator 평가에 반영되므로, 요청한 다음 프레임부터 실제 State를 확인한다.
        if (Time.frameCount > deathPlayFrame)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.fullPathHash == DeathStateHash && info.normalizedTime >= 1f)
            {
                FinishDeath();
                return;
            }
        }

        if (deathElapsed >= deathFallbackDuration)
        {
            Debug.LogWarning($"{name}: Death 애니메이션 완료를 확인하지 못해 {deathFallbackDuration:0.##}초 후 완료 처리합니다.", this);
            FinishDeath();
        }
    }

    private void FinishDeath()
    {
        if (IsDeathFinished)
        {
            return;
        }
        IsDeathFinished = true;
        OnDeathFinished?.Invoke();
    }

    private void ResetDeathTracking()
    {
        IsDeathFinished = false;
        deathElapsed = 0f;
        deathPlayFrame = -1;
    }

    /// <summary>
    /// 실제로 State를 재생할 수 있는지. Play 모드가 아니거나(EditMode 테스트), Animator/Controller/State가 없으면 false.
    /// false여도 상태 결정과 CurrentStateName 갱신은 그대로 하며, Death는 기다리지 않고 즉시 완료 처리한다.
    /// </summary>
    private bool CanPlay(int stateHash)
    {
        if (!Application.isPlaying)
        {
            return false;
        }

        bool ok = animator != null
                  && animator.isActiveAndEnabled
                  && animator.runtimeAnimatorController != null
                  && animator.HasState(0, stateHash);
        if (!ok && !warnedMissingAnimator)
        {
            warnedMissingAnimator = true;
            Debug.LogWarning($"{name}: Animator 또는 State '{CurrentStateName}'를 사용할 수 없어 애니메이션을 재생하지 않습니다.", this);
        }
        return ok;
    }

    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = GetComponentInParent<PlayerController>();
        }
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>우선순위: Death → Jump(공중, v &gt; threshold) → Fall(공중) → Run(이동) → Idle.</summary>
    public static AnimState ResolveState(bool isDead, bool isGrounded, float verticalVelocity, Vector2 moveDirection,
        float runThreshold, float jumpVelocityThreshold)
    {
        if (isDead)
        {
            return AnimState.Death;
        }
        if (!isGrounded)
        {
            return verticalVelocity > jumpVelocityThreshold ? AnimState.Jump : AnimState.Fall;
        }
        return moveDirection.sqrMagnitude >= runThreshold * runThreshold && moveDirection.sqrMagnitude > 0f
            ? AnimState.Run
            : AnimState.Idle;
    }

    /// <summary>가장 가까운 45° 방향 인덱스(Missile.Direction8 순서). zero/NaN은 South.</summary>
    public static int GetDirectionIndex(Vector2 facing)
    {
        return Missile.GetDirectionIndex(facing);
    }

    /// <summary>
    /// 8방향 인덱스를 결정하되, 현재 방향 중심에서 22.5° + hysteresis 이내면 현재 방향을 유지한다.
    /// facing이 zero/NaN이면 South.
    /// </summary>
    public static int ResolveDirectionIndex(Vector2 facing, int currentIndex, float hysteresisDegrees)
    {
        if (facing.sqrMagnitude <= 0f || float.IsNaN(facing.x) || float.IsNaN(facing.y))
        {
            return (int)Missile.Direction8.South;
        }

        int nearest = Missile.GetDirectionIndex(facing);
        if (nearest == currentIndex || currentIndex < 0 || currentIndex >= Missile.DirectionCount || hysteresisDegrees <= 0f)
        {
            return nearest;
        }

        float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        float currentCenter = -90f + 45f * currentIndex; // South(-90°)에서 반시계 방향
        return Mathf.Abs(Mathf.DeltaAngle(angle, currentCenter)) <= 22.5f + hysteresisDegrees ? currentIndex : nearest;
    }

    /// <summary>Animator State 이름. 예: (Run, NorthEast) → "RunNorthEast", Death → "Death".</summary>
    public static string GetStateName(AnimState state, Missile.Direction8 direction)
    {
        if (state == AnimState.Death)
        {
            return DeathStateName;
        }
        if (state == AnimState.Stretching)
        {
            return StretchingStateName;
        }
        return LoopStatePrefixes[(int)state] + DirectionSuffixes[(int)direction];
    }

    /// <summary>Animator Controller에 있어야 하는 State 이름 33개.</summary>
    public static string[] GetAllStateNames()
    {
        var names = new string[LoopStatePrefixes.Length * DirectionSuffixes.Length + 1];
        int i = 0;
        for (int s = 0; s < LoopStatePrefixes.Length; s++)
        {
            for (int d = 0; d < DirectionSuffixes.Length; d++)
            {
                names[i++] = GetStateName((AnimState)s, (Missile.Direction8)d);
            }
        }
        names[i] = DeathStateName;
        return names;
    }

    private static int[,] BuildStateHashes()
    {
        var hashes = new int[LoopStatePrefixes.Length, DirectionSuffixes.Length];
        for (int s = 0; s < LoopStatePrefixes.Length; s++)
        {
            for (int d = 0; d < DirectionSuffixes.Length; d++)
            {
                hashes[s, d] = Animator.StringToHash(LayerName + "." + GetStateName((AnimState)s, (Missile.Direction8)d));
            }
        }
        return hashes;
    }
}
