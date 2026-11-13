using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 실제 Physics2D로 Player(Kinematic + Trigger)와 Missile(Kinematic + Trigger) 충돌을 검증한다.
/// 실제 Play.unity의 Player(Collider 반지름/오프셋 그대로)와 실제 Missile.prefab을 사용한다.
/// 씬/프리팹 파일은 수정하지 않는다(런타임 인스턴스만 조작).
/// </summary>
public class PlayerHitPhysicsTests
{
    private const string ScenePath = "Assets/Scenes/Play.unity";
    private const string PrefabPath = "Assets/Prefabs/Missile.prefab";
    private static readonly Bounds NoDespawn = new Bounds(Vector3.zero, Vector3.one * 1000f);

    private PlayerController player;
    private PlayerHitDetector detector;
    private CircleCollider2D playerCol;
    private TriggerProbe probe;
    private Missile prefab;
    private readonly List<float> hitHeights = new List<float>();

    [UnitySetUp]
    public IEnumerator SetUp()
    {
#if UNITY_EDITOR
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        // 테스트가 미사일과 상태를 직접 제어하도록 자동 시스템 제거
        foreach (var s in Object.FindObjectsByType<MissileSpawner>(FindObjectsSortMode.None)) Object.Destroy(s.gameObject);
        foreach (var g in Object.FindObjectsByType<GameManager>(FindObjectsSortMode.None)) Object.Destroy(g.gameObject);
        foreach (var m in Object.FindObjectsByType<Missile>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);

        player = Object.FindFirstObjectByType<PlayerController>();
        Assert.IsNotNull(player, "Play.unity에 PlayerController가 필요");
        playerCol = player.GetComponent<CircleCollider2D>();
        detector = player.GetComponent<PlayerHitDetector>();
        if (detector == null) detector = player.gameObject.AddComponent<PlayerHitDetector>();
        probe = player.gameObject.AddComponent<TriggerProbe>();

        prefab = AssetDatabase.LoadAssetAtPath<Missile>(PrefabPath);
        Assert.IsNotNull(prefab, "Missile.prefab 루트에 Missile 컴포넌트가 필요");

        hitHeights.Clear();
        detector.OnPlayerHit += _ => hitHeights.Add(player.JumpHeight);
        yield return new WaitForFixedUpdate();
        detector.ResetHitState();
        player.SetControlEnabled(true);
#else
        Assert.Ignore("에디터 전용 테스트");
        yield break;
#endif
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = 1f;
        yield return null;
    }

    private void SetOverlapQuery(bool enabled)
    {
#if UNITY_EDITOR
        var so = new SerializedObject(detector);
        so.FindProperty("useOverlapQuery").boolValue = enabled;
        so.ApplyModifiedPropertiesWithoutUndo();
#endif
    }

    private Vector2 PlayerColliderCenter => playerCol.bounds.center;

    private float RadiusSum(CircleCollider2D missileCol)
    {
        float ps = Mathf.Max(Mathf.Abs(playerCol.transform.lossyScale.x), Mathf.Abs(playerCol.transform.lossyScale.y));
        float ms = Mathf.Max(Mathf.Abs(missileCol.transform.lossyScale.x), Mathf.Abs(missileCol.transform.lossyScale.y));
        return playerCol.radius * ps + missileCol.radius * ms;
    }

    /// <summary>미사일 Collider 중심이 colliderCenter에 오도록 생성한다(프리팹의 Collider Offset 반영).</summary>
    private Missile SpawnAt(Vector2 colliderCenter, Vector2 dir, float speed, out CircleCollider2D col)
    {
        Missile m = Object.Instantiate(prefab, new Vector3(0f, 50f, 0f), Quaternion.identity);
        col = m.GetComponent<CircleCollider2D>();
        Vector2 offsetWorld = (Vector2)m.transform.TransformPoint(col.offset) - (Vector2)m.transform.position;
        Vector2 pos = colliderCenter - offsetWorld;
        // 목표점을 아주 멀리 둬서 테스트 중 화면 밖 제거가 일어나지 않게 한다
        m.Initialize(pos, dir, speed, pos + dir * 1000f, NoDespawn);
        return m;
    }

    private bool Overlapping(Collider2D a, Collider2D b) => Physics2D.Distance(a, b).isOverlapped;

    // ---------------------------------------------------------------

    [UnityTest]
    public IEnumerator KinematicTriggerCallbacks_FireBetweenPlayerAndMissile_CallbackPathOnly()
    {
        SetOverlapQuery(false); // Trigger 콜백 경로만으로 판정
        Missile m = SpawnAt(PlayerColliderCenter + new Vector2(-1f, 0f), Vector2.right, 2.0f, out var mc);

        for (int i = 0; i < 100 && hitHeights.Count == 0; i++) yield return new WaitForFixedUpdate();

        Debug.Log($"[Stage4] callback-only: Enter={probe.EnterCount} Stay={probe.StayCount} hits={hitHeights.Count}");
        Assert.Greater(probe.EnterCount, 0, "Kinematic-Kinematic Trigger Enter 콜백이 발생하지 않음");
        Assert.AreEqual(1, hitHeights.Count, "콜백 경로로 피격되지 않음");
        Object.Destroy(m.gameObject);
    }

    [UnityTest]
    public IEnumerator MovingMissile_RealContact_HitsGroundedPlayer()
    {
        Missile m = SpawnAt(PlayerColliderCenter + new Vector2(0f, 1.2f), Vector2.down, 2.33f, out var mc);
        bool hitWhileOverlapping = false;
        detector.OnPlayerHit += _ => hitWhileOverlapping = Overlapping(playerCol, mc);

        for (int i = 0; i < 100 && hitHeights.Count == 0; i++) yield return new WaitForFixedUpdate();

        Assert.AreEqual(1, hitHeights.Count);
        Assert.AreEqual(0f, hitHeights[0]);
        Assert.IsTrue(hitWhileOverlapping, "실제 겹친 상태에서 피격되어야 함");

        // 계속 겹쳐 지나가도 이벤트 중복 없음
        for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
        Assert.AreEqual(1, hitHeights.Count);
        Object.Destroy(m.gameObject);
    }

    [UnityTest]
    public IEnumerator NotAMissileTrigger_IsIgnoredByPhysics()
    {
        var wall = new GameObject("Wall");
        wall.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var box = wall.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one;
        wall.transform.position = PlayerColliderCenter;

        for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
        Assert.IsTrue(Overlapping(playerCol, box));
        Assert.AreEqual(0, hitHeights.Count);
        Assert.IsFalse(detector.IsHitProcessed);
        Object.Destroy(wall);
    }

    [UnityTest]
    public IEnumerator JumpAboveThreshold_MissilePassesThrough_NoHit()
    {
        const float speed = 2.33f;
        Missile tmp = SpawnAt(new Vector2(0f, 50f), Vector2.right, 0f, out var tmpCol);
        float rSum = RadiusSum(tmpCol);
        Object.Destroy(tmp.gameObject);
        Assume.That(rSum, Is.LessThan(0.5f), "Collider가 커서 체공 시간 안에 통과할 수 없는 구성");

        // 점프 시작 약 0.15초 후 겹치기 시작 → (0.15 + 2·rSum/speed)초 후 겹침 종료 (최고점 ≈0.47, 0.18 이상 구간 ≈0.08~0.67초)
        Vector2 start = PlayerColliderCenter + new Vector2(-(rSum + speed * 0.15f), 0f);
        Assert.IsTrue(player.TryJump());
        Missile m = SpawnAt(start, Vector2.right, speed, out var mc);

        int overlappedSteps = 0;
        float minHeightWhileOverlapping = float.MaxValue;
        for (int i = 0; i < 75; i++) // 1.5초
        {
            yield return new WaitForFixedUpdate();
            if (Overlapping(playerCol, mc))
            {
                overlappedSteps++;
                minHeightWhileOverlapping = Mathf.Min(minHeightWhileOverlapping, player.JumpHeight);
            }
        }

        Debug.Log($"[Stage4] dodge: overlappedSteps={overlappedSteps} minH={minHeightWhileOverlapping:F3} Enter={probe.EnterCount} hits={hitHeights.Count}");
        Assert.Greater(overlappedSteps, 3, "미사일이 실제로 플레이어 Collider와 겹쳐야 함");
        Assert.GreaterOrEqual(minHeightWhileOverlapping, 0.18f);
        Assert.IsTrue(player.IsGrounded, "착지 완료");
        Assert.AreEqual(0, hitHeights.Count, "회피 높이에서 겹친 미사일로 피격되면 안 됨");
        Object.Destroy(m.gameObject);
    }

    [UnityTest]
    public IEnumerator LandingWhileOverlapping_IsHit([Values(true, false)] bool useOverlapQuery)
    {
        SetOverlapQuery(useOverlapQuery);
        Assert.IsTrue(player.TryJump());
        for (int i = 0; i < 50 && player.JumpHeight < 0.3f; i++) yield return new WaitForFixedUpdate();
        Assert.GreaterOrEqual(player.JumpHeight, 0.3f);

        // 공중에서 정지 미사일과 겹친 채로 하강
        Missile m = SpawnAt(PlayerColliderCenter, Vector2.right, 0f, out var mc);
        bool hitAboveThreshold = false;
        detector.OnPlayerHit += _ => hitAboveThreshold |= player.JumpHeight >= 0.18f;

        for (int i = 0; i < 75 && hitHeights.Count == 0; i++) yield return new WaitForFixedUpdate();

        Debug.Log($"[Stage4] land(overlapQuery={useOverlapQuery}): Enter={probe.EnterCount} Stay={probe.StayCount} hits={hitHeights.Count} h={(hitHeights.Count > 0 ? hitHeights[0] : -1f):F3}");
        Assert.IsTrue(Overlapping(playerCol, mc));
        Assert.AreEqual(1, hitHeights.Count, "겹친 채 착지하면 피격되어야 함");
        Assert.IsFalse(hitAboveThreshold);
        Assert.Less(hitHeights[0], 0.18f);
        Object.Destroy(m.gameObject);
    }
}
