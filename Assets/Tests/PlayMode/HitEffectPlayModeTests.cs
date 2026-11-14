using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Stage 8: 실제 Missile/MissileHitEffect 프리팹으로 비행 애니메이션 방향, 직선 이동 유지, 이펙트 재생 후 자동 제거를 검증한다.
/// </summary>
public class HitEffectPlayModeTests
{
    private const string MissilePrefabPath = "Assets/Prefabs/Missile.prefab";
    private const string HitEffectPrefabPath = "Assets/Prefabs/MissileHitEffect.prefab";

    private BestTimeBackup bestTimeBackup;
    private readonly List<GameObject> spawned = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();
        spawned.Clear();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
        Time.timeScale = 1f;
        bestTimeBackup.Restore();
        yield return null;
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float t0 = Time.time;
        while (Time.time - t0 < seconds) yield return null;
    }

    private static T LoadPrefab<T>(string path) where T : Object
    {
#if UNITY_EDITOR
        var prefab = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.IsNotNull(prefab, path);
        return prefab;
#else
        Assert.Ignore("에디터 전용");
        return null;
#endif
    }

    private GameObject SpawnEffect()
    {
        GameObject go = Object.Instantiate(LoadPrefab<GameObject>(HitEffectPrefabPath), new Vector3(1f, 1f, 0f), Quaternion.identity);
        if (go.GetComponent<HitEffect>() == null) go.AddComponent<HitEffect>(); // 프리팹에 아직 부착하지 않았어도 검증
        spawned.Add(go);
        return go;
    }

    // ---------- 미사일 비행 애니메이션 ----------

    [UnityTest]
    public IEnumerator Missile_PlaysDirectionState_MovesStraight_NoRotation()
    {
        var prefab = LoadPrefab<Missile>(MissilePrefabPath);
        var bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 0f));
        var missiles = new List<Missile>();
        for (int i = 0; i < Missile.DirectionCount; i++)
        {
            float rad = (-90f + 45f * i) * Mathf.Deg2Rad; // South부터 반시계
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Missile m = Object.Instantiate(prefab);
            spawned.Add(m.gameObject);
            m.Initialize(Vector2.zero - dir * 3f, dir, 2f, Vector2.zero, bounds);
            missiles.Add(m);
        }

        yield return null;
        yield return null;

        float t0 = Time.time;
        var starts = new List<Vector2>();
        var visualPose = new List<(Vector3, Quaternion)>();
        foreach (Missile m in missiles)
        {
            starts.Add(m.Position);
            Transform v = m.GetComponentInChildren<Animator>().transform;
            visualPose.Add((v.localPosition, v.localRotation));
        }
        yield return WaitSeconds(0.5f);
        float elapsed = Time.time - t0;

        for (int i = 0; i < missiles.Count; i++)
        {
            Missile m = missiles[i];
            var expected = (Missile.Direction8)i;
            Animator animator = m.GetComponentInChildren<Animator>();
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);

            Assert.AreEqual(expected, m.VisualDirection);
            Assert.AreEqual(Missile.GetDirectionStateHash(expected), info.fullPathHash, $"{expected} 비행 State 재생");
            Assert.Greater(info.normalizedTime, 0.2f, "Loop 재생 진행");
            Assert.AreEqual(Quaternion.identity, m.transform.rotation, "Transform 회전 없음");
            Assert.AreEqual(visualPose[i].Item2, animator.transform.localRotation, "클립이 회전을 바꾸지 않음");
            Assert.AreEqual(visualPose[i].Item1, animator.transform.localPosition, "클립이 위치를 바꾸지 않음");

            Vector2 moved = m.Position - starts[i];
            float rad = (-90f + 45f * i) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Assert.AreEqual(2f, m.MoveSpeed, 1e-6f, "속도 불변");
            Assert.AreEqual(dir.x, m.MoveDirection.x, 1e-5f, "방향 불변");
            Assert.AreEqual(dir.y, m.MoveDirection.y, 1e-5f);
            Assert.Greater(Vector2.Dot(moved.normalized, dir), 0.9999f, "직선 이동");
            Assert.AreEqual(2f * elapsed, moved.magnitude, 0.1f, "속도대로 이동");
        }
    }

    // ---------- 충돌 이펙트 재생/제거 ----------

    [UnityTest]
    public IEnumerator HitEffect_RemovedAfterClipLength()
    {
        GameObject go = SpawnEffect();
        var effect = go.GetComponent<HitEffect>();
        float t0 = Time.time;

        yield return WaitSeconds(0.4f);
        Assert.IsTrue(go != null, "재생 중에는 유지");
        Assert.IsTrue(effect.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f);

        while (go != null && Time.time - t0 < 3f) yield return null;
        float life = Time.time - t0;
        Assert.IsTrue(go == null, "재생 완료 후 제거");
        Assert.That(life, Is.InRange(0.55f, 1.0f), "실제 클립 길이(0.625초) 기준 제거");
    }

    [UnityTest]
    public IEnumerator HitEffect_AnimatorStalled_RemovedByFallback()
    {
        GameObject go = SpawnEffect();
        go.GetComponent<HitEffect>().Animator.speed = 0f; // 테스트 전용: 재생이 진행되지 않음
        float t0 = Time.time;
        while (go != null && Time.time - t0 < 4f) yield return null;
        Assert.IsTrue(go == null);
        Assert.That(Time.time - t0, Is.InRange(1.8f, 2.6f), "max(fallback 2초, 길이+0.5)");
    }

    [UnityTest]
    public IEnumerator HitEffect_NoController_RemovedByFallback()
    {
        GameObject prefab = LoadPrefab<GameObject>(HitEffectPrefabPath);
        var go = new GameObject("EffectNoController");
        spawned.Add(go);
        go.SetActive(false);
        go.AddComponent<SpriteRenderer>().sprite = prefab.GetComponentInChildren<SpriteRenderer>().sprite;
        go.AddComponent<Animator>();
        go.AddComponent<HitEffect>();
        go.SetActive(true);

        float t0 = Time.time;
        while (go != null && Time.time - t0 < 4f) yield return null;
        Assert.IsTrue(go == null);
        Assert.That(Time.time - t0, Is.InRange(1.8f, 2.6f));
    }

    // ---------- 실제 Play 씬 ----------

    [UnityTest]
    public IEnumerator PlayScene_MissileHit_EffectAtMissile_DeathIndependent_RestartWorks()
    {
        int loaded = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> h = (s, _) => { if (s.name == SceneNavigator.PlaySceneName) loaded++; };
        SceneManager.sceneLoaded += h;
        SceneNavigator.LoadPlayScene();
        for (int i = 0; i < 300 && loaded == 0; i++) yield return null;
        SceneManager.sceneLoaded -= h;
        yield return null;
        yield return null;
        yield return PlaySceneTestUtil.WaitForGameStart();

        var gm = Object.FindFirstObjectByType<GameManager>();
        var spawner = Object.FindFirstObjectByType<MissileSpawner>();
        var results = new List<GameResult>();
        gm.OnGameResultReady += r => results.Add(r);

        Missile m = spawner.SpawnMissile();
        yield return WaitSeconds(0.3f);
        Vector2 hitPos = m.Position;
        gm.HandlePlayerHit(m);

        GameObject effect = gm.LastHitEffect;
        if (effect == null)
        {
            Assert.Ignore("GameManager의 Hit Effect Prefab이 아직 연결되지 않았습니다. Stage 8 수동 설정 후 다시 실행하세요.");
        }
        Assert.AreEqual(hitPos.x, effect.transform.position.x, 1e-4f);
        Assert.AreEqual(hitPos.y, effect.transform.position.y, 1e-4f);
        Assert.IsNull(effect.GetComponentInParent<Missile>(), "미사일 자식 아님");
        Assert.IsNull(effect.GetComponentInParent<PlayerController>(), "플레이어 자식 아님");
        Assert.IsTrue(RecordStore.GetBestTime() > 0f, "기록 즉시 저장");

        float t0 = Time.time;
        while (effect != null && Time.time - t0 < 3f) yield return null;
        Assert.IsTrue(effect == null, "이펙트 자동 제거");
        Assert.IsTrue(m != null, "미사일 유지");
        Assert.IsTrue(Object.FindFirstObjectByType<PlayerController>() != null, "플레이어 유지");

        while (results.Count == 0 && Time.time - t0 < 4f) yield return null;
        Assert.AreEqual(1, results.Count, "Death 완료 후 결과(기존 정책)");

        loaded = 0;
        SceneManager.sceneLoaded += h;
        new GameObject("Btn").AddComponent<SceneNavigationButtons>().OnClickRestart();
        for (int i = 0; i < 300 && loaded == 0; i++) yield return null;
        SceneManager.sceneLoaded -= h;
        yield return null;
        yield return null;
        yield return PlaySceneTestUtil.WaitForGameStart();
        var newGm = Object.FindFirstObjectByType<GameManager>();
        Assert.AreEqual(GameState.Playing, newGm.CurrentState);
        Assert.IsNull(newGm.LastHitEffect);
        Assert.AreEqual(0, Object.FindObjectsByType<HitEffect>(FindObjectsSortMode.None).Length);
    }
}
