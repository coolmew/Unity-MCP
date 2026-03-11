using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityMCP.Transport;
using UnityMCP.Handlers;
using UnityMCP.Utils;

namespace UnityMCP
{
    [InitializeOnLoad]
    public static class UnityMCPServerBootstrap
    {
        static UnityMCPServerBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                if (UnityMCPSettings.AutoStartServer)
                {
                    UnityMCPServer.Instance.StartServer();
                }
            };
        }
    }

    public class UnityMCPServer
    {
        private static UnityMCPServer s_Instance;
        public static UnityMCPServer Instance => s_Instance ??= new UnityMCPServer();

        private HttpTransport _httpTransport;
        private StdioTransport _stdioTransport;
        private bool _isRunning;
        private readonly List<LogEntry> _requestLog = new List<LogEntry>();
        private const int MaxLogEntries = 100;

        public bool IsRunning => _isRunning;
        public int HttpPort => _httpTransport?.Port ?? UnityMCPSettings.HttpPort;
        public IReadOnlyList<LogEntry> RequestLog => _requestLog;

        public event Action OnServerStarted;
        public event Action OnServerStopped;
        public event Action OnLogUpdated;

        private UnityMCPServer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened += (scene, mode) => HierarchyHandler.InvalidateCache();
            EditorSceneManager.sceneClosed += (scene) => HierarchyHandler.InvalidateCache();
            
            // Initialize mutation setting
            MutationHandler.MutationsEnabled = UnityMCPSettings.EnableMutations;
        }

        public void StartServer()
        {
            if (_isRunning) return;

            try
            {
                // Start HTTP transport
                _httpTransport = new HttpTransport();
                _httpTransport.OnMessageReceived += HandleHttpMessage;
                _httpTransport.OnError += (err) => AddLog("ERROR", err);
                _httpTransport.OnLog += (msg) => AddLog("INFO", msg);
                _httpTransport.Start(UnityMCPSettings.HttpPort);

                // Optionally start stdio transport
                if (UnityMCPSettings.EnableStdioTransport)
                {
                    _stdioTransport = new StdioTransport();
                    _stdioTransport.OnMessageReceived += HandleStdioMessage;
                    _stdioTransport.OnError += (err) => AddLog("ERROR", err);
                    _stdioTransport.Start();
                }

                _isRunning = true;
                AddLog("INFO", $"Unity MCP Server started on port {_httpTransport.Port}");
                OnServerStarted?.Invoke();
            }
            catch (Exception ex)
            {
                AddLog("ERROR", $"Failed to start server: {ex.Message}");
                StopServer();
            }
        }

        public void StopServer()
        {
            _httpTransport?.Stop();
            _httpTransport?.Dispose();
            _httpTransport = null;

            _stdioTransport?.Stop();
            _stdioTransport?.Dispose();
            _stdioTransport = null;

            _isRunning = false;
            AddLog("INFO", "Unity MCP Server stopped");
            OnServerStopped?.Invoke();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    AddLog("INFO", "Entering Play Mode - caches invalidated");
                    HierarchyHandler.InvalidateCache();
                    ScriptHandler.InvalidateCache();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    AddLog("INFO", "Exited Play Mode - caches invalidated");
                    HierarchyHandler.InvalidateCache();
                    ScriptHandler.InvalidateCache();
                    break;
            }
        }

        private void HandleHttpMessage(string message, Action<string> respond)
        {
            AddLog("REQUEST", message);
            string response = ProcessMessage(message);
            AddLog("RESPONSE", response);
            respond(response);
        }

        private void HandleStdioMessage(string message)
        {
            AddLog("REQUEST", message);
            string response = ProcessMessage(message);
            AddLog("RESPONSE", response);
            _stdioTransport?.SendMessage(response);
        }

        private string ProcessMessage(string message)
        {
            try
            {
                var request = JsonRpcMessage.Parse(message);

                // Handle MCP initialization
                if (request.method == "initialize")
                {
                    return HandleInitialize(request);
                }

                // Handle tools/list
                if (request.method == "tools/list")
                {
                    return HandleToolsList(request);
                }

                // Handle tools/call
                if (request.method == "tools/call")
                {
                    return HandleToolsCall(request);
                }

                // Handle resources/list
                if (request.method == "resources/list")
                {
                    return HandleResourcesList(request);
                }

                // Handle legacy direct method calls (unity/*)
                if (request.method?.StartsWith("unity/") == true)
                {
                    return HandleUnityMethod(request);
                }

                // Method not found
                return JsonRpcMessage.CreateError(
                    request.id,
                    JsonRpcError.MethodNotFound,
                    $"Method not found: {request.method}"
                ).ToJson();
            }
            catch (Exception ex)
            {
                return JsonRpcMessage.CreateError(
                    null,
                    JsonRpcError.InternalError,
                    ex.Message,
                    new Dictionary<string, object> { { "stackTrace", ex.StackTrace } }
                ).ToJson();
            }
        }

        private string HandleInitialize(JsonRpcMessage request)
        {
            var result = new Dictionary<string, object>
            {
                { "protocolVersion", "2024-11-05" },
                { "capabilities", new Dictionary<string, object>
                    {
                        { "tools", new Dictionary<string, object> { { "listChanged", true } } },
                        { "resources", new Dictionary<string, object> { { "subscribe", false }, { "listChanged", true } } }
                    }
                },
                { "serverInfo", new Dictionary<string, object>
                    {
                        { "name", "unity-mcp" },
                        { "version", "1.0.0" }
                    }
                }
            };

            return JsonRpcMessage.CreateResponse(request.id, result).ToJson();
        }

        private string HandleToolsList(JsonRpcMessage request)
        {
            var tools = GetToolDefinitions();
            var result = new Dictionary<string, object> { { "tools", tools } };
            return JsonRpcMessage.CreateResponse(request.id, result).ToJson();
        }

        private string HandleToolsCall(JsonRpcMessage request)
        {
            if (request.@params == null || !request.@params.TryGetValue("name", out object nameObj))
            {
                return JsonRpcMessage.CreateError(request.id, JsonRpcError.InvalidParams, "Missing tool name").ToJson();
            }

            string toolName = nameObj.ToString();
            Dictionary<string, object> arguments = null;

            if (request.@params.TryGetValue("arguments", out object argsObj) && argsObj is Dictionary<string, object> args)
            {
                arguments = args;
            }

            var toolResult = ExecuteTool(toolName, arguments);

            var result = new Dictionary<string, object>
            {
                { "content", new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            { "type", "text" },
                            { "text", SerializationHelper.ToJson(toolResult, true) }
                        }
                    }
                }
            };

            return JsonRpcMessage.CreateResponse(request.id, result).ToJson();
        }

        private string HandleResourcesList(JsonRpcMessage request)
        {
            var resources = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "uri", "unity://project/info" },
                    { "name", "Project Info" },
                    { "description", "Basic Unity project information" },
                    { "mimeType", "application/json" }
                }
            };

            var result = new Dictionary<string, object> { { "resources", resources } };
            return JsonRpcMessage.CreateResponse(request.id, result).ToJson();
        }

        private string HandleUnityMethod(JsonRpcMessage request)
        {
            string methodName = request.method.Substring("unity/".Length);
            var result = ExecuteTool(methodName, request.@params);
            return JsonRpcMessage.CreateResponse(request.id, result).ToJson();
        }

        private Dictionary<string, object> ExecuteTool(string toolName, Dictionary<string, object> arguments)
        {
            try
            {
                switch (toolName.ToLowerInvariant())
                {
                    case "getscenehierarchy":
                        return HierarchyHandler.GetSceneHierarchy(arguments);

                    case "getgameobject":
                        return HierarchyHandler.GetGameObject(arguments);

                    case "findgameobjects":
                        return HierarchyHandler.FindGameObjects(arguments);

                    case "getcomponent":
                        return ComponentHandler.GetComponent(arguments);

                    case "getscripts":
                        return ScriptHandler.GetScripts(arguments);

                    case "readscript":
                        return ScriptHandler.ReadScript(arguments);

                    case "getprojectsettings":
                        return ProjectSettingsHandler.GetProjectSettings(arguments);

                    case "getassets":
                        return AssetHandler.GetAssets(arguments);

                    case "getpackages":
                        return AssetHandler.GetPackages(arguments);

                    case "getassetdependencies":
                        return AssetHandler.GetAssetDependencies(arguments);

                    case "getfolderstructure":
                        return AssetHandler.GetFolderStructure(arguments);

                    case "runeditorcommand":
                        return EditorCommandHandler.RunEditorCommand(arguments);

                    case "getavailablecommands":
                        return EditorCommandHandler.GetAvailableCommands();

                    case "getprojectinfo":
                        return GetProjectInfo();

                    // Mutation tools
                    case "creategameobject":
                        return MutationHandler.CreateGameObject(arguments);

                    case "deletegameobject":
                        return MutationHandler.DeleteGameObject(arguments);

                    case "renamegameobject":
                        return MutationHandler.RenameGameObject(arguments);

                    case "movegameobject":
                        return MutationHandler.MoveGameObject(arguments);

                    case "duplicategameobject":
                        return MutationHandler.DuplicateGameObject(arguments);

                    case "setgameobjectactive":
                        return MutationHandler.SetGameObjectActive(arguments);

                    case "settransform":
                        return MutationHandler.SetTransform(arguments);

                    case "addcomponent":
                        return MutationHandler.AddComponent(arguments);

                    case "removecomponent":
                        return MutationHandler.RemoveComponent(arguments);

                    case "setcomponentenabled":
                        return MutationHandler.SetComponentEnabled(arguments);

                    case "setcomponentproperty":
                        return MutationHandler.SetComponentProperty(arguments);

                    case "setmultipleproperties":
                        return MutationHandler.SetMultipleProperties(arguments);

                    case "savescene":
                        return MutationHandler.SaveScene(arguments);

                    case "createprefab":
                        return MutationHandler.CreatePrefab(arguments);

                    case "instantiateprefab":
                        return MutationHandler.InstantiatePrefab(arguments);

                    case "assignobjectreference":
                        return MutationHandler.AssignObjectReference(arguments);

                    // Animation tools
                    case "createanimationclip":
                        return AnimationHandler.CreateAnimationClip(arguments);

                    case "getanimationclipinfo":
                        return AnimationHandler.GetAnimationClipInfo(arguments);

                    case "setanimationcurve":
                        return AnimationHandler.SetAnimationCurve(arguments);

                    case "removeanimationcurve":
                        return AnimationHandler.RemoveAnimationCurve(arguments);

                    case "addanimationkeyframe":
                        return AnimationHandler.AddAnimationKeyframe(arguments);

                    case "addanimationevent":
                        return AnimationHandler.AddAnimationEvent(arguments);

                    case "setclipsettings":
                        return AnimationHandler.SetClipSettings(arguments);

                    case "createanimatorcontroller":
                        return AnimationHandler.CreateAnimatorController(arguments);

                    case "getanimatorcontrollerinfo":
                        return AnimationHandler.GetAnimatorControllerInfo(arguments);

                    case "addanimatorparameter":
                        return AnimationHandler.AddAnimatorParameter(arguments);

                    case "addanimatorstate":
                        return AnimationHandler.AddAnimatorState(arguments);

                    case "addanimatortransition":
                        return AnimationHandler.AddAnimatorTransition(arguments);

                    case "addanimatorlayer":
                        return AnimationHandler.AddAnimatorLayer(arguments);

                    case "assignanimator":
                        return AnimationHandler.AssignAnimator(arguments);

                    default:
                        return new Dictionary<string, object>
                        {
                            { "error", $"Unknown tool: {toolName}" },
                            { "code", JsonRpcError.MethodNotFound }
                        };
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "error", ex.Message },
                    { "code", JsonRpcError.UnityRuntimeError },
                    { "stackTrace", ex.StackTrace }
                };
            }
        }

        private Dictionary<string, object> GetProjectInfo()
        {
            return new Dictionary<string, object>
            {
                { "productName", Application.productName },
                { "companyName", Application.companyName },
                { "version", Application.version },
                { "unityVersion", Application.unityVersion },
                { "platform", Application.platform.ToString() },
                { "dataPath", Application.dataPath },
                { "persistentDataPath", Application.persistentDataPath },
                { "isPlaying", Application.isPlaying },
                { "isEditor", Application.isEditor },
                { "buildGUID", Application.buildGUID },
                { "systemLanguage", Application.systemLanguage.ToString() }
            };
        }

        private List<Dictionary<string, object>> GetToolDefinitions()
        {
            return new List<Dictionary<string, object>>
            {
                CreateToolDefinition("getSceneHierarchy",
                    "Returns the full scene hierarchy with all GameObjects, their names, instance IDs, tags, layers, active state, and children",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "maxDepth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Maximum depth to traverse (default: 8)" } } },
                                { "includeInactive", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Include inactive GameObjects (default: true)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("getGameObject",
                    "Returns detailed information about a specific GameObject including transform, components, and children",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "Instance ID of the GameObject" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Hierarchy path to the GameObject (e.g., /Canvas/Panel/Button)" } } },
                                { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "Name of the GameObject to find" } } }
                            }
                        }
                    }),

                CreateToolDefinition("findGameObjects",
                    "Searches for GameObjects matching specified filters",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "nameFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by name (partial match)" } } },
                                { "tagFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by tag (exact match)" } } },
                                { "componentFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by component type name" } } },
                                { "layerFilter", new Dictionary<string, object> { { "type", "integer" }, { "description", "Filter by layer index" } } },
                                { "offset", new Dictionary<string, object> { { "type", "integer" }, { "description", "Pagination offset" } } },
                                { "limit", new Dictionary<string, object> { { "type", "integer" }, { "description", "Maximum results to return (default: 100, max: 500)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("getComponent",
                    "Returns all serialized fields of a specific component with their values, types, and tooltips",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "Instance ID of the GameObject" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Type name of the component" } } }
                            }
                        },
                        { "required", new List<string> { "instanceID", "componentType" } }
                    }),

                CreateToolDefinition("getScripts",
                    "Returns a list of all MonoBehaviour and ScriptableObject scripts in the project with their fields and methods",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "nameFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by script name" } } },
                                { "namespaceFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by namespace" } } },
                                { "offset", new Dictionary<string, object> { { "type", "integer" }, { "description", "Pagination offset" } } },
                                { "limit", new Dictionary<string, object> { { "type", "integer" }, { "description", "Maximum results (default: 100)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("readScript",
                    "Returns the full source code of a script file",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "scriptPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the script (e.g., Assets/Scripts/Player.cs)" } } }
                            }
                        },
                        { "required", new List<string> { "scriptPath" } }
                    }),

                CreateToolDefinition("getProjectSettings",
                    "Returns Unity project settings including PlayerSettings, QualitySettings, Physics, Tags/Layers, Input, Graphics, and Build settings",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "sections", new Dictionary<string, object>
                                    {
                                        { "type", "array" },
                                        { "items", new Dictionary<string, object> { { "type", "string" } } },
                                        { "description", "Specific sections to include: player, quality, physics, physics2d, tags, layers, input, graphics, build, time, audio" }
                                    }
                                }
                            }
                        }
                    }),

                CreateToolDefinition("getAssets",
                    "Searches for assets in the project by type, folder, or name",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "type", new Dictionary<string, object> { { "type", "string" }, { "description", "Asset type filter (e.g., Texture2D, Material, Prefab, AudioClip)" } } },
                                { "folder", new Dictionary<string, object> { { "type", "string" }, { "description", "Folder path to search in (e.g., Assets/Textures)" } } },
                                { "nameFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by asset name" } } },
                                { "labelFilter", new Dictionary<string, object> { { "type", "string" }, { "description", "Filter by asset label" } } },
                                { "offset", new Dictionary<string, object> { { "type", "integer" }, { "description", "Pagination offset" } } },
                                { "limit", new Dictionary<string, object> { { "type", "integer" }, { "description", "Maximum results (default: 100)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("getPackages",
                    "Returns all installed UPM packages with their names, versions, and sources",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>() }
                    }),

                CreateToolDefinition("getAssetDependencies",
                    "Returns dependencies of a specific asset",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Asset path" } } },
                                { "recursive", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Include recursive dependencies (default: false)" } } }
                            }
                        },
                        { "required", new List<string> { "path" } }
                    }),

                CreateToolDefinition("getFolderStructure",
                    "Returns the folder structure of the Assets directory",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Root path (default: Assets)" } } },
                                { "maxDepth", new Dictionary<string, object> { { "type", "integer" }, { "description", "Maximum depth (default: 3)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("runEditorCommand",
                    "Executes a C# expression in the Unity Editor (requires explicit opt-in in settings)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "code", new Dictionary<string, object> { { "type", "string" }, { "description", "C# expression to execute" } } }
                            }
                        },
                        { "required", new List<string> { "code" } }
                    }),

                CreateToolDefinition("getAvailableCommands",
                    "Returns list of available editor commands that can be executed",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>() }
                    }),

                CreateToolDefinition("getProjectInfo",
                    "Returns basic Unity project information",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>() }
                    }),

                // Mutation tools
                CreateToolDefinition("createGameObject",
                    "Creates a new GameObject in the scene (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "Name of the GameObject" } } },
                                { "parentInstanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "Parent GameObject instance ID" } } },
                                { "parentPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Parent GameObject hierarchy path" } } },
                                { "position", new Dictionary<string, object> { { "type", "object" }, { "description", "Local position {x, y, z}" } } },
                                { "rotation", new Dictionary<string, object> { { "type", "object" }, { "description", "Local euler rotation {x, y, z}" } } },
                                { "scale", new Dictionary<string, object> { { "type", "object" }, { "description", "Local scale {x, y, z}" } } },
                                { "tag", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject tag" } } },
                                { "layer", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject layer" } } },
                                { "primitive", new Dictionary<string, object> { { "type", "string" }, { "description", "Primitive type: cube, sphere, capsule, cylinder, plane, quad" } } }
                            }
                        }
                    }),

                CreateToolDefinition("deleteGameObject",
                    "Deletes a GameObject from the scene (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject name" } } }
                            }
                        }
                    }),

                CreateToolDefinition("renameGameObject",
                    "Renames a GameObject (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "newName", new Dictionary<string, object> { { "type", "string" }, { "description", "New name for the GameObject" } } }
                            }
                        },
                        { "required", new List<string> { "newName" } }
                    }),

                CreateToolDefinition("moveGameObject",
                    "Moves a GameObject to a new parent or changes sibling index (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "newParentInstanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "New parent instance ID (null for root)" } } },
                                { "newParentPath", new Dictionary<string, object> { { "type", "string" }, { "description", "New parent hierarchy path" } } },
                                { "siblingIndex", new Dictionary<string, object> { { "type", "integer" }, { "description", "Sibling index position" } } }
                            }
                        }
                    }),

                CreateToolDefinition("duplicateGameObject",
                    "Duplicates a GameObject (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "newName", new Dictionary<string, object> { { "type", "string" }, { "description", "Name for the duplicate" } } }
                            }
                        }
                    }),

                CreateToolDefinition("setGameObjectActive",
                    "Sets a GameObject's active state (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "active", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Active state" } } }
                            }
                        },
                        { "required", new List<string> { "active" } }
                    }),

                CreateToolDefinition("setTransform",
                    "Sets a GameObject's transform (position, rotation, scale) (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "position", new Dictionary<string, object> { { "type", "object" }, { "description", "Position {x, y, z}" } } },
                                { "rotation", new Dictionary<string, object> { { "type", "object" }, { "description", "Euler rotation {x, y, z}" } } },
                                { "scale", new Dictionary<string, object> { { "type", "object" }, { "description", "Scale {x, y, z}" } } },
                                { "local", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Use local space (default: true)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("addComponent",
                    "Adds a component to a GameObject (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type name (e.g., Rigidbody, BoxCollider)" } } }
                            }
                        },
                        { "required", new List<string> { "componentType" } }
                    }),

                CreateToolDefinition("removeComponent",
                    "Removes a component from a GameObject (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type name" } } }
                            }
                        },
                        { "required", new List<string> { "componentType" } }
                    }),

                CreateToolDefinition("setComponentEnabled",
                    "Enables or disables a component (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type name" } } },
                                { "enabled", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Enabled state" } } }
                            }
                        },
                        { "required", new List<string> { "componentType", "enabled" } }
                    }),

                CreateToolDefinition("setComponentProperty",
                    "Sets a single property/field value on a component (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type name" } } },
                                { "propertyName", new Dictionary<string, object> { { "type", "string" }, { "description", "Property/field name (e.g., m_Mass for Rigidbody)" } } },
                                { "value", new Dictionary<string, object> { { "description", "New value (type depends on property)" } } }
                            }
                        },
                        { "required", new List<string> { "componentType", "propertyName", "value" } }
                    }),

                CreateToolDefinition("setMultipleProperties",
                    "Sets multiple properties on a component at once (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type name" } } },
                                { "properties", new Dictionary<string, object> { { "type", "object" }, { "description", "Dictionary of property names to values" } } }
                            }
                        },
                        { "required", new List<string> { "componentType", "properties" } }
                    }),

                CreateToolDefinition("saveScene",
                    "Saves the current or specified scene (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "scenePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Scene path (optional, saves active scene if not specified)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("createPrefab",
                    "Creates a prefab from a GameObject (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "Prefab save path (e.g., Assets/Prefabs/MyPrefab.prefab)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("instantiatePrefab",
                    "Instantiates a prefab into the scene (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "prefabPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the prefab asset" } } },
                                { "parentInstanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "Parent GameObject instance ID" } } },
                                { "position", new Dictionary<string, object> { { "type", "object" }, { "description", "World position {x, y, z}" } } }
                            }
                        },
                        { "required", new List<string> { "prefabPath" } }
                    }),

                // Object reference assignment (drag & drop equivalent)
                CreateToolDefinition("assignObjectReference",
                    "Assigns a scene object (GameObject, Transform, Component) or asset to a component's serialized field. This is the programmatic equivalent of drag-and-drop in the Inspector. (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "targetInstanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "Target GameObject instance ID (the one with the field to assign to)" } } },
                                { "targetPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Target GameObject hierarchy path" } } },
                                { "targetName", new Dictionary<string, object> { { "type", "string" }, { "description", "Target GameObject name" } } },
                                { "componentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type on target that contains the field" } } },
                                { "propertyName", new Dictionary<string, object> { { "type", "string" }, { "description", "Serialized field name to assign to" } } },
                                { "sourceInstanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "Source object instance ID to assign" } } },
                                { "sourcePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Source GameObject hierarchy path" } } },
                                { "sourceName", new Dictionary<string, object> { { "type", "string" }, { "description", "Source GameObject name" } } },
                                { "sourceComponentType", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type to get from source (e.g., Transform, Rigidbody). Omit to assign the GameObject itself." } } },
                                { "assetPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Asset path to assign (e.g., Assets/Materials/MyMat.mat)" } } },
                                { "clear", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Set to true to clear/null the reference" } } }
                            }
                        },
                        { "required", new List<string> { "componentType", "propertyName" } }
                    }),

                // Animation clip tools
                CreateToolDefinition("createAnimationClip",
                    "Creates a new AnimationClip asset (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "Clip name" } } },
                                { "savePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Save path (e.g., Assets/Animations/Walk.anim)" } } },
                                { "loop", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Loop the animation" } } },
                                { "frameRate", new Dictionary<string, object> { { "type", "number" }, { "description", "Frame rate (default: 60)" } } },
                                { "wrapMode", new Dictionary<string, object> { { "type", "string" }, { "description", "Wrap mode: loop, pingpong, clampforever, once" } } }
                            }
                        }
                    }),

                CreateToolDefinition("getAnimationClipInfo",
                    "Returns full info about an animation clip including all curves, keyframes, and events",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .anim asset" } } }
                            }
                        },
                        { "required", new List<string> { "clipPath" } }
                    }),

                CreateToolDefinition("setAnimationCurve",
                    "Sets/replaces an entire animation curve with keyframes on a clip (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .anim asset" } } },
                                { "propertyName", new Dictionary<string, object> { { "type", "string" }, { "description", "Property name (e.g., localPosition.x, m_LocalScale.y)" } } },
                                { "type", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type (e.g., Transform, SpriteRenderer)" } } },
                                { "relativePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Relative path to child (empty for root)" } } },
                                { "keyframes", new Dictionary<string, object> { { "type", "array" }, { "description", "Array of {time, value, inTangent?, outTangent?}" } } }
                            }
                        },
                        { "required", new List<string> { "clipPath", "propertyName", "type", "keyframes" } }
                    }),

                CreateToolDefinition("removeAnimationCurve",
                    "Removes an animation curve from a clip (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .anim asset" } } },
                                { "propertyName", new Dictionary<string, object> { { "type", "string" }, { "description", "Property name to remove" } } },
                                { "type", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type" } } },
                                { "relativePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Relative path to child" } } }
                            }
                        },
                        { "required", new List<string> { "clipPath", "propertyName", "type" } }
                    }),

                CreateToolDefinition("addAnimationKeyframe",
                    "Adds a single keyframe to an existing or new curve on a clip (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .anim asset" } } },
                                { "propertyName", new Dictionary<string, object> { { "type", "string" }, { "description", "Property name" } } },
                                { "type", new Dictionary<string, object> { { "type", "string" }, { "description", "Component type" } } },
                                { "time", new Dictionary<string, object> { { "type", "number" }, { "description", "Keyframe time in seconds" } } },
                                { "value", new Dictionary<string, object> { { "type", "number" }, { "description", "Keyframe value" } } },
                                { "relativePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Relative path to child" } } },
                                { "inTangent", new Dictionary<string, object> { { "type", "number" }, { "description", "In tangent" } } },
                                { "outTangent", new Dictionary<string, object> { { "type", "number" }, { "description", "Out tangent" } } }
                            }
                        },
                        { "required", new List<string> { "clipPath", "propertyName", "type", "time", "value" } }
                    }),

                CreateToolDefinition("addAnimationEvent",
                    "Adds an animation event that calls a function at a specific time (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .anim asset" } } },
                                { "functionName", new Dictionary<string, object> { { "type", "string" }, { "description", "Function to call" } } },
                                { "time", new Dictionary<string, object> { { "type", "number" }, { "description", "Event time in seconds" } } },
                                { "intParameter", new Dictionary<string, object> { { "type", "integer" }, { "description", "Int parameter" } } },
                                { "floatParameter", new Dictionary<string, object> { { "type", "number" }, { "description", "Float parameter" } } },
                                { "stringParameter", new Dictionary<string, object> { { "type", "string" }, { "description", "String parameter" } } }
                            }
                        },
                        { "required", new List<string> { "clipPath", "functionName", "time" } }
                    }),

                CreateToolDefinition("setClipSettings",
                    "Modifies animation clip settings like loop, frame rate, start/stop time (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .anim asset" } } },
                                { "loopTime", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Enable looping" } } },
                                { "loopBlend", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Loop pose" } } },
                                { "cycleOffset", new Dictionary<string, object> { { "type", "number" }, { "description", "Cycle offset" } } },
                                { "frameRate", new Dictionary<string, object> { { "type", "number" }, { "description", "Frame rate" } } },
                                { "startTime", new Dictionary<string, object> { { "type", "number" }, { "description", "Start time" } } },
                                { "stopTime", new Dictionary<string, object> { { "type", "number" }, { "description", "Stop time" } } }
                            }
                        },
                        { "required", new List<string> { "clipPath" } }
                    }),

                // Animator controller tools
                CreateToolDefinition("createAnimatorController",
                    "Creates a new Animator Controller asset (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "Controller name" } } },
                                { "savePath", new Dictionary<string, object> { { "type", "string" }, { "description", "Save path (e.g., Assets/Animations/Player.controller)" } } }
                            }
                        }
                    }),

                CreateToolDefinition("getAnimatorControllerInfo",
                    "Returns full info about an animator controller including layers, states, parameters, and transitions",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "controllerPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .controller asset" } } }
                            }
                        },
                        { "required", new List<string> { "controllerPath" } }
                    }),

                CreateToolDefinition("addAnimatorParameter",
                    "Adds a parameter to an animator controller (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "controllerPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .controller asset" } } },
                                { "parameterName", new Dictionary<string, object> { { "type", "string" }, { "description", "Parameter name" } } },
                                { "parameterType", new Dictionary<string, object> { { "type", "string" }, { "description", "Type: float, int, bool, trigger" } } },
                                { "defaultValue", new Dictionary<string, object> { { "description", "Default value for the parameter" } } }
                            }
                        },
                        { "required", new List<string> { "controllerPath", "parameterName" } }
                    }),

                CreateToolDefinition("addAnimatorState",
                    "Adds a state to an animator controller layer (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "controllerPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .controller asset" } } },
                                { "stateName", new Dictionary<string, object> { { "type", "string" }, { "description", "State name" } } },
                                { "layerIndex", new Dictionary<string, object> { { "type", "integer" }, { "description", "Layer index (default: 0)" } } },
                                { "clipPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Animation clip to assign as motion" } } },
                                { "speed", new Dictionary<string, object> { { "type", "number" }, { "description", "Playback speed (default: 1)" } } },
                                { "tag", new Dictionary<string, object> { { "type", "string" }, { "description", "State tag" } } },
                                { "isDefault", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Set as default state" } } }
                            }
                        },
                        { "required", new List<string> { "controllerPath", "stateName" } }
                    }),

                CreateToolDefinition("addAnimatorTransition",
                    "Adds a transition between two states with optional conditions (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "controllerPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .controller asset" } } },
                                { "sourceState", new Dictionary<string, object> { { "type", "string" }, { "description", "Source state name" } } },
                                { "destinationState", new Dictionary<string, object> { { "type", "string" }, { "description", "Destination state name" } } },
                                { "layerIndex", new Dictionary<string, object> { { "type", "integer" }, { "description", "Layer index (default: 0)" } } },
                                { "hasExitTime", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Has exit time" } } },
                                { "exitTime", new Dictionary<string, object> { { "type", "number" }, { "description", "Exit time (0-1)" } } },
                                { "duration", new Dictionary<string, object> { { "type", "number" }, { "description", "Transition duration" } } },
                                { "conditions", new Dictionary<string, object> { { "type", "array" }, { "description", "Array of {parameter, mode, threshold}. mode: greater/less/equals/notequal/if/ifnot" } } }
                            }
                        },
                        { "required", new List<string> { "controllerPath", "sourceState", "destinationState" } }
                    }),

                CreateToolDefinition("addAnimatorLayer",
                    "Adds a new layer to an animator controller (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "controllerPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .controller asset" } } },
                                { "layerName", new Dictionary<string, object> { { "type", "string" }, { "description", "Layer name" } } },
                                { "defaultWeight", new Dictionary<string, object> { { "type", "number" }, { "description", "Default weight (0-1)" } } }
                            }
                        },
                        { "required", new List<string> { "controllerPath", "layerName" } }
                    }),

                CreateToolDefinition("assignAnimator",
                    "Assigns an Animator Controller to a GameObject's Animator component (adds Animator if missing) (requires mutations enabled)",
                    new Dictionary<string, object>
                    {
                        { "type", "object" },
                        { "properties", new Dictionary<string, object>
                            {
                                { "instanceID", new Dictionary<string, object> { { "type", "integer" }, { "description", "GameObject instance ID" } } },
                                { "path", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject hierarchy path" } } },
                                { "name", new Dictionary<string, object> { { "type", "string" }, { "description", "GameObject name" } } },
                                { "controllerPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to the .controller asset" } } },
                                { "avatarPath", new Dictionary<string, object> { { "type", "string" }, { "description", "Path to avatar asset" } } },
                                { "applyRootMotion", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Apply root motion" } } }
                            }
                        }
                    })
            };
        }

        private Dictionary<string, object> CreateToolDefinition(string name, string description, Dictionary<string, object> inputSchema)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "description", description },
                { "inputSchema", inputSchema }
            };
        }

        private void AddLog(string type, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Type = type,
                Message = message.Length > 1000 ? message.Substring(0, 1000) + "..." : message
            };

            _requestLog.Add(entry);

            while (_requestLog.Count > MaxLogEntries)
            {
                _requestLog.RemoveAt(0);
            }

            OnLogUpdated?.Invoke();
        }

        public void ClearLog()
        {
            _requestLog.Clear();
            OnLogUpdated?.Invoke();
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp;
        public string Type;
        public string Message;
    }

    public static class UnityMCPSettings
    {
        private const string AutoStartKey = "UnityMCP_AutoStart";
        private const string HttpPortKey = "UnityMCP_HttpPort";
        private const string EnableStdioKey = "UnityMCP_EnableStdio";
        private const string EnableCommandsKey = "UnityMCP_EnableCommands";

        public static bool AutoStartServer
        {
            get => EditorPrefs.GetBool(AutoStartKey, true);
            set => EditorPrefs.SetBool(AutoStartKey, value);
        }

        public static int HttpPort
        {
            get => EditorPrefs.GetInt(HttpPortKey, 6400);
            set => EditorPrefs.SetInt(HttpPortKey, value);
        }

        public static bool EnableStdioTransport
        {
            get => EditorPrefs.GetBool(EnableStdioKey, false);
            set => EditorPrefs.SetBool(EnableStdioKey, value);
        }

        public static bool EnableEditorCommands
        {
            get => EditorPrefs.GetBool(EnableCommandsKey, false);
            set
            {
                EditorPrefs.SetBool(EnableCommandsKey, value);
                EditorCommandHandler.CommandsEnabled = value;
            }
        }

        private const string EnableMutationsKey = "UnityMCP_EnableMutations";

        public static bool EnableMutations
        {
            get => EditorPrefs.GetBool(EnableMutationsKey, false);
            set
            {
                EditorPrefs.SetBool(EnableMutationsKey, value);
                MutationHandler.MutationsEnabled = value;
            }
        }
    }
}
