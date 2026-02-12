#if ODIN_INSPECTOR
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor.Prefab
{
    public class ReconnectPrefabWithRemoveOverrideWindow : OdinEditorWindow
    {
        public static void ShowWindow(GameObject target)
        {
            var window = GetWindow<ReconnectPrefabWithRemoveOverrideWindow>();
            window.targetGameObject = target;
            window.Show();
        }

        [ReadOnly,Required,InfoBox("注意：如果它的父prefab存在嵌套使用或变体prefabB，重连后可能会导致prefabB对该prefab的override丢失",InfoMessageType.Warning,nameof(TargetInOtherPrefab))]
        public GameObject targetGameObject;
        [Title("请选择要重连的prefab", TitleAlignment = TitleAlignments.Centered)]
        [AssetsOnly, HideLabel]
        public GameObject prefabAsset;

        [Button, EnableIf("@this.prefabAsset!=null&&this.targetGameObject!=null")]
        private void Reconnect()
        {
            ReconnectPrefabWithRemoveOverride(targetGameObject, prefabAsset);
            Close();
        }

        private bool TargetInOtherPrefab()
        {
            if (!targetGameObject)
                return false;
            return PrefabStageUtility.GetPrefabStage(targetGameObject);
        }

        public static void ReconnectPrefabWithRemoveOverride(GameObject destGo, GameObject sourcePrefab)
        {
            var childBefore = destGo.GetComponentsInChildren<Transform>(true).ToHashSet();
            var childCompBefore = destGo.GetComponentsInChildren<Component>(true).ToHashSet();

            var setting = new ConvertToPrefabInstanceSettings()
            {
                changeRootNameToAssetName = false,
                componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true,
                logInfo = false,
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                recordPropertyOverridesOfMatches = true
            };
            PrefabUtility.ConvertToPrefabInstance(destGo, sourcePrefab, setting, InteractionMode.AutomatedAction);

            //删除掉的gameObject保持删除
            var childAfter = destGo.GetComponentsInChildren<Transform>(true);
            foreach (var child in childAfter)
            {
                if (child && !childBefore.Contains(child))
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            //删除掉的Component保持删除
            var childCompAfter = destGo.GetComponentsInChildren<Component>(true);
            var childCompAfterQueue = new Queue<Component>();
            foreach (var child in childCompAfter)
            {
                if (child && !childCompBefore.Contains(child))
                    childCompAfterQueue.Enqueue(child);
            }
            var num = 0;
            while (childCompAfterQueue.Count > 0)
            {
                var child = childCompAfterQueue.Dequeue();
                if (child && !childCompBefore.Contains(child))
                {
                    if (CompExistRequire(child))
                    {
                        //存在RequireComponent时延迟删除
                        childCompAfterQueue.Enqueue(child);
                    }
                    else
                    {
                        DestroyImmediate(child);
                    }
                }

                if (num++ >= 9999)
                {
                    Debug.LogError("循环次数过多，请检查");
                    return;
                }
            }

            EditorUtility.SetDirty(destGo);
        }

        public static bool CompExistRequire(Component cp)
        {
            if (cp == null)
            {
                return false;
            }

            var comps = cp.GetComponents<Component>();
            foreach (var comp in comps)
            {
                var requireAttributes = comp.GetType()
                    .GetCustomAttributes(typeof(RequireComponent), true)
                    .Cast<RequireComponent>();
                if (requireAttributes.Any(att =>
                {
                    var childType = cp.GetType();
                    return att.m_Type0 == childType || att.m_Type1 == childType || att.m_Type2 == childType;
                }))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif