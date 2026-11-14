using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 메인화면의 최고기록 표시, 조작법 선택(PC/Mobile), 게임 종료 버튼을 관리한다.
/// - 최고기록은 RecordStore에서 읽기만 한다.
/// - 조작법은 ControlSettings에 저장하고, PlayScene의 PlayerInputRouter가 시작 시 읽어 적용한다.
///   (MainMenuUI는 PlayScene의 GameManager를 직접 참조하지 않는다.)
/// - 선택 상태는 버튼 Image의 색상만 바꿔 표시한다(스프라이트/RectTransform은 그대로 → 픽셀아트 유지).
/// - PlayButton은 기존 SceneNavigationButtons.OnClickPlay에 연결된 상태를 그대로 사용한다.
/// 버튼 이벤트는 모두 Inspector의 OnClick으로 연결한다(코드에서 AddListener하지 않음).
/// </summary>
[DisallowMultipleComponent]
public class MainMenuUI : MonoBehaviour
{
    [Header("Best Record")]
    [Tooltip("Canvas/BackgroundImage/BestRecord/BestRecordText")]
    [SerializeField] private TextMeshProUGUI bestRecordText;
    [Tooltip("{0} 자리에 MM:SS.ss 형식의 최고기록이 들어간다.")]
    [SerializeField] private string bestRecordFormat = "Best {0}";

    [Header("Control Mode Buttons")]
    [Tooltip("ControlModeSelector/PCButton의 Image")]
    [SerializeField] private Image pcButtonImage;
    [Tooltip("ControlModeSelector/MobileButton의 Image")]
    [SerializeField] private Image mobileButtonImage;
    [Tooltip("선택된 버튼의 Image 색상 (흰색 = 원본 이미지 그대로)")]
    [SerializeField] private Color selectedColor = Color.white;
    [Tooltip("선택되지 않은 버튼의 Image 색상 (원본 이미지를 어둡게 표시)")]
    [SerializeField] private Color unselectedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    /// <summary>현재 화면에 선택 표시된 조작 방식.</summary>
    public ControlMode SelectedMode { get; private set; } = ControlSettings.DefaultMode;

    private void Start()
    {
        RefreshBestTime();
        RefreshControlMode();
    }

    /// <summary>저장된 최고기록을 텍스트에 표시한다.</summary>
    public void RefreshBestTime()
    {
        if (bestRecordText == null)
        {
            return;
        }
        bestRecordText.text = string.Format(bestRecordFormat, TimeFormatter.Format(RecordStore.GetBestTime()));
    }

    /// <summary>저장된 조작 방식을 읽어 선택 상태를 복원한다(저장값이 없으면 PC).</summary>
    public void RefreshControlMode()
    {
        ApplySelectionVisual(ControlSettings.GetControlMode());
    }

    /// <summary>PCButton OnClick.</summary>
    public void SelectPC()
    {
        SelectMode(ControlMode.PC);
    }

    /// <summary>MobileButton OnClick.</summary>
    public void SelectMobile()
    {
        SelectMode(ControlMode.Mobile);
    }

    /// <summary>조작 방식을 저장하고 버튼 선택 표시를 갱신한다.</summary>
    public void SelectMode(ControlMode mode)
    {
        ControlSettings.SetControlMode(mode);
        ApplySelectionVisual(ControlSettings.GetControlMode()); // 저장소가 보정한 실제 값으로 표시
    }

    /// <summary>QuitButton OnClick. 빌드에서는 앱을 종료한다. Unity Editor에서는 종료되지 않는다.</summary>
    public void OnClickQuit()
    {
#if UNITY_EDITOR
        Debug.Log($"{name}: Quit 요청 (Editor에서는 종료하지 않습니다).", this);
#endif
        Application.Quit();
    }

    private void ApplySelectionVisual(ControlMode mode)
    {
        SelectedMode = mode;
        if (pcButtonImage != null)
        {
            pcButtonImage.color = mode == ControlMode.PC ? selectedColor : unselectedColor;
        }
        if (mobileButtonImage != null)
        {
            mobileButtonImage.color = mode == ControlMode.Mobile ? selectedColor : unselectedColor;
        }
    }
}
