using UnityEngine;

/// <summary>
/// 점프 높이에 따라 플레이어 그림자(Player/Shadow)의 크기와 투명도를 바꾼다. Shadow 오브젝트에 붙인다.
/// - 지면(높이 0): Inspector에 설정된 원래 localScale / color 그대로.
/// - 높이에 비례해 선형으로 축소·투명해지고, 최대 점프 높이에서 MinScale / MinAlphaMultiplier가 된다.
/// - 최대 점프 높이는 PlayerController의 JumpPower, Gravity로 계산(v² / 2g ≈ 0.47)해 밸런스 변경과 자동으로 맞춘다.
/// - 높이는 PlayerController.VisualJumpHeight(Visual에 실제 적용된 보간 높이)를 읽으므로 캐릭터 그래픽과 같은 프레임에 움직인다.
/// 위치, Sorting Order, 스프라이트, 색상(RGB)은 바꾸지 않는다. PlayerController/Rigidbody2D/Collider2D는 읽기만 한다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // PlayerController.LateUpdate(표시 높이 갱신) 이후에 읽는다
public class PlayerShadowController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("높이를 읽을 플레이어. 비우면 부모에서 찾는다.")]
    [SerializeField] private PlayerController playerController;
    [Tooltip("그림자 SpriteRenderer. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private SpriteRenderer shadowRenderer;

    [Header("At Max Jump Height")]
    [Tooltip("최대 점프 높이에서의 크기 배율 (원래 크기 = 1).")]
    [Range(0.05f, 1f)] [SerializeField] private float minScale = 0.7f;
    [Tooltip("최대 점프 높이에서의 불투명도 배율 (원래 알파 = 1).")]
    [Range(0f, 1f)] [SerializeField] private float minAlphaMultiplier = 0.6f;

    [Header("Max Jump Height")]
    [Tooltip("켜면 PlayerController의 JumpPower/Gravity로 최대 높이를 계산한다(v² / 2g).")]
    [SerializeField] private bool useControllerJumpPhysics = true;
    [Tooltip("계산을 끄거나 계산할 수 없을 때 사용하는 최대 점프 높이(unit).")]
    [Min(0.01f)] [SerializeField] private float fallbackMaxJumpHeight = 0.47f;

    private bool baseCaptured;
    private Vector3 baseScale;
    private Color baseColor;

    public float MinScale => minScale;
    public float MinAlphaMultiplier => minAlphaMultiplier;
    public Vector3 BaseScale => baseScale;
    public Color BaseColor => baseColor;

    /// <summary>현재 사용하는 최대 점프 높이(unit).</summary>
    public float MaxJumpHeight
    {
        get
        {
            if (useControllerJumpPhysics && playerController != null)
            {
                float computed = ComputeMaxJumpHeight(playerController.JumpPower, playerController.Gravity);
                if (computed > 0f)
                {
                    return computed;
                }
            }
            return Mathf.Max(0.01f, fallbackMaxJumpHeight);
        }
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureBase();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void OnDisable()
    {
        RestoreBase(); // 비활성화되면 원래 그림자로
    }

    /// <summary>PlayerController의 현재 표시 높이로 그림자를 갱신한다.</summary>
    public void Refresh()
    {
        ApplyHeight(playerController != null ? playerController.VisualJumpHeight : 0f);
    }

    /// <summary>주어진 점프 높이(unit)에 맞춰 크기/투명도를 적용한다. 테스트에서 직접 호출할 수 있다.</summary>
    public void ApplyHeight(float height)
    {
        ResolveReferences();
        CaptureBase();

        float t = ComputeNormalizedHeight(height, MaxJumpHeight);
        float scale = Mathf.Lerp(1f, minScale, t);
        transform.localScale = new Vector3(baseScale.x * scale, baseScale.y * scale, baseScale.z);

        if (shadowRenderer != null)
        {
            Color c = baseColor;
            c.a = baseColor.a * Mathf.Lerp(1f, minAlphaMultiplier, t);
            shadowRenderer.color = c;
        }
    }

    /// <summary>원래 크기와 색으로 되돌린다.</summary>
    public void RestoreBase()
    {
        if (!baseCaptured)
        {
            return;
        }
        transform.localScale = baseScale;
        if (shadowRenderer != null)
        {
            shadowRenderer.color = baseColor;
        }
    }

    private void CaptureBase()
    {
        if (baseCaptured)
        {
            return;
        }
        baseCaptured = true;
        baseScale = transform.localScale;
        baseColor = shadowRenderer != null ? shadowRenderer.color : Color.white;
    }

    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = GetComponentInParent<PlayerController>();
        }
        if (shadowRenderer == null)
        {
            shadowRenderer = GetComponent<SpriteRenderer>();
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>등가속도 점프의 최고 높이 v² / 2g. 값이 잘못되었으면 0.</summary>
    public static float ComputeMaxJumpHeight(float jumpPower, float gravity)
    {
        if (!(jumpPower > 0f) || !(gravity > 0f) || float.IsInfinity(jumpPower) || float.IsInfinity(gravity))
        {
            return 0f;
        }
        return jumpPower * jumpPower / (2f * gravity);
    }

    /// <summary>높이를 0~1로 정규화한다. 0 이하/NaN → 0, 최대 높이 이상/+∞ → 1. 최대 높이가 잘못되었으면 0.</summary>
    public static float ComputeNormalizedHeight(float height, float maxHeight)
    {
        if (float.IsNaN(height) || height <= 0f || !(maxHeight > 0f) || float.IsInfinity(maxHeight))
        {
            return 0f;
        }
        if (float.IsPositiveInfinity(height))
        {
            return 1f;
        }
        return Mathf.Clamp01(height / maxHeight);
    }
}
