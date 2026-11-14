using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>점프 높이에 따른 그림자 크기/투명도 테스트 (EditMode). PlayerController는 Tick(dt)으로 고정 스텝을 시뮬레이션한다.</summary>
public class PlayerShadowControllerTests
{
    private const float Dt = 0.02f;
    private const float Eps = 1e-5f;
    private static readonly Vector2 Center = new Vector2(0.5f, -0.5f);
    // 씬의 Player/Shadow 설정
    private static readonly Vector3 BaseScale = new Vector3(0.35f, 0.15f, 1f);
    private static readonly Vector3 BasePosition = new Vector3(-0.015f, 0.06f, 0f);
    private static readonly Color BaseColor = new Color(0f, 0f, 0f, 0.5882353f);

    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created) if (go != null) Object.DestroyImmediate(go);
        created.Clear();
    }

    private PlayerController CreatePlayer(bool withShadow, out PlayerShadowController shadow, out SpriteRenderer shadowRenderer)
    {
        var arenaGo = new GameObject("Arena");
        created.Add(arenaGo);
        var arena = arenaGo.AddComponent<ArenaBounds>();
        arena.Center = Center;
        arena.HalfSize = 2f;

        var playerGo = new GameObject("Player");
        created.Add(playerGo);
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.3f;
        new GameObject("Visual").transform.SetParent(playerGo.transform, false);
        var router = playerGo.AddComponent<PlayerInputRouter>();
        var player = playerGo.AddComponent<PlayerController>();
        var so = new SerializedObject(player);
        so.FindProperty("inputRouter").objectReferenceValue = router;
        so.FindProperty("arenaBounds").objectReferenceValue = arena;
        so.FindProperty("groundCollider").objectReferenceValue = col;
        so.ApplyModifiedPropertiesWithoutUndo();
        player.ResetPlayer(Center);

        shadow = null;
        shadowRenderer = null;
        if (withShadow)
        {
            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(playerGo.transform, false);
            shadowGo.transform.localPosition = BasePosition;
            shadowGo.transform.localScale = BaseScale;
            shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
            shadowRenderer.color = BaseColor;
            shadowRenderer.sortingOrder = 5;
            shadow = shadowGo.AddComponent<PlayerShadowController>();
            typeof(PlayerShadowController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(shadow, null);
        }
        return player;
    }

    private static void AssertOriginal(PlayerShadowController shadow, SpriteRenderer sr, string message)
    {
        Assert.AreEqual(BaseScale, shadow.transform.localScale, message + " (scale)");
        Assert.AreEqual(BaseColor, sr.color, message + " (color)");
    }

    // ---------- 순수 계산 ----------

    [Test]
    public void MaxJumpHeight_FromJumpPowerAndGravity()
    {
        Assert.AreEqual(2.5f * 2.5f / (2f * 6.67f), PlayerShadowController.ComputeMaxJumpHeight(2.5f, 6.67f), Eps);
        Assert.AreEqual(0.4685f, PlayerShadowController.ComputeMaxJumpHeight(2.5f, 6.67f), 1e-3f, "기본 밸런스 ≈ 0.47");
        Assert.AreEqual(0f, PlayerShadowController.ComputeMaxJumpHeight(0f, 6.67f));
        Assert.AreEqual(0f, PlayerShadowController.ComputeMaxJumpHeight(2.5f, 0f));
        Assert.AreEqual(0f, PlayerShadowController.ComputeMaxJumpHeight(float.NaN, 6.67f));
    }

    [TestCase(0f, 0f)]
    [TestCase(0.2f, 0.5f)]
    [TestCase(0.4f, 1f)]
    [TestCase(1f, 1f)]                     // 최대 이상 → 1
    [TestCase(-0.1f, 0f)]                  // 음수 → 0
    [TestCase(float.NaN, 0f)]
    [TestCase(float.NegativeInfinity, 0f)]
    [TestCase(float.PositiveInfinity, 1f)]
    public void NormalizedHeight_ClampedAndSafe(float height, float expected)
    {
        Assert.AreEqual(expected, PlayerShadowController.ComputeNormalizedHeight(height, 0.4f), Eps);
    }

    [Test]
    public void NormalizedHeight_InvalidMax_IsZero()
    {
        Assert.AreEqual(0f, PlayerShadowController.ComputeNormalizedHeight(0.2f, 0f));
        Assert.AreEqual(0f, PlayerShadowController.ComputeNormalizedHeight(0.2f, float.NaN));
    }

    // ---------- 컴포넌트 ----------

    [Test]
    public void HeightZero_KeepsOriginalScaleAndColor()
    {
        CreatePlayer(true, out var shadow, out var sr);
        shadow.ApplyHeight(0f);
        AssertOriginal(shadow, sr, "지면");
        shadow.Refresh(); // 서 있는 플레이어 → VisualJumpHeight 0
        AssertOriginal(shadow, sr, "Refresh 지면");
        Assert.AreEqual(BasePosition, shadow.transform.localPosition, "위치 유지");
        Assert.AreEqual(5, sr.sortingOrder, "Sorting Order 유지");
    }

    [Test]
    public void MaxHeight_MinScaleAndMinAlpha()
    {
        CreatePlayer(true, out var shadow, out var sr);
        Assert.AreEqual(0.4685f, shadow.MaxJumpHeight, 1e-3f, "PlayerController 밸런스와 동기화");

        shadow.ApplyHeight(shadow.MaxJumpHeight);

        Assert.AreEqual(BaseScale.x * 0.7f, shadow.transform.localScale.x, Eps);
        Assert.AreEqual(BaseScale.y * 0.7f, shadow.transform.localScale.y, Eps);
        Assert.AreEqual(BaseScale.z, shadow.transform.localScale.z, "z는 유지");
        Assert.AreEqual(BaseColor.a * 0.6f, sr.color.a, Eps);
        Assert.AreEqual(BaseColor.r, sr.color.r);
        Assert.AreEqual(BaseColor.g, sr.color.g);
        Assert.AreEqual(BaseColor.b, sr.color.b, "RGB 유지");
        Assert.AreEqual(BasePosition, shadow.transform.localPosition, "지면 위치 유지");
    }

    [Test]
    public void HalfHeight_LinearInterpolation()
    {
        CreatePlayer(true, out var shadow, out var sr);
        shadow.ApplyHeight(shadow.MaxJumpHeight * 0.5f);

        Assert.AreEqual(BaseScale.x * 0.85f, shadow.transform.localScale.x, Eps);
        Assert.AreEqual(BaseScale.y * 0.85f, shadow.transform.localScale.y, Eps);
        Assert.AreEqual(BaseColor.a * 0.8f, sr.color.a, Eps);
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.NegativeInfinity)]
    public void InvalidLowHeights_ShowOriginal(float height)
    {
        CreatePlayer(true, out var shadow, out var sr);
        shadow.ApplyHeight(0.3f);
        shadow.ApplyHeight(height);
        AssertOriginal(shadow, sr, $"height {height}");
    }

    [TestCase(10f)]
    [TestCase(float.PositiveInfinity)]
    public void ExcessiveHeights_ClampToMinimum(float height)
    {
        CreatePlayer(true, out var shadow, out var sr);
        shadow.ApplyHeight(height);
        Assert.AreEqual(BaseScale.x * 0.7f, shadow.transform.localScale.x, Eps);
        Assert.AreEqual(BaseColor.a * 0.6f, sr.color.a, Eps);
    }

    [Test]
    public void InspectorMinimums_AreApplied()
    {
        CreatePlayer(true, out var shadow, out var sr);
        var so = new SerializedObject(shadow);
        so.FindProperty("minScale").floatValue = 0.5f;
        so.FindProperty("minAlphaMultiplier").floatValue = 0.25f;
        so.ApplyModifiedPropertiesWithoutUndo();

        shadow.ApplyHeight(shadow.MaxJumpHeight);
        Assert.AreEqual(BaseScale.x * 0.5f, shadow.transform.localScale.x, Eps);
        Assert.AreEqual(BaseColor.a * 0.25f, sr.color.a, Eps);
    }

    [Test]
    public void MaxJumpHeight_FollowsBalanceChange()
    {
        PlayerController player = CreatePlayer(true, out var shadow, out _);
        var so = new SerializedObject(player);
        so.FindProperty("jumpPower").floatValue = 3f;
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreEqual(3f * 3f / (2f * 6.67f), shadow.MaxJumpHeight, Eps);
    }

    [Test]
    public void FullJump_ShrinksToMinimumAtApex_RestoresExactlyOnLanding()
    {
        PlayerController player = CreatePlayer(true, out var shadow, out var sr);
        Assert.IsTrue(player.TryJump());

        float minScaleSeen = float.MaxValue;
        float minAlphaSeen = float.MaxValue;
        float previousScale = BaseScale.x;
        float previousAlpha = BaseColor.a;
        float previousHeight = 0f;
        int steps = 0;
        while ((!player.IsGrounded || steps == 0) && steps < 200)
        {
            player.Tick(Dt);
            shadow.Refresh();
            steps++;
            float sx = shadow.transform.localScale.x;
            float a = sr.color.a;
            minScaleSeen = Mathf.Min(minScaleSeen, sx);
            minAlphaSeen = Mathf.Min(minAlphaSeen, a);

            // 높이가 오르면 작아지고 투명해지며, 내려가면 커지고 진해진다.
            if (player.JumpHeight > previousHeight)
            {
                Assert.LessOrEqual(sx, previousScale + Eps, "상승 중 축소");
                Assert.LessOrEqual(a, previousAlpha + Eps, "상승 중 투명해짐");
            }
            else if (player.JumpHeight < previousHeight)
            {
                Assert.GreaterOrEqual(sx, previousScale - Eps, "하강 중 복구");
                Assert.GreaterOrEqual(a, previousAlpha - Eps, "하강 중 진해짐");
            }
            previousScale = sx;
            previousAlpha = a;
            previousHeight = player.JumpHeight;
            Assert.AreEqual(player.VisualJumpHeight, player.JumpHeight, Eps, "Visual과 같은 높이 사용");
        }

        Assert.IsTrue(player.IsGrounded, "착지");
        Assert.AreEqual(BaseScale.x * 0.7f, minScaleSeen, 0.01f, "정점에서 최소 크기에 근접");
        Assert.AreEqual(BaseColor.a * 0.6f, minAlphaSeen, 0.01f, "정점에서 최소 불투명도에 근접");
        Assert.GreaterOrEqual(minScaleSeen, BaseScale.x * 0.7f - Eps, "최소값 아래로 내려가지 않음");
        AssertOriginal(shadow, sr, "착지 후 정확히 원래 상태");
    }

    [Test]
    public void Shadow_DoesNotAffectPlayerController()
    {
        PlayerController withShadow = CreatePlayer(true, out var shadow, out _);
        PlayerController control = CreatePlayer(false, out _, out _);

        Assert.IsTrue(withShadow.TryJump());
        Assert.IsTrue(control.TryJump());
        for (int i = 0; i < 60; i++)
        {
            withShadow.Tick(Dt);
            control.Tick(Dt);
            shadow.Refresh();
            Assert.AreEqual(control.JumpHeight, withShadow.JumpHeight, "점프 높이 동일");
            Assert.AreEqual(control.VerticalVelocity, withShadow.VerticalVelocity);
            Assert.AreEqual(control.IsGrounded, withShadow.IsGrounded);
            Assert.AreEqual(control.GroundPosition, withShadow.GroundPosition, "지면 위치 동일");
        }
        Assert.AreEqual(control.LastJumpTime, withShadow.LastJumpTime, "쿨다운 기준 동일");
        Assert.AreEqual(0.3f, withShadow.GetComponent<CircleCollider2D>().radius, "Collider 변경 없음");
        Assert.AreEqual(RigidbodyType2D.Kinematic, withShadow.GetComponent<Rigidbody2D>().bodyType);
    }

    [Test]
    public void Disable_RestoresOriginal()
    {
        CreatePlayer(true, out var shadow, out var sr);
        shadow.ApplyHeight(0.3f);
        typeof(PlayerShadowController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(shadow, null);
        AssertOriginal(shadow, sr, "비활성화 시 원래 상태");
    }

    [Test]
    public void ResetPlayer_ReturnsShadowToOriginal()
    {
        PlayerController player = CreatePlayer(true, out var shadow, out var sr);
        player.TryJump();
        for (int i = 0; i < 10; i++) player.Tick(Dt);
        shadow.Refresh();
        Assert.Less(shadow.transform.localScale.x, BaseScale.x);

        player.ResetPlayer(Center); // 재시작/리셋
        shadow.Refresh();
        AssertOriginal(shadow, sr, "리셋 후 원래 상태");
    }
}
