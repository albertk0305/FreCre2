using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미사일과 플레이어의 지면상 겹침을 감지하고, 점프 높이로 실제 피격 여부를 판정한다.
/// - 피격: JumpHeight &lt; DodgeHeightThreshold, 회피: JumpHeight &gt;= DodgeHeightThreshold
/// - 겹침 감지 경로 두 가지(둘 중 어느 쪽이 먼저 감지해도 결과는 같다):
///   1) OnTriggerEnter2D / OnTriggerStay2D 콜백
///   2) 매 물리 스텝 Collider2D.Overlap 질의 (콜백 생성 규칙이나 Sleep 상태와 무관한 지속 겹침 검사)
///   → 점프 중 겹친 뒤 겹친 채로 하강해 회피 높이 아래로 내려오는 경우도 피격된다.
/// - 실제 피격 시 OnPlayerHit을 한 번만 발생시킨다. 게임 상태 변경, 미사일 정지, UI 등은 하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // PlayerController(기본 0)의 FixedUpdate 이후에 최신 JumpHeight로 판정
public class PlayerHitDetector : MonoBehaviour
{
    public const float DefaultDodgeHeightThreshold = 0.18f;

    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [Tooltip("게임 상태 확인용. 비어 있으면 상태 검사 없이 판정한다(단독 테스트용).")]
    [SerializeField] private GameManager gameManager;
    [Tooltip("미사일과의 겹침을 검사할 플레이어 지면 Collider. 비우면 같은 오브젝트의 Collider2D.")]
    [SerializeField] private Collider2D hitCollider;

    [Header("Judgement")]
    [Tooltip("이 높이 이상이면 미사일을 회피한다 (unit).")]
    [Min(0f)] [SerializeField] private float dodgeHeightThreshold = DefaultDodgeHeightThreshold;

    [Tooltip("매 물리 스텝 Collider2D.Overlap으로 지속 겹침을 검사한다. Trigger 콜백과 함께 사용.")]
    [SerializeField] private bool useOverlapQuery = true;

    private readonly List<Collider2D> overlapBuffer = new List<Collider2D>(8);
    private ContactFilter2D overlapFilter;
    private bool initialized;

    /// <summary>실제 피격이 확인되면 한 번 발생한다.</summary>
    public event Action<Missile> OnPlayerHit;

    public bool IsHitProcessed { get; private set; }
    public float DodgeHeightThreshold => dodgeHeightThreshold;
    public PlayerController PlayerController => playerController;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void FixedUpdate()
    {
        if (useOverlapQuery)
        {
            CheckOverlaps();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        EvaluateCollider(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        EvaluateCollider(other);
    }

    /// <summary>게임 초기화 시 피격 처리 상태를 되돌린다.</summary>
    public void ResetHitState()
    {
        IsHitProcessed = false;
    }

    /// <summary>현재 플레이어 Collider와 겹친 모든 Collider를 검사한다.</summary>
    public void CheckOverlaps()
    {
        EnsureInitialized();
        if (hitCollider == null || !hitCollider.enabled || IsHitProcessed)
        {
            return;
        }

        overlapBuffer.Clear();
        hitCollider.Overlap(overlapFilter, overlapBuffer);
        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            if (EvaluateCollider(overlapBuffer[i]))
            {
                return;
            }
        }
    }

    /// <summary>겹친 Collider가 Missile이면 미사일 피격 판정을 한다. Missile이 아니면 무시.</summary>
    public bool EvaluateCollider(Collider2D other)
    {
        if (other == null)
        {
            return false;
        }

        Missile missile = other.GetComponentInParent<Missile>();
        return missile != null && CheckMissileCollision(missile);
    }

    /// <summary>
    /// 겹친 미사일에 대한 피격 판정. 피격이면 OnPlayerHit을 발생시키고 true를 반환한다.
    /// 조건: 미처리 상태, 게임 Playing, 미사일 유효, JumpHeight &lt; 회피 높이.
    /// </summary>
    public bool CheckMissileCollision(Missile missile)
    {
        EnsureInitialized();

        if (IsHitProcessed || missile == null || missile.IsDespawned)
        {
            return false;
        }
        if (gameManager != null && gameManager.CurrentState != GameState.Playing)
        {
            return false;
        }

        float jumpHeight = playerController != null ? playerController.JumpHeight : 0f;
        if (!IsHitByHeight(jumpHeight, dodgeHeightThreshold))
        {
            return false; // 회피 성공: 겹침이 계속되면 다음 스텝에 다시 판정한다.
        }

        IsHitProcessed = true;
        OnPlayerHit?.Invoke(missile);
        return true;
    }

    /// <summary>높이 판정: JumpHeight &lt; threshold 이면 피격.</summary>
    public static bool IsHitByHeight(float jumpHeight, float threshold)
    {
        return jumpHeight < threshold;
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }
        if (hitCollider == null)
        {
            hitCollider = GetComponent<Collider2D>();
        }

        overlapFilter = ContactFilter2D.noFilter; // 레이어 구분 없이 모두 → Missile 여부는 컴포넌트로 판단
        overlapFilter.useTriggers = true; // 미사일 Collider는 Trigger
    }
}
