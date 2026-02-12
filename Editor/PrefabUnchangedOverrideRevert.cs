using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor.Prefab
{
    public static class PrefabUnchangedOverrideRevert
    {
        public static bool CleanPrefabOverrideOnSave
        {
            get => EditorPrefs.GetBool(nameof(CleanPrefabOverrideOnSave), true);
            set => EditorPrefs.SetBool(nameof(CleanPrefabOverrideOnSave), value);
        }

        internal static void OnPrefabStageSaving(GameObject obj)
        {
            if (CleanPrefabOverrideOnSave)
                RevertUnchangedOverride(obj, true);
        }

        private static int RevertUnchangedOverride(GameObject obj, bool withLog)
        {
            var revertCount = 0;
            obj.transform.ForeachChild(child =>
            {
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject)
                    && PrefabUtility.HasPrefabInstanceAnyOverrides(child.gameObject, false))
                {
                    revertCount += RevertOverride(child.gameObject, withLog);
                }

                return true;
            });

            if (withLog)
                Debug.Log($"【清理Override】<color=#00FF00>共还原了 {revertCount} 个相同的属性Override。</color>");

            return revertCount;
        }

        private static int RevertOverride(GameObject prefabInstanceRoot, bool withLog)
        {
            var revertCount = 0;

            var modifications = PrefabUtility.GetPropertyModifications(prefabInstanceRoot);
            if (modifications == null || modifications.Length == 0)
            {
                Debug.Log("没有找到属性覆盖。");
                return 0;
            }

            var modGroup = modifications.GroupBy(mod => mod.target, group => group)
                .Where(group => group.Key)
                .ToDictionary(group => group.Key);

            var overrides = PrefabUtility.GetObjectOverrides(prefabInstanceRoot);
            foreach (var objectOverride in overrides)
            {
                var assetObj = objectOverride.GetAssetObject();

                if (!modGroup.TryGetValue(assetObj, out var mods))
                {
                    Debug.LogError("找不到target");
                    continue;
                }

                foreach (var mod in mods)
                {
                    if (mod.target == null || string.IsNullOrEmpty(mod.propertyPath))
                        continue;

                    // 获取实例对象的 SerializedProperty
                    using var instanceSo = new SerializedObject(objectOverride.instanceObject);
                    using var instanceProp = instanceSo.FindProperty(mod.propertyPath);
                    if (instanceProp == null)
                        continue;

                    // 获取原始 Prefab 对象
                    var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(objectOverride.instanceObject);
                    if (prefabSource == null)
                        continue;

                    // 获取原始对象的 SerializedProperty
                    using var prefabSo = new SerializedObject(prefabSource);
                    using var prefabProp = prefabSo.FindProperty(mod.propertyPath);
                    if (prefabProp == null)
                        continue;

                    var isSame = false;
                    if (instanceProp.propertyType == SerializedPropertyType.ObjectReference && instanceProp.objectReferenceValue && PrefabUtility.IsPartOfPrefabInstance(instanceProp.objectReferenceValue))
                    {
                        var assetObjRef = PrefabUtility.GetCorrespondingObjectFromSource(instanceProp.objectReferenceValue);
                        isSame = assetObjRef == prefabProp.objectReferenceValue;
                    }
                    else if (instanceProp.propertyType == SerializedPropertyType.Float)
                    {
                        isSame = Mathf.Approximately(instanceProp.floatValue, prefabProp.floatValue);
                    }
                    else if (SerializedProperty.DataEquals(instanceProp, prefabProp))
                    {
                        isSame = true;
                    }

                    // 如果值相同，则还原覆盖
                    if (isSame)
                    {
                        PrefabUtility.RevertPropertyOverride(instanceProp, InteractionMode.AutomatedAction);
                        revertCount++;

                        var targetComp = objectOverride.instanceObject as Component;
                        var targetGo = targetComp ? targetComp.gameObject : objectOverride.instanceObject as GameObject;
                        if (!targetGo)
                            continue;

                        if (withLog)
                        {
                            var oldValue = mod.value;
                            if (string.IsNullOrEmpty(oldValue))
                            {
                                oldValue = mod.objectReference?.ToString();
                            }
                            Debug.Log($"【清理Override】已还原相同的属性Override：{prefabInstanceRoot.transform.root.GetHierarchyPath(targetGo.transform)}\n - {objectOverride.instanceObject.GetType()}-{mod.propertyPath} - oldValue:{oldValue}");
                        }
                    }
                }
            }

            return revertCount;
        }

        [MenuItem("Tools/清理所有prefab的无变化的Override")]
        private static void CleanAllPrefab()
        {
            var findAssets = AssetDatabase.FindAssets("t:Prefab");
            var revertCount = 0;
            var beforeCleanPrefabOverrideOnSave = CleanPrefabOverrideOnSave;
            //临时关闭
            CleanPrefabOverrideOnSave = false;
            var progressNum = 0f;
            foreach (var guid in findAssets)
            {
                try
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (EditorUtility.DisplayCancelableProgressBar("清理所有prefab的无变化的Override", path, progressNum++ / findAssets.Length))
                    {
                        break;
                    }

                    var existOverride = false;
                    asset.transform.ForeachChild(child =>
                    {
                        if (PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject)
                            && PrefabUtility.HasPrefabInstanceAnyOverrides(child.gameObject, false))
                        {
                            existOverride = true;
                            return false;
                        }

                        return true;
                    });

                    if (existOverride)
                    {
                        var content = PrefabUtility.LoadPrefabContents(path);
                        var count = RevertUnchangedOverride(content, false);
                        if (count > 0)
                        {
                            PrefabUtility.SaveAsPrefabAsset(content, path);
                            revertCount += count;
                        }
                        PrefabUtility.UnloadPrefabContents(content);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            //还原
            CleanPrefabOverrideOnSave = beforeCleanPrefabOverrideOnSave;

            EditorUtility.ClearProgressBar();

            Debug.Log($"【清理Override】<color=#00FF00>共还原了 {revertCount} 个相同的属性Override。</color>");
        }
    }
}