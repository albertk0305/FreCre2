using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 실제 Portrait.controller로 Idle 반복 → GameOver 즉시 Sad 1회 재생 → 마지막 프레임 유지 → 재시작 시 Idle 복귀를 검증한다.
/// </summary>
public class CharacterPortraitPlayModeTests
{
    private const string ControllerPath = "Assets/Art/Portrait/Animations/Portrait.controller";

    private BestTimeBackup bestTimeBackup;
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        bestTimeBackup = BestTimeBackup.Capture();
        BestTimeBackup.ClearForTest();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        bestTimeBackup.Restore();
        yield return null;
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float t0 = Time.time;
        while (Time.time - t0 < seconds) yield return null;
    }

    [UnityTest]
    public IEnumerator Portrait_IdleLoops_SadPlaysOnceAndHolds_RestartReturnsToIdle()
    {
#if UNITY_EDITOR
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Assert.IsNotNull(controller, ControllerPath);
#else
        Assert.Ignore("에디터 전용");
        RuntimeAnimatorController controller = null;
#endif
        // 비활성 상태로 구성한 뒤 켜서 Awake/OnEnable 순서를 씬과 같게 만든다.
        root = new GameObject("PortraitPlayModeRoot");
        root.SetActive(false);
        var gm = root.AddComponent<GameManager>(); // 참조 없는 GameManager: 상태/이벤트만 사용

        var canvasGo = new GameObject("UICanvas", typeof(Canvas));
        canvasGo.transform.SetParent(root.transform, false);
        var portraitRoot = new GameObject("CharacterPortrait", typeof(RectTransform));
        portraitRoot.transform.SetParent(canvasGo.transform, false);
        var portraitGo = new GameObject("Portrait", typeof(RectTransform));
        portraitGo.transform.SetParent(portraitRoot.transform, false);
        var image = portraitGo.AddComponent<Image>();
        var animator = portraitGo.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        var ui = portraitRoot.AddComponent<CharacterPortraitUI>();
#if UNITY_EDITOR
        var so = new SerializedObject(ui);
        so.FindProperty("gameManager").objectReferenceValue = gm;
        so.FindProperty("portraitAnimator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();
#endif
        root.SetActive(true); // GameManager.Start → StartGame → Playing

        yield return null;
        yield return null;
        Assert.AreEqual(GameState.Playing, gm.CurrentState);
        Assert.AreEqual(CharacterPortraitUI.IdleStateHash, animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "Playing: Idle");
        Assert.IsNotNull(image.sprite, "클립이 UI Image Sprite를 바꿈");

        yield return WaitSeconds(2f); // Idle 길이(1.5초)보다 길게
        AnimatorStateInfo idleInfo = animator.GetCurrentAnimatorStateInfo(0);
        Assert.AreEqual(CharacterPortraitUI.IdleStateHash, idleInfo.fullPathHash, "Idle 반복 유지");
        Assert.IsTrue(idleInfo.loop);

        int requestsBefore = ui.PlayRequestCount;
        gm.EndGame();
        Assert.AreEqual(CharacterPortraitUI.PortraitState.Sad, ui.CurrentPortraitState, "GameOver 전환 즉시 Sad 요청");
        Assert.AreEqual(requestsBefore + 1, ui.PlayRequestCount);
        Assert.IsFalse(gm.IsWaitingForDeathAnimation, "Portrait와 무관하게 결과 처리 진행");

        yield return null;
        AnimatorStateInfo sadStart = animator.GetCurrentAnimatorStateInfo(0);
        Assert.AreEqual(CharacterPortraitUI.SadStateHash, sadStart.fullPathHash);
        Assert.Less(sadStart.normalizedTime, 0.5f, "Sad는 처음부터 재생");

        yield return WaitSeconds(2f); // Sad 길이(1.125초)보다 길게
        AnimatorStateInfo sadEnd = animator.GetCurrentAnimatorStateInfo(0);
        Assert.AreEqual(CharacterPortraitUI.SadStateHash, sadEnd.fullPathHash, "Sad 종료 후 Idle로 자동 전환하지 않음");
        Assert.IsFalse(sadEnd.loop, "Sad Loop Time 꺼짐");
        Assert.GreaterOrEqual(sadEnd.normalizedTime, 1f);
        Sprite heldSprite = image.sprite;
        yield return WaitSeconds(0.3f);
        Assert.AreSame(heldSprite, image.sprite, "마지막 프레임 유지");
        Assert.AreEqual(requestsBefore + 1, ui.PlayRequestCount, "Sad 재생 명령 반복 없음");

        gm.StartGame(); // 재시작
        yield return null;
        Assert.AreEqual(CharacterPortraitUI.IdleStateHash, animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "재시작: Idle");
    }
}
