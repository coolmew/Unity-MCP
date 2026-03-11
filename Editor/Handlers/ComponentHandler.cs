using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityMCP.Utils;

namespace UnityMCP.Handlers
{
    public static class ComponentHandler
    {
        public static Dictionary<string, object> GetComponent(Dictionary<string, object> @params)
        {
            if (@params == null)
            {
                return CreateError("Missing parameters");
            }

            if (!@params.TryGetValue("instanceID", out object idObj))
            {
                return CreateError("Missing instanceID parameter");
            }

            int instanceID = Convert.ToInt32(idObj);
            var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;

            if (go == null)
            {
                return CreateError("GameObject not found");
            }

            if (!@params.TryGetValue("componentType", out object typeObj))
            {
                return CreateError("Missing componentType parameter");
            }

            string componentTypeName = typeObj.ToString();
            Component component = null;

            // Find component by type name
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(componentTypeName, StringComparison.OrdinalIgnoreCase) ||
                    comp.GetType().FullName.Equals(componentTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
            {
                return CreateError($"Component '{componentTypeName}' not found on GameObject '{go.name}'");
            }

            return SerializeComponentFull(component);
        }

        public static Dictionary<string, object> SerializeComponentSummary(Component comp)
        {
            if (comp == null)
            {
                return new Dictionary<string, object> { { "type", "null" }, { "missing", true } };
            }

            var data = new Dictionary<string, object>
            {
                { "type", comp.GetType().Name },
                { "fullType", comp.GetType().FullName },
                { "instanceID", comp.GetInstanceID() }
            };

            // Check if component is enabled (for Behaviours)
            if (comp is Behaviour behaviour)
            {
                data["enabled"] = behaviour.enabled;
            }
            else if (comp is Renderer renderer)
            {
                data["enabled"] = renderer.enabled;
            }
            else if (comp is Collider collider)
            {
                data["enabled"] = collider.enabled;
            }

            return data;
        }

        public static Dictionary<string, object> SerializeComponentFull(Component comp)
        {
            if (comp == null)
            {
                return new Dictionary<string, object> { { "type", "null" }, { "missing", true } };
            }

            var data = new Dictionary<string, object>
            {
                { "type", comp.GetType().Name },
                { "fullType", comp.GetType().FullName },
                { "namespace", comp.GetType().Namespace ?? "" },
                { "instanceID", comp.GetInstanceID() },
                { "gameObject", comp.gameObject.name },
                { "gameObjectInstanceID", comp.gameObject.GetInstanceID() }
            };

            // Check if component is enabled
            if (comp is Behaviour behaviour)
            {
                data["enabled"] = behaviour.enabled;
            }
            else if (comp is Renderer renderer)
            {
                data["enabled"] = renderer.enabled;
            }
            else if (comp is Collider collider)
            {
                data["enabled"] = collider.enabled;
            }

            // Serialize fields using SerializedObject for accurate Unity serialization
            var serializedFields = SerializeComponentFields(comp);
            data["fields"] = serializedFields;

            // Add type-specific data
            AddTypeSpecificData(comp, data);

            return data;
        }

        private static Dictionary<string, object> SerializeComponentFields(Component comp)
        {
            var fields = new Dictionary<string, object>();

            try
            {
                var serializedObject = new SerializedObject(comp);
                var iterator = serializedObject.GetIterator();

                if (iterator.NextVisible(true))
                {
                    do
                    {
                        // Skip script reference
                        if (iterator.name == "m_Script") continue;

                        var fieldData = SerializeSerializedProperty(iterator);
                        if (fieldData != null)
                        {
                            fields[iterator.name] = fieldData;
                        }
                    }
                    while (iterator.NextVisible(false));
                }
            }
            catch (Exception ex)
            {
                fields["_serializationError"] = ex.Message;
            }

            return fields;
        }

        private static Dictionary<string, object> SerializeSerializedProperty(SerializedProperty prop)
        {
            var data = new Dictionary<string, object>
            {
                { "name", prop.name },
                { "displayName", prop.displayName },
                { "type", prop.type },
                { "propertyType", prop.propertyType.ToString() },
                { "isArray", prop.isArray },
                { "isExpanded", prop.isExpanded },
                { "hasChildren", prop.hasChildren },
                { "depth", prop.depth }
            };

            // Get tooltip if available
            string tooltip = prop.tooltip;
            if (!string.IsNullOrEmpty(tooltip))
            {
                data["tooltip"] = tooltip;
            }

            // Serialize value based on type
            try
            {
                object value = GetPropertyValue(prop);
                if (value != null)
                {
                    data["value"] = value;
                }
            }
            catch
            {
                data["value"] = "<unable to read>";
            }

            return data;
        }

        private static object GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;

                case SerializedPropertyType.Boolean:
                    return prop.boolValue;

                case SerializedPropertyType.Float:
                    return prop.floatValue;

                case SerializedPropertyType.String:
                    return prop.stringValue;

                case SerializedPropertyType.Color:
                    return SerializationHelper.SerializeColor(prop.colorValue);

                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                    {
                        return SerializationHelper.SerializeUnityObjectReference(prop.objectReferenceValue);
                    }
                    return null;

                case SerializedPropertyType.LayerMask:
                    return prop.intValue;

                case SerializedPropertyType.Enum:
                    return new Dictionary<string, object>
                    {
                        { "index", prop.enumValueIndex },
                        { "name", prop.enumNames.Length > prop.enumValueIndex ? prop.enumNames[prop.enumValueIndex] : "Unknown" },
                        { "options", prop.enumNames }
                    };

                case SerializedPropertyType.Vector2:
                    return SerializationHelper.SerializeVector2(prop.vector2Value);

                case SerializedPropertyType.Vector3:
                    return SerializationHelper.SerializeVector3(prop.vector3Value);

                case SerializedPropertyType.Vector4:
                    return SerializationHelper.SerializeVector4(prop.vector4Value);

                case SerializedPropertyType.Rect:
                    return SerializationHelper.SerializeRect(prop.rectValue);

                case SerializedPropertyType.ArraySize:
                    return prop.intValue;

                case SerializedPropertyType.Character:
                    return (char)prop.intValue;

                case SerializedPropertyType.AnimationCurve:
                    return SerializationHelper.SerializeValue(prop.animationCurveValue);

                case SerializedPropertyType.Bounds:
                    return SerializationHelper.SerializeBounds(prop.boundsValue);

                case SerializedPropertyType.Quaternion:
                    return SerializationHelper.SerializeQuaternion(prop.quaternionValue);

                case SerializedPropertyType.Vector2Int:
                    return new Dictionary<string, object> { { "x", prop.vector2IntValue.x }, { "y", prop.vector2IntValue.y } };

                case SerializedPropertyType.Vector3Int:
                    return new Dictionary<string, object> { { "x", prop.vector3IntValue.x }, { "y", prop.vector3IntValue.y }, { "z", prop.vector3IntValue.z } };

                case SerializedPropertyType.RectInt:
                    var ri = prop.rectIntValue;
                    return new Dictionary<string, object> { { "x", ri.x }, { "y", ri.y }, { "width", ri.width }, { "height", ri.height } };

                case SerializedPropertyType.BoundsInt:
                    var bi = prop.boundsIntValue;
                    return new Dictionary<string, object>
                    {
                        { "position", new Dictionary<string, object> { { "x", bi.position.x }, { "y", bi.position.y }, { "z", bi.position.z } } },
                        { "size", new Dictionary<string, object> { { "x", bi.size.x }, { "y", bi.size.y }, { "z", bi.size.z } } }
                    };

                case SerializedPropertyType.Generic:
                    if (prop.isArray)
                    {
                        var array = new List<object>();
                        int count = Math.Min(prop.arraySize, 50);
                        for (int i = 0; i < count; i++)
                        {
                            var element = prop.GetArrayElementAtIndex(i);
                            array.Add(GetPropertyValue(element));
                        }
                        if (prop.arraySize > 50)
                        {
                            array.Add($"<... {prop.arraySize - 50} more elements>");
                        }
                        return array;
                    }
                    return $"<{prop.type}>";

                default:
                    return $"<{prop.propertyType}>";
            }
        }

        private static void AddTypeSpecificData(Component comp, Dictionary<string, object> data)
        {
            // Add type-specific commonly needed data
            switch (comp)
            {
                case Transform t:
                    data["transform"] = SerializationHelper.SerializeTransform(t);
                    break;

                case Camera cam:
                    data["cameraData"] = new Dictionary<string, object>
                    {
                        { "fieldOfView", cam.fieldOfView },
                        { "nearClipPlane", cam.nearClipPlane },
                        { "farClipPlane", cam.farClipPlane },
                        { "orthographic", cam.orthographic },
                        { "orthographicSize", cam.orthographicSize },
                        { "depth", cam.depth },
                        { "cullingMask", cam.cullingMask },
                        { "clearFlags", cam.clearFlags.ToString() },
                        { "backgroundColor", SerializationHelper.SerializeColor(cam.backgroundColor) }
                    };
                    break;

                case Light light:
                    data["lightData"] = new Dictionary<string, object>
                    {
                        { "type", light.type.ToString() },
                        { "color", SerializationHelper.SerializeColor(light.color) },
                        { "intensity", light.intensity },
                        { "range", light.range },
                        { "spotAngle", light.spotAngle },
                        { "shadows", light.shadows.ToString() }
                    };
                    break;

                case MeshRenderer mr:
                    var materials = new List<Dictionary<string, object>>();
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            materials.Add(SerializationHelper.SerializeUnityObjectReference(mat));
                        }
                    }
                    data["materials"] = materials;
                    break;

                case MeshFilter mf:
                    if (mf.sharedMesh != null)
                    {
                        data["mesh"] = new Dictionary<string, object>
                        {
                            { "name", mf.sharedMesh.name },
                            { "vertexCount", mf.sharedMesh.vertexCount },
                            { "triangleCount", mf.sharedMesh.triangles.Length / 3 },
                            { "subMeshCount", mf.sharedMesh.subMeshCount },
                            { "bounds", SerializationHelper.SerializeBounds(mf.sharedMesh.bounds) }
                        };
                    }
                    break;

                case Rigidbody rb:
                    data["rigidbodyData"] = new Dictionary<string, object>
                    {
                        { "mass", rb.mass },
                        { "drag", rb.drag },
                        { "angularDrag", rb.angularDrag },
                        { "useGravity", rb.useGravity },
                        { "isKinematic", rb.isKinematic },
                        { "interpolation", rb.interpolation.ToString() },
                        { "collisionDetectionMode", rb.collisionDetectionMode.ToString() },
                        { "velocity", SerializationHelper.SerializeVector3(rb.velocity) },
                        { "angularVelocity", SerializationHelper.SerializeVector3(rb.angularVelocity) }
                    };
                    break;

                case Collider col:
                    data["colliderData"] = new Dictionary<string, object>
                    {
                        { "isTrigger", col.isTrigger },
                        { "bounds", SerializationHelper.SerializeBounds(col.bounds) }
                    };
                    if (col is BoxCollider box)
                    {
                        data["boxCollider"] = new Dictionary<string, object>
                        {
                            { "center", SerializationHelper.SerializeVector3(box.center) },
                            { "size", SerializationHelper.SerializeVector3(box.size) }
                        };
                    }
                    else if (col is SphereCollider sphere)
                    {
                        data["sphereCollider"] = new Dictionary<string, object>
                        {
                            { "center", SerializationHelper.SerializeVector3(sphere.center) },
                            { "radius", sphere.radius }
                        };
                    }
                    else if (col is CapsuleCollider capsule)
                    {
                        data["capsuleCollider"] = new Dictionary<string, object>
                        {
                            { "center", SerializationHelper.SerializeVector3(capsule.center) },
                            { "radius", capsule.radius },
                            { "height", capsule.height },
                            { "direction", capsule.direction }
                        };
                    }
                    break;

                case AudioSource audio:
                    data["audioData"] = new Dictionary<string, object>
                    {
                        { "clip", audio.clip != null ? SerializationHelper.SerializeUnityObjectReference(audio.clip) : null },
                        { "volume", audio.volume },
                        { "pitch", audio.pitch },
                        { "loop", audio.loop },
                        { "playOnAwake", audio.playOnAwake },
                        { "spatialBlend", audio.spatialBlend },
                        { "isPlaying", audio.isPlaying }
                    };
                    break;

                case Animator animator:
                    data["animatorData"] = new Dictionary<string, object>
                    {
                        { "runtimeAnimatorController", animator.runtimeAnimatorController != null ? 
                            SerializationHelper.SerializeUnityObjectReference(animator.runtimeAnimatorController) : null },
                        { "avatar", animator.avatar != null ? SerializationHelper.SerializeUnityObjectReference(animator.avatar) : null },
                        { "applyRootMotion", animator.applyRootMotion },
                        { "updateMode", animator.updateMode.ToString() },
                        { "cullingMode", animator.cullingMode.ToString() }
                    };
                    break;
            }
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
