using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CharacterPortraitUI 상태 전환 로직과 Portrait Animator 에셋 구성 테스트 (EditMode).
/// EditMode에서는 OnEnable/OnDisable이 자동 호출되지 않으므로 직접 호출한다. Animator 실제 재생은 PlayMode 테스트에서 확인.
/// </summary>
public class CharacterPortraitUITests
{
    private const string ControllerPath = "Assets/Art/Portrait/Animations/Portrait.controller";
    private const string IdleClipPath = "Assets/Art/Portrait/Animations/PortraitIdle.anim";
    private const string SadClipPath = "Assets/Art/Portrait/Animations/PortraitSad.anim";

    private BestTimeBackup bestTimeBackup;
    private GameObject root;
    private GameManager gm;
    private CharacterPortraitUI portrait;

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture(); // EndGame()이 기록 저장을 시도하므로 실제 기록 보호
        BestTimeBackup.ClearForTest();

        root = new GameObject("PortraitTestRoot");
        gm = root.AddComponent<GameManager>();
        portrait = new GameObject("CharacterPortrait").AddComponent<CharacterPortraitUI>();
        portrait.transform.SetParent(root.transform, false);
        var so = new SerializedObject(portrait);
        so.FindProperty("gameManager").objectReferenceValue = gm;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        bestTimeBackup.Restore();
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);
    }

    // ---------- 상태 매핑 ----------

    [TestCase(GameState.Ready, CharacterPortraitUI.PortraitState.Idle)]
    [TestCase(GameState.Playing, CharacterPortraitUI.PortraitState.Idle)]
    [TestCase(GameState.GameOver, CharacterPortraitUI.PortraitState.Sad)]
    public void ResolvePortraitState_MapsGameState(GameState state, CharacterPortraitUI.PortraitState expected)
    {
        Assert.AreEqual(expected, CharacterPortraitUI.ResolvePortraitState(state));
    }

    // ---------- 이벤트 흐름 ----------

    [Test]
    public void Ready_OnEnable_SelectsIdle()
    {
        Assert.AreEqual(GameState.Ready, gm.CurrentState);
        Call(portrait, "OnEnable");

        Assert.AreEqual(CharacterPortraitUI.PortraitState.Idle, portrait.CurrentPortraitState);
        Assert.AreEqual(0, portrait.PlayRequestCount, "기본 State가 Idle이므로 첫 동기화는 재생 명령 없음");
        Call(portrait, "OnDisable");
    }

    [Test]
    public void Playing_KeepsIdle_WithoutReplay()
    {
        Call(portrait, "OnEnable");
        gm.StartGame(); // Ready → Playing

        Assert.AreEqual(CharacterPortraitUI.PortraitState.Idle, portrait.CurrentPortraitState);
        Assert.AreEqual(0, portrait.PlayRequestCount, "Ready→Playing은 같은 Idle이므로 재생 명령 없음");
        Call(portrait, "OnDisable");
    }

    [Test]
    public void GameOver_SelectsSad_Immediately()
    {
        Call(portrait, "OnEnable");
        gm.StartGame();
        gm.EndGame();

        Assert.AreEqual(GameState.GameOver, gm.CurrentState);
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Sad, portrait.CurrentPortraitState);
        Assert.AreEqual(1, portrait.PlayRequestCount);
        Call(portrait, "OnDisable");
    }

    [Test]
    public void DuplicateGameOverEvent_DoesNotRestartSad()
    {
        Call(portrait, "OnEnable");
        gm.StartGame();
        gm.EndGame();
        int afterGameOver = portrait.PlayRequestCount;

        // 같은 GameOver 이벤트가 다시 전달되는 상황
        var handler = typeof(CharacterPortraitUI).GetMethod("HandleGameStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        handler.Invoke(portrait, new object[] { GameState.GameOver });
        handler.Invoke(portrait, new object[] { GameState.GameOver });
        gm.EndGame(); // GameManager도 GameOver 중 EndGame은 무시

        Assert.AreEqual(afterGameOver, portrait.PlayRequestCount, "Sad 재생 명령은 한 번만");
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Sad, portrait.CurrentPortraitState);
        Call(portrait, "OnDisable");
    }

    [Test]
    public void NewGame_ReturnsToIdle()
    {
        Call(portrait, "OnEnable");
        gm.StartGame();
        gm.EndGame();
        gm.StartGame(); // 같은 씬에서 재시작

        Assert.AreEqual(CharacterPortraitUI.PortraitState.Idle, portrait.CurrentPortraitState);
        Assert.AreEqual(2, portrait.PlayRequestCount, "Sad → Idle 재생");
        Call(portrait, "OnDisable");
    }

    [Test]
    public void OnEnable_DuringGameOver_SyncsToSad()
    {
        gm.StartGame();
        gm.EndGame();

        Call(portrait, "OnEnable");

        Assert.AreEqual(CharacterPortraitUI.PortraitState.Sad, portrait.CurrentPortraitState);
        Call(portrait, "OnDisable");
    }

    [Test]
    public void Unsubscribe_StopsReceivingEvents_AndResubscribeWorks()
    {
        Call(portrait, "OnEnable");
        Call(portrait, "OnEnable"); // 중복 구독 방지
        Call(portrait, "OnDisable");

        gm.StartGame();
        gm.EndGame();
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Idle, portrait.CurrentPortraitState, "해제 후 이벤트 무시");

        Call(portrait, "OnEnable"); // 재구독 + 현재 상태 동기화
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Sad, portrait.CurrentPortraitState);
        int count = portrait.PlayRequestCount;

        gm.StartGame();
        Assert.AreEqual(count + 1, portrait.PlayRequestCount, "재구독 후 한 번만 처리(중복 구독 없음)");
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Idle, portrait.CurrentPortraitState);
        Call(portrait, "OnDisable");
    }

    [Test]
    public void MissingGameManager_DefaultsToIdle()
    {
        var lone = new GameObject("LonePortrait").AddComponent<CharacterPortraitUI>();
        lone.transform.SetParent(root.transform, false);

        Assert.DoesNotThrow(() => Call(lone, "OnEnable"));
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Idle, lone.CurrentPortraitState);
        Call(lone, "OnDisable");
    }

    // ---------- Portrait Animator 에셋 구성 ----------

    [Test]
    public void PortraitController_HasIdleDefault_AndSadWithoutTransitions()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, ControllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        Assert.AreEqual(CharacterPortraitUI.LayerName, controller.layers[0].name);

        AnimatorState idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == CharacterPortraitUI.IdleStateName);
        AnimatorState sad = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == CharacterPortraitUI.SadStateName);
        Assert.IsNotNull(idle, "PortraitIdle State");
        Assert.IsNotNull(sad, "PortraitSad State");
        Assert.AreSame(idle, sm.defaultState, "기본 State = Idle");
        Assert.IsEmpty(sad.transitions, "Sad 종료 후 자동 전환 없음");
        Assert.IsEmpty(sm.anyStateTransitions, "Any State 전환 없음(Sad 유지)");
    }

    [Test]
    public void PortraitClips_LoopSettings_AndBindToUIImageSprite()
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var sad = AssetDatabase.LoadAssetAtPath<AnimationClip>(SadClipPath);
        Assert.IsNotNull(idle);
        Assert.IsNotNull(sad);
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(idle).loopTime, "Idle Loop Time 켬");
        Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(sad).loopTime, "Sad Loop Time 끔(마지막 프레임 유지)");

        foreach (AnimationClip clip in new[] { idle, sad })
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.IsTrue(bindings.Any(b => b.type == typeof(Image) && b.propertyName == "m_Sprite" && b.path == ""),
                $"{clip.name}: 같은 오브젝트의 UI Image.sprite를 변경해야 함");
            Assert.IsFalse(bindings.Any(b => b.type == typeof(SpriteRenderer)), $"{clip.name}: SpriteRenderer 바인딩 없음");
        }
    }
}
