using TMPro;
using UnityEngine;

/// <summary>
/// 화면 중앙 메시지(UICanvas/TextPanel + Text)의 표시를 한곳에서 관리한다.
/// - StartCountdownController(3, 2, 1, START!)와 GameOverUI(GAME OVER / NEW RECORD!)가 이 컴포넌트를 통해서만 TextPanel을 제어한다.
/// - 소유자 규칙: Show(owner, text)는 마지막 요청자가 소유자가 된다. Hide(owner)는 현재 소유자일 때만 숨긴다.
///   따라서 한쪽이 늦게 Hide를 호출해도 다른 쪽이 표시 중인 메시지를 지우지 않는다.
/// 숨길 패널(TextPanel)이 아닌 항상 활성인 오브젝트(예: UICanvas)에 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class CenterMessageUI : MonoBehaviour
{
    [Tooltip("메시지 패널 (UICanvas/TextPanel)")]
    [SerializeField] private GameObject panel;

    [Tooltip("메시지 텍스트 (UICanvas/TextPanel/Text)")]
    [SerializeField] private TextMeshProUGUI messageText;

    /// <summary>현재 메시지를 표시 중인 컴포넌트. 숨김 상태면 null.</summary>
    public Object CurrentOwner { get; private set; }

    /// <summary>현재 표시 중인 메시지. 숨김 상태면 빈 문자열.</summary>
    public string CurrentMessage { get; private set; } = string.Empty;

    public bool IsVisible => CurrentOwner != null;

    private void Awake()
    {
        if (panel != null && transform.IsChildOf(panel.transform))
        {
            Debug.LogError($"{name}: CenterMessageUI가 숨길 패널({panel.name}) 안에 있습니다. UICanvas 등 항상 활성인 오브젝트에 붙이세요.", this);
        }
        if (CurrentOwner == null)
        {
            HideInternal(); // 시작은 숨김. 다른 컴포넌트가 먼저 Show했다면 유지.
        }
    }

    /// <summary>owner의 메시지를 표시한다. 이전 소유자의 메시지는 대체된다.</summary>
    public void Show(Object owner, string message)
    {
        if (owner == null)
        {
            return;
        }
        CurrentOwner = owner;
        CurrentMessage = message ?? string.Empty;
        if (messageText != null)
        {
            messageText.text = CurrentMessage;
        }
        if (panel != null)
        {
            panel.SetActive(true);
        }
    }

    /// <summary>owner가 현재 소유자(또는 아무도 표시 중이 아닐 때)이면 숨긴다. 숨겼으면 true.</summary>
    public bool Hide(Object owner)
    {
        if (CurrentOwner != null && CurrentOwner != owner)
        {
            return false; // 다른 컴포넌트의 메시지는 건드리지 않음
        }
        HideInternal();
        return true;
    }

    private void HideInternal()
    {
        CurrentOwner = null;
        CurrentMessage = string.Empty;
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }
}
