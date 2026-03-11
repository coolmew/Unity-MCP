using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityMCP.Utils;

namespace UnityMCP.Handlers
{
    public static class HierarchyHandler
    {
        private static Dictionary<string, object> s_CachedHierarchy;
        private static int s_CacheFrame = -1;

        static HierarchyHandler()
        {
            EditorSceneManager.sceneLoaded += (scene, mode) => InvalidateCache();
            EditorSceneManager.sceneUnloaded += (scene) => InvalidateCache();
            EditorApplication.hierarchyChanged += InvalidateCache;
            Undo.postprocessModifications += (mods) => { InvalidateCache(); return mods; };
        }

        public static void InvalidateCache()
        {
            s_CachedHierarchy = null;
            s_CacheFrame = -1;
        }

        public static Dictionary<string, object> GetSceneHierarchy(Dictionary<string, object> @params = null)
        {
            int maxDepth = 8;
            bool includeInactive = true;

            if (@params != null)
            {
                if (@params.TryGetValue("maxDepth", out object md))
                    maxDepth = Convert.ToInt32(md);
                if (@params.TryGetValue("includeInactive", out object ia))
                    includeInactive = Convert.ToBoolean(ia);
            }

            // Use cache if valid
            int currentFrame = Time.frameCount;
            if (s_CachedHierarchy != null && s_CacheFrame == currentFrame && maxDepth == 8 && includeInactive)
            {
                return s_CachedHierarchy;
            }

            var result = new Dictionary<string, object>();
            var scenes = new List<Dictionary<string, object>>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;

                var sceneData = new Dictionary<string, object>
                {
                    { "name", scene.name },
                    { "path", scene.path },
                    { "buildIndex", scene.buildIndex },
                    { "isLoaded", scene.isLoaded },
                    { "isDirty", scene.isDirty },
                    { "rootCount", scene.rootCount }
                };

                if (scene.isLoaded)
                {
                    var rootObjects = new List<Dictionary<string, object>>();
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (!includeInactive && !root.activeInHierarchy)
                            continue;
                        rootObjects.Add(SerializeGameObjectHierarchy(root, 0, maxDepth, includeInactive));
                    }
                    sceneData["rootGameObjects"] = rootObjects;
                }

                scenes.Add(sceneData);
            }

            result["scenes"] = scenes;
            result["activeScene"] = SceneManager.GetActiveScene().name;
            result["totalSceneCount"] = SceneManager.sceneCount;

            // Cache result
            if (maxDepth == 8 && includeInactive)
            {
                s_CachedHierarchy = result;
                s_CacheFrame = currentFrame;
            }

            return result;
        }

        public static Dictionary<string, object> SerializeGameObjectHierarchy(GameObject go, int depth, int maxDepth, bool includeInactive)
        {
            var data = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceID", go.GetInstanceID() },
                { "tag", go.tag },
                { "layer", go.layer },
                { "layerName", LayerMask.LayerToName(go.layer) },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "isStatic", go.isStatic },
                { "childCount", go.transform.childCount },
                { "path", GetGameObjectPath(go) }
            };

            // Add component summary
            var components = go.GetComponents<Component>();
            var componentTypes = new List<string>();
            foreach (var comp in components)
            {
                if (comp != null)
                    componentTypes.Add(comp.GetType().Name);
            }
            data["componentTypes"] = componentTypes;

            // Add children if within depth limit
            if (depth < maxDepth && go.transform.childCount > 0)
            {
                var children = new List<Dictionary<string, object>>();
                foreach (Transform child in go.transform)
                {
                    if (!includeInactive && !child.gameObject.activeInHierarchy)
                        continue;
                    children.Add(SerializeGameObjectHierarchy(child.gameObject, depth + 1, maxDepth, includeInactive));
                }
                data["children"] = children;
            }
            else if (go.transform.childCount > 0)
            {
                data["children"] = $"<{go.transform.childCount} children, depth limit reached>";
            }

            return data;
        }

        public static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform current = go.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return "/" + path;
        }

        public static Dictionary<string, object> GetGameObject(Dictionary<string, object> @params)
        {
            if (@params == null)
            {
                return CreateError("Missing parameters");
            }

            GameObject go = null;

            if (@params.TryGetValue("instanceID", out object idObj))
            {
                int instanceID = Convert.ToInt32(idObj);
                go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            }
            else if (@params.TryGetValue("path", out object pathObj))
            {
                string path = pathObj.ToString();
                go = GameObject.Find(path);

                // Try finding by exact hierarchy path
                if (go == null && path.StartsWith("/"))
                {
                    go = FindGameObjectByPath(path);
                }
            }
            else if (@params.TryGetValue("name", out object nameObj))
            {
                string name = nameObj.ToString();
                go = GameObject.Find(name);
            }

            if (go == null)
            {
                return CreateError("GameObject not found");
            }

            return SerializeGameObjectFull(go);
        }

        public static GameObject FindGameObjectByPath(string path)
        {
            string[] parts = path.Trim('/').Split('/');
            if (parts.Length == 0) return null;

            // Find root object
            GameObject current = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == parts[0])
                    {
                        current = root;
                        break;
                    }
                }
                if (current != null) break;
            }

            if (current == null) return null;

            // Navigate to child
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        public static Dictionary<string, object> SerializeGameObjectFull(GameObject go)
        {
            var data = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceID", go.GetInstanceID() },
                { "tag", go.tag },
                { "layer", go.layer },
                { "layerName", LayerMask.LayerToName(go.layer) },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "isStatic", go.isStatic },
                { "hideFlags", go.hideFlags.ToString() },
                { "scene", go.scene.name },
                { "path", GetGameObjectPath(go) },
                { "transform", SerializationHelper.SerializeTransform(go.transform) }
            };

            // Serialize all components
            var components = new List<Dictionary<string, object>>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                components.Add(ComponentHandler.SerializeComponentSummary(comp));
            }
            data["components"] = components;

            // Add parent info
            if (go.transform.parent != null)
            {
                data["parent"] = new Dictionary<string, object>
                {
                    { "name", go.transform.parent.name },
                    { "instanceID", go.transform.parent.gameObject.GetInstanceID() },
                    { "path", GetGameObjectPath(go.transform.parent.gameObject) }
                };
            }

            // Add children summary
            var children = new List<Dictionary<string, object>>();
            foreach (Transform child in go.transform)
            {
                children.Add(new Dictionary<string, object>
                {
                    { "name", child.name },
                    { "instanceID", child.gameObject.GetInstanceID() },
                    { "active", child.gameObject.activeSelf }
                });
            }
            data["children"] = children;
            data["childCount"] = go.transform.childCount;

            return data;
        }

        public static Dictionary<string, object> FindGameObjects(Dictionary<string, object> @params)
        {
            var results = new List<Dictionary<string, object>>();
            int offset = 0;
            int limit = 100;

            string nameFilter = null;
            string tagFilter = null;
            string componentFilter = null;
            int? layerFilter = null;

            if (@params != null)
            {
                if (@params.TryGetValue("nameFilter", out object nf))
                    nameFilter = nf?.ToString();
                if (@params.TryGetValue("tagFilter", out object tf))
                    tagFilter = tf?.ToString();
                if (@params.TryGetValue("componentFilter", out object cf))
                    componentFilter = cf?.ToString();
                if (@params.TryGetValue("layerFilter", out object lf))
                    layerFilter = Convert.ToInt32(lf);
                if (@params.TryGetValue("offset", out object o))
                    offset = Convert.ToInt32(o);
                if (@params.TryGetValue("limit", out object l))
                    limit = Math.Min(Convert.ToInt32(l), 500);
            }

            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            int totalMatches = 0;
            int added = 0;

            foreach (var go in allObjects)
            {
                if (!MatchesFilters(go, nameFilter, tagFilter, componentFilter, layerFilter))
                    continue;

                totalMatches++;

                if (totalMatches <= offset)
                    continue;

                if (added >= limit)
                    continue;

                results.Add(new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "instanceID", go.GetInstanceID() },
                    { "tag", go.tag },
                    { "layer", go.layer },
                    { "layerName", LayerMask.LayerToName(go.layer) },
                    { "active", go.activeInHierarchy },
                    { "path", GetGameObjectPath(go) }
                });

                added++;
            }

            return new Dictionary<string, object>
            {
                { "gameObjects", results },
                { "totalMatches", totalMatches },
                { "offset", offset },
                { "limit", limit },
                { "hasMore", totalMatches > offset + limit }
            };
        }

        private static bool MatchesFilters(GameObject go, string nameFilter, string tagFilter, string componentFilter, int? layerFilter)
        {
            if (!string.IsNullOrEmpty(nameFilter))
            {
                if (!go.name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrEmpty(tagFilter))
            {
                if (!go.CompareTag(tagFilter))
                    return false;
            }

            if (layerFilter.HasValue)
            {
                if (go.layer != layerFilter.Value)
                    return false;
            }

            if (!string.IsNullOrEmpty(componentFilter))
            {
                bool hasComponent = false;
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp != null && comp.GetType().Name.Contains(componentFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        hasComponent = true;
                        break;
                    }
                }
                if (!hasComponent) return false;
            }

            return true;
        }

        private static Dictionary<string, object> CreateError(string message)
        {
            return new Dictionary<string, object>
            {
                { "error", message }
            };
        }
    }
}
