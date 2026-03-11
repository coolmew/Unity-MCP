using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UnityMCP.Utils
{
    public static class SerializationHelper
    {
        private const int DefaultMaxDepth = 8;
        private static readonly HashSet<object> s_VisitedObjects = new HashSet<object>();

        public static Dictionary<string, object> SerializeVector2(Vector2 v)
        {
            return new Dictionary<string, object> { { "x", v.x }, { "y", v.y } };
        }

        public static Dictionary<string, object> SerializeVector3(Vector3 v)
        {
            return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } };
        }

        public static Dictionary<string, object> SerializeVector4(Vector4 v)
        {
            return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z }, { "w", v.w } };
        }

        public static Dictionary<string, object> SerializeQuaternion(Quaternion q)
        {
            var euler = q.eulerAngles;
            return new Dictionary<string, object>
            {
                { "x", q.x },
                { "y", q.y },
                { "z", q.z },
                { "w", q.w },
                { "euler", new Dictionary<string, object> { { "x", euler.x }, { "y", euler.y }, { "z", euler.z } } }
            };
        }

        public static Dictionary<string, object> SerializeColor(Color c)
        {
            return new Dictionary<string, object>
            {
                { "r", c.r },
                { "g", c.g },
                { "b", c.b },
                { "a", c.a },
                { "hex", ColorUtility.ToHtmlStringRGBA(c) }
            };
        }

        public static Dictionary<string, object> SerializeBounds(Bounds b)
        {
            return new Dictionary<string, object>
            {
                { "center", SerializeVector3(b.center) },
                { "extents", SerializeVector3(b.extents) },
                { "size", SerializeVector3(b.size) },
                { "min", SerializeVector3(b.min) },
                { "max", SerializeVector3(b.max) }
            };
        }

        public static Dictionary<string, object> SerializeRect(Rect r)
        {
            return new Dictionary<string, object>
            {
                { "x", r.x },
                { "y", r.y },
                { "width", r.width },
                { "height", r.height }
            };
        }

        public static Dictionary<string, object> SerializeUnityObjectReference(UnityEngine.Object obj)
        {
            if (obj == null)
                return null;

            var result = new Dictionary<string, object>
            {
                { "instanceID", obj.GetInstanceID() },
                { "name", obj.name },
                { "type", obj.GetType().Name }
            };

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                result["path"] = assetPath;
                result["guid"] = AssetDatabase.AssetPathToGUID(assetPath);
            }

            return result;
        }

        public static Dictionary<string, object> SerializeTransform(Transform t)
        {
            return new Dictionary<string, object>
            {
                { "position", SerializeVector3(t.position) },
                { "localPosition", SerializeVector3(t.localPosition) },
                { "rotation", SerializeQuaternion(t.rotation) },
                { "localRotation", SerializeQuaternion(t.localRotation) },
                { "localScale", SerializeVector3(t.localScale) },
                { "lossyScale", SerializeVector3(t.lossyScale) }
            };
        }

        public static object SerializeValue(object value, int depth = 0, int maxDepth = DefaultMaxDepth)
        {
            if (value == null)
                return null;

            if (depth > maxDepth)
                return "<max depth exceeded>";

            Type type = value.GetType();

            // Primitives and strings
            if (type.IsPrimitive || value is string || value is decimal)
                return value;

            // Enums
            if (type.IsEnum)
                return value.ToString();

            // Unity-specific types
            if (value is Vector2 v2) return SerializeVector2(v2);
            if (value is Vector3 v3) return SerializeVector3(v3);
            if (value is Vector4 v4) return SerializeVector4(v4);
            if (value is Vector2Int v2i) return new Dictionary<string, object> { { "x", v2i.x }, { "y", v2i.y } };
            if (value is Vector3Int v3i) return new Dictionary<string, object> { { "x", v3i.x }, { "y", v3i.y }, { "z", v3i.z } };
            if (value is Quaternion q) return SerializeQuaternion(q);
            if (value is Color c) return SerializeColor(c);
            if (value is Color32 c32) return SerializeColor(c32);
            if (value is Bounds b) return SerializeBounds(b);
            if (value is Rect r) return SerializeRect(r);
            if (value is Matrix4x4 m) return SerializeMatrix4x4(m);
            if (value is AnimationCurve curve) return SerializeAnimationCurve(curve);
            if (value is Gradient gradient) return SerializeGradient(gradient);
            if (value is LayerMask layerMask) return new Dictionary<string, object> { { "value", layerMask.value }, { "layers", GetLayerNames(layerMask) } };

            // Unity Object references
            if (value is UnityEngine.Object unityObj)
                return SerializeUnityObjectReference(unityObj);

            // Check for circular references
            if (!type.IsValueType)
            {
                if (s_VisitedObjects.Contains(value))
                    return "<circular reference>";
                s_VisitedObjects.Add(value);
            }

            try
            {
                // Arrays and Lists
                if (value is IList list)
                {
                    var result = new List<object>();
                    int count = Math.Min(list.Count, 100); // Limit array size
                    for (int i = 0; i < count; i++)
                    {
                        result.Add(SerializeValue(list[i], depth + 1, maxDepth));
                    }
                    if (list.Count > 100)
                        result.Add($"<... {list.Count - 100} more items>");
                    return result;
                }

                // Dictionaries
                if (value is IDictionary dict)
                {
                    var result = new Dictionary<string, object>();
                    int count = 0;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (count++ >= 100) break;
                        string key = entry.Key?.ToString() ?? "null";
                        result[key] = SerializeValue(entry.Value, depth + 1, maxDepth);
                    }
                    return result;
                }

                // Generic objects - serialize public fields and properties
                if (type.IsClass || type.IsValueType)
                {
                    return SerializeObject(value, depth, maxDepth);
                }
            }
            finally
            {
                if (!type.IsValueType)
                    s_VisitedObjects.Remove(value);
            }

            return value.ToString();
        }

        private static Dictionary<string, object> SerializeObject(object obj, int depth, int maxDepth)
        {
            var result = new Dictionary<string, object>();
            Type type = obj.GetType();

            result["$type"] = type.Name;

            // Serialize fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                try
                {
                    object fieldValue = field.GetValue(obj);
                    result[field.Name] = SerializeValue(fieldValue, depth + 1, maxDepth);
                }
                catch
                {
                    result[field.Name] = "<error reading field>";
                }
            }

            return result;
        }

        private static Dictionary<string, object> SerializeMatrix4x4(Matrix4x4 m)
        {
            return new Dictionary<string, object>
            {
                { "m00", m.m00 }, { "m01", m.m01 }, { "m02", m.m02 }, { "m03", m.m03 },
                { "m10", m.m10 }, { "m11", m.m11 }, { "m12", m.m12 }, { "m13", m.m13 },
                { "m20", m.m20 }, { "m21", m.m21 }, { "m22", m.m22 }, { "m23", m.m23 },
                { "m30", m.m30 }, { "m31", m.m31 }, { "m32", m.m32 }, { "m33", m.m33 }
            };
        }

        private static Dictionary<string, object> SerializeAnimationCurve(AnimationCurve curve)
        {
            var keys = new List<Dictionary<string, object>>();
            foreach (var key in curve.keys)
            {
                keys.Add(new Dictionary<string, object>
                {
                    { "time", key.time },
                    { "value", key.value },
                    { "inTangent", key.inTangent },
                    { "outTangent", key.outTangent }
                });
            }
            return new Dictionary<string, object>
            {
                { "keys", keys },
                { "preWrapMode", curve.preWrapMode.ToString() },
                { "postWrapMode", curve.postWrapMode.ToString() }
            };
        }

        private static Dictionary<string, object> SerializeGradient(Gradient gradient)
        {
            var colorKeys = new List<Dictionary<string, object>>();
            foreach (var key in gradient.colorKeys)
            {
                colorKeys.Add(new Dictionary<string, object>
                {
                    { "time", key.time },
                    { "color", SerializeColor(key.color) }
                });
            }

            var alphaKeys = new List<Dictionary<string, object>>();
            foreach (var key in gradient.alphaKeys)
            {
                alphaKeys.Add(new Dictionary<string, object>
                {
                    { "time", key.time },
                    { "alpha", key.alpha }
                });
            }

            return new Dictionary<string, object>
            {
                { "colorKeys", colorKeys },
                { "alphaKeys", alphaKeys },
                { "mode", gradient.mode.ToString() }
            };
        }

        private static List<string> GetLayerNames(LayerMask mask)
        {
            var layers = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if ((mask.value & (1 << i)) != 0)
                {
                    string layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                        layers.Add(layerName);
                }
            }
            return layers;
        }

        public static string ToJson(object obj, bool prettyPrint = false)
        {
            return JsonSerialize(obj, prettyPrint ? 0 : -1);
        }

        private static string JsonSerialize(object obj, int indent)
        {
            if (obj == null)
                return "null";

            if (obj is bool b)
                return b ? "true" : "false";

            if (obj is string s)
                return EscapeJsonString(s);

            if (obj is int || obj is long || obj is short || obj is byte ||
                obj is uint || obj is ulong || obj is ushort || obj is sbyte)
                return obj.ToString();

            if (obj is float f)
                return float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);

            if (obj is double d)
                return double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

            if (obj is decimal dec)
                return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (obj is IList list)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(",");
                    if (indent >= 0)
                    {
                        sb.AppendLine();
                        sb.Append(new string(' ', (indent + 1) * 2));
                    }
                    sb.Append(JsonSerialize(item, indent >= 0 ? indent + 1 : -1));
                    first = false;
                }
                if (indent >= 0 && list.Count > 0)
                {
                    sb.AppendLine();
                    sb.Append(new string(' ', indent * 2));
                }
                sb.Append("]");
                return sb.ToString();
            }

            if (obj is IDictionary<string, object> dict)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                bool first = true;
                foreach (var kvp in dict)
                {
                    if (!first) sb.Append(",");
                    if (indent >= 0)
                    {
                        sb.AppendLine();
                        sb.Append(new string(' ', (indent + 1) * 2));
                    }
                    sb.Append(EscapeJsonString(kvp.Key));
                    sb.Append(":");
                    if (indent >= 0) sb.Append(" ");
                    sb.Append(JsonSerialize(kvp.Value, indent >= 0 ? indent + 1 : -1));
                    first = false;
                }
                if (indent >= 0 && dict.Count > 0)
                {
                    sb.AppendLine();
                    sb.Append(new string(' ', indent * 2));
                }
                sb.Append("}");
                return sb.ToString();
            }

            return EscapeJsonString(obj.ToString());
        }

        private static string EscapeJsonString(string s)
        {
            if (s == null) return "null";

            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static Dictionary<string, object> ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, object>();

            int index = 0;
            return ParseObject(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>();
            SkipWhitespace(json, ref index);

            if (index >= json.Length || json[index] != '{')
                return result;

            index++; // Skip '{'

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);

                if (index >= json.Length || json[index] == '}')
                {
                    index++;
                    break;
                }

                // Parse key
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);

                if (index >= json.Length || json[index] != ':')
                    break;

                index++; // Skip ':'
                SkipWhitespace(json, ref index);

                // Parse value
                object value = ParseValue(json, ref index);
                result[key] = value;

                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                    index++;
            }

            return result;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);

            if (index >= json.Length)
                return null;

            char c = json[index];

            if (c == '"')
                return ParseString(json, ref index);

            if (c == '{')
                return ParseObject(json, ref index);

            if (c == '[')
                return ParseArray(json, ref index);

            if (c == 't' || c == 'f')
                return ParseBool(json, ref index);

            if (c == 'n')
            {
                index += 4; // "null"
                return null;
            }

            return ParseNumber(json, ref index);
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();
            index++; // Skip '['

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);

                if (index >= json.Length || json[index] == ']')
                {
                    index++;
                    break;
                }

                result.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                    index++;
            }

            return result;
        }

        private static string ParseString(string json, ref int index)
        {
            if (json[index] != '"')
                return "";

            index++; // Skip opening quote
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];

                if (c == '"')
                {
                    index++;
                    break;
                }

                if (c == '\\' && index + 1 < json.Length)
                {
                    index++;
                    char escaped = json[index];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 < json.Length)
                            {
                                string hex = json.Substring(index + 1, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                    sb.Append((char)code);
                                index += 4;
                            }
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }

                index++;
            }

            return sb.ToString();
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index, 4) == "true")
            {
                index += 4;
                return true;
            }
            index += 5; // "false"
            return false;
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            bool isFloat = false;

            while (index < json.Length)
            {
                char c = json[index];
                if (c == '.' || c == 'e' || c == 'E')
                    isFloat = true;
                else if (!char.IsDigit(c) && c != '-' && c != '+')
                    break;
                index++;
            }

            string numStr = json.Substring(start, index - start);

            if (isFloat)
            {
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, out long l))
                    return l < int.MinValue || l > int.MaxValue ? l : (int)l;
            }

            return 0;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }
    }
}
