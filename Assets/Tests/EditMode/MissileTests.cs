using NUnit.Framework;
using UnityEngine;

public class MissileTests
{
    private const float Dt = 0.02f;
    private const float Eps = 1e-4f;

    // 실제 씬 기준: 카메라 (0.5,-0.5), size 2.5, 16:9, margin 0.33
    private static readonly Bounds SceneDespawnBounds =
        MissileSpawner.CalculateCameraBounds(new Vector2(0.5f, -0.5f), 2.5f, 16f / 9f, 0.33f);

    private GameObject go;
    private Missile missile;
    private SpriteRenderer renderer;
    private Sprite[] sprites;
    private Sprite prefabSouth;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("Missile");
        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<CircleCollider2D>().isTrigger = true;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        renderer = visual.AddComponent<SpriteRenderer>();

        sprites = new Sprite[8];
        for (int i = 0; i < 8; i++)
        {
            sprites[i] = MakeSprite("dir" + i);
        }
        prefabSouth = MakeSprite("prefabSouth");
        renderer.sprite = prefabSouth;

        missile = go.AddComponent<Missile>();
    }

    [TearDown]
    public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
        foreach (var s in sprites) if (s != null) { Object.DestroyImmediate(s.texture); Object.DestroyImmediate(s); }
        Object.DestroyImmediate(prefabSouth.texture);
        Object.DestroyImmediate(prefabSouth);
    }

    private static Sprite MakeSprite(string name)
    {
        var tex = new Texture2D(2, 2);
        var s = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        s.name = name;
        return s;
    }

    private void SetDirectionSprites(Sprite[] value)
    {
        var so = new UnityEditor.SerializedObject(missile);
        var prop = so.FindProperty("directionSprites");
        prop.arraySize = value.Length;
        for (int i = 0; i < value.Length; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = value[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- 8방향 인덱스 ----------

    [TestCase(0f, -1f, 0)]   // S
    [TestCase(1f, -1f, 1)]   // SE
    [TestCase(1f, 0f, 2)]    // E
    [TestCase(1f, 1f, 3)]    // NE
    [TestCase(0f, 1f, 4)]    // N
    [TestCase(-1f, 1f, 5)]   // NW
    [TestCase(-1f, 0f, 6)]   // W
    [TestCase(-1f, -1f, 7)]  // SW
    public void DirectionIndex_EightDirections(float x, float y, int expected)
    {
        Assert.AreEqual(expected, Missile.GetDirectionIndex(new Vector2(x, y)));
        Assert.AreEqual(expected, Missile.GetDirectionIndex(new Vector2(x, y) * 7.3f), "크기와 무관");
    }

    [TestCase(-90f + 20f, 0)]   // South에서 20° → S
    [TestCase(-90f + 25f, 1)]   // 25° → SE
    [TestCase(-90f - 20f, 0)]
    [TestCase(-90f - 25f, 7)]   // → SW
    [TestCase(10f, 2)]
    [TestCase(170f, 6)]
    [TestCase(-170f, 6)]
    [TestCase(112f, 4)]
    [TestCase(113f, 5)]
    public void DirectionIndex_NearestFortyFive(float angleDeg, int expected)
    {
        float r = angleDeg * Mathf.Deg2Rad;
        Assert.AreEqual(expected, Missile.GetDirectionIndex(new Vector2(Mathf.Cos(r), Mathf.Sin(r))));
    }

    [Test]
    public void DirectionIndex_AllAnglesInRange()
    {
        for (float a = 0f; a < 360f; a += 0.5f)
        {
            float r = a * Mathf.Deg2Rad;
            int i = Missile.GetDirectionIndex(new Vector2(Mathf.Cos(r), Mathf.Sin(r)));
            Assert.That(i, Is.InRange(0, 7));
        }
    }

    [Test]
    public void DirectionIndex_ZeroOrNaN_IsSouth()
    {
        Assert.AreEqual(0, Missile.GetDirectionIndex(Vector2.zero));
        Assert.AreEqual(0, Missile.GetDirectionIndex(new Vector2(float.NaN, 1f)));
    }

    // ---------- 스프라이트 대체 ----------

    [Test]
    public void ResolveSprite_FallbackChain()
    {
        var arr = new Sprite[8];
        Assert.AreSame(prefabSouth, Missile.ResolveSprite(arr, 3, prefabSouth), "모두 비면 프리팹 기본");
        arr[0] = sprites[0];
        Assert.AreSame(sprites[0], Missile.ResolveSprite(arr, 3, prefabSouth), "빈 방향은 South 슬롯");
        arr[3] = sprites[3];
        Assert.AreSame(sprites[3], Missile.ResolveSprite(arr, 3, prefabSouth));
        Assert.AreSame(prefabSouth, Missile.ResolveSprite(null, 3, prefabSouth));
        Assert.AreSame(sprites[0], Missile.ResolveSprite(new[] { sprites[0] }, 6, prefabSouth), "짧은 배열");
        Assert.IsNull(Missile.ResolveSprite(null, 2, null));
    }

    [Test]
    public void Initialize_AppliesDirectionSpriteWithoutRotation()
    {
        SetDirectionSprites(sprites);
        missile.Initialize(new Vector2(-5f, 0f), Vector2.right, 2f, Vector2.zero, SceneDespawnBounds);

        Assert.AreEqual(Missile.Direction8.East, missile.VisualDirection);
        Assert.AreSame(sprites[2], renderer.sprite);
        Assert.AreEqual(Quaternion.identity, go.transform.rotation);
        Assert.AreEqual(Quaternion.identity, renderer.transform.localRotation);
    }

    [Test]
    public void Initialize_NoSpritesRegistered_KeepsPrefabSouth()
    {
        missile.Initialize(new Vector2(0f, 5f), Vector2.down + Vector2.right * 0.9f, 2f, Vector2.zero, SceneDespawnBounds);
        Assert.AreSame(prefabSouth, renderer.sprite);
    }

    [Test]
    public void Initialize_OnlySouthRegistered_UsesSouthForOtherDirections()
    {
        var onlySouth = new Sprite[8];
        onlySouth[0] = sprites[0];
        SetDirectionSprites(onlySouth);
        missile.Initialize(new Vector2(-5f, -5f), new Vector2(1f, 1f), 2f, Vector2.zero, SceneDespawnBounds);
        Assert.AreEqual(Missile.Direction8.NorthEast, missile.VisualDirection);
        Assert.AreSame(sprites[0], renderer.sprite);
    }

    [Test]
    public void Initialize_WithoutSpriteRenderer_DoesNotThrow()
    {
        Object.DestroyImmediate(renderer.gameObject);
        Assert.DoesNotThrow(() =>
            missile.Initialize(new Vector2(0f, 5f), Vector2.down, 2f, Vector2.zero, SceneDespawnBounds));
    }

    // ---------- 이동 ----------

    [Test]
    public void Move_StraightLineWithFixedDirectionAndSpeed()
    {
        Vector2 start = new Vector2(-4.5f, -2.5f);
        Vector2 dir = new Vector2(3f, 1f).normalized; // 8방향이 아닌 자유 각도
        missile.Initialize(start, new Vector2(3f, 1f), 2.0f, new Vector2(0.5f, -0.5f), SceneDespawnBounds);

        Assert.AreEqual(1f, missile.MoveDirection.magnitude, 1e-5f);
        for (int i = 1; i <= 50; i++)
        {
            missile.Tick(Dt);
            Vector2 expected = start + dir * (2.0f * Dt * i);
            Assert.AreEqual(expected.x, missile.Position.x, Eps);
            Assert.AreEqual(expected.y, missile.Position.y, Eps);
        }
        Assert.AreEqual(dir.x, missile.MoveDirection.x, 1e-6f, "방향 불변");
        Assert.AreEqual(2.0f, missile.MoveSpeed, 1e-6f, "속도 불변");
    }

    [Test]
    public void StopMovement_StopsAndDoesNotDespawn()
    {
        missile.Initialize(new Vector2(0f, 5f), Vector2.down, 2f, new Vector2(0f, 0f), SceneDespawnBounds);
        missile.Tick(Dt);
        missile.StopMovement();
        Vector2 p = missile.Position;
        for (int i = 0; i < 100; i++) missile.Tick(Dt);

        Assert.IsFalse(missile.IsMoving);
        Assert.AreEqual(p, missile.Position);
        Assert.IsFalse(missile.IsDespawned);
    }

    // ---------- 제거 ----------

    [Test]
    public void SpawnedOffscreen_IsNotDespawnedBeforePassingTarget()
    {
        // 씬 제거 범위 X [-4.27, 5.27], Y [-3.33, 2.33] → (0.5, 4.5)는 화면 밖
        Vector2 spawn = new Vector2(0.5f, 4.5f);
        Assert.IsTrue(Missile.IsOutside(SceneDespawnBounds, spawn));

        int events = 0;
        missile.OnDespawned += _ => events++;
        missile.Initialize(spawn, Vector2.down, 2f, new Vector2(0.5f, -0.5f), SceneDespawnBounds);

        for (int i = 0; i < 20; i++) missile.Tick(Dt); // 0.4초, 여전히 화면 밖
        Assert.IsTrue(Missile.IsOutside(SceneDespawnBounds, missile.Position));
        Assert.IsFalse(missile.IsDespawned);
        Assert.AreEqual(0, events);
    }

    [Test]
    public void PassesTargetThenLeavesScreen_DespawnsOnceWithEvent()
    {
        int events = 0;
        Missile received = null;
        missile.OnDespawned += m => { events++; received = m; };

        Vector2 target = new Vector2(1.0f, -1.0f);
        Vector2 spawn = new Vector2(0.5f, -0.5f) + new Vector2(-5f, 0f);
        missile.Initialize(spawn, target - spawn, 2.0f, target, SceneDespawnBounds);

        bool sawPassedInside = false;
        int steps = 0;
        while (!missile.IsDespawned && steps < 1000)
        {
            missile.Tick(Dt);
            steps++;
            if (missile.IsDespawned) break;
            if (missile.HasPassedTarget && !Missile.IsOutside(SceneDespawnBounds, missile.Position))
                sawPassedInside = true;
        }

        Assert.IsTrue(sawPassedInside, "목표 통과 후 화면 안을 지나야 함");
        Assert.AreEqual(1, events);
        Assert.AreSame(missile, received);
        Assert.IsTrue(go == null, "Destroy되어야 함");

        go = null; // TearDown 중복 파괴 방지
    }

    [Test]
    public void Despawn_CalledTwice_SingleEvent()
    {
        int events = 0;
        missile.OnDespawned += _ => events++;
        missile.Initialize(new Vector2(0f, 5f), Vector2.down, 2f, Vector2.zero, SceneDespawnBounds);

        missile.Despawn();
        Assert.AreEqual(1, events);
        Assert.DoesNotThrow(() => missile.Despawn());
        Assert.AreEqual(1, events);
        go = null;
    }

    [Test]
    public void HasPassed_AndIsOutside_PureChecks()
    {
        Assert.IsFalse(Missile.HasPassed(new Vector2(-1f, 0f), Vector2.zero, Vector2.right));
        Assert.IsTrue(Missile.HasPassed(Vector2.zero, Vector2.zero, Vector2.right));
        Assert.IsTrue(Missile.HasPassed(new Vector2(0.1f, 3f), Vector2.zero, Vector2.right));

        var b = new Bounds(Vector3.zero, new Vector3(2f, 2f, 0f));
        Assert.IsFalse(Missile.IsOutside(b, new Vector2(1f, -1f)), "경계선 위는 내부");
        Assert.IsTrue(Missile.IsOutside(b, new Vector2(1.01f, 0f)));
        Assert.IsTrue(Missile.IsOutside(b, new Vector2(0f, -1.01f)));
    }
}
