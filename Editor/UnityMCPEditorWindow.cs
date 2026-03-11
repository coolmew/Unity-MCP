using System;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UnityMCP
{
    public class UnityMCPEditorWindow : EditorWindow
    {
        private Vector2 _logScrollPosition;
        private bool _autoScroll = true;
        private string _mcpConfigJson;
        private int _newPort;

        [MenuItem("Window/Unity MCP")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityMCPEditorWindow>("Unity MCP");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            _newPort = UnityMCPSettings.HttpPort;
            UnityMCPServer.Instance.OnLogUpdated += Repaint;
            GenerateMcpConfig();
        }

        private void OnDisable()
        {
            UnityMCPServer.Instance.OnLogUpdated -= Repaint;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            DrawServerStatus();
            EditorGUILayout.Space(10);

            DrawSettings();
            EditorGUILayout.Space(10);

            DrawMcpConfig();
            EditorGUILayout.Space(10);

            DrawRequestLog();
        }

        private void DrawServerStatus()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Server Status", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // Status indicator
            var statusStyle = new GUIStyle(EditorStyles.label);
            if (UnityMCPServer.Instance.IsRunning)
            {
                statusStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
                EditorGUILayout.LabelField("● Running", statusStyle, GUILayout.Width(80));
                EditorGUILayout.LabelField($"Port: {UnityMCPServer.Instance.HttpPort}");
            }
            else
            {
                statusStyle.normal.textColor = new Color(0.8f, 0.2f, 0.2f);
                EditorGUILayout.LabelField("● Stopped", statusStyle, GUILayout.Width(80));
            }

            GUILayout.FlexibleSpace();

            if (UnityMCPServer.Instance.IsRunning)
            {
                if (GUILayout.Button("Stop Server", GUILayout.Width(100)))
                {
                    UnityMCPServer.Instance.StopServer();
                }
            }
            else
            {
                if (GUILayout.Button("Start Server", GUILayout.Width(100)))
                {
                    UnityMCPServer.Instance.StartServer();
                }
            }

            EditorGUILayout.EndHorizontal();

            // Connection info
            if (UnityMCPServer.Instance.IsRunning)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"HTTP Endpoint: http://localhost:{UnityMCPServer.Instance.HttpPort}/rpc");
                EditorGUILayout.LabelField($"Health Check: http://localhost:{UnityMCPServer.Instance.HttpPort}/health");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            // Auto-start
            EditorGUI.BeginChangeCheck();
            bool autoStart = EditorGUILayout.Toggle("Auto-start on Unity Launch", UnityMCPSettings.AutoStartServer);
            if (EditorGUI.EndChangeCheck())
            {
                UnityMCPSettings.AutoStartServer = autoStart;
            }

            // Port
            EditorGUILayout.BeginHorizontal();
            _newPort = EditorGUILayout.IntField("HTTP Port", _newPort);
            if (_newPort != UnityMCPSettings.HttpPort)
            {
                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    UnityMCPSettings.HttpPort = _newPort;
                    if (UnityMCPServer.Instance.IsRunning)
                    {
                        UnityMCPServer.Instance.StopServer();
                        UnityMCPServer.Instance.StartServer();
                    }
                    GenerateMcpConfig();
                }
            }
            EditorGUILayout.EndHorizontal();

            // Stdio transport
            EditorGUI.BeginChangeCheck();
            bool enableStdio = EditorGUILayout.Toggle("Enable Stdio Transport", UnityMCPSettings.EnableStdioTransport);
            if (EditorGUI.EndChangeCheck())
            {
                UnityMCPSettings.EnableStdioTransport = enableStdio;
            }

            // Editor commands
            EditorGUILayout.Space(5);
            EditorGUI.BeginChangeCheck();
            bool enableCommands = EditorGUILayout.Toggle(
                new GUIContent("Enable Editor Commands", "⚠️ Security Risk: Allows AI agents to execute code in the editor"),
                UnityMCPSettings.EnableEditorCommands);
            if (EditorGUI.EndChangeCheck())
            {
                if (enableCommands)
                {
                    if (EditorUtility.DisplayDialog("Security Warning",
                        "Enabling editor commands allows AI agents to execute code in your Unity Editor.\n\n" +
                        "This could potentially be used to modify your project or system.\n\n" +
                        "Only enable this if you trust the AI agents connecting to this server.",
                        "Enable", "Cancel"))
                    {
                        UnityMCPSettings.EnableEditorCommands = true;
                    }
                }
                else
                {
                    UnityMCPSettings.EnableEditorCommands = false;
                }
            }

            if (UnityMCPSettings.EnableEditorCommands)
            {
                EditorGUILayout.HelpBox("⚠️ Editor commands are enabled. AI agents can execute code.", MessageType.Warning);
            }

            // Mutations
            EditorGUILayout.Space(5);
            EditorGUI.BeginChangeCheck();
            bool enableMutations = EditorGUILayout.Toggle(
                new GUIContent("Enable Mutations", "⚠️ Allows AI agents to modify your Unity project (create/delete/edit GameObjects, components, properties)"),
                UnityMCPSettings.EnableMutations);
            if (EditorGUI.EndChangeCheck())
            {
                if (enableMutations)
                {
                    if (EditorUtility.DisplayDialog("Enable Mutations",
                        "Enabling mutations allows AI agents to modify your Unity project:\n\n" +
                        "• Create/delete/rename GameObjects\n" +
                        "• Add/remove components\n" +
                        "• Modify component properties\n" +
                        "• Move objects in hierarchy\n" +
                        "• Create prefabs and save scenes\n\n" +
                        "All changes support Undo (Ctrl+Z).\n\n" +
                        "Only enable this if you trust the AI agents connecting to this server.",
                        "Enable", "Cancel"))
                    {
                        UnityMCPSettings.EnableMutations = true;
                    }
                }
                else
                {
                    UnityMCPSettings.EnableMutations = false;
                }
            }

            if (UnityMCPSettings.EnableMutations)
            {
                EditorGUILayout.HelpBox("⚠️ Mutations are enabled. AI agents can modify your project (Undo supported).", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMcpConfig()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("MCP Configuration", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox("Copy this configuration to your AI client's MCP settings (e.g., Claude Desktop, Cursor)", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy HTTP Config"))
            {
                GenerateMcpConfig();
                EditorGUIUtility.systemCopyBuffer = _mcpConfigJson;
                Debug.Log("MCP config copied to clipboard");
            }

            if (GUILayout.Button("Copy SSE Config"))
            {
                GenerateSseConfig();
                EditorGUIUtility.systemCopyBuffer = _mcpConfigJson;
                Debug.Log("MCP SSE config copied to clipboard");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Show config preview
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(_mcpConfigJson, GUILayout.Height(100));
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void DrawRequestLog()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Request Log", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            _autoScroll = EditorGUILayout.Toggle("Auto-scroll", _autoScroll, GUILayout.Width(100));

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                UnityMCPServer.Instance.ClearLog();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, GUILayout.ExpandHeight(true));

            var log = UnityMCPServer.Instance.RequestLog;
            foreach (var entry in log)
            {
                DrawLogEntry(entry);
            }

            if (_autoScroll && log.Count > 0)
            {
                _logScrollPosition.y = float.MaxValue;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawLogEntry(LogEntry entry)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = true
            };

            string color = entry.Type switch
            {
                "ERROR" => "#ff6666",
                "REQUEST" => "#66ccff",
                "RESPONSE" => "#66ff66",
                _ => "#cccccc"
            };

            string text = $"<color=#888888>[{entry.Timestamp:HH:mm:ss}]</color> <color={color}>[{entry.Type}]</color> {entry.Message}";
            EditorGUILayout.LabelField(text, style);
        }

        private void GenerateMcpConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"mcpServers\": {");
            sb.AppendLine("    \"unity\": {");
            sb.AppendLine($"      \"url\": \"http://localhost:{UnityMCPSettings.HttpPort}/mcp\",");
            sb.AppendLine("      \"transport\": \"http\"");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            _mcpConfigJson = sb.ToString();
        }

        private void GenerateSseConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"mcpServers\": {");
            sb.AppendLine("    \"unity\": {");
            sb.AppendLine($"      \"url\": \"http://localhost:{UnityMCPSettings.HttpPort}/sse\",");
            sb.AppendLine("      \"transport\": \"sse\"");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            _mcpConfigJson = sb.ToString();
        }
    }
}
