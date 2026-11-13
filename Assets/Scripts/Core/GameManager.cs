using System;
using UnityEngine;

/// <summary>
/// 게임의 시작/종료와 GameState를 관리한다. 세부 동작은 각 시스템에 명령만 전달한다.
/// - 시작: Playing 전환 → 피격 상태 초기화 → 플레이어 조작 활성화 → 미사일 생성 시작 → 이벤트
/// - 피격: PlayerHitDetector.OnPlayerHit 수신 → EndGame()
/// - 종료: GameOver 전환 → 플레이어 사망(이동/점프 중단) → 입력 차단 → 생성 중지 → 미사일 정지 → 이벤트
/// Time.timeScale은 변경하지 않는다.
/// Stage 5~9 확장 지점: OnGameStateChanged 구독(SurvivalTimer, DifficultyManager, UI, 애니메이션),
/// EndGame() 내부의 결과 계산/기록 저장 연결.
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerHitDetector playerHitDetector;
    [SerializeField] private MissileSpawner missileSpawner;
    [Tooltip("게임 오버 시 입력을 차단할 라우터. 비우면 PlayerController와 같은 오브젝트에서 찾는다.")]
    [SerializeField] private PlayerInputRouter inputRouter;

    [Header("Start")]
    [Tooltip("시작 UI가 없는 Stage 4에서는 씬 시작 시 자동으로 게임을 시작한다.")]
    [SerializeField] private bool startGameOnStart = true;

    private bool hitSubscribed;

    public GameState CurrentState { get; private set; } = GameState.Ready;

    /// <summary>게임을 끝낸 미사일 (없으면 null).</summary>
    public Missile LastHitMissile { get; private set; }

    /// <summary>GameState가 바뀔 때마다 새 상태로 발생한다.</summary>
    public event Action<GameState> OnGameStateChanged;

    private void Awake()
    {
        ResolveReferences();

        // GameManager가 생성 시작을 제어하므로 Spawner의 자동 시작을 끈다(중복 시작/Ready 상태 생성 방지).
        // 모든 Awake는 어떤 Start보다 먼저 실행되므로 Spawner.Start()에 반영된다.
        if (missileSpawner != null && missileSpawner.SpawnOnStart)
        {
            missileSpawner.SpawnOnStart = false;
            Debug.Log($"{name}: MissileSpawner의 Spawn On Start를 런타임에서 비활성화했습니다. Inspector에서도 꺼 두는 것을 권장합니다.", this);
        }
    }

    private void OnEnable()
    {
        SubscribeHit();
    }

    private void OnDisable()
    {
        UnsubscribeHit();
    }

    private void Start()
    {
        if (startGameOnStart)
        {
            StartGame();
        }
    }

    /// <summary>게임을 시작한다. 이미 Playing이면 아무것도 하지 않는다.</summary>
    public void StartGame()
    {
        if (CurrentState == GameState.Playing)
        {
            return;
        }

        ResolveReferences();
        SubscribeHit();
        LastHitMissile = null;

        if (playerHitDetector != null)
        {
            playerHitDetector.ResetHitState();
        }

        if (playerController != null)
        {
            if (playerController.IsDead)
            {
                playerController.ResetPlayer(); // 재시작(Stage 6) 대비: 사망 상태 해제 및 시작 위치 복귀
            }
            playerController.SetControlEnabled(true);
        }

        if (inputRouter != null)
        {
            inputRouter.SetInputEnabled(true);
        }

        if (missileSpawner != null)
        {
            missileSpawner.StartSpawning();
        }

        SetState(GameState.Playing);
    }

    /// <summary>PlayerHitDetector.OnPlayerHit 핸들러. Playing일 때만 게임을 끝낸다.</summary>
    public void HandlePlayerHit(Missile missile)
    {
        if (CurrentState != GameState.Playing)
        {
            return;
        }

        LastHitMissile = missile;
        EndGame();
    }

    /// <summary>게임을 종료한다. Playing이 아니면 아무것도 하지 않는다(중복 실행 방지).</summary>
    public void EndGame()
    {
        if (CurrentState != GameState.Playing)
        {
            return;
        }

        // 상태를 먼저 바꿔서 이후 처리 도중 들어오는 피격/종료 요청을 막는다.
        CurrentState = GameState.GameOver;

        if (playerController != null)
        {
            playerController.Die(); // 이동/점프 중단, IsDead (Stage 7 Death 애니메이션이 참조)
        }
        if (inputRouter != null)
        {
            inputRouter.SetInputEnabled(false);
        }
        if (missileSpawner != null)
        {
            missileSpawner.StopSpawning();
            missileSpawner.StopAllMissiles();
        }

        // Stage 5~9: 생존시간 정지, 결과 계산, 기록 저장, 사망 연출 → 결과 UI 순서로 여기서 연결한다.

        OnGameStateChanged?.Invoke(CurrentState);
    }

    private void SetState(GameState state)
    {
        if (CurrentState == state)
        {
            return;
        }
        CurrentState = state;
        OnGameStateChanged?.Invoke(state);
    }

    private void SubscribeHit()
    {
        if (hitSubscribed || playerHitDetector == null)
        {
            return;
        }
        playerHitDetector.OnPlayerHit += HandlePlayerHit;
        hitSubscribed = true;
    }

    private void UnsubscribeHit()
    {
        if (!hitSubscribed)
        {
            return;
        }
        if (playerHitDetector != null)
        {
            playerHitDetector.OnPlayerHit -= HandlePlayerHit;
        }
        hitSubscribed = false;
    }

    private void ResolveReferences()
    {
        if (inputRouter == null && playerController != null)
        {
            inputRouter = playerController.GetComponent<PlayerInputRouter>();
        }
    }
}
