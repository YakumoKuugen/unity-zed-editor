using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SGame.Zed.Editor.ProjectGeneration
{
    /// <summary>
    /// Invokes the <c>AssetPostprocessor</c> generation hooks that Unity tooling relies on
    /// (<c>OnPreGeneratingCSProjectFiles</c>, <c>OnGeneratedCSProject</c>, <c>OnGeneratedSlnSolution</c>,
    /// <c>OnGeneratedCSProjectFiles</c>). Keeping these hooks means other editor packages which
    /// customise generated projects keep working when Zed is the selected editor.
    /// </summary>
    internal static class GenerationCallbacks
    {
        public static bool OnPreGeneratingCSProjectFiles()
        {
            var handled = false;
            foreach (var method in GetPostProcessorCallbacks(nameof(OnPreGeneratingCSProjectFiles)))
            {
                try
                {
                    var result = method.Invoke(null, Array.Empty<object>());
                    if (method.ReturnType == typeof(bool) && result is bool value)
                        handled |= value;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Zed] {method.DeclaringType?.FullName}.{method.Name} failed: {exception.Message}");
                }
            }

            return handled;
        }

        public static void OnGeneratedCSProjectFiles()
        {
            foreach (var method in GetPostProcessorCallbacks(nameof(OnGeneratedCSProjectFiles)))
            {
                try
                {
                    method.Invoke(null, Array.Empty<object>());
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Zed] {method.DeclaringType?.FullName}.{method.Name} failed: {exception.Message}");
                }
            }
        }

        public static string OnGeneratedCSProject(string path, string content)
        {
            return InvokeContentCallback(nameof(OnGeneratedCSProject), path, content);
        }

        public static string OnGeneratedSlnSolution(string path, string content)
        {
            return InvokeContentCallback(nameof(OnGeneratedSlnSolution), path, content);
        }

        private static string InvokeContentCallback(string name, string path, string content)
        {
            foreach (var method in GetPostProcessorCallbacks(name))
            {
                try
                {
                    var result = method.Invoke(null, new object[] { path, content });
                    if (method.ReturnType == typeof(string) && result is string updated)
                        content = updated;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Zed] {method.DeclaringType?.FullName}.{method.Name} failed: {exception.Message}");
                }
            }

            return content;
        }

        private static IEnumerable<MethodInfo> GetPostProcessorCallbacks(string name)
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<AssetPostprocessor>())
            {
                if (type.Assembly == typeof(GenerationCallbacks).Assembly)
                    continue;

                MethodInfo method = null;
                try
                {
                    method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                catch (Exception)
                {
                    // Ignore types that cannot be reflected over.
                }

                if (method != null)
                    yield return method;
            }
        }
    }
}
