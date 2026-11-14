using UnityEngine;

/// <summary>
/// 단발성 충돌 이펙트. 생성되면 Animator의 기본 State(또는 지정 State)가 재생되고, 한 번 재생이 끝나면 자기 GameObject만 제거한다.
/// - 완료 감지: Animator의 실제 재생 상태(normalizedTime ≥ 1). 클립 길이는 Animator가 보고하는 State 길이를 사용한다.
/// - 보호 장치: Animator/Controller/State가 없거나 재생이 진행되지 않으면
///   max(fallbackDuration, 실제 State 길이 + 여유) 이후 제거한다. 어떤 경우에도 무한히 남지 않는다.
/// Animation Event가 필요 없다. Time.timeScale, Sorting, Transform은 건드리지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class HitEffect : MonoBehaviour
{
    [Tooltip("이펙트 Animator. 비우면 이 오브젝트/자식에서 찾는다.")]
    [SerializeField] private Animator animator;

    [Tooltip("재생할 State 이름(선택). 비우면 Animator Controller의 기본 State를 그대로 사용한다.")]
    [SerializeField] private string stateName = string.Empty;

    [Tooltip("재생 길이를 알 수 없거나 재생이 진행되지 않을 때 제거까지 기다리는 시간(초).")]
    [Min(0.05f)] [SerializeField] private float fallbackDuration = 2f;

    [Tooltip("실제 State 길이에 더하는 안전 여유(초). 재생이 멈춘 경우 길이 + 여유 후 제거.")]
    [Min(0f)] [SerializeField] private float lengthMargin = 0.5f;

    private int startFrame;
    private bool playable;
    private int expectedStateHash; // State short name hash. 0이면 기본 State(첫 평가 때 기록)
    private float observedLength = -1f;
    private readonly System.Collections.Generic.List<AnimatorClipInfo> clipInfos = new System.Collections.Generic.List<AnimatorClipInfo>();

    public Animator Animator => animator;
    public float Elapsed { get; private set; }
    public bool IsFinished { get; private set; }
    /// <summary>현재 적용 중인 최대 수명(초). 실제 State 길이를 알게 되면 갱신된다.</summary>
    public float SafetyLifetime => ResolveSafetyLifetime(observedLength, fallbackDuration, lengthMargin);
    public float FallbackDuration => fallbackDuration;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void Start()
    {
        startFrame = Time.frameCount;
        playable = CheckPlayable();
        if (playable && !string.IsNullOrEmpty(stateName))
        {
            animator.Play(expectedStateHash, 0, 0f);
        }
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>경과 시간을 진행하고 재생 완료/보호 시간을 확인한다. 테스트에서 직접 호출할 수 있다.</summary>
    public void Tick(float deltaTime)
    {
        if (IsFinished)
        {
            return;
        }

        Elapsed += Mathf.Max(0f, deltaTime);

        // Animator는 Update 이후에 평가되므로 시작 다음 프레임부터 실제 재생 상태를 읽는다.
        if (playable && Application.isPlaying && Time.frameCount > startFrame && animator != null && animator.isActiveAndEnabled)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (expectedStateHash == 0)
            {
                expectedStateHash = info.shortNameHash; // 기본 State
            }

            if (info.shortNameHash == expectedStateHash)
            {
                // 실제 클립 재생 길이(속도 1 기준). 멈춘 Animator가 수명을 늘리지 못하게 속도는 반영하지 않는다.
                // AnimatorStateInfo.length는 재생 속도로 나눈 값이라 speed=0이면 Infinity가 되므로 클립 길이를 우선 사용한다.
                float length = ResolveUnscaledLength(info);
                if (length > 0f)
                {
                    observedLength = length;
                }
                if (IsPlaybackFinished(info.normalizedTime))
                {
                    Finish();
                    return;
                }
            }
        }

        if (Elapsed >= SafetyLifetime)
        {
            if (playable)
            {
                Debug.LogWarning($"{name}: 충돌 이펙트 재생 완료를 확인하지 못해 {Elapsed:0.##}초 후 제거합니다.", this);
            }
            Finish();
        }
    }

    /// <summary>현재 State 클립의 원래 길이(속도 무관). 클립 정보가 없으면 유한한 State 길이, 그것도 없으면 -1.</summary>
    private float ResolveUnscaledLength(AnimatorStateInfo info)
    {
        float clipLength = -1f;
        animator.GetCurrentAnimatorClipInfo(0, clipInfos);
        foreach (AnimatorClipInfo clip in clipInfos)
        {
            if (clip.clip != null)
            {
                clipLength = Mathf.Max(clipLength, clip.clip.length);
            }
        }
        if (IsValidLength(clipLength))
        {
            return clipLength;
        }
        return IsValidLength(info.length) ? info.length : -1f;
    }

    private bool CheckPlayable()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"{name}: 충돌 이펙트 Animator/Controller가 없어 {fallbackDuration:0.##}초 후 제거합니다.", this);
            return false;
        }

        if (!string.IsNullOrEmpty(stateName))
        {
            expectedStateHash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, expectedStateHash))
            {
                Debug.LogWarning($"{name}: State '{stateName}'가 없어 {fallbackDuration:0.##}초 후 제거합니다.", this);
                expectedStateHash = 0;
                return false;
            }
        }
        return true;
    }

    /// <summary>자기 GameObject만 제거한다(부모 EffectContainer, 미사일, 플레이어는 영향 없음).</summary>
    private void Finish()
    {
        if (IsFinished)
        {
            return;
        }
        IsFinished = true;

        if (Application.isPlaying)
        {
            Destroy(gameObject);
        }
        else
        {
            DestroyImmediate(gameObject);
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>단발 재생 완료 여부: normalizedTime ≥ 1.</summary>
    public static bool IsPlaybackFinished(float normalizedTime)
    {
        return normalizedTime >= 1f;
    }

    /// <summary>보호 수명: 실제 길이를 알면 max(fallback, 길이 + 여유), 모르거나 유한하지 않으면 fallback. 항상 유한한 양수.</summary>
    public static float ResolveSafetyLifetime(float observedLength, float fallbackDuration, float margin)
    {
        float fallback = Mathf.Max(0.05f, fallbackDuration);
        return IsValidLength(observedLength) ? Mathf.Max(fallback, observedLength + Mathf.Max(0f, margin)) : fallback;
    }

    private static bool IsValidLength(float length)
    {
        return length > 0f && !float.IsInfinity(length) && !float.IsNaN(length);
    }
}
