using UnityEngine;

/// <summary>
/// 플레이어가 이동할 수 있는 정사각형 경기장 경계를 관리한다.
/// 입력 처리나 실제 이동은 담당하지 않으며, 보정된 좌표만 계산해 반환한다.
/// PlayerController(Stage 2)가 이동 좌표를 결정할 때 ClampPosition()을 호출한다.
/// </summary>
[DisallowMultipleComponent]
public class ArenaBounds : MonoBehaviour
{
    public const float DefaultHalfSize = 2.00f;

    [Tooltip("경기장(모래 영역)의 월드 중심 좌표. 카메라 위치가 아니라 Tilemap의 실제 모래 중심을 기준으로 지정한다.")]
    [SerializeField] private Vector2 center = Vector2.zero;

    [Tooltip("경기장 중심에서 각 변까지의 거리(이동 경계 한 변의 1/2). 기본 1.65 → 3.3×3.3 unit.")]
    [Min(0f)]
    [SerializeField] private float halfSize = DefaultHalfSize;

    /// <summary>경기장 월드 중심. 비정상 값(NaN/Infinity)은 0으로 처리한다.</summary>
    public Vector2 Center
    {
        get => SanitizeCenter(center);
        set => center = SanitizeCenter(value);
    }

    /// <summary>중심에서 각 변까지의 거리. 음수/NaN은 0으로 처리한다.</summary>
    public float HalfSize
    {
        get => SanitizeNonNegative(halfSize);
        set => halfSize = SanitizeNonNegative(value);
    }

    /// <summary>
    /// 목표 위치를 경기장 내부로 보정해 반환한다.
    /// 각 축을 Center ± (HalfSize - playerRadius) 범위로 독립적으로 제한한다.
    /// Transform이나 Rigidbody2D는 변경하지 않는다.
    /// </summary>
    public Vector2 ClampPosition(Vector2 position, float playerRadius)
    {
        return ClampToSquare(position, Center, HalfSize, playerRadius);
    }

    /// <summary>
    /// ClampPosition의 순수 계산부. 컴포넌트 없이도 사용/테스트할 수 있다.
    /// - halfSize, playerRadius가 음수 또는 NaN이면 0으로 취급한다.
    /// - playerRadius가 halfSize 이상이면 limit = 0 이 되어 Center로 고정된다.
    /// - position 성분이 NaN이면 해당 축은 Center 값으로 대체한다.
    /// </summary>
    public static Vector2 ClampToSquare(Vector2 position, Vector2 center, float halfSize, float playerRadius)
    {
        center = SanitizeCenter(center);
        float limit = Mathf.Max(0f, SanitizeNonNegative(halfSize) - SanitizeNonNegative(playerRadius));

        float x = float.IsNaN(position.x) ? center.x : position.x;
        float y = float.IsNaN(position.y) ? center.y : position.y;

        return new Vector2(
            Mathf.Clamp(x, center.x - limit, center.x + limit),
            Mathf.Clamp(y, center.y - limit, center.y + limit));
    }

    private static float SanitizeNonNegative(float value)
    {
        // NaN, 음수 → 0. +Infinity는 그대로 두면 경계가 무한대가 되므로 0으로 처리.
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            return 0f;
        }
        return value;
    }

    private static Vector2 SanitizeCenter(Vector2 value)
    {
        return new Vector2(
            float.IsNaN(value.x) || float.IsInfinity(value.x) ? 0f : value.x,
            float.IsNaN(value.y) || float.IsInfinity(value.y) ? 0f : value.y);
    }

    private void OnValidate()
    {
        halfSize = SanitizeNonNegative(halfSize);
        center = SanitizeCenter(center);
    }

    private void OnDrawGizmosSelected()
    {
        // 에디터 Scene 뷰 전용. 게임 화면에는 렌더링되지 않는다.
        // 직렬화 필드를 매 프레임 읽으므로 Inspector 변경이 즉시 반영된다.
        Vector2 c = Center;
        float size = HalfSize * 2f;

        Color previous = Gizmos.color;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(new Vector3(c.x, c.y, 0f), new Vector3(size, size, 0f));
        Gizmos.color = previous;
    }
}
