using System;
using UnityEngine;

/// <summary>
/// 게임의 시작/종료와 GameState를 관리한다. 세부 동작은 각 시스템에 명령만 전달한다.
/// - 시작: 피격 상태 초기화 → 플레이어 조작 활성화 → 생존시간 초기화/측정 시작 → 미사일 생성 시작 → Playing 전환 → 이벤트
/// - 피격: PlayerHitDetector.OnPlayerHit 수신 → (Stage 8) 피격 미사일 위치에 충돌 이펙트 1개 생성 → EndGame()
///   이펙트는 독립 오브젝트(선택: EffectContainer 자식)로 스스로 재생/제거되며, 실패해도 게임 오버를 막지 않는다.
/// - 종료: GameOver 전환 → 생존시간 정지 → 플레이어 사망(이동/점프 중단) → 입력 차단 → 생성 중지 → 미사일 정지
///         → 최종 생존시간으로 RecordStore.TryUpdateRecord → GameResult 생성(즉시 확정) → OnGameStateChanged
///         → (Stage 7) PlayerAnimationController.OnDeathFinished 대기 → OnGameResultReady
/// - 사망 애니메이션 컨트롤러가 없거나 비활성이면 OnGameResultReady를 즉시 보낸다.
///   완료 신호가 오지 않아도 deathResultTimeout 후에는 반드시 보낸다(무한 대기 방지).
/// - 기록 저장은 게임 오버(미사일 피격)에서만 한다. Exit/Restart 버튼의 씬 전환은 EndGame()을 거치지 않으므로 기록되지 않는다.
///   기록은 EndGame()에서 즉시 저장되므로 사망 연출 도중 씬을 나가도 기록은 유지된다.
/// Time.timeScale은 변경하지 않는다.
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
    [Tooltip("사망 애니메이션 완료(OnDeathFinished) 후 결과를 전달한다. 비우면 PlayerController 자식에서 찾고, 없으면 결과를 즉시 전달.")]
    [SerializeField] private PlayerAnimationController playerAnimation;

    [Header("Hit Effect")]
    [Tooltip("미사일 피격 시 생성할 충돌 이펙트 프리팹(MissileHitEffect). 비우면 이펙트 없이 게임 오버만 처리.")]
    [SerializeField] private GameObject hitEffectPrefab;
    [Tooltip("생성된 이펙트의 부모(선택). 비우면 씬 루트에 생성.")]
    [SerializeField] private Transform effectContainer;

    [Header("Game Over")]
    [Tooltip("사망 애니메이션 완료 신호를 기다리는 최대 시간(초). 지나면 결과를 강제로 전달한다.")]
    [Min(0f)] [SerializeField] private float deathResultTimeout = 3f;

    [Header("Start")]
    [Tooltip("시작 UI가 없는 Stage 4에서는 씬 시작 시 자동으로 게임을 시작한다.")]
    [SerializeField] private bool startGameOnStart = true;

    private bool hitSubscribed;
    private bool deathSubscribed;
    private float resultWaitElapsed;

    public GameState CurrentState { get; private set; } = GameState.Ready;

    /// <summary>게임을 끝낸 미사일 (없으면 null).</summary>
    public Missile LastHitMissile { get; private set; }

    /// <summary>이번 판에 생성한 충돌 이펙트 (없거나 이미 제거되었으면 null).</summary>
    public GameObject LastHitEffect { get; private set; }

    /// <summary>마지막 게임 오버의 결과. HasGameResult가 false면 기본값.</summary>
    public GameResult LastGameResult { get; private set; }

    /// <summary>이번 판의 게임 오버 결과가 확정되었는지. StartGame()에서 false로 초기화.</summary>
    public bool HasGameResult { get; private set; }

    /// <summary>이번 판의 OnGameResultReady를 이미 보냈는지.</summary>
    public bool IsGameResultPublished { get; private set; }

    /// <summary>결과는 확정되었고 사망 애니메이션 완료를 기다리는 중인지.</summary>
    public bool IsWaitingForDeathAnimation { get; private set; }

    /// <summary>GameState가 바뀔 때마다 새 상태로 발생한다.</summary>
    public event Action<GameState> OnGameStateChanged;

    /// <summary>
    /// 게임 오버 결과(생존시간/최고기록/신기록 여부)를 사망 애니메이션 종료 후 한 번 전달한다. Stage 9 GameOverUI용.
    /// 기록 저장은 이보다 먼저(게임 오버 즉시) 끝나 있다.
    /// </summary>
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
        SubscribeDeath();
    }

    private void OnDisable()
    {
        UnsubscribeHit();
        UnsubscribeDeath();
    }

    private void Update()
    {
        TickResultWait(Time.deltaTime);
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
        SubscribeDeath();
        LastHitMissile = null;
        LastHitEffect = null; // 이전 판 이펙트는 스스로 끝까지 재생 후 제거된다
        LastGameResult = default;
        HasGameResult = false;
        IsGameResultPublished = false;
        IsWaitingForDeathAnimation = false; // 이전 판의 사망 대기/완료 신호가 새 게임에 영향을 주지 않음

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
        // 게임 오버 처리(미사일 정지 등) 전에 피격 위치를 확정해 이펙트를 만든다. Playing 검사로 판당 1개만 생성된다.
        SpawnHitEffect(ResolveHitPosition(missile));
        EndGame();
    }

    /// <summary>피격 미사일의 현재 월드 좌표. 미사일이 없으면 플레이어 지면 위치(없으면 GameManager 위치).</summary>
    private Vector3 ResolveHitPosition(Missile missile)
    {
        if (missile != null)
        {
            // Missile.Position은 이동 로직이 갱신하는 현재 좌표(Transform은 물리 스텝 반영 전일 수 있음).
            Vector2 p = missile.Position;
            return new Vector3(p.x, p.y, missile.transform.position.z);
        }
        if (playerController != null)
        {
            Vector2 p = playerController.GroundPosition;
            return new Vector3(p.x, p.y, playerController.transform.position.z);
        }
        return transform.position;
    }

    /// <summary>충돌 이펙트를 독립 오브젝트로 생성한다. 프리팹이 없거나 생성 중 오류가 나도 게임 오버는 계속 진행된다.</summary>
    private void SpawnHitEffect(Vector3 position)
    {
        if (hitEffectPrefab == null)
        {
            return;
        }

        try
        {
            GameObject effect = Instantiate(hitEffectPrefab, position, hitEffectPrefab.transform.rotation, effectContainer);
            if (effect.GetComponent<HitEffect>() == null)
            {
                // 프리팹에 HitEffect가 빠져 있어도 무한히 남지 않도록 자동 제거 컴포넌트를 붙인다.
                effect.AddComponent<HitEffect>();
            }
            LastHitEffect = effect;
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
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

        // 사망 애니메이션이 있으면 끝난 뒤(OnDeathFinished) 결과를 전달하고, 없으면 즉시 전달한다.
        if (CanWaitForDeathAnimation())
        {
            IsWaitingForDeathAnimation = true;
            resultWaitElapsed = 0f;
        }
        else
        {
            PublishGameResult();
        }
    }

    /// <summary>
    /// 결과 대기 보호 장치. 사망 애니메이션 컨트롤러가 사라졌거나 꺼졌으면 즉시, 완료 신호가 없으면 deathResultTimeout 후 전달한다.
    /// Update에서 호출되며 테스트에서 직접 호출할 수 있다.
    /// </summary>
    public void TickResultWait(float deltaTime)
    {
        if (!IsWaitingForDeathAnimation)
        {
            return;
        }

        if (!CanWaitForDeathAnimation())
        {
            PublishGameResult();
            return;
        }

        resultWaitElapsed += Mathf.Max(0f, deltaTime);
        if (resultWaitElapsed >= deathResultTimeout)
        {
            Debug.LogWarning($"{name}: 사망 애니메이션 완료 신호가 {deathResultTimeout:0.##}초 동안 없어 게임 결과를 전달합니다.", this);
            PublishGameResult();
        }
    }

    /// <summary>PlayerAnimationController.OnDeathFinished 핸들러. 이번 판의 결과를 기다리는 중일 때만 전달한다.</summary>
    private void HandleDeathFinished()
    {
        if (IsWaitingForDeathAnimation && CurrentState == GameState.GameOver)
        {
            PublishGameResult();
        }
    }

    private bool CanWaitForDeathAnimation()
    {
        // isActiveAndEnabled는 OnEnable 이전(EditMode 등)에 false이므로 enabled/activeInHierarchy로 판단한다.
        return playerAnimation != null && playerAnimation.enabled && playerAnimation.gameObject.activeInHierarchy && deathSubscribed;
    }

    /// <summary>최종 생존시간으로 최고기록 갱신을 시도하고 GameResult를 만든다.</summary>
    private static GameResult CreateGameResult(float survivalTime)
    {
        bool isNewRecord = RecordStore.TryUpdateRecord(survivalTime);
        return new GameResult(survivalTime, RecordStore.GetBestTime(), isNewRecord);
    }

    private void PublishGameResult()
    {
        IsWaitingForDeathAnimation = false;
        if (!HasGameResult || IsGameResultPublished)
        {
            return; // 한 판에 한 번만
        }
        IsGameResultPublished = true;
        OnGameResultReady?.Invoke(LastGameResult);
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

    private void SubscribeDeath()
    {
        if (deathSubscribed || playerAnimation == null)
        {
            return;
        }
        playerAnimation.OnDeathFinished += HandleDeathFinished;
        deathSubscribed = true;
    }

    private void UnsubscribeDeath()
    {
        if (!deathSubscribed)
        {
            return;
        }
        if (playerAnimation != null)
        {
            playerAnimation.OnDeathFinished -= HandleDeathFinished;
        }
        deathSubscribed = false;
    }

    private void ResolveReferences()
    {
        if (inputRouter == null && playerController != null)
        {
            inputRouter = playerController.GetComponent<PlayerInputRouter>();
        }
        if (playerAnimation == null && playerController != null)
        {
            playerAnimation = playerController.GetComponentInChildren<PlayerAnimationController>(true);
        }
    }
}
