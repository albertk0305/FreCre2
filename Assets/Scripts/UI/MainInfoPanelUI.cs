using UnityEngine;

/// <summary>
/// 메인화면의 How To Play / Credits 정보 패널. 하나의 TextPanel 안에서 두 콘텐츠 중 하나만 표시한다.
/// - ShowHowToPlay(): 패널 열기 + HowToPlay만 활성
/// - ShowCredits():   패널 열기 + Credits만 활성
/// - ClosePanel():    패널과 두 콘텐츠 모두 비활성
/// 메인화면 진입 시(Awake) 패널은 닫힌 상태로 시작한다.
/// 버튼 이벤트는 Inspector의 OnClick으로만 연결한다(코드에서 AddListener하지 않음).
/// 패널이 열려 있는 동안 뒤쪽 메뉴 클릭은 TextPanel/BackgroundPanel(전체 화면, Raycast Target)이 막는다.
/// 이 컴포넌트는 TextPanel이 닫혀도 활성 상태인 Canvas 또는 BackgroundImage에 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class MainInfoPanelUI : MonoBehaviour
{
    /// <summary>패널에 표시 중인 콘텐츠.</summary>
    public enum InfoContent
    {
        None = 0,
        HowToPlay = 1,
        Credits = 2,
    }

    [Tooltip("공통 패널 (Canvas/BackgroundImage/TextPanel)")]
    [SerializeField] private GameObject textPanel;

    [Tooltip("조작법 콘텐츠 (TextPanel/HowToPlay)")]
    [SerializeField] private GameObject howToPlay;

    [Tooltip("크레딧 콘텐츠 (TextPanel/Credits)")]
    [SerializeField] private GameObject credits;

    /// <summary>현재 표시 중인 콘텐츠. 닫혀 있으면 None.</summary>
    public InfoContent CurrentContent { get; private set; } = InfoContent.None;

    public bool IsOpen => CurrentContent != InfoContent.None;

    private void Awake()
    {
        if (textPanel != null && transform.IsChildOf(textPanel.transform))
        {
            Debug.LogError($"{name}: MainInfoPanelUI가 TextPanel 안에 있습니다. Canvas 또는 BackgroundImage에 붙이세요.", this);
        }
        ClosePanel();
    }

    /// <summary>HowToPlayButton OnClick.</summary>
    public void ShowHowToPlay()
    {
        Show(InfoContent.HowToPlay);
    }

    /// <summary>CreditsButton OnClick.</summary>
    public void ShowCredits()
    {
        Show(InfoContent.Credits);
    }

    /// <summary>CloseButton OnClick. 패널과 두 콘텐츠를 모두 숨긴다.</summary>
    public void ClosePanel()
    {
        CurrentContent = InfoContent.None;
        SetActive(howToPlay, false);
        SetActive(credits, false);
        SetActive(textPanel, false);
    }

    private void Show(InfoContent content)
    {
        CurrentContent = content;
        // 콘텐츠를 먼저 정한 뒤 패널을 켜서 이전 콘텐츠가 한 프레임도 함께 보이지 않게 한다.
        SetActive(howToPlay, content == InfoContent.HowToPlay);
        SetActive(credits, content == InfoContent.Credits);
        SetActive(textPanel, true);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
