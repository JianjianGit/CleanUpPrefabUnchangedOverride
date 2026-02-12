using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Prefab
{
    [Overlay(typeof(SceneView), "Prefab工具", defaultDockZone = DockZone.RightColumn, defaultDockPosition = DockPosition.Top, id = nameof(PrefabToolbar))]
    public class PrefabToolbar : Overlay
    {
        internal static void OnPrefabStageClosing(PrefabStage obj)
        {
            if (!PrefabStageUtility.GetCurrentPrefabStage()
                && SceneView.lastActiveSceneView && SceneView.lastActiveSceneView.TryGetOverlay(nameof(PrefabToolbar), out var overlay))
            {
                overlay.displayed = false;
            }
        }

        internal static void OnPrefabStageOpened(PrefabStage obj)
        {
            if (SceneView.lastActiveSceneView && SceneView.lastActiveSceneView.TryGetOverlay(nameof(PrefabToolbar), out var overlay))
            {
                overlay.displayed = true;
            }
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();

            root.Add(NewUnchangedOverrideRevertToggle());
            //root.Add(NewHelpButton());

            return root;
        }

        private static VisualElement NewUnchangedOverrideRevertToggle()
        {
            var cleanOverrideTog = new PrefabToolBarToggle
            {
                label = "保存时清理Override属性",
                tooltip = "保存时，将没有变化的Override属性还原，建议保持开启",
                value = PrefabUnchangedOverrideRevert.CleanPrefabOverrideOnSave,
            };
            cleanOverrideTog.RegisterValueChangedCallback(evt =>
            {
                PrefabUnchangedOverrideRevert.CleanPrefabOverrideOnSave = evt.newValue;

                var state = evt.newValue ? "开启" : "关闭";
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent($"{cleanOverrideTog.label}：{state}"));
            });

            return cleanOverrideTog;
        }
/*
        private static VisualElement NewHelpButton()
        {
            var helpButton = new Button
            {
                text = "Prefab使用的注意事项",
                tooltip = "跳转到文档",
                style = { unityFontStyleAndWeight = FontStyle.Bold }
            };
            helpButton.clicked += () =>
            {
                Application.OpenURL("");
            };

            return helpButton;
        }
*/
    }
    internal class PrefabToolBarToggle : Toggle
    {
        public PrefabToolBarToggle() : this(null) { }

        public PrefabToolBarToggle(string label) : base(label)
        {
            labelElement.style.minWidth = 150;
            labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
        }
    }
}
