using System;
using UnityEngine;

/// <summary>
/// 개별 미사일의 직선 이동, 방향별 이미지 선택, 정지 및 제거를 담당한다.
/// - 생성 시 MissileSpawner가 Initialize()로 위치/방향/속도/목표점/제거 범위를 전달한다.
/// - 생성 후 방향과 속도는 바뀌지 않으며 플레이어를 추적하지 않는다.
/// - 실제 이동 방향은 360° 자유 각도. 이미지만 가장 가까운 8방향 스프라이트를 사용한다(회전 없음).
/// - 목표점을 지나친 뒤 제거 범위(카메라 + DespawnMargin)를 벗어나면 제거된다.
///   생성 직후 화면 밖에 있는 것은 정상이며 그 이유로 제거하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class Missile : MonoBehaviour
{
    public const int DirectionCount = 8;

    /// <summary>방향 인덱스: 0 S, 1 SE, 2 E, 3 NE, 4 N, 5 NW, 6 W, 7 SW.</summary>
    public enum Direction8
    {
        South = 0,
        SouthEast = 1,
        East = 2,
        NorthEast = 3,
        North = 4,
        NorthWest = 5,
        West = 6,
        SouthWest = 7,
    }

    [Header("Visual")]
    [Tooltip("방향별 이미지를 표시할 SpriteRenderer. 비우면 자식에서 찾는다.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("0 S, 1 SE, 2 E, 3 NE, 4 N, 5 NW, 6 W, 7 SW. 비어 있는 방향은 South(0) 이미지로 대체된다.")]
    [SerializeField] private Sprite[] directionSprites = new Sprite[DirectionCount];

    [Header("Safety")]
    [Tooltip("설정 오류로 제거 조건이 충족되지 않을 때를 대비한 최대 수명(초). 0 이하면 사용하지 않음.")]
    [SerializeField] private float maxLifetime = 20f;

    private Rigidbody2D body;
    private Sprite defaultSprite;
    private bool initialized;
    private bool despawned;

    /// <summary>제거될 때 정확히 한 번 발생한다.</summary>
    public event Action<Missile> OnDespawned;

    public Vector2 SpawnPosition { get; private set; }
    public Vector2 Position { get; private set; }
    public Vector2 MoveDirection { get; private set; }
    public float MoveSpeed { get; private set; }
    public Vector2 AimTarget { get; private set; }
    public Bounds DespawnBounds { get; private set; }
    public bool IsMoving { get; private set; }
    /// <summary>목표점(경기장 내부)을 지나쳤는지. true가 된 뒤에만 화면 밖 제거가 적용된다.</summary>
    public bool HasPassedTarget { get; private set; }
    public bool IsDespawned => despawned;
    public float Age { get; private set; }
    /// <summary>선택된 8방향 인덱스. Stage 8 애니메이션에서 같은 값을 사용할 수 있다.</summary>
    public Direction8 VisualDirection { get; private set; }

    /// <summary>
    /// 생성 초기값을 설정한다. direction은 정규화되어 저장되며 이후 변경되지 않는다.
    /// </summary>
    public void Initialize(Vector2 spawnPosition, Vector2 direction, float speed, Vector2 aimTarget, Bounds despawnBounds)
    {
        CacheComponents();

        SpawnPosition = spawnPosition;
        Position = spawnPosition;
        MoveDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.down;
        MoveSpeed = Mathf.Max(0f, speed);
        AimTarget = aimTarget;
        DespawnBounds = despawnBounds;
        HasPassedTarget = HasPassed(Position, AimTarget, MoveDirection);
        Age = 0f;
        IsMoving = true;
        initialized = true;

        body.position = spawnPosition;
        transform.position = new Vector3(spawnPosition.x, spawnPosition.y, transform.position.z);

        ApplyDirectionVisual(GetDirectionIndex(MoveDirection));
    }

    private void FixedUpdate()
    {
        Tick(Time.fixedDeltaTime);
    }

    /// <summary>고정 스텝 1회: 직선 이동 후 제거 조건 확인. 테스트에서 직접 호출할 수 있다.</summary>
    public void Tick(float deltaTime)
    {
        if (!initialized || despawned || deltaTime <= 0f)
        {
            return;
        }

        Age += deltaTime;
        if (IsMoving)
        {
            Move(deltaTime);
        }

        if (ShouldDespawn())
        {
            Despawn();
        }
    }

    /// <summary>생성 시 정해진 방향과 속도로 직선 이동한다.</summary>
    public void Move(float deltaTime)
    {
        if (!initialized || despawned || !IsMoving)
        {
            return;
        }

        Position += MoveDirection * (MoveSpeed * deltaTime);
        body.MovePosition(Position);

        if (!HasPassedTarget)
        {
            HasPassedTarget = HasPassed(Position, AimTarget, MoveDirection);
        }
    }

    /// <summary>이동을 멈춘다(게임 오버 등). 미사일은 제거되지 않고 제자리에 남는다.</summary>
    public void StopMovement()
    {
        IsMoving = false;
    }

    /// <summary>미사일을 제거하고 OnDespawned를 한 번 발생시킨다. 중복 호출은 무시된다.</summary>
    public void Despawn()
    {
        if (despawned)
        {
            return;
        }

        NotifyDespawned();

        if (Application.isPlaying)
        {
            Destroy(gameObject);
        }
        else
        {
            DestroyImmediate(gameObject);
        }
    }

    private void OnDestroy()
    {
        // Despawn()을 거치지 않고 파괴된 경우(씬 종료, 외부 Destroy)에도 목록에 파괴된 참조가 남지 않도록 알린다.
        NotifyDespawned();
    }

    private void OnValidate()
    {
        if (directionSprites == null || directionSprites.Length != DirectionCount)
        {
            Array.Resize(ref directionSprites, DirectionCount);
        }
    }

    private bool ShouldDespawn()
    {
        if (maxLifetime > 0f && Age >= maxLifetime)
        {
            return true;
        }
        return HasPassedTarget && IsOutside(DespawnBounds, Position);
    }

    private void NotifyDespawned()
    {
        if (despawned)
        {
            return;
        }
        despawned = true;
        IsMoving = false;

        Action<Missile> handler = OnDespawned;
        OnDespawned = null; // 구독자 참조 해제
        handler?.Invoke(this);
    }

    private void CacheComponents()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
            // 인스턴스에만 적용(프리팹 자산은 변경하지 않음). Kinematic이면 원래 중력 영향이 없지만 명시적으로 보장한다.
            body.gravityScale = 0f;
        }
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
        if (defaultSprite == null && spriteRenderer != null)
        {
            // 프리팹에 지정된 South 이미지를 최종 대체 이미지로 기억한다.
            defaultSprite = spriteRenderer.sprite;
        }
    }

    /// <summary>
    /// 방향 이미지를 적용한다. Stage 8에서 Animator를 붙일 경우 이 지점에서 방향 파라미터를 설정하면 된다.
    /// </summary>
    private void ApplyDirectionVisual(int index)
    {
        VisualDirection = (Direction8)index;
        if (spriteRenderer == null)
        {
            return;
        }

        Sprite sprite = ResolveSprite(directionSprites, index, defaultSprite);
        if (sprite != null)
        {
            spriteRenderer.sprite = sprite;
        }
    }

    // ---------------- 순수 계산 (EditMode 테스트 대상) ----------------

    /// <summary>
    /// 이동 벡터에 가장 가까운 45° 방향 인덱스(0 S, 1 SE, 2 E, 3 NE, 4 N, 5 NW, 6 W, 7 SW).
    /// South(-90°)에서 반시계 방향으로 증가한다. 0 벡터/NaN은 South.
    /// </summary>
    public static int GetDirectionIndex(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0f || float.IsNaN(direction.x) || float.IsNaN(direction.y))
        {
            return (int)Direction8.South;
        }

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg; // (-180, 180], East = 0
        float stepsFromSouth = (angle + 90f) / 45f;
        int index = Mathf.FloorToInt(stepsFromSouth + 0.5f);
        return ((index % DirectionCount) + DirectionCount) % DirectionCount;
    }

    /// <summary>방향 스프라이트 → 없으면 South 슬롯 → 없으면 fallback(프리팹 기본 이미지).</summary>
    public static Sprite ResolveSprite(Sprite[] sprites, int index, Sprite fallback)
    {
        if (sprites != null)
        {
            if (index >= 0 && index < sprites.Length && sprites[index] != null)
            {
                return sprites[index];
            }
            if (sprites.Length > 0 && sprites[0] != null)
            {
                return sprites[0];
            }
        }
        return fallback;
    }

    /// <summary>position이 이동 방향 기준으로 target에 도달했거나 지나쳤는지.</summary>
    public static bool HasPassed(Vector2 position, Vector2 target, Vector2 direction)
    {
        return Vector2.Dot(position - target, direction) >= 0f;
    }

    /// <summary>XY 평면 기준으로 bounds 밖인지.</summary>
    public static bool IsOutside(Bounds bounds, Vector2 position)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return position.x < min.x || position.x > max.x || position.y < min.y || position.y > max.y;
    }
}
