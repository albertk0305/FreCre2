using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PlayerHitDetector 판정 로직 테스트 (EditMode, 물리 시뮬레이션 없음).
/// 겹침 자체는 EvaluateCollider(=Trigger 콜백/Overlap 질의가 호출하는 진입점)로 시뮬레이션한다.
/// 실제 Physics2D 겹침은 PlayMode 테스트(PlayerHitPhysicsTests)에서 검증한다.
/// </summary>
public class PlayerHitDetectorTests
{
    private const float Dt = 0.02f;

    private GameObject playerGo;
    private PlayerController player;
    private PlayerHitDetector detector;
    private GameObject missileGo;
    private Missile missile;
    private Collider2D missileCollider;
    private GameObject wallGo;
    private GameObject managerGo;
    private readonly List<Missile> hits = new List<Missile>();

    [SetUp]
    public void SetUp()
    {
        playerGo = new GameObject("Player");
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.15f;
        player = playerGo.AddComponent<PlayerController>();
        detector = playerGo.AddComponent<PlayerHitDetector>();
        SetRef(detector, "playerController", player);
        player.ResetPlayer(Vector2.zero);

        missileGo = new GameObject("Missile");
        missileGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        missileCollider = missileGo.AddComponent<CircleCollider2D>();
        missileCollider.isTrigger = true;
        missile = missileGo.AddComponent<Missile>();

        wallGo = new GameObject("NotAMissile");
        wallGo.AddComponent<BoxCollider2D>().isTrigger = true;

        hits.Clear();
        detector.OnPlayerHit += m => hits.Add(m);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerGo);
        if (missileGo != null) Object.DestroyImmediate(missileGo);
        Object.DestroyImmediate(wallGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void SetThreshold(float value)
    {
        var so = new SerializedObject(detector);
        so.FindProperty("dodgeHeightThreshold").floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void StartJumpAndStep(int steps)
    {
        Assert.IsTrue(player.TryJump());
        for (int i = 0; i < steps; i++) player.Tick(Dt);
    }

    private GameManager CreatePlayingManager()
    {
        managerGo = new GameObject("GameManager");
        var gm = managerGo.AddComponent<GameManager>();
        SetRef(gm, "playerController", player);
        SetRef(gm, "playerHitDetector", detector);
        SetRef(detector, "gameManager", gm);
        gm.StartGame();
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        return gm;
    }

    // ---------- 기본값 / 높이 규칙 ----------

    [Test]
    public void DefaultThreshold_Is018()
    {
        Assert.AreEqual(0.18f, detector.DodgeHeightThreshold, 1e-6f);
    }

    [TestCase(0f, true)]
    [TestCase(0.1799f, true)]
    [TestCase(0.18f, false)]   // 정확히 0.18 → 회피
    [TestCase(0.1801f, false)]
    [TestCase(0.47f, false)]
    public void IsHitByHeight_Rule(float height, bool expectedHit)
    {
        Assert.AreEqual(expectedHit, PlayerHitDetector.IsHitByHeight(height, 0.18f));
    }

    // ---------- 판정 ----------

    [Test]
    public void GroundedPlayer_OverlappingMissile_IsHit()
    {
        Assert.AreEqual(0f, player.JumpHeight);
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.IsTrue(detector.IsHitProcessed);
        Assert.AreEqual(1, hits.Count);
        Assert.AreSame(missile, hits[0]);
    }

    [Test]
    public void LowJumpHeight_BelowThreshold_IsHit()
    {
        StartJumpAndStep(1); // ≈0.049
        Assert.That(player.JumpHeight, Is.GreaterThan(0f).And.LessThan(0.18f));
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(1, hits.Count);
    }

    [Test]
    public void JumpHeightExactlyAtThreshold_Dodges()
    {
        StartJumpAndStep(3);
        float h = player.JumpHeight;
        Assert.Greater(h, 0f);

        SetThreshold(h); // JumpHeight == threshold
        Assert.IsFalse(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(0, hits.Count);

        SetThreshold(h + 1e-4f); // 바로 위 → 피격
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(1, hits.Count);
    }

    [Test]
    public void JumpHeightAboveThreshold_Dodges()
    {
        StartJumpAndStep(10); // ≈0.43
        Assert.Greater(player.JumpHeight, 0.18f);
        Assert.IsFalse(detector.EvaluateCollider(missileCollider));
        Assert.IsFalse(detector.IsHitProcessed);
        Assert.AreEqual(0, hits.Count);
    }

    [Test]
    public void HighJumpWhileOverlapping_NeverHitWhileAboveThreshold()
    {
        StartJumpAndStep(4);
        while (player.JumpHeight >= 0.18f)
        {
            Assert.IsFalse(detector.EvaluateCollider(missileCollider), $"h={player.JumpHeight}");
            player.Tick(Dt);
        }
        Assert.AreEqual(0, hits.Count);
    }

    [Test]
    public void LandingWhileStillOverlapping_IsHit()
    {
        StartJumpAndStep(5); // 회피 높이 이상에서 겹치기 시작
        Assert.GreaterOrEqual(player.JumpHeight, 0.18f);

        float heightAtHit = -1f;
        for (int i = 0; i < 100 && hits.Count == 0; i++)
        {
            float h = player.JumpHeight;
            if (detector.EvaluateCollider(missileCollider)) heightAtHit = h; // 매 스텝 Stay 판정
            else Assert.GreaterOrEqual(h, 0.18f, "회피 높이 미만인데 피격되지 않음");
            player.Tick(Dt);
        }

        Assert.AreEqual(1, hits.Count);
        Assert.That(heightAtHit, Is.GreaterThanOrEqualTo(0f).And.LessThan(0.18f));
    }

    [Test]
    public void NonMissileCollider_IsIgnored()
    {
        Assert.IsFalse(detector.EvaluateCollider(wallGo.GetComponent<Collider2D>()));
        Assert.IsFalse(detector.EvaluateCollider(null));
        Assert.IsFalse(detector.IsHitProcessed);
        Assert.AreEqual(0, hits.Count);
    }

    [Test]
    public void MissileColliderOnChild_IsRecognized()
    {
        var child = new GameObject("ChildCollider");
        child.transform.SetParent(missileGo.transform, false);
        var c = child.AddComponent<CircleCollider2D>();
        Assert.IsTrue(detector.EvaluateCollider(c));
    }

    [Test]
    public void OnPlayerHit_NotDuplicated()
    {
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.IsFalse(detector.EvaluateCollider(missileCollider));
        Assert.IsFalse(detector.CheckMissileCollision(missile));

        var second = new GameObject("Missile2");
        second.AddComponent<Rigidbody2D>();
        var m2 = second.AddComponent<Missile>();
        Assert.IsFalse(detector.CheckMissileCollision(m2), "다른 미사일도 추가 피격 없음");
        Object.DestroyImmediate(second);

        Assert.AreEqual(1, hits.Count);
    }

    [Test]
    public void ResetHitState_AllowsHitAgain()
    {
        detector.EvaluateCollider(missileCollider);
        detector.ResetHitState();
        Assert.IsFalse(detector.IsHitProcessed);
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(2, hits.Count);
    }

    [Test]
    public void DespawnedMissile_IsIgnored()
    {
        missile.Initialize(Vector2.zero, Vector2.right, 1f, Vector2.zero, new Bounds(Vector3.zero, Vector3.one * 100f));
        var m = missile;
        m.Despawn();
        missileGo = null;
        Assert.IsFalse(detector.CheckMissileCollision(m));
        Assert.AreEqual(0, hits.Count);
    }

    // ---------- 게임 상태 ----------

    [Test]
    public void ReadyState_IsIgnored()
    {
        managerGo = new GameObject("GameManager");
        var gm = managerGo.AddComponent<GameManager>();
        SetRef(detector, "gameManager", gm);
        Assert.AreEqual(GameState.Ready, gm.CurrentState);

        Assert.IsFalse(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(0, hits.Count);
    }

    [Test]
    public void PlayingState_IsHit()
    {
        CreatePlayingManager();
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(1, hits.Count);
    }

    [Test]
    public void GameOverState_NoFurtherHits()
    {
        var gm = CreatePlayingManager();
        gm.EndGame();
        Assert.AreEqual(GameState.GameOver, gm.CurrentState);

        detector.ResetHitState(); // 피격 플래그와 무관하게 상태로도 막히는지
        Assert.IsFalse(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(0, hits.Count);
    }

    [Test]
    public void DetectorAlone_DoesNotStopPlayerOrChangeAnything()
    {
        // GameManager 없이 피격 → 이벤트만 발생하고 플레이어 상태는 그대로 (책임 분리)
        Assert.IsTrue(detector.EvaluateCollider(missileCollider));
        Assert.AreEqual(1, hits.Count);
        Assert.IsFalse(player.IsDead);
        Assert.IsTrue(player.CanControl);
    }
}
