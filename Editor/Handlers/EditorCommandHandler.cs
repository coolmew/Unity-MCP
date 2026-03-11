using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UnityMCP.Handlers
{
    public static class EditorCommandHandler
    {
        // Whitelist of safe namespaces for code execution
        private static readonly HashSet<string> SafeNamespaces = new HashSet<string>
        {
            "UnityEngine",
            "UnityEditor",
            "System",
            "System.Collections",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text"
        };

        // Blacklist of dangerous types/methods
        private static readonly HashSet<string> DangerousPatterns = new HashSet<string>
        {
            "System.IO.File.Delete",
            "System.IO.Directory.Delete",
            "System.IO.File.WriteAllText",
            "System.IO.File.WriteAllBytes",
            "System.Diagnostics.Process",
            "System.Reflection.Assembly.Load",
            "System.AppDomain",
            "System.Runtime",
            "System.Net.WebClient",
            "System.Net.Http",
            "UnityEditor.FileUtil.DeleteFileOrDirectory",
            "UnityEditor.AssetDatabase.DeleteAsset",
            "UnityEditor.BuildPipeline",
            "PlayerPrefs.DeleteAll",
            "PlayerPrefs.DeleteKey"
        };

        private static bool s_CommandsEnabled = false;
        private static readonly List<string> s_LogBuffer = new List<string>();

        public static bool CommandsEnabled
        {
            get => s_CommandsEnabled;
            set => s_CommandsEnabled = value;
        }

        public static Dictionary<string, object> RunEditorCommand(Dictionary<string, object> @params)
        {
            if (!s_CommandsEnabled)
            {
                return new Dictionary<string, object>
                {
                    { "error", "Editor commands are disabled. Enable them in Window > Unity MCP settings." },
                    { "code", -32001 }
                };
            }

            if (@params == null || !@params.TryGetValue("code", out object codeObj))
            {
                return new Dictionary<string, object>
                {
                    { "error", "Missing 'code' parameter" },
                    { "code", -32602 }
                };
            }

            string code = codeObj.ToString();

            // Security check
            var securityResult = ValidateCodeSecurity(code);
            if (securityResult != null)
            {
                return securityResult;
            }

            // Clear log buffer
            s_LogBuffer.Clear();

            // Hook into Unity's log
            Application.logMessageReceived += CaptureLog;

            try
            {
                object result = ExecuteCode(code);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "result", result?.ToString() ?? "null" },
                    { "resultType", result?.GetType().Name ?? "null" },
                    { "logs", new List<string>(s_LogBuffer) }
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", ex.Message },
                    { "errorType", ex.GetType().Name },
                    { "stackTrace", ex.StackTrace },
                    { "logs", new List<string>(s_LogBuffer) }
                };
            }
            finally
            {
                Application.logMessageReceived -= CaptureLog;
            }
        }

        private static void CaptureLog(string condition, string stackTrace, LogType type)
        {
            s_LogBuffer.Add($"[{type}] {condition}");
        }

        private static Dictionary<string, object> ValidateCodeSecurity(string code)
        {
            // Check for dangerous patterns
            foreach (var pattern in DangerousPatterns)
            {
                if (code.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new Dictionary<string, object>
                    {
                        { "error", $"Security violation: Code contains forbidden pattern '{pattern}'" },
                        { "code", -32001 }
                    };
                }
            }

            // Check for potentially dangerous operations
            var dangerousKeywords = new[] { "Process.Start", "Assembly.Load", "Activator.CreateInstance" };
            foreach (var keyword in dangerousKeywords)
            {
                if (code.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new Dictionary<string, object>
                    {
                        { "error", $"Security violation: Code contains potentially dangerous operation '{keyword}'" },
                        { "code", -32001 }
                    };
                }
            }

            return null;
        }

        private static object ExecuteCode(string code)
        {
            // Simple expression evaluator for common operations
            code = code.Trim();

            // Handle simple property/method access on common Unity types
            if (code.StartsWith("Selection."))
            {
                return EvaluateSelectionExpression(code);
            }

            if (code.StartsWith("EditorApplication."))
            {
                return EvaluateEditorApplicationExpression(code);
            }

            if (code.StartsWith("SceneManager.") || code.StartsWith("UnityEngine.SceneManagement.SceneManager."))
            {
                return EvaluateSceneManagerExpression(code);
            }

            if (code.StartsWith("Debug.Log"))
            {
                return EvaluateDebugLog(code);
            }

            if (code.StartsWith("GameObject.Find"))
            {
                return EvaluateGameObjectFind(code);
            }

            if (code.StartsWith("AssetDatabase."))
            {
                return EvaluateAssetDatabaseExpression(code);
            }

            // For more complex expressions, we'd need a proper C# compiler/interpreter
            // For now, return an informative message
            return $"Expression evaluation not supported for: {code}. Supported prefixes: Selection., EditorApplication., SceneManager., Debug.Log, GameObject.Find, AssetDatabase.";
        }

        private static object EvaluateSelectionExpression(string code)
        {
            if (code == "Selection.activeGameObject" || code == "Selection.activeGameObject.name")
            {
                var go = Selection.activeGameObject;
                if (code.EndsWith(".name"))
                    return go?.name ?? "null";
                return go != null ? $"GameObject: {go.name} (ID: {go.GetInstanceID()})" : "null";
            }

            if (code == "Selection.activeObject")
            {
                var obj = Selection.activeObject;
                return obj != null ? $"{obj.GetType().Name}: {obj.name}" : "null";
            }

            if (code == "Selection.gameObjects" || code == "Selection.gameObjects.Length")
            {
                var objects = Selection.gameObjects;
                if (code.EndsWith(".Length"))
                    return objects.Length;
                return $"Selected {objects.Length} GameObjects";
            }

            if (code == "Selection.objects" || code == "Selection.objects.Length")
            {
                var objects = Selection.objects;
                if (code.EndsWith(".Length"))
                    return objects.Length;
                return $"Selected {objects.Length} Objects";
            }

            if (code == "Selection.instanceIDs")
            {
                return string.Join(", ", Selection.instanceIDs);
            }

            return $"Unknown Selection expression: {code}";
        }

        private static object EvaluateEditorApplicationExpression(string code)
        {
            switch (code)
            {
                case "EditorApplication.isPlaying":
                    return EditorApplication.isPlaying;
                case "EditorApplication.isPaused":
                    return EditorApplication.isPaused;
                case "EditorApplication.isCompiling":
                    return EditorApplication.isCompiling;
                case "EditorApplication.isUpdating":
                    return EditorApplication.isUpdating;
                case "EditorApplication.applicationPath":
                    return EditorApplication.applicationPath;
                case "EditorApplication.applicationContentsPath":
                    return EditorApplication.applicationContentsPath;
                case "EditorApplication.timeSinceStartup":
                    return EditorApplication.timeSinceStartup;
                default:
                    return $"Unknown EditorApplication expression: {code}";
            }
        }

        private static object EvaluateSceneManagerExpression(string code)
        {
            code = code.Replace("UnityEngine.SceneManagement.", "");

            switch (code)
            {
                case "SceneManager.sceneCount":
                    return UnityEngine.SceneManagement.SceneManager.sceneCount;
                case "SceneManager.loadedSceneCount":
                    return UnityEngine.SceneManagement.SceneManager.loadedSceneCount;
                default:
                    if (code.StartsWith("SceneManager.GetActiveScene()"))
                    {
                        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                        if (code.EndsWith(".name"))
                            return scene.name;
                        if (code.EndsWith(".path"))
                            return scene.path;
                        return $"Scene: {scene.name} (path: {scene.path})";
                    }
                    return $"Unknown SceneManager expression: {code}";
            }
        }

        private static object EvaluateDebugLog(string code)
        {
            // Extract message from Debug.Log("message")
            int start = code.IndexOf('(');
            int end = code.LastIndexOf(')');
            if (start >= 0 && end > start)
            {
                string message = code.Substring(start + 1, end - start - 1).Trim();
                // Remove quotes if present
                if (message.StartsWith("\"") && message.EndsWith("\""))
                {
                    message = message.Substring(1, message.Length - 2);
                }
                Debug.Log($"[MCP Command] {message}");
                return $"Logged: {message}";
            }
            return "Invalid Debug.Log syntax";
        }

        private static object EvaluateGameObjectFind(string code)
        {
            // Extract name from GameObject.Find("name")
            int start = code.IndexOf('(');
            int end = code.LastIndexOf(')');
            if (start >= 0 && end > start)
            {
                string name = code.Substring(start + 1, end - start - 1).Trim();
                // Remove quotes if present
                if (name.StartsWith("\"") && name.EndsWith("\""))
                {
                    name = name.Substring(1, name.Length - 2);
                }

                var go = GameObject.Find(name);
                if (go != null)
                {
                    return $"Found: {go.name} (ID: {go.GetInstanceID()}, active: {go.activeInHierarchy})";
                }
                return $"GameObject '{name}' not found";
            }
            return "Invalid GameObject.Find syntax";
        }

        private static object EvaluateAssetDatabaseExpression(string code)
        {
            if (code.StartsWith("AssetDatabase.FindAssets"))
            {
                // Extract filter from AssetDatabase.FindAssets("filter")
                int start = code.IndexOf('(');
                int end = code.LastIndexOf(')');
                if (start >= 0 && end > start)
                {
                    string filter = code.Substring(start + 1, end - start - 1).Trim();
                    if (filter.StartsWith("\"") && filter.EndsWith("\""))
                    {
                        filter = filter.Substring(1, filter.Length - 2);
                    }

                    var guids = AssetDatabase.FindAssets(filter);
                    return $"Found {guids.Length} assets matching '{filter}'";
                }
            }

            if (code.StartsWith("AssetDatabase.GetAssetPath"))
            {
                return "AssetDatabase.GetAssetPath requires an object reference";
            }

            if (code == "AssetDatabase.Refresh()")
            {
                AssetDatabase.Refresh();
                return "AssetDatabase refreshed";
            }

            return $"Unknown AssetDatabase expression: {code}";
        }

        public static Dictionary<string, object> GetAvailableCommands()
        {
            return new Dictionary<string, object>
            {
                { "enabled", s_CommandsEnabled },
                { "commands", new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            { "category", "Selection" },
                            { "expressions", new List<string>
                                {
                                    "Selection.activeGameObject",
                                    "Selection.activeGameObject.name",
                                    "Selection.activeObject",
                                    "Selection.gameObjects",
                                    "Selection.gameObjects.Length",
                                    "Selection.objects",
                                    "Selection.objects.Length",
                                    "Selection.instanceIDs"
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "category", "EditorApplication" },
                            { "expressions", new List<string>
                                {
                                    "EditorApplication.isPlaying",
                                    "EditorApplication.isPaused",
                                    "EditorApplication.isCompiling",
                                    "EditorApplication.isUpdating",
                                    "EditorApplication.applicationPath",
                                    "EditorApplication.timeSinceStartup"
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "category", "SceneManager" },
                            { "expressions", new List<string>
                                {
                                    "SceneManager.sceneCount",
                                    "SceneManager.loadedSceneCount",
                                    "SceneManager.GetActiveScene().name",
                                    "SceneManager.GetActiveScene().path"
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "category", "Debug" },
                            { "expressions", new List<string>
                                {
                                    "Debug.Log(\"message\")"
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "category", "GameObject" },
                            { "expressions", new List<string>
                                {
                                    "GameObject.Find(\"name\")"
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "category", "AssetDatabase" },
                            { "expressions", new List<string>
                                {
                                    "AssetDatabase.FindAssets(\"filter\")",
                                    "AssetDatabase.Refresh()"
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
