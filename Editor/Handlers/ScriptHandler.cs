using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityMCP.Utils;

namespace UnityMCP.Handlers
{
    public static class ScriptHandler
    {
        private static List<Dictionary<string, object>> s_CachedScripts;
        private static DateTime s_CacheTime;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        public static Dictionary<string, object> GetScripts(Dictionary<string, object> @params = null)
        {
            int offset = 0;
            int limit = 100;
            string nameFilter = null;
            string namespaceFilter = null;

            if (@params != null)
            {
                if (@params.TryGetValue("offset", out object o))
                    offset = Convert.ToInt32(o);
                if (@params.TryGetValue("limit", out object l))
                    limit = Math.Min(Convert.ToInt32(l), 500);
                if (@params.TryGetValue("nameFilter", out object nf))
                    nameFilter = nf?.ToString();
                if (@params.TryGetValue("namespaceFilter", out object nsf))
                    namespaceFilter = nsf?.ToString();
            }

            // Refresh cache if needed
            if (s_CachedScripts == null || DateTime.Now - s_CacheTime > CacheDuration)
            {
                RefreshScriptCache();
            }

            // Apply filters
            var filtered = s_CachedScripts.AsEnumerable();

            if (!string.IsNullOrEmpty(nameFilter))
            {
                filtered = filtered.Where(s => 
                    s["name"].ToString().Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(namespaceFilter))
            {
                filtered = filtered.Where(s =>
                {
                    var ns = s["namespace"]?.ToString() ?? "";
                    return ns.Contains(namespaceFilter, StringComparison.OrdinalIgnoreCase);
                });
            }

            var filteredList = filtered.ToList();
            var paged = filteredList.Skip(offset).Take(limit).ToList();

            return new Dictionary<string, object>
            {
                { "scripts", paged },
                { "totalCount", filteredList.Count },
                { "offset", offset },
                { "limit", limit },
                { "hasMore", filteredList.Count > offset + limit }
            };
        }

        private static void RefreshScriptCache()
        {
            s_CachedScripts = new List<Dictionary<string, object>>();

            // Find all MonoBehaviour scripts in the project
            var scriptGuids = AssetDatabase.FindAssets("t:MonoScript");

            foreach (var guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                // Skip packages and Unity built-in scripts
                if (path.StartsWith("Packages/") && !path.StartsWith("Packages/com."))
                    continue;

                var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (monoScript == null) continue;

                var scriptClass = monoScript.GetClass();
                
                // Only include MonoBehaviour and ScriptableObject derived classes
                if (scriptClass == null) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(scriptClass) && 
                    !typeof(ScriptableObject).IsAssignableFrom(scriptClass))
                    continue;

                var scriptData = new Dictionary<string, object>
                {
                    { "name", monoScript.name },
                    { "path", path },
                    { "guid", guid },
                    { "namespace", scriptClass.Namespace ?? "" },
                    { "fullName", scriptClass.FullName },
                    { "baseType", scriptClass.BaseType?.Name ?? "" },
                    { "isMonoBehaviour", typeof(MonoBehaviour).IsAssignableFrom(scriptClass) },
                    { "isScriptableObject", typeof(ScriptableObject).IsAssignableFrom(scriptClass) }
                };

                // Get serialized fields
                var fields = GetSerializedFields(scriptClass);
                scriptData["fields"] = fields;

                // Get public methods (excluding inherited Unity methods)
                var methods = GetPublicMethods(scriptClass);
                scriptData["methods"] = methods;

                s_CachedScripts.Add(scriptData);
            }

            s_CacheTime = DateTime.Now;
        }

        private static List<Dictionary<string, object>> GetSerializedFields(Type type)
        {
            var fields = new List<Dictionary<string, object>>();
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in type.GetFields(bindingFlags))
            {
                // Check if field is serialized
                bool isPublic = field.IsPublic;
                bool hasSerializeField = field.GetCustomAttribute<SerializeField>() != null;
                bool hasNonSerialized = field.GetCustomAttribute<NonSerializedAttribute>() != null;
                bool hasHideInInspector = field.GetCustomAttribute<HideInInspector>() != null;

                if (hasNonSerialized) continue;
                if (!isPublic && !hasSerializeField) continue;

                // Skip backing fields
                if (field.Name.Contains("<") || field.Name.Contains(">")) continue;

                var fieldData = new Dictionary<string, object>
                {
                    { "name", field.Name },
                    { "type", GetFriendlyTypeName(field.FieldType) },
                    { "fullType", field.FieldType.FullName },
                    { "isPublic", isPublic },
                    { "hasSerializeField", hasSerializeField },
                    { "hideInInspector", hasHideInInspector }
                };

                // Get tooltip
                var tooltip = field.GetCustomAttribute<TooltipAttribute>();
                if (tooltip != null)
                {
                    fieldData["tooltip"] = tooltip.tooltip;
                }

                // Get range
                var range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    fieldData["range"] = new Dictionary<string, object> { { "min", range.min }, { "max", range.max } };
                }

                // Get header
                var header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null)
                {
                    fieldData["header"] = header.header;
                }

                fields.Add(fieldData);
            }

            return fields;
        }

        private static List<Dictionary<string, object>> GetPublicMethods(Type type)
        {
            var methods = new List<Dictionary<string, object>>();
            var excludedMethods = new HashSet<string>
            {
                "Awake", "Start", "Update", "FixedUpdate", "LateUpdate",
                "OnEnable", "OnDisable", "OnDestroy", "OnValidate",
                "OnGUI", "OnDrawGizmos", "OnDrawGizmosSelected",
                "Reset", "OnApplicationQuit", "OnApplicationPause", "OnApplicationFocus",
                "ToString", "GetHashCode", "Equals", "GetType", "GetInstanceID",
                "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren",
                "GetComponentInParent", "GetComponentsInParent", "CompareTag",
                "SendMessage", "SendMessageUpwards", "BroadcastMessage",
                "Invoke", "InvokeRepeating", "CancelInvoke", "IsInvoking",
                "StartCoroutine", "StopCoroutine", "StopAllCoroutines"
            };

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue; // Skip property accessors
                if (excludedMethods.Contains(method.Name)) continue;

                var parameters = new List<Dictionary<string, object>>();
                foreach (var param in method.GetParameters())
                {
                    parameters.Add(new Dictionary<string, object>
                    {
                        { "name", param.Name },
                        { "type", GetFriendlyTypeName(param.ParameterType) },
                        { "isOptional", param.IsOptional },
                        { "defaultValue", param.HasDefaultValue ? param.DefaultValue?.ToString() : null }
                    });
                }

                methods.Add(new Dictionary<string, object>
                {
                    { "name", method.Name },
                    { "returnType", GetFriendlyTypeName(method.ReturnType) },
                    { "parameters", parameters }
                });
            }

            return methods;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(object)) return "object";

            if (type.IsGenericType)
            {
                string genericName = type.Name.Split('`')[0];
                var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
                return $"{genericName}<{genericArgs}>";
            }

            if (type.IsArray)
            {
                return GetFriendlyTypeName(type.GetElementType()) + "[]";
            }

            return type.Name;
        }

        public static Dictionary<string, object> ReadScript(Dictionary<string, object> @params)
        {
            if (@params == null || !@params.TryGetValue("scriptPath", out object pathObj))
            {
                return new Dictionary<string, object> { { "error", "Missing scriptPath parameter" } };
            }

            string scriptPath = pathObj.ToString();

            // Security check - only allow reading from Assets folder
            if (!scriptPath.StartsWith("Assets/") && !scriptPath.StartsWith("Packages/com."))
            {
                return new Dictionary<string, object> { { "error", "Can only read scripts from Assets or local Packages folder" } };
            }

            // Get full path
            string fullPath = Path.GetFullPath(scriptPath);
            
            if (!File.Exists(fullPath))
            {
                return new Dictionary<string, object> { { "error", $"Script not found: {scriptPath}" } };
            }

            try
            {
                string content = File.ReadAllText(fullPath);
                var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);

                var result = new Dictionary<string, object>
                {
                    { "path", scriptPath },
                    { "content", content },
                    { "lineCount", content.Split('\n').Length },
                    { "size", content.Length }
                };

                if (monoScript != null)
                {
                    result["name"] = monoScript.name;
                    var scriptClass = monoScript.GetClass();
                    if (scriptClass != null)
                    {
                        result["className"] = scriptClass.Name;
                        result["namespace"] = scriptClass.Namespace ?? "";
                        result["fullName"] = scriptClass.FullName;
                    }
                }

                // Parse basic structure
                result["structure"] = ParseScriptStructure(content);

                return result;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { { "error", $"Failed to read script: {ex.Message}" } };
            }
        }

        private static Dictionary<string, object> ParseScriptStructure(string content)
        {
            var structure = new Dictionary<string, object>();

            // Extract using statements
            var usings = new List<string>();
            var usingMatches = Regex.Matches(content, @"using\s+([\w.]+);");
            foreach (Match match in usingMatches)
            {
                usings.Add(match.Groups[1].Value);
            }
            structure["usings"] = usings;

            // Extract namespace
            var namespaceMatch = Regex.Match(content, @"namespace\s+([\w.]+)");
            if (namespaceMatch.Success)
            {
                structure["namespace"] = namespaceMatch.Groups[1].Value;
            }

            // Extract class declarations
            var classes = new List<Dictionary<string, object>>();
            var classMatches = Regex.Matches(content, @"(public|private|internal|protected)?\s*(abstract|sealed|static)?\s*class\s+(\w+)(?:\s*:\s*([\w\s,<>]+))?");
            foreach (Match match in classMatches)
            {
                classes.Add(new Dictionary<string, object>
                {
                    { "name", match.Groups[3].Value },
                    { "modifier", match.Groups[1].Value },
                    { "keyword", match.Groups[2].Value },
                    { "baseTypes", match.Groups[4].Success ? match.Groups[4].Value.Trim() : "" }
                });
            }
            structure["classes"] = classes;

            // Extract method signatures
            var methods = new List<string>();
            var methodMatches = Regex.Matches(content, @"(public|private|protected|internal)\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?([\w<>\[\],\s]+)\s+(\w+)\s*\([^)]*\)");
            foreach (Match match in methodMatches)
            {
                methods.Add($"{match.Groups[2].Value.Trim()} {match.Groups[3].Value}()");
            }
            structure["methodSignatures"] = methods;

            // Extract field declarations
            var fields = new List<string>();
            var fieldMatches = Regex.Matches(content, @"\[SerializeField\]\s*(?:private|protected)?\s*([\w<>\[\]]+)\s+(\w+)\s*[;=]");
            foreach (Match match in fieldMatches)
            {
                fields.Add($"{match.Groups[1].Value} {match.Groups[2].Value}");
            }
            var publicFieldMatches = Regex.Matches(content, @"public\s+([\w<>\[\]]+)\s+(\w+)\s*[;=]");
            foreach (Match match in publicFieldMatches)
            {
                fields.Add($"public {match.Groups[1].Value} {match.Groups[2].Value}");
            }
            structure["serializedFields"] = fields;

            return structure;
        }

        public static void InvalidateCache()
        {
            s_CachedScripts = null;
        }
    }
}
