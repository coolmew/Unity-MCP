using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Build;
using UnityMCP.Utils;

namespace UnityMCP.Handlers
{
    public static class ProjectSettingsHandler
    {
        public static Dictionary<string, object> GetProjectSettings(Dictionary<string, object> @params = null)
        {
            var settings = new Dictionary<string, object>();

            // Determine which sections to include
            bool includeAll = @params == null || !@params.ContainsKey("sections");
            List<string> sections = null;

            if (@params != null && @params.TryGetValue("sections", out object sectionsObj))
            {
                if (sectionsObj is List<object> list)
                {
                    sections = list.Select(s => s.ToString().ToLowerInvariant()).ToList();
                }
            }

            if (includeAll || sections?.Contains("player") == true)
                settings["playerSettings"] = GetPlayerSettings();

            if (includeAll || sections?.Contains("quality") == true)
                settings["qualitySettings"] = GetQualitySettings();

            if (includeAll || sections?.Contains("physics") == true)
                settings["physicsSettings"] = GetPhysicsSettings();

            if (includeAll || sections?.Contains("physics2d") == true)
                settings["physics2DSettings"] = GetPhysics2DSettings();

            if (includeAll || sections?.Contains("tags") == true || sections?.Contains("layers") == true)
                settings["tagsAndLayers"] = GetTagsAndLayers();

            if (includeAll || sections?.Contains("input") == true)
                settings["inputSettings"] = GetInputSettings();

            if (includeAll || sections?.Contains("graphics") == true)
                settings["graphicsSettings"] = GetGraphicsSettings();

            if (includeAll || sections?.Contains("build") == true)
                settings["buildSettings"] = GetBuildSettings();

            if (includeAll || sections?.Contains("time") == true)
                settings["timeSettings"] = GetTimeSettings();

            if (includeAll || sections?.Contains("audio") == true)
                settings["audioSettings"] = GetAudioSettings();

            return settings;
        }

        private static Dictionary<string, object> GetPlayerSettings()
        {
            return new Dictionary<string, object>
            {
                { "companyName", PlayerSettings.companyName },
                { "productName", PlayerSettings.productName },
                { "bundleVersion", PlayerSettings.bundleVersion },
                
                // Platform-specific bundle identifiers
                { "applicationIdentifier", PlayerSettings.applicationIdentifier },
                
                // Scripting
                { "scriptingBackend", PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString() },
                { "apiCompatibilityLevel", PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString() },
                
                // Rendering
                { "colorSpace", PlayerSettings.colorSpace.ToString() },
                { "gpuSkinning", PlayerSettings.gpuSkinning },
                
                // Resolution
                { "defaultIsFullScreen", PlayerSettings.fullScreenMode.ToString() },
                { "defaultScreenWidth", PlayerSettings.defaultScreenWidth },
                { "defaultScreenHeight", PlayerSettings.defaultScreenHeight },
                { "runInBackground", PlayerSettings.runInBackground },
                
                // Splash
                { "showUnitySplashScreen", PlayerSettings.SplashScreen.show },
                
                // Other
                { "allowedAutorotateToLandscapeLeft", PlayerSettings.allowedAutorotateToLandscapeLeft },
                { "allowedAutorotateToLandscapeRight", PlayerSettings.allowedAutorotateToLandscapeRight },
                { "allowedAutorotateToPortrait", PlayerSettings.allowedAutorotateToPortrait },
                { "allowedAutorotateToPortraitUpsideDown", PlayerSettings.allowedAutorotateToPortraitUpsideDown },
                
                // Icons
                { "virtualRealitySupported", PlayerSettings.virtualRealitySupported },
                
                // Managed stripping
                { "stripEngineCode", PlayerSettings.stripEngineCode }
            };
        }

        private static Dictionary<string, object> GetQualitySettings()
        {
            var levels = new List<Dictionary<string, object>>();
            string[] names = QualitySettings.names;

            for (int i = 0; i < names.Length; i++)
            {
                // We can only get detailed info for current quality level
                if (i == QualitySettings.GetQualityLevel())
                {
                    levels.Add(new Dictionary<string, object>
                    {
                        { "index", i },
                        { "name", names[i] },
                        { "isCurrent", true },
                        { "pixelLightCount", QualitySettings.pixelLightCount },
                        { "shadows", QualitySettings.shadows.ToString() },
                        { "shadowResolution", QualitySettings.shadowResolution.ToString() },
                        { "shadowDistance", QualitySettings.shadowDistance },
                        { "shadowCascades", QualitySettings.shadowCascades },
                        { "antiAliasing", QualitySettings.antiAliasing },
                        { "softParticles", QualitySettings.softParticles },
                        { "realtimeReflectionProbes", QualitySettings.realtimeReflectionProbes },
                        { "vSyncCount", QualitySettings.vSyncCount },
                        { "lodBias", QualitySettings.lodBias },
                        { "maximumLODLevel", QualitySettings.maximumLODLevel },
                        { "particleRaycastBudget", QualitySettings.particleRaycastBudget },
                        { "asyncUploadTimeSlice", QualitySettings.asyncUploadTimeSlice },
                        { "asyncUploadBufferSize", QualitySettings.asyncUploadBufferSize },
                        { "anisotropicFiltering", QualitySettings.anisotropicFiltering.ToString() },
                        { "globalTextureMipmapLimit", QualitySettings.globalTextureMipmapLimit }
                    });
                }
                else
                {
                    levels.Add(new Dictionary<string, object>
                    {
                        { "index", i },
                        { "name", names[i] },
                        { "isCurrent", false }
                    });
                }
            }

            return new Dictionary<string, object>
            {
                { "currentLevel", QualitySettings.GetQualityLevel() },
                { "currentLevelName", names[QualitySettings.GetQualityLevel()] },
                { "levels", levels }
            };
        }

        private static Dictionary<string, object> GetPhysicsSettings()
        {
            return new Dictionary<string, object>
            {
                { "gravity", SerializationHelper.SerializeVector3(Physics.gravity) },
                { "defaultContactOffset", Physics.defaultContactOffset },
                { "sleepThreshold", Physics.sleepThreshold },
                { "defaultSolverIterations", Physics.defaultSolverIterations },
                { "defaultSolverVelocityIterations", Physics.defaultSolverVelocityIterations },
                { "queriesHitBackfaces", Physics.queriesHitBackfaces },
                { "queriesHitTriggers", Physics.queriesHitTriggers },
                { "bounceThreshold", Physics.bounceThreshold },
                { "defaultMaxDepenetrationVelocity", Physics.defaultMaxDepenetrationVelocity },
                { "autoSimulation", Physics.autoSimulation },
                { "autoSyncTransforms", Physics.autoSyncTransforms },
                { "reuseCollisionCallbacks", Physics.reuseCollisionCallbacks },
                { "interCollisionDistance", Physics.interCollisionDistance },
                { "interCollisionStiffness", Physics.interCollisionStiffness }
            };
        }

        private static Dictionary<string, object> GetPhysics2DSettings()
        {
            return new Dictionary<string, object>
            {
                { "gravity", SerializationHelper.SerializeVector2(Physics2D.gravity) },
                { "defaultContactOffset", Physics2D.defaultContactOffset },
                { "velocityIterations", Physics2D.velocityIterations },
                { "positionIterations", Physics2D.positionIterations },
                { "queriesHitTriggers", Physics2D.queriesHitTriggers },
                { "queriesStartInColliders", Physics2D.queriesStartInColliders },
                { "callbacksOnDisable", Physics2D.callbacksOnDisable },
                { "reuseCollisionCallbacks", Physics2D.reuseCollisionCallbacks },
                { "autoSyncTransforms", Physics2D.autoSyncTransforms }
            };
        }

        private static Dictionary<string, object> GetTagsAndLayers()
        {
            // Get tags
            var tags = new List<string>(UnityEditorInternal.InternalEditorUtility.tags);

            // Get layers
            var layers = new List<Dictionary<string, object>>();
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layers.Add(new Dictionary<string, object>
                    {
                        { "index", i },
                        { "name", layerName }
                    });
                }
            }

            // Get sorting layers
            var sortingLayers = new List<Dictionary<string, object>>();
            foreach (var layer in SortingLayer.layers)
            {
                sortingLayers.Add(new Dictionary<string, object>
                {
                    { "id", layer.id },
                    { "name", layer.name },
                    { "value", layer.value }
                });
            }

            return new Dictionary<string, object>
            {
                { "tags", tags },
                { "layers", layers },
                { "sortingLayers", sortingLayers }
            };
        }

        private static Dictionary<string, object> GetInputSettings()
        {
            var axes = new List<Dictionary<string, object>>();

            // Read InputManager.asset
            var inputManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (inputManager.Length > 0)
            {
                var serializedObject = new SerializedObject(inputManager[0]);
                var axesProperty = serializedObject.FindProperty("m_Axes");

                if (axesProperty != null && axesProperty.isArray)
                {
                    for (int i = 0; i < axesProperty.arraySize; i++)
                    {
                        var axis = axesProperty.GetArrayElementAtIndex(i);
                        axes.Add(new Dictionary<string, object>
                        {
                            { "name", axis.FindPropertyRelative("m_Name")?.stringValue },
                            { "descriptiveName", axis.FindPropertyRelative("descriptiveName")?.stringValue },
                            { "negativeButton", axis.FindPropertyRelative("negativeButton")?.stringValue },
                            { "positiveButton", axis.FindPropertyRelative("positiveButton")?.stringValue },
                            { "altNegativeButton", axis.FindPropertyRelative("altNegativeButton")?.stringValue },
                            { "altPositiveButton", axis.FindPropertyRelative("altPositiveButton")?.stringValue },
                            { "gravity", axis.FindPropertyRelative("gravity")?.floatValue },
                            { "dead", axis.FindPropertyRelative("dead")?.floatValue },
                            { "sensitivity", axis.FindPropertyRelative("sensitivity")?.floatValue },
                            { "type", axis.FindPropertyRelative("type")?.intValue },
                            { "axis", axis.FindPropertyRelative("axis")?.intValue },
                            { "joyNum", axis.FindPropertyRelative("joyNum")?.intValue }
                        });
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "axes", axes },
                { "axisCount", axes.Count }
            };
        }

        private static Dictionary<string, object> GetGraphicsSettings()
        {
            var result = new Dictionary<string, object>();

            // Render pipeline
            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP != null)
            {
                result["renderPipelineAsset"] = SerializationHelper.SerializeUnityObjectReference(currentRP);
                result["renderPipelineType"] = currentRP.GetType().Name;
            }
            else
            {
                result["renderPipelineAsset"] = null;
                result["renderPipelineType"] = "Built-in";
            }

            // Transparency sort mode
            result["transparencySortMode"] = GraphicsSettings.transparencySortMode.ToString();
            result["transparencySortAxis"] = SerializationHelper.SerializeVector3(GraphicsSettings.transparencySortAxis);

            // Lightmap settings
            result["lightsUseLinearIntensity"] = GraphicsSettings.lightsUseLinearIntensity;
            result["lightsUseColorTemperature"] = GraphicsSettings.lightsUseColorTemperature;

            // Default render queue
            result["defaultRenderingLayerMask"] = GraphicsSettings.defaultRenderingLayerMask;

            // Shader settings
            result["logWhenShaderIsCompiled"] = GraphicsSettings.logWhenShaderIsCompiled;

            // Get tier settings (only available in Unity 2022.3, removed in Unity 6.x)
            #if !UNITY_6_OR_NEWER
            var tierSettings = new List<Dictionary<string, object>>();
            foreach (GraphicsTier tier in Enum.GetValues(typeof(GraphicsTier)))
            {
                var settings = UnityEditor.Rendering.EditorGraphicsSettings.GetTierSettings(EditorUserBuildSettings.selectedBuildTargetGroup, tier);
                tierSettings.Add(new Dictionary<string, object>
                {
                    { "tier", tier.ToString() },
                    { "standardShaderQuality", settings.standardShaderQuality.ToString() },
                    { "renderingPath", settings.renderingPath.ToString() },
                    { "hdr", settings.hdr },
                    { "hdrMode", settings.hdrMode.ToString() },
                    { "realtimeGICPUUsage", settings.realtimeGICPUUsage.ToString() }
                });
            }
            result["tierSettings"] = tierSettings;
            #else
            result["tierSettings"] = new List<object>(); // Tier settings removed in Unity 6.x
            #endif

            return result;
        }

        private static Dictionary<string, object> GetBuildSettings()
        {
            // Get scenes in build
            var scenes = new List<Dictionary<string, object>>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(new Dictionary<string, object>
                {
                    { "path", scene.path },
                    { "guid", scene.guid.ToString() },
                    { "enabled", scene.enabled }
                });
            }

            return new Dictionary<string, object>
            {
                { "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() },
                { "selectedBuildTargetGroup", EditorUserBuildSettings.selectedBuildTargetGroup.ToString() },
                { "development", EditorUserBuildSettings.development },
                { "allowDebugging", EditorUserBuildSettings.allowDebugging },
                { "connectProfiler", EditorUserBuildSettings.connectProfiler },
                { "buildWithDeepProfilingSupport", EditorUserBuildSettings.buildWithDeepProfilingSupport },
                { "scenes", scenes },
                { "sceneCount", scenes.Count }
            };
        }

        private static Dictionary<string, object> GetTimeSettings()
        {
            return new Dictionary<string, object>
            {
                { "fixedDeltaTime", Time.fixedDeltaTime },
                { "maximumDeltaTime", Time.maximumDeltaTime },
                { "timeScale", Time.timeScale },
                { "maximumParticleDeltaTime", Time.maximumParticleDeltaTime },
                { "captureDeltaTime", Time.captureDeltaTime },
                { "captureFramerate", Time.captureFramerate }
            };
        }

        private static Dictionary<string, object> GetAudioSettings()
        {
            var config = AudioSettings.GetConfiguration();
            return new Dictionary<string, object>
            {
                { "speakerMode", config.speakerMode.ToString() },
                { "dspBufferSize", config.dspBufferSize },
                { "sampleRate", config.sampleRate },
                { "numRealVoices", config.numRealVoices },
                { "numVirtualVoices", config.numVirtualVoices },
                { "driverCapabilities", AudioSettings.driverCapabilities.ToString() },
                { "outputSampleRate", AudioSettings.outputSampleRate }
            };
        }
    }
}
