using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Stage 8: HitEffect 수명/제거(EditMode)와 미사일·충돌 이펙트 애니메이션 에셋 구성 검사.
/// EditMode에서는 Animator가 재생되지 않으므로 보호 시간 경로만 검증한다. 실제 재생 완료 제거는 PlayMode에서 검증.
/// </summary>
public class HitEffectTests
{
    private const string MissilePrefabPath = "Assets/Prefabs/Missile.prefab";
    private const string MissileControllerPath = "Assets/Art/Missiles/Anims/Visual.controller";
    private const string HitEffectPrefabPath = "Assets/Prefabs/MissileHitEffect.prefab";
    private const string HitEffectControllerPath = "Assets/Art/VFX/MissileHitEffect.controller";

    // ---------- 순수 계산 ----------

    [Test]
    public void PlaybackFinished_AtNormalizedTimeOne()
    {
        Assert.IsFalse(HitEffect.IsPlaybackFinished(0f));
        Assert.IsFalse(HitEffect.IsPlaybackFinished(0.999f));
        Assert.IsTrue(HitEffect.IsPlaybackFinished(1f));
        Assert.IsTrue(HitEffect.IsPlaybackFinished(1.3f));
    }

    [Test]
    public void SafetyLifetime_UsesActualLengthWhenKnown()
    {
        Assert.AreEqual(2f, HitEffect.ResolveSafetyLifetime(-1f, 2f, 0.5f), 1e-6f, "길이 모름 → fallback");
        Assert.AreEqual(2f, HitEffect.ResolveSafetyLifetime(0.625f, 2f, 0.5f), 1e-6f, "짧은 클립 → fallback 이상");
        Assert.AreEqual(3.5f, HitEffect.ResolveSafetyLifetime(3f, 2f, 0.5f), 1e-6f, "긴 클립 → 길이 + 여유");
        Assert.Greater(HitEffect.ResolveSafetyLifetime(-1f, 0f, 0f), 0f, "0 이하 설정도 유한한 양수 수명");
    }

    [TestCase(float.PositiveInfinity)] // Animator.speed = 0일 때 AnimatorStateInfo.length
    [TestCase(float.NaN)]
    public void SafetyLifetime_NonFiniteLength_FallsBackToFiniteDuration(float length)
    {
        Assert.AreEqual(2f, HitEffect.ResolveSafetyLifetime(length, 2f, 0.5f), 1e-6f, "멈춘 Animator가 수명을 무한히 늘리지 않음");
    }

    // ---------- 보호 장치 / 제거 범위 ----------

    [Test]
    public void NoAnimator_RemovedAfterFallback()
    {
        var go = new GameObject("Effect");
        var effect = go.AddComponent<HitEffect>();
        Assert.IsNull(effect.Animator);

        effect.Tick(1.9f);
        Assert.IsTrue(go != null, "fallback(2초) 전에는 유지");
        effect.Tick(0.2f);
        Assert.IsTrue(go == null, "Animator가 없어도 무한히 남지 않음");
    }

    [Test]
    public void Removal_DestroysOnlyItself()
    {
        var container = new GameObject("EffectContainer");
        var sibling = new GameObject("OtherEffect");
        sibling.transform.SetParent(container.transform);
        var player = new GameObject("Player");
        var effectGo = new GameObject("Effect");
        effectGo.transform.SetParent(container.transform);
        var child = new GameObject("EffectChild");
        child.transform.SetParent(effectGo.transform);
        var effect = effectGo.AddComponent<HitEffect>();

        effect.Tick(10f);

        Assert.IsTrue(effectGo == null);
        Assert.IsTrue(child == null, "이펙트 자식은 함께 제거");
        Assert.IsTrue(container != null, "부모 EffectContainer 유지");
        Assert.IsTrue(sibling != null);
        Assert.IsTrue(player != null);
        Assert.AreEqual(1, container.transform.childCount);

        Object.DestroyImmediate(container);
        Object.DestroyImmediate(player);
    }

    [Test]
    public void Tick_AfterFinished_NoError()
    {
        var go = new GameObject("Effect");
        var effect = go.AddComponent<HitEffect>();
        effect.Tick(5f);
        Assert.IsTrue(effect.IsFinished);
        Assert.DoesNotThrow(() => effect.Tick(1f));
    }

    // ---------- 미사일 비행 애니메이션 에셋 ----------

    [Test]
    public void MissileDirectionStateNames_MatchDirection8()
    {
        CollectionAssert.AreEqual(
            new[] { "South", "SouthEast", "East", "NorthEast", "North", "NorthWest", "West", "SouthWest" },
            Enumerable.Range(0, Missile.DirectionCount).Select(i => Missile.GetDirectionStateName((Missile.Direction8)i)).ToArray());
        Assert.AreEqual(Animator.StringToHash("Base Layer.NorthEast"), Missile.GetDirectionStateHash(Missile.Direction8.NorthEast));
    }

    [Test]
    public void MissileController_HasEightLoopingSpriteOnlyStates()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MissileControllerPath);
        Assert.IsNotNull(controller, MissileControllerPath);
        Assert.AreEqual(Missile.AnimatorLayerName, controller.layers[0].name);
        var states = controller.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);

        for (int i = 0; i < Missile.DirectionCount; i++)
        {
            string name = Missile.GetDirectionStateName((Missile.Direction8)i);
            Assert.IsTrue(states.ContainsKey(name), $"State '{name}' 없음");
            var clip = states[name].motion as AnimationClip;
            Assert.IsNotNull(clip, name);
            Assert.IsTrue(clip.isLooping, $"{clip.name}: 비행 애니메이션은 Loop");
            AssertSpriteOnly(clip);
        }
    }

    [Test]
    public void MissilePrefab_AnimatorOnVisualChild_NoRootMotion_NoRotation()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MissilePrefabPath);
        Assert.IsNotNull(prefab);
        Assert.IsNotNull(prefab.GetComponent<Missile>());
        Assert.AreEqual(Quaternion.identity, prefab.transform.rotation, "Transform 회전 없음(8방향 스프라이트 방식)");

        var animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.IsNotNull(animator, "비행 Animator");
        Assert.IsFalse(animator.applyRootMotion);
        Assert.IsNotNull(animator.runtimeAnimatorController);
        Assert.AreEqual(MissileControllerPath, AssetDatabase.GetAssetPath(animator.runtimeAnimatorController));
        Assert.AreSame(animator.GetComponent<SpriteRenderer>(), prefab.GetComponentInChildren<SpriteRenderer>(true),
            "Animator와 방향 SpriteRenderer가 같은 오브젝트");
    }

    // ---------- 충돌 이펙트 에셋 ----------

    [Test]
    public void HitEffectPrefab_DefaultStateIsSingleShotSpriteClip()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HitEffectPrefabPath);
        Assert.IsNotNull(prefab, HitEffectPrefabPath);
        var animator = prefab.GetComponentInChildren<Animator>(true);
        Assert.IsNotNull(animator);
        Assert.IsFalse(animator.applyRootMotion);
        Assert.AreEqual(HitEffectControllerPath, AssetDatabase.GetAssetPath(animator.runtimeAnimatorController));

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(HitEffectControllerPath);
        var clip = controller.layers[0].stateMachine.defaultState.motion as AnimationClip;
        Assert.IsNotNull(clip, "기본 State 클립");
        Assert.IsFalse(clip.isLooping, "충돌 이펙트는 Loop Time 비활성화");
        Assert.Greater(clip.length, 0f);
        AssertSpriteOnly(clip);
    }

    private static void AssertSpriteOnly(AnimationClip clip)
    {
        Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length, $"{clip.name}: Position/Rotation 등 float 커브 없음");
        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            Assert.AreEqual(typeof(SpriteRenderer), b.type, clip.name);
            Assert.AreEqual("m_Sprite", b.propertyName, clip.name);
        }
    }
}
