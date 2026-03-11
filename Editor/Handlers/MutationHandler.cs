using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityMCP.Utils;

namespace UnityMCP.Handlers
{
    public static class MutationHandler
    {
        private static bool s_MutationsEnabled = false;

        public static bool MutationsEnabled
        {
            get => s_MutationsEnabled;
            set => s_MutationsEnabled = value;
        }

        private static Dictionary<string, object> CheckMutationsEnabled()
        {
            if (!s_MutationsEnabled)
            {
                return new Dictionary<string, object>
                {
                    { "error", "Mutations are disabled. Enable them in Window > Unity MCP settings." },
                    { "code", -32001 }
                };
            }
            return null;
        }

        #region GameObject Operations

        public static Dictionary<string, object> CreateGameObject(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            if (@params == null)
            {
                return new Dictionary<string, object> { { "error", "Missing parameters" } };
            }

            string name = "New GameObject";
            if (@params.TryGetValue("name", out object nameObj))
                name = nameObj.ToString();

            GameObject parent = null;
            if (@params.TryGetValue("parentInstanceID", out object parentIdObj))
            {
                int parentId = Convert.ToInt32(parentIdObj);
                parent = EditorUtility.InstanceIDToObject(parentId) as GameObject;
            }
            else if (@params.TryGetValue("parentPath", out object parentPathObj))
            {
                parent = HierarchyHandler.FindGameObjectByPath(parentPathObj.ToString());
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Create GameObject '{name}'");

            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            if (parent != null)
            {
                Undo.SetTransformParent(go.transform, parent.transform, $"Set parent of {name}");
            }

            // Set transform if provided
            if (@params.TryGetValue("position", out object posObj) && posObj is Dictionary<string, object> posDict)
            {
                Vector3 pos = ParseVector3(posDict);
                Undo.RecordObject(go.transform, "Set position");
                go.transform.localPosition = pos;
            }

            if (@params.TryGetValue("rotation", out object rotObj) && rotObj is Dictionary<string, object> rotDict)
            {
                Vector3 euler = ParseVector3(rotDict);
                Undo.RecordObject(go.transform, "Set rotation");
                go.transform.localEulerAngles = euler;
            }

            if (@params.TryGetValue("scale", out object scaleObj) && scaleObj is Dictionary<string, object> scaleDict)
            {
                Vector3 scale = ParseVector3(scaleDict);
                Undo.RecordObject(go.transform, "Set scale");
                go.transform.localScale = scale;
            }

            // Set tag if provided
            if (@params.TryGetValue("tag", out object tagObj))
            {
                try
                {
                    Undo.RecordObject(go, "Set tag");
                    go.tag = tagObj.ToString();
                }
                catch { }
            }

            // Set layer if provided
            if (@params.TryGetValue("layer", out object layerObj))
            {
                Undo.RecordObject(go, "Set layer");
                go.layer = Convert.ToInt32(layerObj);
            }

            // Add primitive mesh if requested
            if (@params.TryGetValue("primitive", out object primitiveObj))
            {
                string primitiveName = primitiveObj.ToString().ToLower();
                PrimitiveType? primitiveType = primitiveName switch
                {
                    "cube" => PrimitiveType.Cube,
                    "sphere" => PrimitiveType.Sphere,
                    "capsule" => PrimitiveType.Capsule,
                    "cylinder" => PrimitiveType.Cylinder,
                    "plane" => PrimitiveType.Plane,
                    "quad" => PrimitiveType.Quad,
                    _ => null
                };

                if (primitiveType.HasValue)
                {
                    GameObject temp = GameObject.CreatePrimitive(primitiveType.Value);
                    var meshFilter = temp.GetComponent<MeshFilter>();
                    var meshRenderer = temp.GetComponent<MeshRenderer>();

                    var newMeshFilter = go.AddComponent<MeshFilter>();
                    newMeshFilter.sharedMesh = meshFilter.sharedMesh;

                    var newMeshRenderer = go.AddComponent<MeshRenderer>();
                    newMeshRenderer.sharedMaterials = meshRenderer.sharedMaterials;

                    if (primitiveType == PrimitiveType.Cube || primitiveType == PrimitiveType.Sphere ||
                        primitiveType == PrimitiveType.Capsule || primitiveType == PrimitiveType.Cylinder)
                    {
                        var collider = temp.GetComponent<Collider>();
                        if (collider is BoxCollider) go.AddComponent<BoxCollider>();
                        else if (collider is SphereCollider) go.AddComponent<SphereCollider>();
                        else if (collider is CapsuleCollider) go.AddComponent<CapsuleCollider>();
                        else if (collider is MeshCollider) go.AddComponent<MeshCollider>();
                    }

                    UnityEngine.Object.DestroyImmediate(temp);
                }
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "instanceID", go.GetInstanceID() },
                { "name", go.name },
                { "path", HierarchyHandler.GetGameObjectPath(go) }
            };
        }

        public static Dictionary<string, object> DeleteGameObject(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            string name = go.name;
            string path = HierarchyHandler.GetGameObjectPath(go);
            Scene scene = go.scene;

            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "deleted", name },
                { "path", path }
            };
        }

        public static Dictionary<string, object> RenameGameObject(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("newName", out object newNameObj))
            {
                return new Dictionary<string, object> { { "error", "Missing newName parameter" } };
            }

            string oldName = go.name;
            string newName = newNameObj.ToString();

            Undo.RecordObject(go, $"Rename {oldName} to {newName}");
            go.name = newName;
            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "oldName", oldName },
                { "newName", newName },
                { "instanceID", go.GetInstanceID() }
            };
        }

        public static Dictionary<string, object> MoveGameObject(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            Transform newParent = null;
            if (@params.TryGetValue("newParentInstanceID", out object parentIdObj))
            {
                int parentId = Convert.ToInt32(parentIdObj);
                var parentGo = EditorUtility.InstanceIDToObject(parentId) as GameObject;
                newParent = parentGo?.transform;
            }
            else if (@params.TryGetValue("newParentPath", out object parentPathObj))
            {
                var parentGo = HierarchyHandler.FindGameObjectByPath(parentPathObj.ToString());
                newParent = parentGo?.transform;
            }
            // If no parent specified, move to root (newParent stays null)

            int siblingIndex = -1;
            if (@params.TryGetValue("siblingIndex", out object siblingObj))
            {
                siblingIndex = Convert.ToInt32(siblingObj);
            }

            Undo.SetTransformParent(go.transform, newParent, $"Move {go.name}");

            if (siblingIndex >= 0)
            {
                Undo.RecordObject(go.transform, "Set sibling index");
                go.transform.SetSiblingIndex(siblingIndex);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "newPath", HierarchyHandler.GetGameObjectPath(go) },
                { "newParent", newParent != null ? newParent.name : "(root)" }
            };
        }

        public static Dictionary<string, object> DuplicateGameObject(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            GameObject duplicate = UnityEngine.Object.Instantiate(go, go.transform.parent);
            duplicate.name = go.name + " (Copy)";
            Undo.RegisterCreatedObjectUndo(duplicate, $"Duplicate {go.name}");

            if (@params.TryGetValue("newName", out object newNameObj))
            {
                duplicate.name = newNameObj.ToString();
            }

            EditorSceneManager.MarkSceneDirty(duplicate.scene);
            Selection.activeGameObject = duplicate;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "originalInstanceID", go.GetInstanceID() },
                { "newInstanceID", duplicate.GetInstanceID() },
                { "name", duplicate.name },
                { "path", HierarchyHandler.GetGameObjectPath(duplicate) }
            };
        }

        public static Dictionary<string, object> SetGameObjectActive(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("active", out object activeObj))
            {
                return new Dictionary<string, object> { { "error", "Missing active parameter" } };
            }

            bool active = Convert.ToBoolean(activeObj);

            Undo.RecordObject(go, $"Set {go.name} active = {active}");
            go.SetActive(active);
            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "active", go.activeSelf }
            };
        }

        #endregion

        #region Transform Operations

        public static Dictionary<string, object> SetTransform(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            Undo.RecordObject(go.transform, $"Set transform of {go.name}");

            bool local = true;
            if (@params.TryGetValue("local", out object localObj))
            {
                local = Convert.ToBoolean(localObj);
            }

            if (@params.TryGetValue("position", out object posObj) && posObj is Dictionary<string, object> posDict)
            {
                Vector3 pos = ParseVector3(posDict);
                if (local)
                    go.transform.localPosition = pos;
                else
                    go.transform.position = pos;
            }

            if (@params.TryGetValue("rotation", out object rotObj) && rotObj is Dictionary<string, object> rotDict)
            {
                Vector3 euler = ParseVector3(rotDict);
                if (local)
                    go.transform.localEulerAngles = euler;
                else
                    go.transform.eulerAngles = euler;
            }

            if (@params.TryGetValue("scale", out object scaleObj) && scaleObj is Dictionary<string, object> scaleDict)
            {
                Vector3 scale = ParseVector3(scaleDict);
                go.transform.localScale = scale;
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "transform", SerializationHelper.SerializeTransform(go.transform) }
            };
        }

        #endregion

        #region Component Operations

        public static Dictionary<string, object> AddComponent(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("componentType", out object typeObj))
            {
                return new Dictionary<string, object> { { "error", "Missing componentType parameter" } };
            }

            string typeName = typeObj.ToString();
            Type componentType = FindComponentType(typeName);

            if (componentType == null)
            {
                return new Dictionary<string, object> { { "error", $"Component type '{typeName}' not found" } };
            }

            Component component = Undo.AddComponent(go, componentType);

            if (component == null)
            {
                return new Dictionary<string, object> { { "error", $"Failed to add component '{typeName}'" } };
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "componentType", component.GetType().Name },
                { "componentInstanceID", component.GetInstanceID() }
            };
        }

        public static Dictionary<string, object> RemoveComponent(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("componentType", out object typeObj))
            {
                return new Dictionary<string, object> { { "error", "Missing componentType parameter" } };
            }

            string typeName = typeObj.ToString();
            Component component = null;

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                    comp.GetType().FullName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
            {
                return new Dictionary<string, object> { { "error", $"Component '{typeName}' not found on {go.name}" } };
            }

            // Can't remove Transform
            if (component is Transform)
            {
                return new Dictionary<string, object> { { "error", "Cannot remove Transform component" } };
            }

            string componentName = component.GetType().Name;
            Undo.DestroyObjectImmediate(component);
            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "removedComponent", componentName }
            };
        }

        public static Dictionary<string, object> SetComponentEnabled(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("componentType", out object typeObj))
            {
                return new Dictionary<string, object> { { "error", "Missing componentType parameter" } };
            }

            if (!@params.TryGetValue("enabled", out object enabledObj))
            {
                return new Dictionary<string, object> { { "error", "Missing enabled parameter" } };
            }

            string typeName = typeObj.ToString();
            bool enabled = Convert.ToBoolean(enabledObj);

            Component component = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
            {
                return new Dictionary<string, object> { { "error", $"Component '{typeName}' not found" } };
            }

            Undo.RecordObject(component, $"Set {typeName} enabled = {enabled}");

            if (component is Behaviour behaviour)
            {
                behaviour.enabled = enabled;
            }
            else if (component is Renderer renderer)
            {
                renderer.enabled = enabled;
            }
            else if (component is Collider collider)
            {
                collider.enabled = enabled;
            }
            else
            {
                return new Dictionary<string, object> { { "error", $"Component '{typeName}' does not have an enabled property" } };
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "componentType", typeName },
                { "enabled", enabled }
            };
        }

        #endregion

        #region Property/Field Operations

        public static Dictionary<string, object> SetComponentProperty(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("componentType", out object typeObj))
            {
                return new Dictionary<string, object> { { "error", "Missing componentType parameter" } };
            }

            if (!@params.TryGetValue("propertyName", out object propNameObj))
            {
                return new Dictionary<string, object> { { "error", "Missing propertyName parameter" } };
            }

            if (!@params.TryGetValue("value", out object valueObj))
            {
                return new Dictionary<string, object> { { "error", "Missing value parameter" } };
            }

            string typeName = typeObj.ToString();
            string propertyName = propNameObj.ToString();

            Component component = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
            {
                return new Dictionary<string, object> { { "error", $"Component '{typeName}' not found" } };
            }

            // Use SerializedObject for proper Unity serialization
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(propertyName);

            if (property == null)
            {
                return new Dictionary<string, object> { { "error", $"Property '{propertyName}' not found on {typeName}" } };
            }

            Undo.RecordObject(component, $"Set {propertyName} on {typeName}");

            bool success = SetSerializedPropertyValue(property, valueObj);

            if (success)
            {
                serializedObject.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(go.scene);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "gameObject", go.name },
                    { "componentType", typeName },
                    { "propertyName", propertyName },
                    { "newValue", valueObj }
                };
            }

            return new Dictionary<string, object> { { "error", $"Failed to set property '{propertyName}'" } };
        }

        public static Dictionary<string, object> SetMultipleProperties(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            if (!@params.TryGetValue("componentType", out object typeObj))
            {
                return new Dictionary<string, object> { { "error", "Missing componentType parameter" } };
            }

            if (!@params.TryGetValue("properties", out object propsObj) || !(propsObj is Dictionary<string, object> props))
            {
                return new Dictionary<string, object> { { "error", "Missing or invalid properties parameter" } };
            }

            string typeName = typeObj.ToString();

            Component component = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
            {
                return new Dictionary<string, object> { { "error", $"Component '{typeName}' not found" } };
            }

            var serializedObject = new SerializedObject(component);
            Undo.RecordObject(component, $"Set multiple properties on {typeName}");

            var results = new Dictionary<string, object>();
            foreach (var kvp in props)
            {
                var property = serializedObject.FindProperty(kvp.Key);
                if (property != null)
                {
                    bool success = SetSerializedPropertyValue(property, kvp.Value);
                    results[kvp.Key] = success ? "set" : "failed";
                }
                else
                {
                    results[kvp.Key] = "not found";
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(go.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "componentType", typeName },
                { "results", results }
            };
        }

        #endregion

        #region Scene Operations

        public static Dictionary<string, object> SaveScene(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            Scene scene;
            if (@params != null && @params.TryGetValue("scenePath", out object pathObj))
            {
                scene = SceneManager.GetSceneByPath(pathObj.ToString());
            }
            else
            {
                scene = SceneManager.GetActiveScene();
            }

            if (!scene.IsValid())
            {
                return new Dictionary<string, object> { { "error", "Invalid scene" } };
            }

            bool saved = EditorSceneManager.SaveScene(scene);

            return new Dictionary<string, object>
            {
                { "success", saved },
                { "sceneName", scene.name },
                { "scenePath", scene.path }
            };
        }

        public static Dictionary<string, object> CreatePrefab(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            GameObject go = FindGameObject(@params);
            if (go == null)
            {
                return new Dictionary<string, object> { { "error", "GameObject not found" } };
            }

            string path = "Assets/";
            if (@params.TryGetValue("path", out object pathObj))
            {
                path = pathObj.ToString();
            }

            if (!path.EndsWith(".prefab"))
            {
                path = System.IO.Path.Combine(path, go.name + ".prefab");
            }

            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);

            if (prefab == null)
            {
                return new Dictionary<string, object> { { "error", "Failed to create prefab" } };
            }

            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "prefabPath", path },
                { "prefabName", prefab.name },
                { "guid", AssetDatabase.AssetPathToGUID(path) }
            };
        }

        public static Dictionary<string, object> InstantiatePrefab(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            if (@params == null || !@params.TryGetValue("prefabPath", out object pathObj))
            {
                return new Dictionary<string, object> { { "error", "Missing prefabPath parameter" } };
            }

            string path = pathObj.ToString();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                return new Dictionary<string, object> { { "error", $"Prefab not found at '{path}'" } };
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");

            // Set parent if provided
            if (@params.TryGetValue("parentInstanceID", out object parentIdObj))
            {
                int parentId = Convert.ToInt32(parentIdObj);
                var parent = EditorUtility.InstanceIDToObject(parentId) as GameObject;
                if (parent != null)
                {
                    Undo.SetTransformParent(instance.transform, parent.transform, "Set parent");
                }
            }

            // Set position if provided
            if (@params.TryGetValue("position", out object posObj) && posObj is Dictionary<string, object> posDict)
            {
                instance.transform.position = ParseVector3(posDict);
            }

            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "instanceID", instance.GetInstanceID() },
                { "name", instance.name },
                { "path", HierarchyHandler.GetGameObjectPath(instance) }
            };
        }

        #endregion

        #region Object Reference Assignment (Drag & Drop equivalent)

        public static Dictionary<string, object> AssignObjectReference(Dictionary<string, object> @params)
        {
            var check = CheckMutationsEnabled();
            if (check != null) return check;

            if (@params == null)
                return new Dictionary<string, object> { { "error", "Missing parameters" } };

            // Target: the GameObject whose component field we're assigning to
            GameObject targetGo = null;
            if (@params.TryGetValue("targetInstanceID", out object targetIdObj))
                targetGo = EditorUtility.InstanceIDToObject(Convert.ToInt32(targetIdObj)) as GameObject;
            else if (@params.TryGetValue("targetPath", out object targetPathObj))
                targetGo = HierarchyHandler.FindGameObjectByPath(targetPathObj.ToString());
            else if (@params.TryGetValue("targetName", out object targetNameObj))
                targetGo = GameObject.Find(targetNameObj.ToString());

            if (targetGo == null)
                return new Dictionary<string, object> { { "error", "Target GameObject not found" } };

            if (!@params.TryGetValue("componentType", out object compTypeObj))
                return new Dictionary<string, object> { { "error", "Missing componentType parameter" } };

            if (!@params.TryGetValue("propertyName", out object propNameObj))
                return new Dictionary<string, object> { { "error", "Missing propertyName parameter" } };

            string compTypeName = compTypeObj.ToString();
            string propertyName = propNameObj.ToString();

            // Find component on target
            Component targetComponent = null;
            foreach (var comp in targetGo.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(compTypeName, StringComparison.OrdinalIgnoreCase) ||
                    comp.GetType().FullName.Equals(compTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    targetComponent = comp;
                    break;
                }
            }

            if (targetComponent == null)
                return new Dictionary<string, object> { { "error", $"Component '{compTypeName}' not found on {targetGo.name}" } };

            // Source: the object to assign (a GameObject, component on it, or asset)
            UnityEngine.Object sourceObject = null;
            string assignedType = "";

            // Option 1: Assign from a scene GameObject (or one of its components)
            if (@params.TryGetValue("sourceInstanceID", out object srcIdObj))
            {
                var srcObj = EditorUtility.InstanceIDToObject(Convert.ToInt32(srcIdObj));
                sourceObject = srcObj;
                assignedType = srcObj != null ? srcObj.GetType().Name : "null";
            }
            else if (@params.TryGetValue("sourcePath", out object srcPathObj) || @params.TryGetValue("sourceName", out srcPathObj))
            {
                bool isSourcePath = @params.ContainsKey("sourcePath");
                GameObject srcGo = isSourcePath
                    ? HierarchyHandler.FindGameObjectByPath(srcPathObj.ToString())
                    : GameObject.Find(srcPathObj.ToString());

                if (srcGo == null)
                    return new Dictionary<string, object> { { "error", $"Source GameObject '{srcPathObj}' not found" } };

                // If sourceComponentType is specified, get that component from the source GO
                if (@params.TryGetValue("sourceComponentType", out object srcCompTypeObj))
                {
                    string srcCompTypeName = srcCompTypeObj.ToString();

                    // Handle special case: "Transform" -> get transform
                    if (srcCompTypeName.Equals("Transform", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceObject = srcGo.transform;
                        assignedType = "Transform";
                    }
                    else
                    {
                        foreach (var comp in srcGo.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            if (comp.GetType().Name.Equals(srcCompTypeName, StringComparison.OrdinalIgnoreCase) ||
                                comp.GetType().FullName.Equals(srcCompTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                sourceObject = comp;
                                assignedType = comp.GetType().Name;
                                break;
                            }
                        }

                        if (sourceObject == null)
                            return new Dictionary<string, object> { { "error", $"Component '{srcCompTypeName}' not found on source '{srcGo.name}'" } };
                    }
                }
                else
                {
                    // Assign the GameObject itself
                    sourceObject = srcGo;
                    assignedType = "GameObject";
                }
            }
            // Option 2: Assign from an asset path
            else if (@params.TryGetValue("assetPath", out object assetPathObj))
            {
                sourceObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPathObj.ToString());
                if (sourceObject == null)
                    return new Dictionary<string, object> { { "error", $"Asset not found at '{assetPathObj}'" } };
                assignedType = sourceObject.GetType().Name;
            }
            // Option 3: Clear (set to null)
            else if (@params.TryGetValue("clear", out object clearObj) && Convert.ToBoolean(clearObj))
            {
                sourceObject = null;
                assignedType = "null";
            }
            else
            {
                return new Dictionary<string, object> { { "error", "Must specify source via sourceInstanceID, sourcePath, sourceName, assetPath, or clear:true" } };
            }

            // Apply via SerializedObject
            var serializedObject = new SerializedObject(targetComponent);
            var property = serializedObject.FindProperty(propertyName);

            if (property == null)
                return new Dictionary<string, object> { { "error", $"Property '{propertyName}' not found on {compTypeName}" } };

            if (property.propertyType != SerializedPropertyType.ObjectReference)
                return new Dictionary<string, object> { { "error", $"Property '{propertyName}' is not an object reference (it's {property.propertyType})" } };

            Undo.RecordObject(targetComponent, $"Assign {assignedType} to {propertyName}");
            property.objectReferenceValue = sourceObject;
            serializedObject.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(targetGo.scene);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "targetGameObject", targetGo.name },
                { "componentType", compTypeName },
                { "propertyName", propertyName },
                { "assignedObject", sourceObject != null ? sourceObject.name : "null" },
                { "assignedType", assignedType }
            };
        }

        #endregion

        #region Helper Methods

        private static GameObject FindGameObject(Dictionary<string, object> @params)
        {
            if (@params == null) return null;

            if (@params.TryGetValue("instanceID", out object idObj))
            {
                int instanceID = Convert.ToInt32(idObj);
                return EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            }

            if (@params.TryGetValue("path", out object pathObj))
            {
                return HierarchyHandler.FindGameObjectByPath(pathObj.ToString());
            }

            if (@params.TryGetValue("name", out object nameObj))
            {
                return GameObject.Find(nameObj.ToString());
            }

            return null;
        }

        private static Vector3 ParseVector3(Dictionary<string, object> dict)
        {
            float x = 0, y = 0, z = 0;
            if (dict.TryGetValue("x", out object xObj)) x = Convert.ToSingle(xObj);
            if (dict.TryGetValue("y", out object yObj)) y = Convert.ToSingle(yObj);
            if (dict.TryGetValue("z", out object zObj)) z = Convert.ToSingle(zObj);
            return new Vector3(x, y, z);
        }

        private static Type FindComponentType(string typeName)
        {
            // Try common Unity types first
            Type type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (type != null) return type;

            // Search all assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;

                // Try with UnityEngine prefix
                type = assembly.GetType($"UnityEngine.{typeName}");
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;
            }

            return null;
        }

        private static bool SetSerializedPropertyValue(SerializedProperty property, object value)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        property.intValue = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return true;

                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return true;

                    case SerializedPropertyType.String:
                        property.stringValue = value.ToString();
                        return true;

                    case SerializedPropertyType.Color:
                        if (value is Dictionary<string, object> colorDict)
                        {
                            float r = 1, g = 1, b = 1, a = 1;
                            if (colorDict.TryGetValue("r", out object rObj)) r = Convert.ToSingle(rObj);
                            if (colorDict.TryGetValue("g", out object gObj)) g = Convert.ToSingle(gObj);
                            if (colorDict.TryGetValue("b", out object bObj)) b = Convert.ToSingle(bObj);
                            if (colorDict.TryGetValue("a", out object aObj)) a = Convert.ToSingle(aObj);
                            property.colorValue = new Color(r, g, b, a);
                            return true;
                        }
                        break;

                    case SerializedPropertyType.Vector2:
                        if (value is Dictionary<string, object> v2Dict)
                        {
                            float x = 0, y = 0;
                            if (v2Dict.TryGetValue("x", out object xObj)) x = Convert.ToSingle(xObj);
                            if (v2Dict.TryGetValue("y", out object yObj)) y = Convert.ToSingle(yObj);
                            property.vector2Value = new Vector2(x, y);
                            return true;
                        }
                        break;

                    case SerializedPropertyType.Vector3:
                        if (value is Dictionary<string, object> v3Dict)
                        {
                            property.vector3Value = ParseVector3(v3Dict);
                            return true;
                        }
                        break;

                    case SerializedPropertyType.Vector4:
                        if (value is Dictionary<string, object> v4Dict)
                        {
                            float x = 0, y = 0, z = 0, w = 0;
                            if (v4Dict.TryGetValue("x", out object xObj)) x = Convert.ToSingle(xObj);
                            if (v4Dict.TryGetValue("y", out object yObj)) y = Convert.ToSingle(yObj);
                            if (v4Dict.TryGetValue("z", out object zObj)) z = Convert.ToSingle(zObj);
                            if (v4Dict.TryGetValue("w", out object wObj)) w = Convert.ToSingle(wObj);
                            property.vector4Value = new Vector4(x, y, z, w);
                            return true;
                        }
                        break;

                    case SerializedPropertyType.Enum:
                        if (value is int intVal)
                        {
                            property.enumValueIndex = intVal;
                            return true;
                        }
                        else if (value is string strVal)
                        {
                            for (int i = 0; i < property.enumNames.Length; i++)
                            {
                                if (property.enumNames[i].Equals(strVal, StringComparison.OrdinalIgnoreCase))
                                {
                                    property.enumValueIndex = i;
                                    return true;
                                }
                            }
                        }
                        break;

                    case SerializedPropertyType.LayerMask:
                        property.intValue = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.ObjectReference:
                        if (value is Dictionary<string, object> objDict)
                        {
                            if (objDict.TryGetValue("path", out object pathObj))
                            {
                                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pathObj.ToString());
                                property.objectReferenceValue = asset;
                                return true;
                            }
                            if (objDict.TryGetValue("instanceID", out object idObj))
                            {
                                var obj = EditorUtility.InstanceIDToObject(Convert.ToInt32(idObj));
                                property.objectReferenceValue = obj;
                                return true;
                            }
                        }
                        else if (value == null)
                        {
                            property.objectReferenceValue = null;
                            return true;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to set property: {ex.Message}");
            }

            return false;
        }

        #endregion
    }
}
