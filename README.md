# Unity MCP Server

A Model Context Protocol (MCP) server plugin for Unity Editor that exposes project context to AI coding agents like Claude, Cursor, Copilot, and others.

## Features

### Read Operations
- **Full Scene Hierarchy Access**: Query GameObjects, their components, and properties
- **Component Inspection**: Read all serialized fields with values, types, and tooltips
- **Script Analysis**: List and read MonoBehaviour/ScriptableObject scripts with field and method information
- **Project Settings**: Access PlayerSettings, QualitySettings, Physics, Tags/Layers, Input, Graphics, and Build settings
- **Asset Database**: Search and query assets by type, folder, or name
- **Package Manager**: List all installed UPM packages

### Write Operations (Opt-in)
- **GameObject Manipulation**: Create, delete, rename, move, duplicate GameObjects
- **Component Management**: Add, remove, enable/disable components
- **Property Editing**: Modify component properties and serialized fields
- **Transform Control**: Set position, rotation, scale
- **Prefab Operations**: Create prefabs from GameObjects, instantiate prefabs
- **Scene Management**: Save scenes
- **Editor Commands**: Execute safe C# expressions (sandboxed)

All mutations support Unity's Undo system (Ctrl+Z).

## Requirements

- Unity 2022.3 LTS or newer (including Unity 6.x)
- Windows, macOS, or Linux

## Installation

### Option 1: Git URL (Recommended)

1. Open Unity Package Manager (Window > Package Manager)
2. Click the "+" button and select "Add package from git URL..."
3. Enter: `https://github.com/yourusername/unity-mcp.git`

### Option 2: Local Package

1. Clone or download this repository
2. Copy the `com.unityai.mcp` folder to your project's `Packages/` directory
3. Unity will automatically detect and import the package

### Option 3: Embedded Package

1. Copy the entire `com.unityai.mcp` folder into your project's `Packages/` folder
2. The package will be embedded in your project

## Quick Start

1. After installation, the MCP server starts automatically with Unity
2. Open **Window > Unity MCP** to see the server status and settings
3. Copy the MCP configuration to your AI client
4. Start querying your Unity project!

## Connecting AI Clients

### Claude Desktop

Add to your Claude Desktop configuration file:

**macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:6400/mcp",
      "transport": "http"
    }
  }
}
```

### Cursor

Add to your Cursor MCP settings:

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:6400/mcp",
      "transport": "http"
    }
  }
}
```

### Custom HTTP Client

Send JSON-RPC 2.0 requests to `http://localhost:6400/rpc`:

```bash
curl -X POST http://localhost:6400/rpc \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## Available Tools

### unity/getSceneHierarchy

Returns the full scene hierarchy with all GameObjects.

**Parameters:**
- `maxDepth` (int, optional): Maximum depth to traverse (default: 8)
- `includeInactive` (bool, optional): Include inactive GameObjects (default: true)

**Response:**
```json
{
  "scenes": [{
    "name": "SampleScene",
    "rootGameObjects": [{
      "name": "Main Camera",
      "instanceID": 12345,
      "tag": "MainCamera",
      "layer": 0,
      "activeSelf": true,
      "children": []
    }]
  }]
}
```

### unity/getGameObject

Returns detailed information about a specific GameObject.

**Parameters (one required):**
- `instanceID` (int): Instance ID of the GameObject
- `path` (string): Hierarchy path (e.g., "/Canvas/Panel/Button")
- `name` (string): Name of the GameObject

**Response:**
```json
{
  "name": "Player",
  "instanceID": 12345,
  "tag": "Player",
  "layer": 0,
  "transform": {
    "position": {"x": 0, "y": 1, "z": 0},
    "rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
    "localScale": {"x": 1, "y": 1, "z": 1}
  },
  "components": [...]
}
```

### unity/getComponent

Returns all serialized fields of a component.

**Parameters:**
- `instanceID` (int, required): Instance ID of the GameObject
- `componentType` (string, required): Type name of the component

**Response:**
```json
{
  "type": "Rigidbody",
  "enabled": true,
  "fields": {
    "m_Mass": {"name": "m_Mass", "value": 1.0, "type": "float"},
    "m_Drag": {"name": "m_Drag", "value": 0.0, "type": "float"}
  }
}
```

### unity/findGameObjects

Searches for GameObjects matching filters.

**Parameters:**
- `nameFilter` (string, optional): Filter by name (partial match)
- `tagFilter` (string, optional): Filter by tag (exact match)
- `componentFilter` (string, optional): Filter by component type
- `layerFilter` (int, optional): Filter by layer index
- `offset` (int, optional): Pagination offset
- `limit` (int, optional): Max results (default: 100, max: 500)

### unity/getScripts

Returns all MonoBehaviour and ScriptableObject scripts.

**Parameters:**
- `nameFilter` (string, optional): Filter by script name
- `namespaceFilter` (string, optional): Filter by namespace
- `offset` (int, optional): Pagination offset
- `limit` (int, optional): Max results (default: 100)

**Response:**
```json
{
  "scripts": [{
    "name": "PlayerController",
    "path": "Assets/Scripts/PlayerController.cs",
    "namespace": "Game",
    "fields": [{"name": "speed", "type": "float"}],
    "methods": [{"name": "Move", "returnType": "void"}]
  }]
}
```

### unity/readScript

Returns the full source code of a script.

**Parameters:**
- `scriptPath` (string, required): Path to the script (e.g., "Assets/Scripts/Player.cs")

### unity/getProjectSettings

Returns Unity project settings.

**Parameters:**
- `sections` (array, optional): Specific sections to include
  - Valid values: "player", "quality", "physics", "physics2d", "tags", "layers", "input", "graphics", "build", "time", "audio"

**Response includes:**
- PlayerSettings (company, product, version, scripting backend, etc.)
- QualitySettings (current level, vSync, shadows, etc.)
- PhysicsSettings (gravity, contact offset, etc.)
- TagsAndLayers (all tags, layer names, sorting layers)
- InputManager (axes definitions)
- GraphicsSettings (render pipeline, tier settings)
- BuildSettings (active target, scenes in build)

### unity/getAssets

Searches for assets in the project.

**Parameters:**
- `type` (string, optional): Asset type (e.g., "Texture2D", "Material", "Prefab")
- `folder` (string, optional): Folder path (e.g., "Assets/Textures")
- `nameFilter` (string, optional): Filter by name
- `labelFilter` (string, optional): Filter by asset label
- `offset` (int, optional): Pagination offset
- `limit` (int, optional): Max results (default: 100)

### unity/getPackages

Returns all installed UPM packages.

**Response:**
```json
{
  "packages": [{
    "name": "com.unity.textmeshpro",
    "displayName": "TextMeshPro",
    "version": "3.0.6",
    "source": "registry"
  }]
}
```

### unity/runEditorCommand

Executes a C# expression (requires opt-in).

**⚠️ Security Note:** This tool is disabled by default. Enable it in Window > Unity MCP settings only if you trust the connecting AI agents.

**Parameters:**
- `code` (string, required): C# expression to execute

**Supported expressions:**
- `Selection.activeGameObject`
- `EditorApplication.isPlaying`
- `SceneManager.GetActiveScene().name`
- `Debug.Log("message")`
- `GameObject.Find("name")`
- `AssetDatabase.FindAssets("filter")`
- `AssetDatabase.Refresh()`

## Mutation Tools (Write Operations)

**⚠️ All mutation tools require "Enable Mutations" to be turned on in Window > Unity MCP settings.**

All mutations support Unity's Undo system - you can press Ctrl+Z (Cmd+Z on Mac) to undo any changes made by AI agents.

### unity/createGameObject

Creates a new GameObject in the scene.

**Parameters:**
- `name` (string, optional): Name of the GameObject
- `parentInstanceID` (int, optional): Parent GameObject instance ID
- `parentPath` (string, optional): Parent GameObject hierarchy path
- `position` (object, optional): Local position `{x, y, z}`
- `rotation` (object, optional): Local euler rotation `{x, y, z}`
- `scale` (object, optional): Local scale `{x, y, z}`
- `tag` (string, optional): GameObject tag
- `layer` (int, optional): GameObject layer
- `primitive` (string, optional): Create primitive mesh: "cube", "sphere", "capsule", "cylinder", "plane", "quad"

### unity/deleteGameObject

Deletes a GameObject from the scene.

**Parameters (one required):**
- `instanceID` (int): GameObject instance ID
- `path` (string): GameObject hierarchy path
- `name` (string): GameObject name

### unity/renameGameObject

Renames a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `newName` (string, required): New name

### unity/moveGameObject

Moves a GameObject to a new parent or changes its sibling index.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `newParentInstanceID` (int, optional): New parent instance ID (null for root)
- `newParentPath` (string, optional): New parent hierarchy path
- `siblingIndex` (int, optional): Sibling index position

### unity/duplicateGameObject

Duplicates a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `newName` (string, optional): Name for the duplicate

### unity/setGameObjectActive

Sets a GameObject's active state.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `active` (bool, required): Active state

### unity/setTransform

Sets a GameObject's transform (position, rotation, scale).

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `position` (object, optional): Position `{x, y, z}`
- `rotation` (object, optional): Euler rotation `{x, y, z}`
- `scale` (object, optional): Scale `{x, y, z}`
- `local` (bool, optional): Use local space (default: true)

### unity/addComponent

Adds a component to a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name (e.g., "Rigidbody", "BoxCollider", "AudioSource")

### unity/removeComponent

Removes a component from a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name

### unity/setComponentEnabled

Enables or disables a component.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name
- `enabled` (bool, required): Enabled state

### unity/setComponentProperty

Sets a single property/field value on a component.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name
- `propertyName` (string, required): Property/field name (e.g., "m_Mass" for Rigidbody)
- `value` (required): New value (type depends on property)

**Example:**
```json
{
  "path": "/Player",
  "componentType": "Rigidbody",
  "propertyName": "m_Mass",
  "value": 2.5
}
```

### unity/setMultipleProperties

Sets multiple properties on a component at once.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name
- `properties` (object, required): Dictionary of property names to values

**Example:**
```json
{
  "path": "/Player",
  "componentType": "Rigidbody",
  "properties": {
    "m_Mass": 2.5,
    "m_Drag": 0.5,
    "m_UseGravity": true
  }
}
```

### unity/saveScene

Saves the current or specified scene.

**Parameters:**
- `scenePath` (string, optional): Scene path (saves active scene if not specified)

### unity/createPrefab

Creates a prefab from a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `path` (string, optional): Prefab save path (e.g., "Assets/Prefabs/MyPrefab.prefab")

### unity/instantiatePrefab

Instantiates a prefab into the scene.

**Parameters:**
- `prefabPath` (string, required): Path to the prefab asset
- `parentInstanceID` (int, optional): Parent GameObject instance ID
- `position` (object, optional): World position `{x, y, z}`

## Editor Window

Open **Window > Unity MCP** to access:

- **Server Status**: View running state and port
- **Settings**: Configure auto-start, port, and security options
- **MCP Config**: Copy configuration for AI clients
- **Request Log**: View incoming requests and responses

## Security

- The server only listens on localhost (127.0.0.1)
- **Mutations are disabled by default** - must be explicitly enabled in settings
- Editor commands are disabled by default
- Code execution is sandboxed with whitelisted operations
- Dangerous operations (file deletion, process spawning) are blocked
- All mutations use Unity's Undo system for easy rollback

## Performance

- Hierarchy snapshots are cached and invalidated on changes
- Large results support pagination with `offset` and `limit`
- All Unity API calls run on the main thread via EditorApplication.update

## Troubleshooting

### Server won't start
- Check if port 6400 is already in use
- Try changing the port in Window > Unity MCP settings

### AI client can't connect
- Ensure Unity is running and the server is started
- Check firewall settings for localhost connections
- Verify the MCP configuration URL matches the server port

### Missing data in responses
- Some data requires the scene to be loaded
- Package list may show "loading" on first request - retry after a moment

## API Reference

### JSON-RPC 2.0 Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/rpc` | POST | JSON-RPC 2.0 requests |
| `/mcp` | GET/POST | MCP protocol endpoint |
| `/sse` | GET | Server-Sent Events stream |
| `/health` | GET | Health check |

### Error Codes

| Code | Description |
|------|-------------|
| -32700 | Parse error |
| -32600 | Invalid request |
| -32601 | Method not found |
| -32602 | Invalid params |
| -32603 | Internal error |
| -32000 | Unity runtime error |
| -32001 | Security error |
| -32002 | Not found error |

## License

MIT License - see LICENSE file for details.

## Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## Changelog

### 1.0.0
- Initial release
- Full MCP protocol support
- HTTP and SSE transports
- Complete Unity Editor integration
