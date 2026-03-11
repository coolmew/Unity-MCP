# Unity MCP Server

A Model Context Protocol (MCP) server plugin for Unity Editor that exposes project context to AI coding agents like Claude, Cursor, Copilot, and others.

## Features

### Read Operations
- **Full Scene Hierarchy Access**: Query GameObjects, their components, and properties
- **Component Inspection**: Read all serialized fields with values, types, and tooltips
- **Script Analysis**: List and read MonoBehaviour/ScriptableObject scripts with field and method information
- **Project Settings**: Access PlayerSettings, QualitySettings, Physics, Tags/Layers, Input, Graphics, and Build settings
- **Asset Database**: Search and query assets by type, folder, or name with type-specific metadata
- **Asset Dependencies**: Inspect dependency graphs for any asset
- **Folder Structure**: Browse the project folder hierarchy
- **Package Manager**: List all installed UPM packages
- **Animation Inspection**: Read animation clips (curves, keyframes, events) and animator controllers (layers, states, transitions, parameters)
- **Project Info**: Query runtime project information (product name, Unity version, platform, etc.)

### Write Operations (Opt-in)
- **GameObject Manipulation**: Create, delete, rename, move, duplicate GameObjects
- **Component Management**: Add, remove, enable/disable components
- **Property Editing**: Modify component properties and serialized fields
- **Object Reference Assignment**: Programmatic drag-and-drop equivalent for Inspector fields
- **Transform Control**: Set position, rotation, scale
- **Prefab Operations**: Create prefabs from GameObjects, instantiate prefabs
- **Scene Management**: Save scenes
- **Animation Authoring**: Create/edit animation clips, curves, keyframes, and events
- **Animator Controller Authoring**: Create controllers, add states, transitions, parameters, and layers
- **Editor Commands**: Execute safe C# expressions (sandboxed)

All mutations support Unity's Undo system (Ctrl+Z).

## Project Structure

```
com.unityai.mcp/
├── Editor/
│   ├── Handlers/
│   │   ├── AnimationHandler.cs      # Animation clip & animator controller tools
│   │   ├── AssetHandler.cs          # Asset search, packages, dependencies, folders
│   │   ├── ComponentHandler.cs      # Component serialization & inspection
│   │   ├── EditorCommandHandler.cs  # Sandboxed C# expression execution
│   │   ├── HierarchyHandler.cs      # Scene hierarchy & GameObject queries
│   │   ├── MutationHandler.cs       # All write operations (GameObjects, components, prefabs)
│   │   ├── ProjectSettingsHandler.cs# Unity project settings reader
│   │   └── ScriptHandler.cs         # Script listing & source reading
│   ├── Transport/
│   │   ├── HttpTransport.cs         # HTTP/SSE server & JSON-RPC transport
│   │   └── StdioTransport.cs        # Stdio transport & JSON-RPC message types
│   ├── Utils/
│   │   └── SerializationHelper.cs   # Unity type serialization & JSON parser
│   ├── UnityMCPServer.cs            # Main server, MCP protocol, tool routing
│   ├── UnityMCPEditorWindow.cs      # Editor UI (Window > Unity MCP)
│   └── com.unityai.mcp.Editor.asmdef
├── package.json
├── mcp_config.json
├── CHANGELOG.md
└── README.md
```

## Requirements

- Unity 2022.3 LTS or newer (including Unity 6.x)
- Windows, macOS, or Linux

## Installation

### Option 1: Git URL (Recommended)

1. Open Unity Package Manager (Window > Package Manager)
2. Click the "+" button and select "Add package from git URL..."
3. Enter: `https://github.com/coolmew/Unity-MCP.git`

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

### Cursor / Windsurf

Add to your MCP settings:

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

---

## Available Tools

### Read Tools

#### getSceneHierarchy

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

#### getGameObject

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

#### getComponent

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

#### findGameObjects

Searches for GameObjects matching filters.

**Parameters:**
- `nameFilter` (string, optional): Filter by name (partial match)
- `tagFilter` (string, optional): Filter by tag (exact match)
- `componentFilter` (string, optional): Filter by component type
- `layerFilter` (int, optional): Filter by layer index
- `offset` (int, optional): Pagination offset
- `limit` (int, optional): Max results (default: 100, max: 500)

#### getScripts

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

#### readScript

Returns the full source code of a script with parsed structure (usings, classes, methods, fields).

**Parameters:**
- `scriptPath` (string, required): Path to the script (e.g., "Assets/Scripts/Player.cs")

#### getProjectSettings

Returns Unity project settings.

**Parameters:**
- `sections` (array, optional): Specific sections to include
  - Valid values: `"player"`, `"quality"`, `"physics"`, `"physics2d"`, `"tags"`, `"layers"`, `"input"`, `"graphics"`, `"build"`, `"time"`, `"audio"`

**Response includes:**
- PlayerSettings (company, product, version, scripting backend, etc.)
- QualitySettings (current level, vSync, shadows, etc.)
- PhysicsSettings (gravity, contact offset, etc.)
- Physics2DSettings (gravity, iterations, etc.)
- TagsAndLayers (all tags, layer names, sorting layers)
- InputManager (axes definitions)
- GraphicsSettings (render pipeline, tier settings)
- BuildSettings (active target, scenes in build)
- TimeSettings (fixed delta time, time scale, etc.)
- AudioSettings (speaker mode, sample rate, etc.)

#### getAssets

Searches for assets in the project. Returns type-specific metadata (texture dimensions, audio length, mesh vertex count, material shader, etc.).

**Parameters:**
- `type` (string, optional): Asset type (e.g., "Texture2D", "Material", "Prefab", "AudioClip", "AnimationClip")
- `folder` (string, optional): Folder path (e.g., "Assets/Textures")
- `nameFilter` (string, optional): Filter by name
- `labelFilter` (string, optional): Filter by asset label
- `offset` (int, optional): Pagination offset
- `limit` (int, optional): Max results (default: 100)

#### getPackages

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

#### getAssetDependencies

Returns the dependency graph for a specific asset.

**Parameters:**
- `path` (string, required): Asset path (e.g., "Assets/Prefabs/Player.prefab")
- `recursive` (bool, optional): Include transitive dependencies (default: false)

#### getFolderStructure

Returns the folder hierarchy of the project.

**Parameters:**
- `path` (string, optional): Root path (default: "Assets")
- `maxDepth` (int, optional): Maximum depth to traverse (default: 3)

#### getProjectInfo

Returns basic Unity project information (product name, company, Unity version, platform, play mode state, etc.). No parameters.

#### getAvailableCommands

Returns the list of supported editor command expressions grouped by category. No parameters.

#### getAnimationClipInfo

Returns full information about an animation clip including all curves, keyframes, and events.

**Parameters:**
- `clipPath` (string, required): Path to the `.anim` asset

#### getAnimatorControllerInfo

Returns full information about an animator controller including layers, states, parameters, and transitions.

**Parameters:**
- `controllerPath` (string, required): Path to the `.controller` asset

#### runEditorCommand

Executes a C# expression (requires opt-in).

**⚠️ Security Note:** This tool is disabled by default. Enable it in Window > Unity MCP settings only if you trust the connecting AI agents.

**Parameters:**
- `code` (string, required): C# expression to execute

**Supported expressions:**
- `Selection.activeGameObject`, `Selection.gameObjects.Length`
- `EditorApplication.isPlaying`, `EditorApplication.isCompiling`
- `SceneManager.GetActiveScene().name`
- `Debug.Log("message")`
- `GameObject.Find("name")`
- `AssetDatabase.FindAssets("filter")`, `AssetDatabase.Refresh()`

---

### Mutation Tools (Write Operations)

**⚠️ All mutation tools require "Enable Mutations" to be turned on in Window > Unity MCP settings.**

All mutations support Unity's Undo system — press Ctrl+Z (Cmd+Z on Mac) to undo any changes made by AI agents.

#### GameObject Operations

##### createGameObject

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
- `primitive` (string, optional): Create primitive mesh: `"cube"`, `"sphere"`, `"capsule"`, `"cylinder"`, `"plane"`, `"quad"`

##### deleteGameObject

Deletes a GameObject from the scene.

**Parameters (one required):**
- `instanceID` (int): GameObject instance ID
- `path` (string): GameObject hierarchy path
- `name` (string): GameObject name

##### renameGameObject

Renames a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `newName` (string, required): New name

##### moveGameObject

Moves a GameObject to a new parent or changes its sibling index.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `newParentInstanceID` (int, optional): New parent instance ID (null for root)
- `newParentPath` (string, optional): New parent hierarchy path
- `siblingIndex` (int, optional): Sibling index position

##### duplicateGameObject

Duplicates a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `newName` (string, optional): Name for the duplicate

##### setGameObjectActive

Sets a GameObject's active state.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `active` (bool, required): Active state

#### Transform Operations

##### setTransform

Sets a GameObject's transform (position, rotation, scale).

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `position` (object, optional): Position `{x, y, z}`
- `rotation` (object, optional): Euler rotation `{x, y, z}`
- `scale` (object, optional): Scale `{x, y, z}`
- `local` (bool, optional): Use local space (default: true)

#### Component Operations

##### addComponent

Adds a component to a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name (e.g., "Rigidbody", "BoxCollider", "AudioSource")

##### removeComponent

Removes a component from a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name

##### setComponentEnabled

Enables or disables a component.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `componentType` (string, required): Component type name
- `enabled` (bool, required): Enabled state

#### Property Operations

##### setComponentProperty

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

##### setMultipleProperties

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

##### assignObjectReference

Assigns a scene object (GameObject, component) or asset to a component's serialized object-reference field. This is the programmatic equivalent of drag-and-drop in the Inspector.

**Parameters:**
- `targetInstanceID` or `targetPath` or `targetName` (required): Target GameObject (the one with the field)
- `componentType` (string, required): Component type on the target
- `propertyName` (string, required): Serialized field name to assign to
- **Source** (one required):
  - `sourceInstanceID` (int): Instance ID of the source object
  - `sourcePath` (string): Source GameObject hierarchy path
  - `sourceName` (string): Source GameObject name
  - `assetPath` (string): Asset path (e.g., "Assets/Materials/MyMat.mat")
  - `clear` (bool): Set to `true` to null the reference
- `sourceComponentType` (string, optional): Get a specific component from the source GameObject instead of the GameObject itself

#### Scene & Prefab Operations

##### saveScene

Saves the current or specified scene.

**Parameters:**
- `scenePath` (string, optional): Scene path (saves active scene if not specified)

##### createPrefab

Creates a prefab from a GameObject.

**Parameters:**
- `instanceID` or `path` (required): GameObject identifier
- `path` (string, optional): Prefab save path (e.g., "Assets/Prefabs/MyPrefab.prefab")

##### instantiatePrefab

Instantiates a prefab into the scene.

**Parameters:**
- `prefabPath` (string, required): Path to the prefab asset
- `parentInstanceID` (int, optional): Parent GameObject instance ID
- `position` (object, optional): World position `{x, y, z}`

#### Animation Clip Operations

##### createAnimationClip

Creates a new AnimationClip asset.

**Parameters:**
- `name` (string, optional): Clip name (default: "New Animation")
- `savePath` (string, optional): Save path (e.g., "Assets/Animations/Walk.anim")
- `loop` (bool, optional): Loop the animation
- `frameRate` (number, optional): Frame rate (default: 60)
- `wrapMode` (string, optional): `"loop"`, `"pingpong"`, `"clampforever"`, `"once"`

##### setAnimationCurve

Sets or replaces an entire animation curve on a clip.

**Parameters:**
- `clipPath` (string, required): Path to the `.anim` asset
- `propertyName` (string, required): Property name (e.g., "localPosition.x")
- `type` (string, required): Component type (e.g., "Transform", "SpriteRenderer")
- `keyframes` (array, required): Array of `{time, value, inTangent?, outTangent?}`
- `relativePath` (string, optional): Relative path to child (empty for root)

##### removeAnimationCurve

Removes an animation curve from a clip.

**Parameters:**
- `clipPath` (string, required): Path to the `.anim` asset
- `propertyName` (string, required): Property name to remove
- `type` (string, required): Component type
- `relativePath` (string, optional): Relative path to child

##### addAnimationKeyframe

Adds a single keyframe to an existing or new curve.

**Parameters:**
- `clipPath` (string, required): Path to the `.anim` asset
- `propertyName` (string, required): Property name
- `type` (string, required): Component type
- `time` (number, required): Keyframe time in seconds
- `value` (number, required): Keyframe value
- `inTangent` (number, optional): In tangent
- `outTangent` (number, optional): Out tangent
- `relativePath` (string, optional): Relative path to child

##### addAnimationEvent

Adds an animation event that calls a function at a specific time.

**Parameters:**
- `clipPath` (string, required): Path to the `.anim` asset
- `functionName` (string, required): Function to call
- `time` (number, required): Event time in seconds
- `intParameter` (int, optional): Int parameter
- `floatParameter` (number, optional): Float parameter
- `stringParameter` (string, optional): String parameter

##### setClipSettings

Modifies animation clip settings.

**Parameters:**
- `clipPath` (string, required): Path to the `.anim` asset
- `loopTime` (bool, optional): Enable looping
- `loopBlend` (bool, optional): Loop pose
- `cycleOffset` (number, optional): Cycle offset
- `frameRate` (number, optional): Frame rate
- `startTime` (number, optional): Start time
- `stopTime` (number, optional): Stop time

#### Animator Controller Operations

##### createAnimatorController

Creates a new Animator Controller asset.

**Parameters:**
- `name` (string, optional): Controller name
- `savePath` (string, optional): Save path (e.g., "Assets/Animations/Player.controller")

##### addAnimatorParameter

Adds a parameter to an animator controller.

**Parameters:**
- `controllerPath` (string, required): Path to the `.controller` asset
- `parameterName` (string, required): Parameter name
- `parameterType` (string, optional): `"float"`, `"int"`, `"bool"`, `"trigger"` (default: "float")
- `defaultValue` (optional): Default value for the parameter

##### addAnimatorState

Adds a state to an animator controller layer.

**Parameters:**
- `controllerPath` (string, required): Path to the `.controller` asset
- `stateName` (string, required): State name
- `layerIndex` (int, optional): Layer index (default: 0)
- `clipPath` (string, optional): Animation clip to assign as motion
- `speed` (number, optional): Playback speed (default: 1)
- `tag` (string, optional): State tag
- `isDefault` (bool, optional): Set as default state

##### addAnimatorTransition

Adds a transition between two states with optional conditions.

**Parameters:**
- `controllerPath` (string, required): Path to the `.controller` asset
- `sourceState` (string, required): Source state name
- `destinationState` (string, required): Destination state name
- `layerIndex` (int, optional): Layer index (default: 0)
- `hasExitTime` (bool, optional): Has exit time
- `exitTime` (number, optional): Exit time (0–1)
- `duration` (number, optional): Transition duration
- `conditions` (array, optional): Array of `{parameter, mode, threshold}` — mode: `"greater"`, `"less"`, `"equals"`, `"notequal"`, `"if"`, `"ifnot"`

##### addAnimatorLayer

Adds a new layer to an animator controller.

**Parameters:**
- `controllerPath` (string, required): Path to the `.controller` asset
- `layerName` (string, required): Layer name
- `defaultWeight` (number, optional): Default weight (0–1)

##### assignAnimator

Assigns an Animator Controller to a GameObject's Animator component (adds Animator if missing).

**Parameters:**
- `instanceID` or `path` or `name` (required): GameObject identifier
- `controllerPath` (string, optional): Path to the `.controller` asset
- `avatarPath` (string, optional): Path to avatar asset
- `applyRootMotion` (bool, optional): Apply root motion

---

## Editor Window

Open **Window > Unity MCP** to access:

- **Server Status**: View running state, port, and endpoint URLs
- **Settings**: Configure auto-start, port, stdio transport, and security options
- **MCP Config**: Copy HTTP or SSE configuration for AI clients
- **Request Log**: View incoming requests and responses with auto-scroll

## Security

- The server only listens on localhost (127.0.0.1)
- **Mutations are disabled by default** — must be explicitly enabled in settings
- **Editor commands are disabled by default** — requires explicit opt-in with a confirmation dialog
- Code execution is sandboxed with namespace whitelisting
- Dangerous operations (file deletion, process spawning, assembly loading) are blocked
- All mutations use Unity's Undo system for easy rollback

## Performance

- Hierarchy snapshots are cached per-frame and invalidated on scene/hierarchy changes
- Script metadata is cached for 30 seconds
- Large results support pagination with `offset` and `limit`
- All Unity API calls run on the main thread via `EditorApplication.update`

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
- Package list may show "loading" on first request — retry after a moment
- Script cache refreshes every 30 seconds; new scripts appear on the next refresh

## API Reference

### JSON-RPC 2.0 Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/rpc` | POST | JSON-RPC 2.0 requests |
| `/message` | POST | Alias for `/rpc` |
| `/mcp` | GET | Server capabilities |
| `/mcp` | POST | MCP protocol requests (JSON-RPC) |
| `/sse` | GET | Server-Sent Events stream |
| `/health` | GET | Health check |

### MCP Protocol

The server implements MCP protocol version `2024-11-05` with support for:
- `initialize` — returns server info and capabilities
- `tools/list` — returns all available tool definitions
- `tools/call` — invokes a tool by name with arguments
- `resources/list` — returns available resources

### Error Codes

| Code | Description |
|------|-------------|
| -32700 | Parse error |
| -32600 | Invalid request |
| -32601 | Method not found |
| -32602 | Invalid params |
| -32603 | Internal error |
| -32000 | Unity runtime error |
| -32001 | Security error (mutations/commands disabled) |
| -32002 | Not found error |

## License

MIT License — see LICENSE file for details.

## Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

### 1.0.1
- Added complete Animation system (14 tools): clip creation, curve/keyframe editing, events, animator controllers, states, transitions, layers
- Added `assignObjectReference` tool for programmatic Inspector drag-and-drop
- Added `getAssetDependencies`, `getFolderStructure`, `getProjectInfo`, `getAvailableCommands` tools
- Added Unity 6.x compatibility with conditional compilation for removed APIs
- Updated README to document all tools and project structure

### 1.0.0
- Initial release
- Full MCP protocol support
- HTTP and SSE transports
- Complete Unity Editor integration
