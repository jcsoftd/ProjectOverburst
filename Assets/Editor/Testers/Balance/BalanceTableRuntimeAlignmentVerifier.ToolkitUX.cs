using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorBalance
{
    public static partial class BalanceTableRuntimeAlignmentVerifier
    {
        static bool toolkitUX;
        [MenuItem("OVERBURST/테스트/밸런스/Toolkit 디자인과 편집 UX")]
        public static void RunToolkitUX() => Run("../개인파일/코덱스산출/Tools/BalanceTable/20261005_ToolkitUX", true);
        static void Click(Button button)
        {
            if(button == null || !button.enabledSelf) throw new InvalidOperationException("Expected enabled Toolkit action");
            var method = typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method.Invoke(button.clickable, new object[] { null, 0 });
        }
        static void VerifyToolkitStep(int step)
        {
            var root=window.rootVisualElement;
            if(step==0){Click(root.Q<Button>("navigate-0"));Check(window.TotalRowCount==34,"Native sidebar opens weapon sheet");Get<ListView>(window,"list").SetSelection(0);return;}
            if(step==1)
            {
                Check(root.Q<Label>("selection-title").text.Contains(RowSource(Get<object>(window,"selected")).name),"Selection details show source");
                Check(!root.Q<Button>("apply-draft").enabledSelf&&!root.Q<Button>("save-applied").enabledSelf,"Clean state disables apply and save");
                var row=root.Query<Label>("row-name").ToList().First().parent;var number=row.Q("cell-0").Q<FloatField>("number");number.value+=1;
                Check(window.HasDraftChanges&&root.Q<Button>("apply-draft").enabledSelf,"Native edit enables apply");
                root.Q<Toggle>("changed-only").value=true;Check(window.VisibleRowCount==1,"Changed-only filter follows draft");return;
            }
            if(step==2)
            {
                Click(root.Q<Button>("reset-row"));Check(!window.HasDraftChanges,"Selected-row draft reset");
                Check(window.VisibleRowCount==0,"Changed-only becomes empty after reset");
                Check(root.Q("empty-state").ClassListContains("balance-empty--visible"),"Empty state explains filters");
                Click(root.Q<Button>("clear-filters"));Check(window.VisibleRowCount==34,"Empty-state clear restores sheet");
                Click(root.Q<Button>("navigate-5"));Check(window.TotalRowCount==Abilities().Length,"Native sidebar opens attack sheet");
                window.position=new Rect(40,40,1000,740);return;
            }
            if(step==3)
            {
                Check(root.ClassListContains("balance-root--compact"),"Compact window stacks details");
                Check(root.Q("selection-panel").worldBound.yMax<=root.worldBound.yMax,"Compact detail panel stays in window");
                Check(root.Q<Button>("save-applied").worldBound.yMax<=root.worldBound.yMax,"Compact action bar stays reachable");
                Capture(window,Output+"/captures/compact.png");window.position=new Rect(40,40,1520,900);return;
            }
            if(step==4)
            {
                Check(!root.ClassListContains("balance-root--compact"),"Wide window uses side details");
                Check(root.Q("selection-panel").worldBound.xMax<=root.worldBound.xMax,"Wide details stay in window");
                Capture(window,Output+"/captures/wide.png");return;
            }
            Check(!root.Query<IMGUIContainer>().ToList().Any(),"Balance editor has no IMGUI container");
            Check(!window.HasDraftChanges,"UX tests return all drafts");
        }
    }
}
