#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Editor.Prefab
{
    public class PrefabDependencyWin : OdinEditorWindow
    {
        private const string SearchDepKey = "Assets/Prefab工具/搜索prefab的引用prefab";

        private const string PrefabPreviewPath = "Temp/PrefabPreview/";

        [AssetsOnly, PropertyOrder(0), Sirenix.OdinInspector.ReadOnly]
        public GameObject target;

        [Sirenix.OdinInspector.ReadOnly, ShowIf("@usedByPrefabs.Count>0"), PropertyOrder(1.5f), LabelText("引用Target的prefab（包括间接引用）")]
        public List<GameObject> usedByPrefabs = new List<GameObject>();

        private string _searchPattern;

        private List<PrefabModification> _prefabModifications = new List<PrefabModification>();

        [InfoBox("@\"搜索匹配错误(正则):\n\"+_exception?.ToString()", InfoMessageType.Error, nameof(ExistException))]
        [LabelText("Target的所有具有override属性的列表"), ShowIf("@_prefabModifications.Count>0"), PropertyOrder(4)]
        [ListDrawerSettings(IsReadOnly = true, OnTitleBarGUI = nameof(OnTitleBarGUI))]
        public List<PrefabModification> prefabModifications = new List<PrefabModification>();

        private bool ExistException => _exception != null;

        private Exception _exception;

        private Dictionary<GameObject, List<GameObject>> _depDic = new Dictionary<GameObject, List<GameObject>>();
        private Dictionary<GameObject, HashSet<GameObject>> _useByDic = new Dictionary<GameObject, HashSet<GameObject>>();

        #region ShowWindow

        [MenuItem(SearchDepKey)]
        private static void ReconnectPrefabWithRemoveOverrideMenu()
        {
            var selectGo = Selection.activeGameObject;
            ShowWindow(selectGo);
        }

        [MenuItem(SearchDepKey, true)]
        private static bool ValidReconnectPrefabWithRemoveOverrideMenu()
        {
            var selectGo = Selection.activeGameObject;
            return selectGo && AssetDatabase.Contains(selectGo);
        }

        private static void ShowWindow(GameObject target)
        {
            var window = GetWindow<PrefabDependencyWin>("Prefab引用Prefab查找工具");
            window.target = target;
            window.ResetResult();
            window.Show();
        }

        private void ResetResult()
        {
            _searchPattern = string.Empty;
            usedByPrefabs.Clear();
            _prefabModifications.Clear();
        }

        #endregion

        [Button("搜索引用Target的prefab（包括间接引用）"), PropertyOrder(1)]
        public void SearchPrefabDependency()
        {
            if (target == null)
            {
                Debug.LogError("target == null");
                return;
            }

            var findAssets = AssetDatabase.FindAssets("t:Prefab");
            var num = 0f;
            try
            {
                foreach (var guid in findAssets)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (EditorUtility.DisplayCancelableProgressBar("搜索引用", assetPath, num++ / findAssets.Length))
                    {
                        return;
                    }

                    var assetGo = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                    var depArray = AssetDatabase.GetDependencies(assetPath);
                    var depList = new List<GameObject>();
                    _depDic[assetGo] = depList;
                    foreach (var dep in depArray)
                    {
                        if (dep.ToLower().EndsWith(".prefab"))
                        {
                            var depGo = AssetDatabase.LoadAssetAtPath<GameObject>(dep);
                            depList.Add(depGo);

                            if (!_useByDic.TryGetValue(depGo, out var useBySet))
                            {
                                useBySet = new HashSet<GameObject>();
                                _useByDic[depGo] = useBySet;
                            }

                            useBySet.Add(assetGo);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorUtility.ClearProgressBar();
                return;
            }
            EditorUtility.ClearProgressBar();
            EditorUtility.UnloadUnusedAssetsImmediate();

            var usedByPrefabSet = new HashSet<GameObject>();
            var stack = new Stack<GameObject>();
            stack.Push(target);
            while (stack.Count > 0)
            {
                var byGo = stack.Pop();
                if (_useByDic.TryGetValue(byGo, out var useBySetT))
                {
                    foreach (var gameObject in useBySetT)
                    {
                        if (gameObject != target && usedByPrefabSet.Add(gameObject))
                        {
                            stack.Push(gameObject);
                        }
                    }
                }
            }

            usedByPrefabs = usedByPrefabSet.ToList();

            if (usedByPrefabs.Count == 0)
            {
                ShowNotification(new GUIContent("没有搜索到被引用的prefab"), 2f);
            }

            Debug.Log("<color=#00FF00>搜索prefab引用结束</color>");
        }

        [Button("搜索prefab的Override属性"), ShowIf("@usedByPrefabs.Count>0"), PropertyOrder(3)]
        public void SearchModifications()
        {
            if (target == null)
            {
                Debug.LogError("target == null");
                return;
            }

            if (usedByPrefabs.Count == 0)
            {
                Debug.LogError("usedByPrefabs为空");
                return;
            }

            _prefabModifications.Clear();

            var prefabModificationDic = new Dictionary<GameObject, PrefabModification>();

            var processedInstanceSet = new HashSet<GameObject>();

            try
            {
                var num = 0f;
                foreach (var byPrefab in usedByPrefabs)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("搜索prefab的Override属性", byPrefab.name, num++ / usedByPrefabs.Count))
                    {
                        return;
                    }

                    var content = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(byPrefab));

                    processedInstanceSet.Clear();
                    PrefabUtil.ForeachChild(content.transform, child =>
                    {
                        if (PrefabUtility.GetCorrespondingObjectFromOriginalSource(child.gameObject) != target) return true;

                        var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(child.gameObject);

                        //每个instance只遍历一次
                        if (!processedInstanceSet.Add(instanceRoot))
                            return false;

                        var modifications = PrefabUtility.GetPropertyModifications(instanceRoot);
                        var overrides = PrefabUtility.GetObjectOverrides(instanceRoot);

                        var modDic = modifications.GroupBy(mod => mod.target)
                            .Where(group => group.Key != null)
                            .ToDictionary(group => group.Key, group => group);
                        /*
                        foreach (var modification in modifications)
                        {
                            if (modification.target == null)
                            {
                                continue;
                            }

                            var sourceTarget = PrefabUtility.GetCorrespondingObjectFromOriginalSource(modification.target);
                            var targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(sourceTarget));
                            if (targetAsset == target)
                            {
                                GetPrefabMod().modifications.Add(modification);
                            }
                        }
                        */
                        PrefabModification GetPrefabMod()
                        {
                            if (!prefabModificationDic.TryGetValue(byPrefab, out var prefabModification))
                            {
                                prefabModification = new PrefabModification()
                                {
                                    prefab = byPrefab
                                };
                                prefabModificationDic[byPrefab] = prefabModification;
                            }

                            return prefabModification;
                        }

                        //Debug.Log(modDic.Count);
                        foreach (var objectOverride in overrides)
                        {
                            var targetComp = objectOverride.instanceObject as Component;
                            var targetGo = targetComp ? targetComp.gameObject : objectOverride.instanceObject as GameObject;
                            if (!targetGo)
                                continue;

                            var targetGoSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(targetGo);
                            var targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(targetGoSource));

                            if (targetAsset == target)
                            {
                                var path = content.transform.GetHierarchyPathWithoutRoot(targetGo.transform);
                                var targetType = objectOverride.instanceObject.GetType();

                                if (modDic.TryGetValue(objectOverride.GetAssetObject(), out var mods))
                                {
                                    foreach (var mod in mods)
                                    {
                                        var componentIndex = 0;
                                        if (targetComp)
                                        {
                                            var components = targetGo.GetComponents(targetType);
                                            componentIndex = Array.IndexOf(components, targetComp);
                                            if (componentIndex == -1)
                                                Debug.LogError("componentIndex == -1");
                                        }

                                        GetPrefabMod().AddOverrideInfo(path, mod.propertyPath, targetType, componentIndex);
                                    }
                                }
                                else
                                {
                                    Debug.LogError($"找不到 {objectOverride.instanceObject}");
                                }
                            }
                        }

                        return false;
                    });

                    PrefabUtility.UnloadPrefabContents(content);
                }

            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
            _prefabModifications = prefabModificationDic.Values.ToList();
            UpdateSearch();

            EditorUtility.ClearProgressBar();
            Debug.Log("<color=#00FF00>搜索prefab的Override属性 结束</color>");
        }

        #region DrawList

        private void OnTitleBarGUI()
        {
            DrawSearchButton();
            DrawRevert();
        }

        private void DrawSearchButton()
        {
            var rect2 = EditorGUILayout.GetControlRect(false).AddYMin(2f);
            if (UnityVersion.IsVersionOrGreater(2019, 3))
                rect2 = rect2.AddY(-2f);
            var searchPattern = SirenixEditorGUI.SearchField(rect2, _searchPattern);
            if (!string.Equals(searchPattern, _searchPattern, StringComparison.Ordinal))
            {
                _searchPattern = searchPattern;
                UpdateSearch();
            }
        }

        private void UpdateSearch()
        {
            if (string.IsNullOrEmpty(_searchPattern))
            {
                prefabModifications = _prefabModifications;
                return;
            }

            _exception = null;
            var mdfList = new List<PrefabModification>();
            try
            {
                foreach (var prefabModification in _prefabModifications)
                {
                    PrefabModification mdf = null;
                    List<OverrideInfo> overrideInfos = null;
                    foreach (var info in prefabModification.overrideInfoList)
                    {
                        if (info.overridePath.Contains(_searchPattern, StringComparison.Ordinal))
                        {
                            overrideInfos ??= new List<OverrideInfo>();
                            overrideInfos.Add(info);

                            mdf ??= new PrefabModification()
                            {
                                prefab = prefabModification.prefab
                            };
                            mdf.overrideInfoList = overrideInfos;
                        }
                    }

                    if (mdf != null)
                    {
                        mdfList.Add(mdf);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                _exception = e;
            }

            prefabModifications = mdfList;
        }

        private void DrawRevert()
        {
            if (SirenixEditorGUI.ToolbarButton(new GUIContent("Revert All Override")))
            {
                for (var i = 0; i < prefabModifications.Count; i++)
                {
                    var mod = prefabModifications[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Revert All Override", mod.prefab.name, i / (prefabModifications.Count - 1f)))
                        break;

                    mod.RevertOverrideAll();
                }
                EditorUtility.ClearProgressBar();
                ShowNotification(new GUIContent("Revert All Override 结束"), 3f);
            }
        }

        #endregion

        [Serializable]
        public class PrefabModification
        {
            [HideLabel]
            public GameObject prefab;

            [HideInInspector]
            public List<PropertyModification> modifications = new List<PropertyModification>();

            [ListDrawerSettings(IsReadOnly = true)]
            public List<OverrideInfo> overrideInfoList = new List<OverrideInfo>();

            public void AddOverrideInfo(string path, string propertyPath, Type componentType, int componentIndex)
            {
                var info = new OverrideInfo(path, propertyPath, componentType, componentIndex);
                info.RevertAction += RevertOverrideOne;
                overrideInfoList.Add(info);
            }

            public void RevertOverrideAll()
            {
                try
                {
                    var prefabPath = AssetDatabase.GetAssetPath(prefab);
                    var content = PrefabUtility.LoadPrefabContents(prefabPath);

                    var set = new HashSet<GameObject>();

                    foreach (var info in overrideInfoList)
                    {
                        var instance = RevertOverrideInfo(content, info);
                        if (instance)
                            set.Add(instance);
                    }

                    if (set.Count > 0)
                        PrefabUtil.RemoveUnusedOverrides(set.ToArray());

                    PrefabUtility.SaveAsPrefabAsset(content, prefabPath);
                    PrefabUtility.UnloadPrefabContents(content);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            private GameObject RevertOverrideInfo(GameObject content, OverrideInfo info)
            {
                try
                {
                    var child = content.transform.Find(info.path);
                    if (child == null)
                    {
                        //Debug.LogError($"content({content.name}) child is null : {info.path}");
                        return null;
                    }
                    var type = info.ComponentType;
                    Object obj = null;
                    if (typeof(Component).IsAssignableFrom(type))
                    {
                        var components = child.GetComponents(type);
                        obj = components[info.componentIndex];
                    }
                    else
                    {
                        obj = child.gameObject;
                    }
                    using var instanceSo = new SerializedObject(obj);
                    using var instanceProp = instanceSo.FindProperty(info.propertyPath);
                    if (instanceProp != null)
                        PrefabUtility.RevertPropertyOverride(instanceProp, InteractionMode.AutomatedAction);

                    info.reverted = true;

                    return PrefabUtility.GetOutermostPrefabInstanceRoot(child);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                return null;
            }

            public void RevertOverrideOne(OverrideInfo info)
            {
                try
                {
                    var prefabPath = AssetDatabase.GetAssetPath(prefab);
                    var content = PrefabUtility.LoadPrefabContents(prefabPath);

                    var instance = RevertOverrideInfo(content, info);

                    if (instance)
                        PrefabUtil.RemoveUnusedOverrides(new[] { instance });

                    PrefabUtility.SaveAsPrefabAsset(content, prefabPath);
                    PrefabUtility.UnloadPrefabContents(content);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }

                Debug.Log($"RevertOverrideOne end");
            }
        }

        [Serializable]
        public class OverrideInfo
        {
            public event Action<OverrideInfo> RevertAction;

            [HideInInspector] public string path;
            [HideInInspector] public string propertyPath;
            [HideInInspector] public string componentTypeStr;
            [HideInInspector] public int componentIndex;
            [HideInInspector] public bool reverted;
            [HorizontalGroup, HideLabel]
            public string overridePath;

            public Type ComponentType => string.IsNullOrEmpty(componentTypeStr) ? null : Type.GetType(componentTypeStr);

            public OverrideInfo(string path, string propertyPath, Type componentType, int componentIndex)
            {
                this.path = path;
                this.propertyPath = propertyPath;
                componentTypeStr = componentType.AssemblyQualifiedName;
                this.componentIndex = componentIndex;

                overridePath = $"{this.path} - {componentType}[{this.componentIndex}] - {this.propertyPath}";
            }

            private bool ShowRevert => RevertAction != null && !reverted;

            [Button, HorizontalGroup(width: 60f), ShowIf(nameof(ShowRevert))]
            private void Revert()
            {
                RevertAction?.Invoke(this);
            }

            [Button, HorizontalGroup(width: 60f), ShowIf(nameof(reverted)), GUIColor("green"), DisableIf(nameof(reverted))]
            private void Reverted() { }
        }
    }
}
#endif