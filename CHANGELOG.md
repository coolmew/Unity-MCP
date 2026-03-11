# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-03-11

### Added

#### Animation System (14 new tools)
- `createAnimationClip` - Create animation clip assets with loop/frame rate/wrap mode options
- `getAnimationClipInfo` - Inspect clips including all curves, keyframes, and events
- `setAnimationCurve` - Set/replace entire animation curves with keyframes
- `removeAnimationCurve` - Remove animation curves from clips
- `addAnimationKeyframe` - Add single keyframes to existing or new curves
- `addAnimationEvent` - Add animation events that call functions at specific times
- `setClipSettings` - Modify clip settings (loop, frame rate, start/stop time)
- `createAnimatorController` - Create animator controller assets
- `getAnimatorControllerInfo` - Inspect controllers (layers, states, parameters, transitions)
- `addAnimatorParameter` - Add float/int/bool/trigger parameters
- `addAnimatorState` - Add states with optional motion clips and speed
- `addAnimatorTransition` - Add transitions with conditions between states
- `addAnimatorLayer` - Add layers with configurable default weight
- `assignAnimator` - Assign controllers to GameObjects (adds Animator component if missing)

#### Mutation Tools
- `assignObjectReference` - Programmatic Inspector drag-and-drop: assign scene objects or assets to component serialized fields

### Improved
- Unity 6.x compatibility via conditional compilation (`UNITY_6_OR_NEWER`) for removed tier settings API
- Documentation updated to cover all 40+ tools, project structure, and MCP protocol details

---

## [1.0.0] - 11-03-2026

### Added
- Initial release of Unity MCP Server
- HTTP transport with JSON-RPC 2.0 support
- Server-Sent Events (SSE) transport for streaming
- Optional stdio transport for process-based communication

#### MCP Tools
- `getSceneHierarchy` - Full scene GameObject tree with caching
- `getGameObject` - Detailed GameObject info by ID, path, or name
- `findGameObjects` - Search with name, tag, component, and layer filters
- `getComponent` - Serialized field inspection with values and metadata
- `getScripts` - List MonoBehaviour/ScriptableObject scripts with reflection data
- `readScript` - Read script source code with structure parsing
- `getProjectSettings` - Comprehensive Unity settings snapshot
- `getAssets` - AssetDatabase queries with type/folder/name filters
- `getPackages` - UPM package listing
- `getAssetDependencies` - Asset dependency graph
- `getFolderStructure` - Project folder hierarchy
- `runEditorCommand` - Sandboxed C# expression execution (opt-in)
- `getAvailableCommands` - List supported editor commands
- `getProjectInfo` - Basic project information

#### Editor Integration
- EditorWindow (Window > Unity MCP) with server controls
- Auto-start on Unity launch (configurable)
- Request/response log viewer
- MCP configuration copy buttons
- Security settings for editor commands

#### Serialization
- Unity type serialization (Vector2/3/4, Quaternion, Color, Bounds, etc.)
- Circular reference handling with depth limits
- Null/missing reference handling
- Large result pagination

### Security
- Localhost-only server binding
- Editor commands disabled by default
- Dangerous operation blacklist
- Namespace whitelisting for code execution

### Performance
- Hierarchy caching with automatic invalidation
- Pagination support for large datasets
- Main thread execution via EditorApplication.update
