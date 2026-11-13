using System;
using UnityEngine;

/// <summary>
/// 게임의 시작/종료와 GameState를 관리한다. 세부 동작은 각 시스템에 명령만 전달한다.
/// - 시작: 피격 상태 초기화 → 플레이어 조작 활성화 → 생존시간 초기화/측정 시작 → 미사일 생성 시작 → Playing 전환 → 이벤트
/// - 피격: PlayerHitDetector.OnPlayerHit 수신 → EndGame()
/// - 종료: GameOver 전환 → 생존시간 정지 → 플레이어 사망(이동/점프 중단) → 입력 차단 → 생성 중지 → 미사일 정지
///         → 최종 생존시간으로 RecordStore.TryUpdateRecord → GameResult 생성 → OnGameStateChanged → OnGameResultReady
/// - 기록 저장은 게임 오버(미사일 피격)에서만 한다. Exit/Restart 버튼의 씬 전환은 EndGame()을 거치지 않으므로 기록되지 않는다.
/// Time.timeScale은 변경하지 않는다.
/// Stage 7/9 확장 지점: Death 애니메이션 완료 후 결과 UI를 띄우려면 PublishGameResult() 호출 시점만 늦추면 된다
/// (기록은 이미 EndGame()에서 확정 저장되므로 연출 도중 씬을 나가도 기록은 유지된다).
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerHitDetector playerHitDetector;
    [SerializeField] private MissileSpawner missileSpawner;
    [Tooltip("생존시간 원본. 시작 시 0초로 초기화 후 측정, 게임 오버 시 정지.")]
    [SerializeField] private SurvivalTimer survivalTimer;
    [Tooltip("게임 오버 시 입력을 차단할 라우터. 비우면 PlayerController와 같은 오브젝트에서 찾는다.")]
    [SerializeField] private PlayerInputRouter inputRouter;

    [Header("Start")]
    [Tooltip("시작 UI가 없는 Stage 4에서는 씬 시작 시 자동으로 게임을 시작한다.")]
    [SerializeField] private bool startGameOnStart = true;

    private bool hitSubscribed;

    public GameState CurrentState { get; private set; } = GameState.Ready;

    /// <summary>게임을 끝낸 미사일 (없으면 null).</summary>
    public Missile LastHitMissile { get; private set; }

    /// <summary>마지막 게임 오버의 결과. HasGameResult가 false면 기본값.</summary>
    public GameResult LastGameResult { get; private set; }

    /// <summary>이번 판의 게임 오버 결과가 확정되었는지. StartGame()에서 false로 초기화.</summary>
    public bool HasGameResult { get; private set; }

    /// <summary>GameState가 바뀔 때마다 새 상태로 발생한다.</summary>
    public event Action<GameState> OnGameStateChanged;

    /// <summary>게임 오버 결과(생존시간/최고기록/신기록 여부)가 준비되면 한 번 발생한다. Stage 9 GameOverUI용.</summary>
    public event Action<GameResult> OnGameResultReady;

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
        LastGameResult = default;
        HasGameResult = false;

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

        // 타이머를 먼저 0초로 맞춰야 Spawner가 시작 시점(0초) 난이도로 첫 간격(1.80초)을 잡는다.
        if (survivalTimer != null)
        {
            survivalTimer.ResetTimer();
            survivalTimer.StartTimer();
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

        // 생존시간은 피격 즉시 멈춘다(이후 처리 중에도 증가하지 않음). 값은 결과/기록용으로 유지된다.
        if (survivalTimer != null)
        {
            survivalTimer.StopTimer();
        }

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

        // 최종 생존시간 → 최고기록 갱신 시도 → 결과 확정. EndGame은 Playing에서 한 번만 통과하므로 기록도 한 번만 처리된다.
        LastGameResult = CreateGameResult(survivalTimer != null ? survivalTimer.ElapsedTime : 0f);
        HasGameResult = true;

        OnGameStateChanged?.Invoke(CurrentState);

        // Stage 7: 사망 애니메이션 완료 이벤트가 생기면 그 시점에 호출하도록 옮긴다. 지금은 대기 없이 즉시 전달한다.
        PublishGameResult();
    }

    /// <summary>최종 생존시간으로 최고기록 갱신을 시도하고 GameResult를 만든다.</summary>
    private static GameResult CreateGameResult(float survivalTime)
    {
        bool isNewRecord = RecordStore.TryUpdateRecord(survivalTime);
        return new GameResult(survivalTime, RecordStore.GetBestTime(), isNewRecord);
    }

    private void PublishGameResult()
    {
        if (HasGameResult)
        {
            OnGameResultReady?.Invoke(LastGameResult);
        }
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
