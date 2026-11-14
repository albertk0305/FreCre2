using UnityEngine;

/// <summary>
/// PlayScene 진입 시 3 → 2 → 1 → START! 카운트다운 후 GameManager.StartGame()을 정확히 한 번 호출한다.
/// - 카운트다운 동안 GameState는 Ready 그대로다(추가 상태 없음). GameManager.PrepareReady()로
///   생존시간 0초 정지, 미사일 생성 중지, 플레이어 조작/입력 차단을 확정한다.
/// - Awake에서 GameManager의 자동 시작(StartGameOnStart)을 끄므로 Timer/Spawner가 먼저 시작되지 않는다.
/// - 시작 시 월드 캐릭터 Stretching(PlayerAnimationController.PlayStretching)을 요청한다.
/// - 메시지는 CenterMessageUI를 통해서만 표시한다(GameOverUI와 같은 TextPanel 공유).
/// - 비활성화/씬 종료 시 진행 중인 카운트다운을 중단하고 메시지를 숨긴다. 다시 활성화되면(아직 Ready면) 처음부터 다시 시작.
/// - Restart/Exit은 씬을 새로 로드하므로 새 컨트롤러가 처음부터 카운트다운한다. Time.timeScale은 건드리지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class StartCountdownController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("GameSystems/GameManager")]
    [SerializeField] private GameManager gameManager;
    [Tooltip("UICanvas의 CenterMessageUI (TextPanel 표시 담당)")]
    [SerializeField] private CenterMessageUI messageUI;
    [Tooltip("선택. Player/Visual의 PlayerAnimationController. 카운트다운 시작 시 Stretching을 재생한다.")]
    [SerializeField] private PlayerAnimationController playerAnimation;

    [Header("Countdown")]
    [Tooltip("시작 숫자 (3이면 3, 2, 1)")]
    [Min(1)] [SerializeField] private int countFrom = 3;
    [Tooltip("숫자 하나를 표시하는 시간(초)")]
    [Min(0.05f)] [SerializeField] private float secondsPerCount = 1f;
    [SerializeField] private string startMessage = "START!";
    [Tooltip("시작 메시지 표시 시간(초). 끝나면 메시지를 숨기고 게임을 시작한다.")]
    [Min(0f)] [SerializeField] private float startMessageDuration = 0.6f;
    [Tooltip("씬 시작 시 자동으로 카운트다운을 시작한다.")]
    [SerializeField] private bool autoStartOnSceneStart = true;

    private bool startedOnce;
    private bool startRequested;

    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }
    public float Elapsed { get; private set; }
    /// <summary>현재 표시 중인 카운트다운 메시지(없으면 빈 문자열).</summary>
    public string CurrentMessage { get; private set; } = string.Empty;
    /// <summary>GameManager.StartGame()을 호출한 횟수(정확히 1이어야 함).</summary>
    public int StartGameRequestCount { get; private set; }

    /// <summary>전체 카운트다운 길이(초) = 숫자 수 × 숫자당 시간 + 시작 메시지 시간.</summary>
    public float TotalDuration => ComputeTotalDuration(countFrom, secondsPerCount, startMessageDuration);

    private void Awake()
    {
        // 모든 Awake는 어떤 Start보다 먼저 실행되므로 GameManager.Start()의 자동 StartGame()을 막을 수 있다.
        if (gameManager != null && gameManager.StartGameOnStart)
        {
            gameManager.StartGameOnStart = false;
            Debug.Log($"{name}: 카운트다운 후 시작하도록 GameManager의 Start Game On Start를 런타임에서 비활성화했습니다. Inspector에서도 꺼 두는 것을 권장합니다.", this);
        }
        if (gameManager == null)
        {
            Debug.LogWarning($"{name}: GameManager가 연결되지 않아 카운트다운 후 게임을 시작할 수 없습니다.", this);
        }
    }

    private void Start()
    {
        startedOnce = true;
        if (autoStartOnSceneStart)
        {
            BeginCountdown();
        }
    }

    private void OnEnable()
    {
        // 첫 활성화는 Start()에서 시작한다. 중단 후 다시 켜진 경우에만 처음부터 재시작.
        if (startedOnce && autoStartOnSceneStart)
        {
            BeginCountdown();
        }
    }

    private void OnDisable()
    {
        CancelCountdown();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>카운트다운을 처음부터 시작한다. 진행 중이거나 게임이 이미 Ready가 아니면 무시한다.</summary>
    public void BeginCountdown()
    {
        if (IsRunning)
        {
            return; // 중복 실행 방지
        }
        if (gameManager != null && gameManager.CurrentState != GameState.Ready)
        {
            return; // 이미 시작되었거나 끝난 게임
        }

        Elapsed = 0f;
        IsCompleted = false;
        startRequested = false;
        IsRunning = true;

        if (gameManager != null)
        {
            gameManager.PrepareReady();
        }
        if (playerAnimation != null)
        {
            playerAnimation.PlayStretching();
        }
        ShowMessage(ResolveMessage(0f, countFrom, secondsPerCount, startMessage));
    }

    /// <summary>시간을 진행한다. Update에서 호출되며 테스트에서 직접 호출할 수 있다.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsRunning)
        {
            return;
        }

        Elapsed += Mathf.Max(0f, deltaTime);
        if (Elapsed >= TotalDuration)
        {
            Complete();
            return;
        }
        ShowMessage(ResolveMessage(Elapsed, countFrom, secondsPerCount, startMessage));
    }

    /// <summary>진행 중인 카운트다운을 중단한다(게임은 시작하지 않음).</summary>
    public void CancelCountdown()
    {
        if (!IsRunning)
        {
            return;
        }
        IsRunning = false;
        Elapsed = 0f;
        ClearMessage();
    }

    private void Complete()
    {
        IsRunning = false;
        IsCompleted = true;
        ClearMessage();

        if (startRequested)
        {
            return;
        }
        startRequested = true;
        StartGameRequestCount++;
        if (gameManager != null)
        {
            gameManager.StartGame();
        }
    }

    private void ShowMessage(string message)
    {
        if (message == CurrentMessage)
        {
            return; // 같은 메시지는 다시 쓰지 않음
        }
        CurrentMessage = message;
        if (messageUI != null)
        {
            messageUI.Show(this, message);
        }
    }

    private void ClearMessage()
    {
        CurrentMessage = string.Empty;
        if (messageUI != null)
        {
            messageUI.Hide(this); // 다른 소유자(GameOverUI)의 메시지는 지우지 않음
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    public static float ComputeTotalDuration(int countFrom, float secondsPerCount, float startMessageDuration)
    {
        return Mathf.Max(1, countFrom) * Mathf.Max(0.05f, secondsPerCount) + Mathf.Max(0f, startMessageDuration);
    }

    /// <summary>경과 시간에 표시할 메시지. 숫자 구간이면 "3"/"2"/"1", 그 이후는 시작 메시지.</summary>
    public static string ResolveMessage(float elapsed, int countFrom, float secondsPerCount, string startMessage)
    {
        int count = Mathf.Max(1, countFrom);
        float perCount = Mathf.Max(0.05f, secondsPerCount);
        int index = Mathf.FloorToInt(Mathf.Max(0f, elapsed) / perCount);
        return index < count ? (count - index).ToString() : startMessage;
    }
}
