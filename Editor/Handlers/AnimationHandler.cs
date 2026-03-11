using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace UnityMCP.Handlers
{
    public static class AnimationHandler
    {
        #region Animation Clip Operations

        public static Dictionary<string, object> CreateAnimationClip(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null)
                return Error("Missing parameters");

            string name = "New Animation";
            if (@params.TryGetValue("name", out object nameObj))
                name = nameObj.ToString();

            string savePath = "Assets/";
            if (@params.TryGetValue("savePath", out object pathObj))
                savePath = pathObj.ToString();

            if (!savePath.EndsWith(".anim"))
                savePath = System.IO.Path.Combine(savePath, name + ".anim");

            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

            var clip = new AnimationClip();
            clip.name = name;

            // Set loop
            if (@params.TryGetValue("loop", out object loopObj))
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = Convert.ToBoolean(loopObj);
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }

            // Set frame rate
            if (@params.TryGetValue("frameRate", out object fpsObj))
            {
                clip.frameRate = Convert.ToSingle(fpsObj);
            }

            // Set wrap mode
            if (@params.TryGetValue("wrapMode", out object wrapObj))
            {
                string wrapName = wrapObj.ToString().ToLower();
                clip.wrapMode = wrapName switch
                {
                    "loop" => WrapMode.Loop,
                    "pingpong" => WrapMode.PingPong,
                    "clampforever" => WrapMode.ClampForever,
                    "once" => WrapMode.Once,
                    _ => WrapMode.Default
                };
            }

            AssetDatabase.CreateAsset(clip, savePath);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clipName", clip.name },
                { "clipPath", savePath },
                { "guid", AssetDatabase.AssetPathToGUID(savePath) },
                { "frameRate", clip.frameRate },
                { "length", clip.length }
            };
        }

        public static Dictionary<string, object> GetAnimationClipInfo(Dictionary<string, object> @params)
        {
            if (@params == null || !@params.TryGetValue("clipPath", out object pathObj))
                return Error("Missing clipPath parameter");

            string clipPath = pathObj.ToString();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
                return Error($"Animation clip not found at '{clipPath}'");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            var curves = new List<Dictionary<string, object>>();
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = new List<Dictionary<string, object>>();
                foreach (var key in curve.keys)
                {
                    keys.Add(new Dictionary<string, object>
                    {
                        { "time", key.time },
                        { "value", key.value },
                        { "inTangent", key.inTangent },
                        { "outTangent", key.outTangent },
                        { "weightedMode", key.weightedMode.ToString() }
                    });
                }

                curves.Add(new Dictionary<string, object>
                {
                    { "path", binding.path },
                    { "propertyName", binding.propertyName },
                    { "type", binding.type.Name },
                    { "keyframes", keys }
                });
            }

            var objCurves = new List<Dictionary<string, object>>();
            foreach (var binding in objectBindings)
            {
                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var keys = new List<Dictionary<string, object>>();
                foreach (var key in keyframes)
                {
                    keys.Add(new Dictionary<string, object>
                    {
                        { "time", key.time },
                        { "value", key.value != null ? key.value.name : "null" }
                    });
                }

                objCurves.Add(new Dictionary<string, object>
                {
                    { "path", binding.path },
                    { "propertyName", binding.propertyName },
                    { "type", binding.type.Name },
                    { "keyframes", keys }
                });
            }

            var events = new List<Dictionary<string, object>>();
            foreach (var evt in AnimationUtility.GetAnimationEvents(clip))
            {
                events.Add(new Dictionary<string, object>
                {
                    { "functionName", evt.functionName },
                    { "time", evt.time },
                    { "intParameter", evt.intParameter },
                    { "floatParameter", evt.floatParameter },
                    { "stringParameter", evt.stringParameter }
                });
            }

            return new Dictionary<string, object>
            {
                { "name", clip.name },
                { "path", clipPath },
                { "length", clip.length },
                { "frameRate", clip.frameRate },
                { "wrapMode", clip.wrapMode.ToString() },
                { "isLooping", settings.loopTime },
                { "hasMotionCurves", clip.hasMotionCurves },
                { "hasRootCurves", clip.hasRootCurves },
                { "humanMotion", clip.humanMotion },
                { "legacy", clip.legacy },
                { "curves", curves },
                { "objectReferenceCurves", objCurves },
                { "events", events },
                { "curveCount", bindings.Length },
                { "objectCurveCount", objectBindings.Length }
            };
        }

        public static Dictionary<string, object> SetAnimationCurve(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("clipPath", out object pathObj))
                return Error("Missing clipPath parameter");

            string clipPath = pathObj.ToString();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
                return Error($"Animation clip not found at '{clipPath}'");

            if (!@params.TryGetValue("propertyName", out object propNameObj))
                return Error("Missing propertyName parameter");

            if (!@params.TryGetValue("type", out object typeObj))
                return Error("Missing type parameter (e.g., Transform, SpriteRenderer)");

            string relativePath = "";
            if (@params.TryGetValue("relativePath", out object relPathObj))
                relativePath = relPathObj.ToString();

            string propertyName = propNameObj.ToString();
            string typeName = typeObj.ToString();
            Type componentType = FindType(typeName);

            if (componentType == null)
                return Error($"Type '{typeName}' not found");

            // Parse keyframes
            if (!@params.TryGetValue("keyframes", out object kfObj))
                return Error("Missing keyframes parameter");

            var keyframes = ParseKeyframes(kfObj);
            if (keyframes == null || keyframes.Length == 0)
                return Error("Invalid or empty keyframes");

            Undo.RecordObject(clip, "Set animation curve");

            var binding = EditorCurveBinding.FloatCurve(relativePath, componentType, propertyName);
            var curve = new AnimationCurve(keyframes);
            AnimationUtility.SetEditorCurve(clip, binding, curve);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clipPath", clipPath },
                { "propertyName", propertyName },
                { "type", typeName },
                { "relativePath", relativePath },
                { "keyframeCount", keyframes.Length }
            };
        }

        public static Dictionary<string, object> RemoveAnimationCurve(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("clipPath", out object pathObj))
                return Error("Missing clipPath parameter");

            string clipPath = pathObj.ToString();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
                return Error($"Animation clip not found at '{clipPath}'");

            if (!@params.TryGetValue("propertyName", out object propNameObj))
                return Error("Missing propertyName parameter");

            if (!@params.TryGetValue("type", out object typeObj))
                return Error("Missing type parameter");

            string relativePath = "";
            if (@params.TryGetValue("relativePath", out object relPathObj))
                relativePath = relPathObj.ToString();

            Type componentType = FindType(typeObj.ToString());
            if (componentType == null)
                return Error($"Type '{typeObj}' not found");

            Undo.RecordObject(clip, "Remove animation curve");

            var binding = EditorCurveBinding.FloatCurve(relativePath, componentType, propNameObj.ToString());
            AnimationUtility.SetEditorCurve(clip, binding, null);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clipPath", clipPath },
                { "removedProperty", propNameObj.ToString() }
            };
        }

        public static Dictionary<string, object> AddAnimationKeyframe(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("clipPath", out object pathObj))
                return Error("Missing clipPath parameter");

            string clipPath = pathObj.ToString();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
                return Error($"Animation clip not found at '{clipPath}'");

            if (!@params.TryGetValue("propertyName", out object propNameObj))
                return Error("Missing propertyName parameter");

            if (!@params.TryGetValue("type", out object typeObj))
                return Error("Missing type parameter");

            if (!@params.TryGetValue("time", out object timeObj))
                return Error("Missing time parameter");

            if (!@params.TryGetValue("value", out object valueObj))
                return Error("Missing value parameter");

            string relativePath = "";
            if (@params.TryGetValue("relativePath", out object relPathObj))
                relativePath = relPathObj.ToString();

            Type componentType = FindType(typeObj.ToString());
            if (componentType == null)
                return Error($"Type '{typeObj}' not found");

            float time = Convert.ToSingle(timeObj);
            float value = Convert.ToSingle(valueObj);

            var binding = EditorCurveBinding.FloatCurve(relativePath, componentType, propNameObj.ToString());

            Undo.RecordObject(clip, "Add animation keyframe");

            var existingCurve = AnimationUtility.GetEditorCurve(clip, binding);
            if (existingCurve == null)
                existingCurve = new AnimationCurve();

            var keyframe = new Keyframe(time, value);

            if (@params.TryGetValue("inTangent", out object inTanObj))
                keyframe.inTangent = Convert.ToSingle(inTanObj);

            if (@params.TryGetValue("outTangent", out object outTanObj))
                keyframe.outTangent = Convert.ToSingle(outTanObj);

            existingCurve.AddKey(keyframe);
            AnimationUtility.SetEditorCurve(clip, binding, existingCurve);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clipPath", clipPath },
                { "propertyName", propNameObj.ToString() },
                { "time", time },
                { "value", value },
                { "totalKeyframes", existingCurve.keys.Length }
            };
        }

        public static Dictionary<string, object> AddAnimationEvent(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("clipPath", out object pathObj))
                return Error("Missing clipPath parameter");

            string clipPath = pathObj.ToString();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
                return Error($"Animation clip not found at '{clipPath}'");

            if (!@params.TryGetValue("functionName", out object funcObj))
                return Error("Missing functionName parameter");

            if (!@params.TryGetValue("time", out object timeObj))
                return Error("Missing time parameter");

            Undo.RecordObject(clip, "Add animation event");

            var evt = new AnimationEvent
            {
                functionName = funcObj.ToString(),
                time = Convert.ToSingle(timeObj)
            };

            if (@params.TryGetValue("intParameter", out object intP))
                evt.intParameter = Convert.ToInt32(intP);
            if (@params.TryGetValue("floatParameter", out object floatP))
                evt.floatParameter = Convert.ToSingle(floatP);
            if (@params.TryGetValue("stringParameter", out object strP))
                evt.stringParameter = strP.ToString();

            var events = AnimationUtility.GetAnimationEvents(clip).ToList();
            events.Add(evt);
            AnimationUtility.SetAnimationEvents(clip, events.ToArray());

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clipPath", clipPath },
                { "functionName", evt.functionName },
                { "time", evt.time },
                { "totalEvents", events.Count }
            };
        }

        public static Dictionary<string, object> SetClipSettings(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("clipPath", out object pathObj))
                return Error("Missing clipPath parameter");

            string clipPath = pathObj.ToString();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
                return Error($"Animation clip not found at '{clipPath}'");

            Undo.RecordObject(clip, "Set clip settings");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);

            if (@params.TryGetValue("loopTime", out object loopObj))
                settings.loopTime = Convert.ToBoolean(loopObj);
            if (@params.TryGetValue("loopBlend", out object loopBlendObj))
                settings.loopBlend = Convert.ToBoolean(loopBlendObj);
            if (@params.TryGetValue("cycleOffset", out object cycleObj))
                settings.cycleOffset = Convert.ToSingle(cycleObj);
            if (@params.TryGetValue("startTime", out object startObj))
                settings.startTime = Convert.ToSingle(startObj);
            if (@params.TryGetValue("stopTime", out object stopObj))
                settings.stopTime = Convert.ToSingle(stopObj);
            if (@params.TryGetValue("keepOriginalOrientation", out object keepOrientObj))
                settings.keepOriginalOrientation = Convert.ToBoolean(keepOrientObj);
            if (@params.TryGetValue("keepOriginalPositionXZ", out object keepPosXZObj))
                settings.keepOriginalPositionXZ = Convert.ToBoolean(keepPosXZObj);
            if (@params.TryGetValue("keepOriginalPositionY", out object keepPosYObj))
                settings.keepOriginalPositionY = Convert.ToBoolean(keepPosYObj);

            if (@params.TryGetValue("frameRate", out object fpsObj))
                clip.frameRate = Convert.ToSingle(fpsObj);

            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clipPath", clipPath },
                { "loopTime", settings.loopTime },
                { "frameRate", clip.frameRate }
            };
        }

        #endregion

        #region Animator Controller Operations

        public static Dictionary<string, object> CreateAnimatorController(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            string name = "New Animator Controller";
            if (@params != null && @params.TryGetValue("name", out object nameObj))
                name = nameObj.ToString();

            string savePath = "Assets/";
            if (@params != null && @params.TryGetValue("savePath", out object pathObj))
                savePath = pathObj.ToString();

            if (!savePath.EndsWith(".controller"))
                savePath = System.IO.Path.Combine(savePath, name + ".controller");

            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(savePath);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controllerName", controller.name },
                { "controllerPath", savePath },
                { "guid", AssetDatabase.AssetPathToGUID(savePath) },
                { "layerCount", controller.layers.Length },
                { "parameterCount", controller.parameters.Length }
            };
        }

        public static Dictionary<string, object> GetAnimatorControllerInfo(Dictionary<string, object> @params)
        {
            if (@params == null || !@params.TryGetValue("controllerPath", out object pathObj))
                return Error("Missing controllerPath parameter");

            string controllerPath = pathObj.ToString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
                return Error($"Animator controller not found at '{controllerPath}'");

            var layers = new List<Dictionary<string, object>>();
            foreach (var layer in controller.layers)
            {
                var states = new List<Dictionary<string, object>>();
                if (layer.stateMachine != null)
                {
                    foreach (var childState in layer.stateMachine.states)
                    {
                        var stateInfo = new Dictionary<string, object>
                        {
                            { "name", childState.state.name },
                            { "speed", childState.state.speed },
                            { "speedMultiplier", childState.state.speedParameterActive ? childState.state.speedParameter : "" },
                            { "motion", childState.state.motion != null ? childState.state.motion.name : "None" },
                            { "motionPath", childState.state.motion != null ? AssetDatabase.GetAssetPath(childState.state.motion) : "" },
                            { "tag", childState.state.tag },
                            { "writeDefaultValues", childState.state.writeDefaultValues },
                            { "position", new Dictionary<string, object> { { "x", childState.position.x }, { "y", childState.position.y } } }
                        };

                        // Get transitions from this state
                        var transitions = new List<Dictionary<string, object>>();
                        foreach (var transition in childState.state.transitions)
                        {
                            var conditions = new List<Dictionary<string, object>>();
                            foreach (var condition in transition.conditions)
                            {
                                conditions.Add(new Dictionary<string, object>
                                {
                                    { "parameter", condition.parameter },
                                    { "mode", condition.mode.ToString() },
                                    { "threshold", condition.threshold }
                                });
                            }

                            transitions.Add(new Dictionary<string, object>
                            {
                                { "destinationState", transition.destinationState != null ? transition.destinationState.name : "Exit" },
                                { "hasExitTime", transition.hasExitTime },
                                { "exitTime", transition.exitTime },
                                { "duration", transition.duration },
                                { "offset", transition.offset },
                                { "hasFixedDuration", transition.hasFixedDuration },
                                { "conditions", conditions }
                            });
                        }
                        stateInfo["transitions"] = transitions;

                        states.Add(stateInfo);
                    }
                }

                layers.Add(new Dictionary<string, object>
                {
                    { "name", layer.name },
                    { "defaultWeight", layer.defaultWeight },
                    { "blendingMode", layer.blendingMode.ToString() },
                    { "states", states },
                    { "defaultState", layer.stateMachine.defaultState != null ? layer.stateMachine.defaultState.name : "None" }
                });
            }

            var parameters = new List<Dictionary<string, object>>();
            foreach (var param in controller.parameters)
            {
                var paramInfo = new Dictionary<string, object>
                {
                    { "name", param.name },
                    { "type", param.type.ToString() }
                };
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        paramInfo["defaultValue"] = param.defaultFloat;
                        break;
                    case AnimatorControllerParameterType.Int:
                        paramInfo["defaultValue"] = param.defaultInt;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        paramInfo["defaultValue"] = param.defaultBool;
                        break;
                }
                parameters.Add(paramInfo);
            }

            return new Dictionary<string, object>
            {
                { "name", controller.name },
                { "path", controllerPath },
                { "layers", layers },
                { "parameters", parameters },
                { "layerCount", controller.layers.Length },
                { "parameterCount", controller.parameters.Length }
            };
        }

        public static Dictionary<string, object> AddAnimatorParameter(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("controllerPath", out object pathObj))
                return Error("Missing controllerPath parameter");

            if (!@params.TryGetValue("parameterName", out object nameObj))
                return Error("Missing parameterName parameter");

            string controllerPath = pathObj.ToString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
                return Error($"Animator controller not found at '{controllerPath}'");

            string paramType = "float";
            if (@params.TryGetValue("parameterType", out object typeObj))
                paramType = typeObj.ToString().ToLower();

            AnimatorControllerParameterType type = paramType switch
            {
                "float" => AnimatorControllerParameterType.Float,
                "int" => AnimatorControllerParameterType.Int,
                "bool" => AnimatorControllerParameterType.Bool,
                "trigger" => AnimatorControllerParameterType.Trigger,
                _ => AnimatorControllerParameterType.Float
            };

            Undo.RecordObject(controller, "Add animator parameter");
            controller.AddParameter(nameObj.ToString(), type);

            // Set default value
            if (@params.TryGetValue("defaultValue", out object defObj))
            {
                var allParams = controller.parameters;
                var param = allParams[allParams.Length - 1];
                switch (type)
                {
                    case AnimatorControllerParameterType.Float:
                        param.defaultFloat = Convert.ToSingle(defObj);
                        break;
                    case AnimatorControllerParameterType.Int:
                        param.defaultInt = Convert.ToInt32(defObj);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        param.defaultBool = Convert.ToBoolean(defObj);
                        break;
                }
                controller.parameters = allParams;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controllerPath", controllerPath },
                { "parameterName", nameObj.ToString() },
                { "parameterType", type.ToString() },
                { "totalParameters", controller.parameters.Length }
            };
        }

        public static Dictionary<string, object> AddAnimatorState(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("controllerPath", out object pathObj))
                return Error("Missing controllerPath parameter");

            if (!@params.TryGetValue("stateName", out object stateNameObj))
                return Error("Missing stateName parameter");

            string controllerPath = pathObj.ToString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
                return Error($"Animator controller not found at '{controllerPath}'");

            int layerIndex = 0;
            if (@params.TryGetValue("layerIndex", out object layerObj))
                layerIndex = Convert.ToInt32(layerObj);

            if (layerIndex >= controller.layers.Length)
                return Error($"Layer index {layerIndex} out of range (has {controller.layers.Length} layers)");

            var stateMachine = controller.layers[layerIndex].stateMachine;

            Undo.RecordObject(stateMachine, "Add animator state");

            var state = stateMachine.AddState(stateNameObj.ToString());

            // Set motion (animation clip)
            if (@params.TryGetValue("clipPath", out object clipPathObj))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPathObj.ToString());
                if (clip != null)
                    state.motion = clip;
            }

            // Set speed
            if (@params.TryGetValue("speed", out object speedObj))
                state.speed = Convert.ToSingle(speedObj);

            // Set tag
            if (@params.TryGetValue("tag", out object tagObj))
                state.tag = tagObj.ToString();

            // Set as default
            if (@params.TryGetValue("isDefault", out object defObj) && Convert.ToBoolean(defObj))
                stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controllerPath", controllerPath },
                { "stateName", state.name },
                { "layerIndex", layerIndex },
                { "motion", state.motion != null ? state.motion.name : "None" }
            };
        }

        public static Dictionary<string, object> AddAnimatorTransition(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("controllerPath", out object pathObj))
                return Error("Missing controllerPath parameter");

            if (!@params.TryGetValue("sourceState", out object sourceObj))
                return Error("Missing sourceState parameter");

            if (!@params.TryGetValue("destinationState", out object destObj))
                return Error("Missing destinationState parameter");

            string controllerPath = pathObj.ToString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
                return Error($"Animator controller not found at '{controllerPath}'");

            int layerIndex = 0;
            if (@params.TryGetValue("layerIndex", out object layerObj))
                layerIndex = Convert.ToInt32(layerObj);

            if (layerIndex >= controller.layers.Length)
                return Error($"Layer index {layerIndex} out of range");

            var stateMachine = controller.layers[layerIndex].stateMachine;
            string sourceName = sourceObj.ToString();
            string destName = destObj.ToString();

            AnimatorState sourceState = null;
            AnimatorState destState = null;

            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
                    sourceState = childState.state;
                if (childState.state.name.Equals(destName, StringComparison.OrdinalIgnoreCase))
                    destState = childState.state;
            }

            if (sourceState == null)
                return Error($"Source state '{sourceName}' not found");
            if (destState == null)
                return Error($"Destination state '{destName}' not found");

            Undo.RecordObject(sourceState, "Add animator transition");

            var transition = sourceState.AddTransition(destState);

            // Set transition properties
            if (@params.TryGetValue("hasExitTime", out object exitTimeObj))
                transition.hasExitTime = Convert.ToBoolean(exitTimeObj);
            if (@params.TryGetValue("exitTime", out object exitObj))
                transition.exitTime = Convert.ToSingle(exitObj);
            if (@params.TryGetValue("duration", out object durObj))
                transition.duration = Convert.ToSingle(durObj);
            if (@params.TryGetValue("offset", out object offObj))
                transition.offset = Convert.ToSingle(offObj);
            if (@params.TryGetValue("hasFixedDuration", out object fixedDurObj))
                transition.hasFixedDuration = Convert.ToBoolean(fixedDurObj);

            // Add conditions
            if (@params.TryGetValue("conditions", out object condObj) && condObj is List<object> condList)
            {
                foreach (var cond in condList)
                {
                    if (cond is Dictionary<string, object> condDict)
                    {
                        if (!condDict.TryGetValue("parameter", out object paramObj)) continue;

                        string modeName = "greater";
                        if (condDict.TryGetValue("mode", out object modeObj))
                            modeName = modeObj.ToString().ToLower();

                        AnimatorConditionMode mode = modeName switch
                        {
                            "greater" => AnimatorConditionMode.Greater,
                            "less" => AnimatorConditionMode.Less,
                            "equals" => AnimatorConditionMode.Equals,
                            "notequal" => AnimatorConditionMode.NotEqual,
                            "if" => AnimatorConditionMode.If,
                            "ifnot" => AnimatorConditionMode.IfNot,
                            _ => AnimatorConditionMode.Greater
                        };

                        float threshold = 0;
                        if (condDict.TryGetValue("threshold", out object threshObj))
                            threshold = Convert.ToSingle(threshObj);

                        transition.AddCondition(mode, threshold, paramObj.ToString());
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controllerPath", controllerPath },
                { "sourceState", sourceName },
                { "destinationState", destName },
                { "hasExitTime", transition.hasExitTime },
                { "duration", transition.duration },
                { "conditionCount", transition.conditions.Length }
            };
        }

        public static Dictionary<string, object> AddAnimatorLayer(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null || !@params.TryGetValue("controllerPath", out object pathObj))
                return Error("Missing controllerPath parameter");

            if (!@params.TryGetValue("layerName", out object nameObj))
                return Error("Missing layerName parameter");

            string controllerPath = pathObj.ToString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            if (controller == null)
                return Error($"Animator controller not found at '{controllerPath}'");

            Undo.RecordObject(controller, "Add animator layer");

            controller.AddLayer(nameObj.ToString());

            // Set weight
            if (@params.TryGetValue("defaultWeight", out object weightObj))
            {
                var layers = controller.layers;
                layers[layers.Length - 1].defaultWeight = Convert.ToSingle(weightObj);
                controller.layers = layers;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controllerPath", controllerPath },
                { "layerName", nameObj.ToString() },
                { "totalLayers", controller.layers.Length }
            };
        }

        #endregion

        #region Assign Animator to GameObject

        public static Dictionary<string, object> AssignAnimator(Dictionary<string, object> @params)
        {
            if (!MutationHandler.MutationsEnabled)
                return ErrorMutationsDisabled();

            if (@params == null)
                return Error("Missing parameters");

            GameObject go = FindGameObject(@params);
            if (go == null)
                return Error("GameObject not found");

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(go);
            }

            if (@params.TryGetValue("controllerPath", out object controllerPathObj))
            {
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPathObj.ToString());
                if (controller != null)
                {
                    Undo.RecordObject(animator, "Set animator controller");
                    animator.runtimeAnimatorController = controller;
                }
                else
                {
                    return Error($"Controller not found at '{controllerPathObj}'");
                }
            }

            if (@params.TryGetValue("avatarPath", out object avatarPathObj))
            {
                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPathObj.ToString());
                if (avatar != null)
                {
                    Undo.RecordObject(animator, "Set avatar");
                    animator.avatar = avatar;
                }
            }

            if (@params.TryGetValue("applyRootMotion", out object rootMotionObj))
            {
                Undo.RecordObject(animator, "Set root motion");
                animator.applyRootMotion = Convert.ToBoolean(rootMotionObj);
            }

            EditorUtility.SetDirty(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "controller", animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "None" },
                { "avatar", animator.avatar != null ? animator.avatar.name : "None" }
            };
        }

        #endregion

        #region Helper Methods

        private static GameObject FindGameObject(Dictionary<string, object> @params)
        {
            if (@params.TryGetValue("instanceID", out object idObj))
            {
                int instanceID = Convert.ToInt32(idObj);
                return EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            }
            if (@params.TryGetValue("path", out object pathObj))
                return HierarchyHandler.FindGameObjectByPath(pathObj.ToString());
            if (@params.TryGetValue("name", out object nameObj))
                return GameObject.Find(nameObj.ToString());
            return null;
        }

        private static Keyframe[] ParseKeyframes(object kfObj)
        {
            if (kfObj is List<object> kfList)
            {
                var keyframes = new List<Keyframe>();
                foreach (var kf in kfList)
                {
                    if (kf is Dictionary<string, object> kfDict)
                    {
                        float time = 0, value = 0;
                        if (kfDict.TryGetValue("time", out object t)) time = Convert.ToSingle(t);
                        if (kfDict.TryGetValue("value", out object v)) value = Convert.ToSingle(v);

                        var keyframe = new Keyframe(time, value);

                        if (kfDict.TryGetValue("inTangent", out object inT))
                            keyframe.inTangent = Convert.ToSingle(inT);
                        if (kfDict.TryGetValue("outTangent", out object outT))
                            keyframe.outTangent = Convert.ToSingle(outT);

                        keyframes.Add(keyframe);
                    }
                }
                return keyframes.ToArray();
            }
            return null;
        }

        private static Type FindType(string typeName)
        {
            Type type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;

                type = assembly.GetType($"UnityEngine.{typeName}");
                if (type != null) return type;
            }
            return null;
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "error", message } };
        }

        private static Dictionary<string, object> ErrorMutationsDisabled()
        {
            return new Dictionary<string, object>
            {
                { "error", "Mutations are disabled. Enable them in Window > Unity MCP settings." },
                { "code", -32001 }
            };
        }

        #endregion
    }
}
