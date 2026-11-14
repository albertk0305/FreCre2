using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>메인화면 How To Play / Credits 패널 테스트 (EditMode). Awake는 직접 호출한다.</summary>
public class MainInfoPanelUITests
{
    private GameObject canvas;
    private GameObject textPanel;
    private GameObject howToPlay;
    private GameObject credits;
    private MainInfoPanelUI ui;

    [SetUp]
    public void SetUp()
    {
        // 씬과 같은 구조: Canvas/BackgroundImage/TextPanel/{HowToPlay, Credits}
        canvas = new GameObject("Canvas");
        var background = Child(canvas, "BackgroundImage");
        textPanel = Child(background, "TextPanel");
        howToPlay = Child(textPanel, "HowToPlay");
        credits = Child(textPanel, "Credits");

        ui = background.AddComponent<MainInfoPanelUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("textPanel").objectReferenceValue = textPanel;
        so.FindProperty("howToPlay").objectReferenceValue = howToPlay;
        so.FindProperty("credits").objectReferenceValue = credits;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 씬에서 실수로 켜 둔 상태여도 Awake가 닫는지 확인하기 위해 모두 켠 상태로 시작
        textPanel.SetActive(true);
        howToPlay.SetActive(true);
        credits.SetActive(true);
        ui.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(canvas);
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private void AssertState(bool panel, bool how, bool cred, MainInfoPanelUI.InfoContent content)
    {
        Assert.AreEqual(panel, textPanel.activeSelf, "TextPanel");
        Assert.AreEqual(how, howToPlay.activeSelf, "HowToPlay");
        Assert.AreEqual(cred, credits.activeSelf, "Credits");
        Assert.AreEqual(content, ui.CurrentContent);
        Assert.IsFalse(howToPlay.activeSelf && credits.activeSelf, "두 콘텐츠 동시 활성 금지");
    }

    [Test]
    public void Awake_HidesEverything()
    {
        AssertState(false, false, false, MainInfoPanelUI.InfoContent.None);
        Assert.IsFalse(ui.IsOpen);
    }

    [Test]
    public void ShowHowToPlay_OpensPanelWithHowToPlayOnly()
    {
        ui.ShowHowToPlay();
        AssertState(true, true, false, MainInfoPanelUI.InfoContent.HowToPlay);
        Assert.IsTrue(howToPlay.activeInHierarchy, "실제로 화면에 보임");
    }

    [Test]
    public void ShowCredits_OpensPanelWithCreditsOnly()
    {
        ui.ShowCredits();
        AssertState(true, false, true, MainInfoPanelUI.InfoContent.Credits);
        Assert.IsTrue(credits.activeInHierarchy);
    }

    [Test]
    public void Switching_NeverShowsBoth()
    {
        ui.ShowHowToPlay();
        ui.ShowCredits();
        AssertState(true, false, true, MainInfoPanelUI.InfoContent.Credits);
        ui.ShowHowToPlay();
        AssertState(true, true, false, MainInfoPanelUI.InfoContent.HowToPlay);
        ui.ShowHowToPlay(); // 같은 버튼 반복
        AssertState(true, true, false, MainInfoPanelUI.InfoContent.HowToPlay);
    }

    [Test]
    public void ClosePanel_HidesPanelAndBothContents()
    {
        ui.ShowCredits();
        ui.ClosePanel();
        AssertState(false, false, false, MainInfoPanelUI.InfoContent.None);
        ui.ClosePanel(); // 닫힌 상태에서 다시 닫아도 안전
        AssertState(false, false, false, MainInfoPanelUI.InfoContent.None);
    }

    [Test]
    public void ReopenAfterClose_ShowsRequestedContent()
    {
        ui.ShowHowToPlay();
        ui.ClosePanel();
        ui.ShowCredits();
        AssertState(true, false, true, MainInfoPanelUI.InfoContent.Credits);
        ui.ClosePanel();
        ui.ShowHowToPlay();
        AssertState(true, true, false, MainInfoPanelUI.InfoContent.HowToPlay);
    }

    [Test]
    public void MissingReferences_DoNotThrow()
    {
        var lone = new GameObject("Lone").AddComponent<MainInfoPanelUI>();
        lone.transform.SetParent(canvas.transform, false);
        Assert.DoesNotThrow(() =>
        {
            lone.ShowHowToPlay();
            lone.ShowCredits();
            lone.ClosePanel();
        });
    }
}
