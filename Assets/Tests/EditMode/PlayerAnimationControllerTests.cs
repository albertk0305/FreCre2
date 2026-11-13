using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Dir = Missile.Direction8;
using S = PlayerAnimationController.AnimState;

/// <summary>
/// Stage 7 PlayerAnimationController (EditMode).
/// EditMode에서는 Animator가 재생되지 않으므로 Animator.Play()를 호출하지 않고 상태/State 이름 결정만 검증한다.
/// 실제 Animator 재생/Death 완료 감지는 PlayerAnimationPlayModeTests에서 검증한다.
/// </summary>
public class PlayerAnimationControllerTests
{
    private const string ControllerPath = "Assets/Art/Cahracters/Animations/Anims/Visual.controller";
    private const float Dt = 0.02f;

    private GameObject playerGo;
    private PlayerController player;
    private Transform visual;
    private PlayerAnimationController anim;
    private int deathFinishedCount;

    [SetUp]
    public void SetUp()
    {
        playerGo = new GameObject("Player");
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        playerGo.AddComponent<CircleCollider2D>().radius = 0.15f;
        playerGo.AddComponent<PlayerInputRouter>();
        visual = new GameObject("Visual").transform;
        visual.SetParent(playerGo.transform, false);
        player = playerGo.AddComponent<PlayerController>();
        player.ResetPlayer(Vector2.zero);

        anim = visual.gameObject.AddComponent<PlayerAnimationController>();
        deathFinishedCount = 0;
        anim.OnDeathFinished += () => deathFinishedCount++;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerGo);
    }

    /// <summary>플레이어 고정 스텝 1회(이동 → 점프 갱신) 후 애니메이션 1프레임.</summary>
    private void Step(Vector2 move)
    {
        player.Move(move, Dt);
        player.UpdateJump(Dt);
        anim.Tick(Dt);
    }

    // ---------- 방향 계산 ----------

    [TestCase(0f, 1f, Dir.North)]
    [TestCase(0f, -1f, Dir.South)]
    [TestCase(1f, 0f, Dir.East)]
    [TestCase(-1f, 0f, Dir.West)]
    [TestCase(1f, 1f, Dir.NorthEast)]
    [TestCase(-1f, 1f, Dir.NorthWest)]
    [TestCase(1f, -1f, Dir.SouthEast)]
    [TestCase(-1f, -1f, Dir.SouthWest)]
    public void Direction_EightWay(float x, float y, Dir expected)
    {
        Assert.AreEqual((int)expected, PlayerAnimationController.GetDirectionIndex(new Vector2(x, y)));
        Assert.AreEqual((int)expected, PlayerAnimationController.GetDirectionIndex(new Vector2(x, y) * 0.37f), "크기와 무관");
    }

    [Test]
    public void Direction_Zero_IsSouth()
    {
        Assert.AreEqual((int)Dir.South, PlayerAnimationController.GetDirectionIndex(Vector2.zero));
        Assert.AreEqual((int)Dir.South, PlayerAnimationController.ResolveDirectionIndex(Vector2.zero, (int)Dir.East, 5f));
        Assert.AreEqual((int)Dir.South, PlayerAnimationController.ResolveDirectionIndex(new Vector2(float.NaN, 1f), (int)Dir.East, 5f));
    }

    [TestCase(0f, Dir.East)]
    [TestCase(22.4f, Dir.East)]
    [TestCase(22.6f, Dir.NorthEast)]
    [TestCase(45f, Dir.NorthEast)]
    [TestCase(67.4f, Dir.NorthEast)]
    [TestCase(67.6f, Dir.North)]
    [TestCase(90f, Dir.North)]
    [TestCase(135f, Dir.NorthWest)]
    [TestCase(180f, Dir.West)]
    [TestCase(-135f, Dir.SouthWest)]
    [TestCase(-90f, Dir.South)]
    [TestCase(-45f, Dir.SouthEast)]
    [TestCase(-22.6f, Dir.SouthEast)]
    [TestCase(337.6f, Dir.East)]
    public void Direction_Nearest45DegreeSector(float degrees, Dir expected)
    {
        float r = degrees * Mathf.Deg2Rad;
        Assert.AreEqual((int)expected, PlayerAnimationController.GetDirectionIndex(new Vector2(Mathf.Cos(r), Mathf.Sin(r))));
    }

    [Test]
    public void Direction_Hysteresis_NoFlickerAtSectorBoundary()
    {
        Vector2 At(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

        // East에서 경계(22.5°)를 조금 넘는 떨림은 East 유지
        Assert.AreEqual((int)Dir.East, PlayerAnimationController.ResolveDirectionIndex(At(22.6f), (int)Dir.East, 5f));
        Assert.AreEqual((int)Dir.East, PlayerAnimationController.ResolveDirectionIndex(At(27f), (int)Dir.East, 5f));
        // 여유 각도를 넘으면 전환
        Assert.AreEqual((int)Dir.NorthEast, PlayerAnimationController.ResolveDirectionIndex(At(28f), (int)Dir.East, 5f));
        // 정확한 45° 단위 입력(키보드)은 즉시 전환
        Assert.AreEqual((int)Dir.NorthEast, PlayerAnimationController.ResolveDirectionIndex(At(45f), (int)Dir.East, 5f));
        Assert.AreEqual((int)Dir.West, PlayerAnimationController.ResolveDirectionIndex(At(180f), (int)Dir.East, 5f));
        // 여유 0이면 최근접
        Assert.AreEqual((int)Dir.NorthEast, PlayerAnimationController.ResolveDirectionIndex(At(22.6f), (int)Dir.East, 0f));
    }

    // ---------- 상태 결정 (순수 계산) ----------

    [Test]
    public void ResolveState_Priority()
    {
        Vector2 moving = Vector2.right;
        Assert.AreEqual(S.Death, PlayerAnimationController.ResolveState(true, false, 2f, moving, 0.01f, 0.001f), "Death 최우선");
        Assert.AreEqual(S.Death, PlayerAnimationController.ResolveState(true, true, 0f, Vector2.zero, 0.01f, 0.001f));
        Assert.AreEqual(S.Jump, PlayerAnimationController.ResolveState(false, false, 2f, moving, 0.01f, 0.001f));
        Assert.AreEqual(S.Fall, PlayerAnimationController.ResolveState(false, false, -1f, moving, 0.01f, 0.001f));
        Assert.AreEqual(S.Fall, PlayerAnimationController.ResolveState(false, false, 0f, moving, 0.01f, 0.001f), "v = 0은 Fall");
        Assert.AreEqual(S.Run, PlayerAnimationController.ResolveState(false, true, 0f, moving, 0.01f, 0.001f));
        Assert.AreEqual(S.Idle, PlayerAnimationController.ResolveState(false, true, 0f, Vector2.zero, 0.01f, 0.001f));
    }

    [Test]
    public void ResolveState_FloatNoise_DoesNotTriggerRunOrJump()
    {
        Assert.AreEqual(S.Idle, PlayerAnimationController.ResolveState(false, true, 0f, new Vector2(1e-4f, -1e-4f), 0.01f, 0.001f));
        Assert.AreEqual(S.Fall, PlayerAnimationController.ResolveState(false, false, 1e-5f, Vector2.zero, 0.01f, 0.001f));
        Assert.AreEqual(S.Idle, PlayerAnimationController.ResolveState(false, true, 5f, Vector2.zero, 0.01f, 0.001f),
            "지면에서는 수직 속도와 무관");
    }

    // ---------- State 이름 / Animator Controller 에셋 ----------

    [Test]
    public void StateNames_FollowNamingRule()
    {
        Assert.AreEqual("IdleSouth", PlayerAnimationController.GetStateName(S.Idle, Dir.South));
        Assert.AreEqual("RunNorthEast", PlayerAnimationController.GetStateName(S.Run, Dir.NorthEast));
        Assert.AreEqual("JumpSouthWest", PlayerAnimationController.GetStateName(S.Jump, Dir.SouthWest));
        Assert.AreEqual("FallNorthWest", PlayerAnimationController.GetStateName(S.Fall, Dir.NorthWest));
        Assert.AreEqual("Death", PlayerAnimationController.GetStateName(S.Death, Dir.East), "방향과 무관하게 정면 사망 State");

        string[] all = PlayerAnimationController.GetAllStateNames();
        Assert.AreEqual(33, all.Length);
        Assert.AreEqual(33, all.Distinct().Count());
    }

    [Test]
    public void AnimatorControllerAsset_HasAllStates_NoTransitionsOrParameters()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, ControllerPath);
        Assert.AreEqual(PlayerAnimationController.LayerName, controller.layers[0].name);
        Assert.AreEqual(0, controller.parameters.Length, "Parameter 미사용");

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        var states = sm.states.Select(s => s.state).ToList();
        var names = new HashSet<string>(states.Select(s => s.name));
        foreach (string expected in PlayerAnimationController.GetAllStateNames())
        {
            Assert.IsTrue(names.Contains(expected), $"State '{expected}' 없음");
        }

        Assert.AreEqual("IdleSouth", sm.defaultState.name, "기본 State");
        Assert.AreEqual(0, sm.anyStateTransitions.Length, "Any State Transition 미사용");
        foreach (AnimatorState s in states)
        {
            Assert.AreEqual(0, s.transitions.Length, $"{s.name}: Transition 미사용");
            Assert.AreEqual(1f, s.speed, $"{s.name}: State Speed 1");
        }
    }

    [Test]
    public void AnimationClips_OnlyChangeSprite_NoTransformCurves()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.IsNotNull(controller);
        var used = new HashSet<string>(PlayerAnimationController.GetAllStateNames());
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (!used.Contains(child.state.name)) continue;
            var clip = child.state.motion as AnimationClip;
            Assert.IsNotNull(clip, $"{child.state.name}: Motion 없음");
            Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length,
                $"{clip.name}: 위치 등 float 커브가 있으면 Visual 점프 높이와 충돌");
            foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                Assert.AreEqual(typeof(SpriteRenderer), b.type, clip.name);
                Assert.AreEqual("m_Sprite", b.propertyName, clip.name);
            }
        }
    }

    // ---------- 상태 전환 (플레이어 구동) ----------

    [Test]
    public void GroundIdle_IsIdleSouth_AndNotRestarted()
    {
        anim.Tick(Dt);
        Assert.AreEqual("IdleSouth", anim.CurrentStateName);
        Assert.AreEqual(1, anim.PlayRequestCount);

        for (int i = 0; i < 100; i++) Step(Vector2.zero);
        Assert.AreEqual("IdleSouth", anim.CurrentStateName);
        Assert.AreEqual(1, anim.PlayRequestCount, "같은 State는 다시 재생하지 않음");
    }

    [Test]
    public void GroundMove_IsRun_SameDirectionNotRestarted()
    {
        Step(Vector2.right);
        Assert.AreEqual("RunEast", anim.CurrentStateName);
        int count = anim.PlayRequestCount;
        for (int i = 0; i < 50; i++) Step(Vector2.right);
        Assert.AreEqual(count, anim.PlayRequestCount);
    }

    [Test]
    public void DirectionChange_PlaysNewDirectionState()
    {
        Step(Vector2.down);
        Assert.AreEqual("RunSouth", anim.CurrentStateName);
        Step(new Vector2(1f, 1f));
        Assert.AreEqual("RunNorthEast", anim.CurrentStateName);
        Step(Vector2.left);
        Assert.AreEqual("RunWest", anim.CurrentStateName);
        Step(new Vector2(-1f, -1f));
        Assert.AreEqual("RunSouthWest", anim.CurrentStateName);
    }

    [Test]
    public void StopMoving_KeepsLastFacing()
    {
        Step(new Vector2(-1f, 1f));
        Assert.AreEqual("RunNorthWest", anim.CurrentStateName);
        Step(Vector2.zero);
        Assert.AreEqual("IdleNorthWest", anim.CurrentStateName);
        for (int i = 0; i < 20; i++) Step(Vector2.zero);
        Assert.AreEqual("IdleNorthWest", anim.CurrentStateName);
    }

    [Test]
    public void SpecExampleSequence_EachChangePlaysOnce()
    {
        var played = new List<string>();
        void Record()
        {
            if (played.Count == 0 || played[played.Count - 1] != anim.CurrentStateName) played.Add(anim.CurrentStateName);
        }

        anim.Tick(Dt); Record();                                   // IdleSouth
        for (int i = 0; i < 5; i++) { Step(Vector2.down); Record(); } // RunSouth
        for (int i = 0; i < 5; i++) { Step(Vector2.one); Record(); }  // RunNorthEast
        Assert.IsTrue(player.TryJump());
        int guard = 0;
        while (!player.IsGrounded && guard++ < 200) { Step(Vector2.one); Record(); } // JumpNorthEast → FallNorthEast → (착지) RunNorthEast
        Step(Vector2.zero); Record();                              // IdleNorthEast

        CollectionAssert.AreEqual(new[]
        {
            "IdleSouth", "RunSouth", "RunNorthEast", "JumpNorthEast", "FallNorthEast", "RunNorthEast", "IdleNorthEast",
        }, played);
        Assert.AreEqual(played.Count, anim.PlayRequestCount, "전환마다 정확히 한 번 재생 요청");
    }

    [Test]
    public void JumpRise_IsJump_Descend_IsFall_LandStill_IsIdle()
    {
        anim.Tick(Dt);
        Assert.IsTrue(player.TryJump());
        Step(Vector2.zero);
        Assert.AreEqual("JumpSouth", anim.CurrentStateName);
        Assert.Greater(player.VerticalVelocity, 0f);

        int guard = 0;
        while (player.VerticalVelocity > 0f && guard++ < 200) Step(Vector2.zero);
        Step(Vector2.zero);
        Assert.AreEqual("FallSouth", anim.CurrentStateName);

        while (!player.IsGrounded && guard++ < 400) Step(Vector2.zero);
        Assert.IsTrue(player.IsGrounded);
        Assert.AreEqual("IdleSouth", anim.CurrentStateName, "착지 후 정지 → Idle");
    }

    [Test]
    public void Land_WhileMoving_IsRun()
    {
        Step(Vector2.right);
        Assert.IsTrue(player.TryJump());
        int guard = 0;
        while (!player.IsGrounded && guard++ < 200) Step(Vector2.right);
        Step(Vector2.right);
        Assert.AreEqual("RunEast", anim.CurrentStateName, "착지 후 이동 중 → Run");
    }

    [Test]
    public void JumpWhileMoving_KeepsDirection()
    {
        Step(Vector2.left);
        Assert.IsTrue(player.TryJump());
        Step(Vector2.left);
        Assert.AreEqual("JumpWest", anim.CurrentStateName);

        // 공중에서 입력을 놓아도 방향 유지
        Step(Vector2.zero);
        Assert.AreEqual("JumpWest", anim.CurrentStateName);
    }

    [Test]
    public void JumpWhileMoving_DirectionChangeInAir()
    {
        Step(Vector2.left);
        Assert.IsTrue(player.TryJump());
        Step(Vector2.left);
        Assert.AreEqual("JumpWest", anim.CurrentStateName);

        Step(Vector2.up);
        Assert.AreEqual("JumpNorth", anim.CurrentStateName, "점프 중 방향 변경");

        int guard = 0;
        while (player.VerticalVelocity > 0f && guard++ < 200) Step(Vector2.up);
        Step(new Vector2(1f, -1f));
        Assert.AreEqual("FallSouthEast", anim.CurrentStateName, "하강 중 방향 변경");
    }

    // ---------- Death ----------

    [Test]
    public void Death_HasPriority_AlwaysDeathState()
    {
        Step(Vector2.right);
        Assert.IsTrue(player.TryJump());
        Step(Vector2.right);
        Assert.AreEqual("JumpEast", anim.CurrentStateName);

        player.Die();
        anim.Tick(Dt);
        Assert.AreEqual(S.Death, anim.CurrentState);
        Assert.AreEqual("Death", anim.CurrentStateName);
        int count = anim.PlayRequestCount;

        // 공중에서 사망 → 하강/착지해도 Death 유지, 재시작 없음
        for (int i = 0; i < 100; i++) Step(Vector2.left);
        Assert.IsTrue(player.IsGrounded);
        Assert.AreEqual("Death", anim.CurrentStateName);
        Assert.AreEqual(count, anim.PlayRequestCount);
    }

    [Test]
    public void DeathFinished_FiresExactlyOnce()
    {
        anim.Tick(Dt);
        player.Die();
        anim.Tick(Dt); // EditMode: Animator 재생 불가 → 기다리지 않고 완료 처리
        Assert.IsTrue(anim.IsDeathFinished);
        Assert.AreEqual(1, deathFinishedCount);

        for (int i = 0; i < 200; i++) anim.Tick(Dt);
        anim.NotifyDeathAnimationFinished(); // Animation Event 중복 호출
        Assert.AreEqual(1, deathFinishedCount, "사망 1회당 한 번");
    }

    [Test]
    public void NotifyDeathFinished_IgnoredWhenNotDead()
    {
        anim.Tick(Dt);
        anim.NotifyDeathAnimationFinished();
        Assert.AreEqual(0, deathFinishedCount);
        Assert.IsFalse(anim.IsDeathFinished);
    }

    [Test]
    public void ResetAfterDeath_ReturnsToIdleSouth_NextDeathFiresAgain()
    {
        Step(Vector2.right);
        player.Die();
        anim.Tick(Dt);
        Assert.AreEqual(1, deathFinishedCount);

        player.ResetPlayer(Vector2.zero);
        anim.Tick(Dt);
        Assert.AreEqual("IdleSouth", anim.CurrentStateName, "재시작 시 IdleSouth");
        Assert.IsFalse(anim.IsDeathFinished);
        anim.NotifyDeathAnimationFinished();
        Assert.AreEqual(1, deathFinishedCount, "이전 사망의 완료 신호가 새 판에 영향 없음");

        player.Die();
        anim.Tick(Dt);
        Assert.AreEqual(2, deathFinishedCount);
    }

    // ---------- 독립성 ----------

    [Test]
    public void AnimationTick_DoesNotTouchVisualTransform()
    {
        Assert.IsTrue(player.TryJump());
        for (int i = 0; i < 5; i++) player.UpdateJump(Dt);
        Vector3 before = visual.localPosition;
        Assert.Greater(before.y, 0f, "점프 높이는 PlayerController가 Visual에 반영");

        for (int i = 0; i < 50; i++) anim.Tick(Dt);
        Assert.AreEqual(before, visual.localPosition);
    }

    [Test]
    public void PlayerController_WorksWithoutAnimationController()
    {
        Object.DestroyImmediate(anim);
        Assert.IsTrue(player.TryJump());
        for (int i = 0; i < 100; i++) { player.Move(Vector2.right, Dt); player.UpdateJump(Dt); }
        Assert.IsTrue(player.IsGrounded);
        Assert.Greater(player.GroundPosition.x, 0f);
    }
}
