using TMPro;
using UnityEngine;

/// <summary>
/// 플레이 중 현재 생존시간과 최고기록을 표시한다. 값을 읽기만 하고 SurvivalTimer/RecordStore를 수정하지 않는다.
/// - 생존시간: SurvivalTimer.ElapsedTime을 매 프레임(LateUpdate) 읽는다. 표시값(1/100초)이 바뀔 때만 텍스트를 갱신한다.
///   게임 오버 후에는 타이머가 정지된 값을 유지하므로 마지막 생존시간이 그대로 남고,
///   재시작(씬 재로드 또는 StartGame의 ResetTimer) 시 0으로 돌아간다.
/// - 최고기록: 시작 시 한 번, 그리고 GameState가 바뀔 때(게임 오버 직후 기록 저장 반영) 다시 읽는다.
/// </summary>
[DisallowMultipleComponent]
public class PlayHUD : MonoBehaviour
{
    [Header("Sources (read only)")]
    [Tooltip("GameSystems의 SurvivalTimer")]
    [SerializeField] private SurvivalTimer survivalTimer;
    [Tooltip("선택. 연결하면 게임 오버 직후 갱신된 최고기록을 다시 표시한다.")]
    [SerializeField] private GameManager gameManager;

    [Header("Texts")]
    [Tooltip("PlayHUD/SurvivalTimePanel/Text (TMP)")]
    [SerializeField] private TextMeshProUGUI survivalTimeText;
    [Tooltip("PlayHUD/BestTimePanel/Text (TMP)")]
    [SerializeField] private TextMeshProUGUI bestTimeText;
    [Tooltip("{0} 자리에 MM:SS.ss 형식의 시간이 들어간다.")]
    [SerializeField] private string survivalTimeFormat = "{0}";
    [SerializeField] private string bestTimeFormat = "{0}";

    private const int NotDisplayed = -1;
    private int displayedSurvivalCentis = NotDisplayed;
    private bool stateSubscribed;

    private void OnEnable()
    {
        Subscribe();
        RefreshBestTime();
        RefreshSurvivalTime(force: true);
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void LateUpdate()
    {
        RefreshSurvivalTime();
    }

    /// <summary>현재 생존시간 표시를 갱신한다. 표시값이 같으면 텍스트를 다시 쓰지 않는다(force=true면 항상).</summary>
    public void RefreshSurvivalTime(bool force = false)
    {
        if (survivalTimeText == null)
        {
            return;
        }

        float elapsed = survivalTimer != null ? survivalTimer.ElapsedTime : 0f;
        int centis = TimeFormatter.ToCentiseconds(elapsed);
        if (!force && centis == displayedSurvivalCentis)
        {
            return;
        }

        displayedSurvivalCentis = centis;
        survivalTimeText.text = string.Format(survivalTimeFormat, TimeFormatter.Format(elapsed));
    }

    /// <summary>저장된 최고기록 표시를 갱신한다.</summary>
    public void RefreshBestTime()
    {
        if (bestTimeText == null)
        {
            return;
        }
        bestTimeText.text = string.Format(bestTimeFormat, TimeFormatter.Format(RecordStore.GetBestTime()));
    }

    private void HandleGameStateChanged(GameState state)
    {
        // GameOver: GameManager가 OnGameStateChanged 전에 기록 저장을 끝내므로 새 최고기록이 반영된다.
        // Playing: 재시작 시 0초 표시를 즉시 반영한다.
        RefreshBestTime();
        RefreshSurvivalTime(force: true);
    }

    private void Subscribe()
    {
        if (stateSubscribed || gameManager == null)
        {
            return;
        }
        gameManager.OnGameStateChanged += HandleGameStateChanged;
        stateSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!stateSubscribed)
        {
            return;
        }
        if (gameManager != null)
        {
            gameManager.OnGameStateChanged -= HandleGameStateChanged;
        }
        stateSubscribed = false;
    }
}
