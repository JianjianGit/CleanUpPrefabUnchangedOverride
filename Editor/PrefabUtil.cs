using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor.Prefab
{
    public static class PrefabUtil
    {
        #region ReconnectPrefabWithRemoveOverride

        private const string ReconnectPrefabWithRemoveOverrideKey = "GameObject/Prefab/Reconnect Prefab 保留 Remove Override";

        [MenuItem(ReconnectPrefabWithRemoveOverrideKey)]
        private static void ReconnectPrefabWithRemoveOverrideMenu()
        {
#if ODIN_INSPECTOR
            var selectGo = Selection.activeGameObject;
            ReconnectPrefabWithRemoveOverrideWindow.ShowWindow(selectGo);
#endif
        }

        [MenuItem(ReconnectPrefabWithRemoveOverrideKey, true)]
        private static bool ValidReconnectPrefabWithRemoveOverrideMenu()
        {
            var selectGo = Selection.activeGameObject;
            return selectGo && !PrefabUtility.IsPartOfNonAssetPrefabInstance(selectGo) && PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot != selectGo;
        }

        #endregion

        public static void ForeachChild(this Transform tran, Func<Transform, bool> func)
        {
            if (func == null) return;

            var searchChild = func.Invoke(tran);
            if (!searchChild) return;

            foreach (Transform child in tran)
                child.ForeachChild(func);
        }

        public static string GetHierarchyPath(this Transform root, Transform child)
        {
            if (child == root)
            {
                return root.name;
            }

            var path = child.name;
            while (child.parent != null && child != root)
            {
                child = child.parent;
                path = $"{child.name}/{path}";
            }

            return path;
        }

        public static string GetHierarchyPathWithoutRoot(this Transform root, Transform child)
        {
            if (child == root)
            {
                return string.Empty;
            }

            var path = child.name;
            while (child.parent != null && child.parent != root)
            {
                child = child.parent;
                path = $"{child.name}/{path}";
            }

            return path;
        }

        /// <summary>
        ///   <para>This method identifies and removes all unused overrides from a list of Prefab Instance roots. See the manual https:docs.unity3d.com2023.1DocumentationManualUnusedOverrides.html|Unused Overides for more detail.</para>
        /// </summary>
        /// <param name="prefabInstances">List of Prefab instances to remove unused overrides from.</param>
        /// <param name="action">UserAction will record undo and write result to Editor log file.</param>
        public static void RemoveUnusedOverrides(GameObject[] prefabInstances)
        {
            HashSet<GameObject> source = prefabInstances != null ? new HashSet<GameObject>(prefabInstances.Length) : throw new ArgumentNullException(nameof(prefabInstances));
            foreach (GameObject prefabInstance in prefabInstances)
            {
                if ((UnityEngine.Object)prefabInstance == (UnityEngine.Object)null)
                    throw new ArgumentException("Input array contains null elements.", nameof(prefabInstances));
                if (!PrefabUtility.IsPartOfPrefabInstance((UnityEngine.Object)prefabInstance))
                    throw new ArgumentException("Input array contains objects which are not part of a Prefab instance.", nameof(prefabInstances));
                source.Add(PrefabUtility.GetOutermostPrefabInstanceRoot((UnityEngine.Object)prefabInstance));
            }
            //PrefabUtility.RemovePrefabInstanceUnusedOverrides(source.Select<GameObject, PrefabUtility.InstanceOverridesInfo>(new Func<GameObject, PrefabUtility.InstanceOverridesInfo>(PrefabUtility.GetPrefabInstanceOverridesInfo_Internal)).ToArray<PrefabUtility.InstanceOverridesInfo>(), action);

            //用反射实现
            var prefabUtilityType = typeof(PrefabUtility);
            var instanceOverridesInfo = prefabUtilityType.GetNestedType("InstanceOverridesInfo", BindingFlags.NonPublic);
            var getPrefabInstanceOverridesInfo_Internal = prefabUtilityType.GetMethod("GetPrefabInstanceOverridesInfo_Internal", BindingFlags.NonPublic | BindingFlags.Static);
            var removePrefabInstanceUnusedOverrides = prefabUtilityType.GetMethod("RemovePrefabInstanceUnusedOverrides",
                BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { instanceOverridesInfo.MakeArrayType() }, null);

            var overrideInfoList = new List<object>();
            foreach (var go in source)
            {
                var info = getPrefabInstanceOverridesInfo_Internal.Invoke(null, new object[] { go });
                overrideInfoList.Add(info);
            }

            var array = Array.CreateInstance(instanceOverridesInfo, overrideInfoList.Count);
            for (var i = 0; i < overrideInfoList.Count; i++)
            {
                array.SetValue(overrideInfoList[i], i);
            }

            removePrefabInstanceUnusedOverrides.Invoke(null, new object[] { array });
        }

        #region Event

        [InitializeOnLoadMethod]
        private static void RegisterPrefabEvent()
        {
            PrefabStage.prefabSaving += OnPrefabStageSaving;
            PrefabStage.prefabStageOpened += PrefabToolbar.OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += PrefabToolbar.OnPrefabStageClosing;
            PrefabUtility.prefabInstanceUpdated += OnPrefabInstanceUpdated;
        }

        private static void OnPrefabStageSaving(GameObject go)
        {
            PrefabUnchangedOverrideRevert.OnPrefabStageSaving(go);
        }

        private static void OnPrefabInstanceUpdated(GameObject instanceGo)
        {

        }

        #endregion
    }
}
