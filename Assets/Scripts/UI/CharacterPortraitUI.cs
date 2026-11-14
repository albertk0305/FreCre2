using UnityEngine;

/// <summary>
/// PlayScene 좌측 상단 캐릭터 Portrait(UI Image)의 표정 애니메이션을 GameState에 맞춰 바꾼다.
/// - Ready / Playing: Idle(반복)
/// - GameOver: Sad를 처음부터 1회 재생, 끝나면 마지막 프레임 유지(클립 Loop Time 끔 + Transition 없음)
/// - 재시작: 씬 재로드 시 Animator 기본 State(Idle)로 시작하고, StartGame()으로 Playing이 되면 Idle로 되돌린다.
/// GameManager.OnGameStateChanged 이벤트로만 반응한다(매 프레임 상태 확인 없음).
/// 같은 표정 요청이 반복되면 Animator에 다시 재생 명령을 보내지 않는다(같은 GameOver 이벤트로 Sad가 재시작되지 않음).
/// 월드 캐릭터 Death(PlayerAnimationController)나 게임 오버 처리 흐름과 독립적이며, Sad 완료를 기다리게 하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class CharacterPortraitUI : MonoBehaviour
{
    /// <summary>Portrait.controller의 State 이름.</summary>
    public const string LayerName = "Base Layer";
    public const string IdleStateName = "PortraitIdle";
    public const string SadStateName = "PortraitSad";

    public static readonly int IdleStateHash = Animator.StringToHash(LayerName + "." + IdleStateName);
    public static readonly int SadStateHash = Animator.StringToHash(LayerName + "." + SadStateName);

    /// <summary>Portrait가 표시하는 표정.</summary>
    public enum PortraitState
    {
        None = 0,
        Idle = 1,
        Sad = 2,
    }

    [Tooltip("상태 변경 이벤트를 받을 GameManager (GameSystems/GameManager)")]
    [SerializeField] private GameManager gameManager;

    [Tooltip("Portrait 표정 Animator (UICanvas/CharacterPortrait/Portrait, Controller: Portrait.controller)")]
    [SerializeField] private Animator portraitAnimator;

    private bool subscribed;

    /// <summary>마지막으로 적용한 표정.</summary>
    public PortraitState CurrentPortraitState { get; private set; } = PortraitState.None;

    /// <summary>Animator에 재생을 요청한 횟수(중복 재생 방지 확인용).</summary>
    public int PlayRequestCount { get; private set; }

    private void Awake()
    {
        if (gameManager == null)
        {
            Debug.LogWarning($"{name}: GameManager가 연결되지 않아 Portrait가 Idle로만 표시됩니다.", this);
        }
        if (portraitAnimator == null)
        {
            Debug.LogWarning($"{name}: Portrait Animator가 연결되지 않았습니다.", this);
        }
    }

    private void OnEnable()
    {
        Subscribe();
        SyncWithGameState();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>현재 GameManager 상태에 맞춰 표정을 맞춘다. Animator가 이미 그 표정이면 다시 재생하지 않는다.</summary>
    public void SyncWithGameState()
    {
        GameState state = gameManager != null ? gameManager.CurrentState : GameState.Ready;
        PortraitState target = ResolvePortraitState(state);
        if (CurrentPortraitState == PortraitState.None && target == PortraitState.Idle)
        {
            // 첫 동기화: Animator 기본 State가 Idle이므로 재생 명령 없이 기록만 한다(씬 로드 직후 불필요한 Play 방지).
            CurrentPortraitState = PortraitState.Idle;
            return;
        }
        ApplyPortraitState(target, force: !IsAnimatorShowing(target));
    }

    /// <summary>GameState → 표정. GameOver만 Sad, 나머지는 Idle.</summary>
    public static PortraitState ResolvePortraitState(GameState state)
    {
        return state == GameState.GameOver ? PortraitState.Sad : PortraitState.Idle;
    }

    /// <summary>표정에 해당하는 Animator State 해시(Base Layer 전체 경로).</summary>
    public static int GetStateHash(PortraitState state)
    {
        return state == PortraitState.Sad ? SadStateHash : IdleStateHash;
    }

    private void HandleGameStateChanged(GameState state)
    {
        ApplyPortraitState(ResolvePortraitState(state), force: false);
    }

    private void ApplyPortraitState(PortraitState target, bool force)
    {
        if (!force && target == CurrentPortraitState)
        {
            return; // 같은 표정 → 재생 명령 생략 (Sad 재시작 방지)
        }

        CurrentPortraitState = target;
        PlayRequestCount++;

        if (CanControlAnimator())
        {
            // 항상 처음(0)부터 재생: GameOver 진입 시 Sad 첫 프레임, 재시작 시 Idle 첫 프레임.
            portraitAnimator.Play(GetStateHash(target), 0, 0f);
        }
    }

    /// <summary>
    /// Animator가 이미 해당 표정을 보여주고 있는지. 아직 평가 전이거나 비활성이면 기록된 표정을 믿는다
    /// (Animator가 다시 활성화되어 기본 State로 돌아간 경우 등 실제와 다를 때만 다시 재생).
    /// </summary>
    private bool IsAnimatorShowing(PortraitState target)
    {
        if (CurrentPortraitState != target)
        {
            return false;
        }
        if (!Application.isPlaying || !CanControlAnimator() || !portraitAnimator.isActiveAndEnabled)
        {
            return true;
        }
        int hash = portraitAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        return hash == GetStateHash(target);
    }

    private bool CanControlAnimator()
    {
        return portraitAnimator != null
            && portraitAnimator.enabled
            && portraitAnimator.gameObject.activeInHierarchy
            && portraitAnimator.runtimeAnimatorController != null;
    }

    private void Subscribe()
    {
        if (subscribed || gameManager == null)
        {
            return;
        }
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
            gameManager.OnGameStateChanged -= HandleGameStateChanged;
        }
        subscribed = false;
    }
}
