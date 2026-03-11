using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityMCP.Utils;

namespace UnityMCP.Handlers
{
    public static class AssetHandler
    {
        private static ListRequest s_PackageListRequest;
        private static List<Dictionary<string, object>> s_CachedPackages;

        public static Dictionary<string, object> GetAssets(Dictionary<string, object> @params = null)
        {
            string typeFilter = null;
            string folderFilter = null;
            string nameFilter = null;
            string labelFilter = null;
            int offset = 0;
            int limit = 100;

            if (@params != null)
            {
                if (@params.TryGetValue("type", out object t))
                    typeFilter = t?.ToString();
                if (@params.TryGetValue("folder", out object f))
                    folderFilter = f?.ToString();
                if (@params.TryGetValue("nameFilter", out object n))
                    nameFilter = n?.ToString();
                if (@params.TryGetValue("labelFilter", out object l))
                    labelFilter = l?.ToString();
                if (@params.TryGetValue("offset", out object o))
                    offset = Convert.ToInt32(o);
                if (@params.TryGetValue("limit", out object lim))
                    limit = Math.Min(Convert.ToInt32(lim), 500);
            }

            // Build search filter
            string searchFilter = "";
            if (!string.IsNullOrEmpty(typeFilter))
            {
                searchFilter = $"t:{typeFilter}";
            }
            if (!string.IsNullOrEmpty(labelFilter))
            {
                searchFilter += $" l:{labelFilter}";
            }

            // Find assets
            string[] searchFolders = null;
            if (!string.IsNullOrEmpty(folderFilter))
            {
                searchFolders = new[] { folderFilter };
            }

            string[] guids;
            if (searchFolders != null)
            {
                guids = AssetDatabase.FindAssets(searchFilter, searchFolders);
            }
            else
            {
                guids = AssetDatabase.FindAssets(searchFilter);
            }

            // Apply name filter and pagination
            var results = new List<Dictionary<string, object>>();
            int totalMatches = 0;
            int added = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);

                // Apply name filter
                if (!string.IsNullOrEmpty(nameFilter))
                {
                    if (!name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) &&
                        !path.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                totalMatches++;

                if (totalMatches <= offset)
                    continue;

                if (added >= limit)
                    continue;

                var assetData = GetAssetInfo(guid, path);
                results.Add(assetData);
                added++;
            }

            return new Dictionary<string, object>
            {
                { "assets", results },
                { "totalMatches", totalMatches },
                { "offset", offset },
                { "limit", limit },
                { "hasMore", totalMatches > offset + limit }
            };
        }

        private static Dictionary<string, object> GetAssetInfo(string guid, string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);

            var data = new Dictionary<string, object>
            {
                { "name", Path.GetFileNameWithoutExtension(path) },
                { "path", path },
                { "guid", guid },
                { "type", type?.Name ?? "Unknown" },
                { "fullType", type?.FullName ?? "Unknown" },
                { "extension", Path.GetExtension(path) }
            };

            // Get file size
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                data["size"] = fileInfo.Length;
                data["sizeFormatted"] = FormatFileSize(fileInfo.Length);
                data["lastModified"] = fileInfo.LastWriteTimeUtc.ToString("o");
            }

            // Get labels
            var labels = AssetDatabase.GetLabels(asset);
            if (labels.Length > 0)
            {
                data["labels"] = labels.ToList();
            }

            // Get dependencies
            var dependencies = AssetDatabase.GetDependencies(path, false);
            data["directDependencyCount"] = dependencies.Length;

            // Add type-specific info
            AddTypeSpecificInfo(asset, data);

            return data;
        }

        private static void AddTypeSpecificInfo(UnityEngine.Object asset, Dictionary<string, object> data)
        {
            if (asset == null) return;

            switch (asset)
            {
                case Texture2D tex:
                    data["textureInfo"] = new Dictionary<string, object>
                    {
                        { "width", tex.width },
                        { "height", tex.height },
                        { "format", tex.format.ToString() },
                        { "mipmapCount", tex.mipmapCount },
                        { "filterMode", tex.filterMode.ToString() },
                        { "wrapMode", tex.wrapMode.ToString() },
                        { "isReadable", tex.isReadable }
                    };
                    break;

                case AudioClip clip:
                    data["audioInfo"] = new Dictionary<string, object>
                    {
                        { "length", clip.length },
                        { "channels", clip.channels },
                        { "frequency", clip.frequency },
                        { "samples", clip.samples },
                        { "loadType", clip.loadType.ToString() },
                        { "loadState", clip.loadState.ToString() }
                    };
                    break;

                case Mesh mesh:
                    data["meshInfo"] = new Dictionary<string, object>
                    {
                        { "vertexCount", mesh.vertexCount },
                        { "triangleCount", mesh.triangles.Length / 3 },
                        { "subMeshCount", mesh.subMeshCount },
                        { "bounds", SerializationHelper.SerializeBounds(mesh.bounds) },
                        { "isReadable", mesh.isReadable }
                    };
                    break;

                case Material mat:
                    data["materialInfo"] = new Dictionary<string, object>
                    {
                        { "shader", mat.shader != null ? mat.shader.name : "None" },
                        { "renderQueue", mat.renderQueue },
                        { "passCount", mat.passCount },
                        { "enableInstancing", mat.enableInstancing }
                    };
                    break;

                case AnimationClip anim:
                    data["animationInfo"] = new Dictionary<string, object>
                    {
                        { "length", anim.length },
                        { "frameRate", anim.frameRate },
                        { "wrapMode", anim.wrapMode.ToString() },
                        { "isLooping", anim.isLooping },
                        { "legacy", anim.legacy },
                        { "hasGenericRootTransform", anim.hasGenericRootTransform },
                        { "hasMotionCurves", anim.hasMotionCurves }
                    };
                    break;

                case ScriptableObject so:
                    data["scriptableObjectInfo"] = new Dictionary<string, object>
                    {
                        { "typeName", so.GetType().Name },
                        { "fullTypeName", so.GetType().FullName }
                    };
                    break;

                case GameObject prefab:
                    var components = prefab.GetComponents<Component>();
                    var componentTypes = new List<string>();
                    foreach (var comp in components)
                    {
                        if (comp != null)
                            componentTypes.Add(comp.GetType().Name);
                    }
                    data["prefabInfo"] = new Dictionary<string, object>
                    {
                        { "componentCount", components.Length },
                        { "componentTypes", componentTypes },
                        { "childCount", prefab.transform.childCount }
                    };
                    break;

                case Shader shader:
                    data["shaderInfo"] = new Dictionary<string, object>
                    {
                        { "name", shader.name },
                        { "passCount", shader.passCount },
                        { "isSupported", shader.isSupported },
                        { "renderQueue", shader.renderQueue }
                    };
                    break;

                case Font font:
                    data["fontInfo"] = new Dictionary<string, object>
                    {
                        { "fontSize", font.fontSize },
                        { "lineHeight", font.lineHeight },
                        { "dynamic", font.dynamic }
                    };
                    break;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        public static Dictionary<string, object> GetPackages(Dictionary<string, object> @params = null)
        {
            // Start async request if not already running
            if (s_PackageListRequest == null || s_PackageListRequest.IsCompleted)
            {
                if (s_PackageListRequest != null && s_PackageListRequest.Status == StatusCode.Success)
                {
                    // Use cached result
                    s_CachedPackages = new List<Dictionary<string, object>>();
                    foreach (var package in s_PackageListRequest.Result)
                    {
                        var packageData = new Dictionary<string, object>
                        {
                            { "name", package.name },
                            { "displayName", package.displayName },
                            { "version", package.version },
                            { "description", package.description },
                            { "source", package.source.ToString() },
                            { "resolvedPath", package.resolvedPath },
                            { "documentationUrl", package.documentationUrl },
                            { "changelogUrl", package.changelogUrl },
                            { "licensesUrl", package.licensesUrl },
                            { "author", package.author?.name ?? "" },
                            { "category", package.category }
                        };
                        
                        // status property was removed in Unity 6.x
                        #if !UNITY_6_OR_NEWER
                        packageData["status"] = package.status.ToString();
                        #endif
                        
                        s_CachedPackages.Add(packageData);
                    }
                }
                else
                {
                    // Start new request
                    s_PackageListRequest = Client.List(true);
                }
            }

            if (s_CachedPackages != null)
            {
                return new Dictionary<string, object>
                {
                    { "packages", s_CachedPackages },
                    { "count", s_CachedPackages.Count },
                    { "status", "complete" }
                };
            }

            return new Dictionary<string, object>
            {
                { "packages", new List<object>() },
                { "count", 0 },
                { "status", "loading" },
                { "message", "Package list is being fetched. Please retry in a moment." }
            };
        }

        public static Dictionary<string, object> GetAssetDependencies(Dictionary<string, object> @params)
        {
            if (@params == null || !@params.TryGetValue("path", out object pathObj))
            {
                return new Dictionary<string, object> { { "error", "Missing path parameter" } };
            }

            string path = pathObj.ToString();
            bool recursive = false;

            if (@params.TryGetValue("recursive", out object r))
            {
                recursive = Convert.ToBoolean(r);
            }

            var dependencies = AssetDatabase.GetDependencies(path, recursive);
            var results = new List<Dictionary<string, object>>();

            foreach (var dep in dependencies)
            {
                if (dep == path) continue; // Skip self

                var guid = AssetDatabase.AssetPathToGUID(dep);
                var type = AssetDatabase.GetMainAssetTypeAtPath(dep);

                results.Add(new Dictionary<string, object>
                {
                    { "path", dep },
                    { "guid", guid },
                    { "type", type?.Name ?? "Unknown" },
                    { "name", Path.GetFileNameWithoutExtension(dep) }
                });
            }

            return new Dictionary<string, object>
            {
                { "assetPath", path },
                { "recursive", recursive },
                { "dependencies", results },
                { "count", results.Count }
            };
        }

        public static Dictionary<string, object> GetFolderStructure(Dictionary<string, object> @params = null)
        {
            string rootPath = "Assets";
            int maxDepth = 3;

            if (@params != null)
            {
                if (@params.TryGetValue("path", out object p))
                    rootPath = p.ToString();
                if (@params.TryGetValue("maxDepth", out object d))
                    maxDepth = Convert.ToInt32(d);
            }

            return GetFolderInfo(rootPath, 0, maxDepth);
        }

        private static Dictionary<string, object> GetFolderInfo(string path, int depth, int maxDepth)
        {
            var data = new Dictionary<string, object>
            {
                { "name", Path.GetFileName(path) },
                { "path", path }
            };

            // Count assets in folder
            var guids = AssetDatabase.FindAssets("", new[] { path });
            var directAssets = guids.Where(g => 
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                return Path.GetDirectoryName(p).Replace("\\", "/") == path;
            }).ToList();

            data["assetCount"] = directAssets.Count;

            // Get subfolders
            if (depth < maxDepth)
            {
                var subfolders = new List<Dictionary<string, object>>();
                var fullPath = Path.GetFullPath(path);

                if (Directory.Exists(fullPath))
                {
                    foreach (var dir in Directory.GetDirectories(fullPath))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (dirName.StartsWith(".")) continue; // Skip hidden folders

                        var relativePath = path + "/" + dirName;
                        subfolders.Add(GetFolderInfo(relativePath, depth + 1, maxDepth));
                    }
                }

                data["subfolders"] = subfolders;
                data["subfolderCount"] = subfolders.Count;
            }

            return data;
        }

        public static void RefreshPackageCache()
        {
            s_CachedPackages = null;
            s_PackageListRequest = Client.List(true);
        }
    }
}
