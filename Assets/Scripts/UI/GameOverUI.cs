using TMPro;
using UnityEngine;

/// <summary>
/// 게임 오버 결과 UI. GameManager.OnGameResultReady(GameResult)를 받아 표시만 한다.
/// - 게임 오버 처리, 기록 비교, 기록 저장은 GameManager/RecordStore가 이미 끝낸 상태다(여기서 중복 처리하지 않음).
/// - OnGameResultReady는 사망 애니메이션 완료 후 발생하므로 기존 연출 순서가 유지된다.
/// - 플레이 중(Awake, GameState.Playing 전환)에는 패널을 숨긴다. 씬 재로드로 재시작하면 Awake에서 숨겨진 상태로 시작한다.
/// - Exit/Restart 버튼은 이 패널에 속하지 않으며 기존 SceneNavigationButtons로 항상 동작한다.
/// 이 컴포넌트는 숨길 패널(TextPanel)이 아닌, 항상 활성인 오브젝트(예: UICanvas)에 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class GameOverUI : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private GameManager gameManager;

    [Header("Panel")]
    [Tooltip("게임 오버 시에만 표시할 패널 (UICanvas/TextPanel)")]
    [SerializeField] private GameObject gameOverPanel;
    [Tooltip("UICanvas/TextPanel/Text")]
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private string gameOverMessage = "GAME OVER";

    [Header("New Record")]
    [SerializeField] private string newRecordMessage = "NEW RECORD!";
    [Tooltip("선택. 신기록 전용 오브젝트가 있으면 연결한다. 비우면 Message Text 아래 줄에 신기록 문구를 붙인다.")]
    [SerializeField] private GameObject newRecordObject;

    [Header("Optional Result Texts")]
    [Tooltip("선택. 비우면 PlayHUD의 생존시간 표시를 그대로 사용한다.")]
    [SerializeField] private TextMeshProUGUI resultSurvivalTimeText;
    [Tooltip("선택. 비우면 PlayHUD의 최고기록 표시를 그대로 사용한다.")]
    [SerializeField] private TextMeshProUGUI resultBestTimeText;

    private bool subscribed;

    /// <summary>결과 UI가 표시 중인지.</summary>
    public bool IsShown { get; private set; }

    private void Awake()
    {
        if (gameOverPanel != null && transform.IsChildOf(gameOverPanel.transform))
        {
            Debug.LogError($"{name}: GameOverUI가 숨길 패널({gameOverPanel.name}) 안에 있으면 비활성화되어 결과를 받을 수 없습니다. UICanvas 등 항상 활성인 오브젝트에 붙이세요.", this);
        }
        Hide();
    }

    private void OnEnable()
    {
        Subscribe();

        // 구독 전에 이미 결과가 전달되었다면 놓치지 않고 표시한다.
        if (gameManager != null && gameManager.IsGameResultPublished && gameManager.HasGameResult)
        {
            Show(gameManager.LastGameResult);
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>게임 결과를 표시한다.</summary>
    public void Show(GameResult result)
    {
        if (messageText != null)
        {
            bool appendNewRecord = result.IsNewRecord && newRecordObject == null;
            messageText.text = appendNewRecord ? $"{gameOverMessage}\n{newRecordMessage}" : gameOverMessage;
        }
        if (newRecordObject != null)
        {
            newRecordObject.SetActive(result.IsNewRecord);
        }
        if (resultSurvivalTimeText != null)
        {
            resultSurvivalTimeText.text = TimeFormatter.Format(result.SurvivalTime);
        }
        if (resultBestTimeText != null)
        {
            resultBestTimeText.text = TimeFormatter.Format(result.BestTime);
        }
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        IsShown = true;
    }

    /// <summary>결과 UI를 숨기고 초기 상태로 되돌린다.</summary>
    public void Hide()
    {
        if (messageText != null)
        {
            messageText.text = gameOverMessage;
        }
        if (newRecordObject != null)
        {
            newRecordObject.SetActive(false);
        }
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
        IsShown = false;
    }

    private void HandleGameResultReady(GameResult result)
    {
        Show(result);
    }

    private void HandleGameStateChanged(GameState state)
    {
        if (state == GameState.Playing)
        {
            Hide(); // 새 게임 시작 시 초기 상태
        }
    }

    private void Subscribe()
    {
        if (subscribed || gameManager == null)
        {
            return;
        }
        gameManager.OnGameResultReady += HandleGameResultReady;
        gameManager.OnGameStateChanged += HandleGameStateChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }
        if (gameManager != null)
        {
            gameManager.OnGameResultReady -= HandleGameResultReady;
            gameManager.OnGameStateChanged -= HandleGameStateChanged;
        }
        subscribed = false;
    }
}
