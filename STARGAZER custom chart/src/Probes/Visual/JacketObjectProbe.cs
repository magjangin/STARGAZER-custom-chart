using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void ProbeCurrentTrackViewerImageMembers(object? instance, object[] args)
        {
            ProbeJacketLikeObject(
                "CurrentTrackViewer",
                "viewer.CurrentTrackViewer.jacketImage",
                LoggedCurrentTrackViewerImageHits,
                instance,
                args,
                "jacketImage",
                "JacketImage");
        }

        private static void ProbeResultPlayInfoJacketMembers(object? instance, object[] args)
        {
            ProbeJacketLikeObject(
                "ResultPlayInfo",
                "PlayInfoViewer.jacket",
                LoggedResultImageHits,
                instance,
                args,
                "jacket",
                "Jacket");
        }

        private static void ProbeJacketLikeObject(
            string source,
            string label,
            HashSet<string> loggedHits,
            object? instance,
            object[] args,
            params string[] memberNames)
        {
            try
            {
                object? owner = instance;
                if (owner is null && args.Length > 0)
                {
                    owner = args[0];
                }

                if (owner is null)
                {
                    if (loggedHits.Add($"{source}:null"))
                    {
                        MelonLogger.Msg($"[JacketProbe][{source}] target=null");
                    }

                    return;
                }

                object? probeTarget = ResolveFirstMemberValue(owner, memberNames) ?? owner;
                Type type = probeTarget.GetType();
                object? gameObject = TryGetMemberValue(probeTarget, type, "gameObject")
                    ?? TryGetMemberValue(probeTarget, type, "GameObject");
                object? transform = TryGetMemberValue(probeTarget, type, "transform")
                    ?? TryGetMemberValue(probeTarget, type, "Transform")
                    ?? (gameObject is null ? null : TryGetPropertyValue(gameObject, "transform"));
                string sceneName = TryGetSceneName(gameObject);
                string path = transform is null ? string.Empty : BuildTransformPath(transform);
                string hit = $"{source}:{label}={type.FullName};scene={sceneName};path={path}";

                if (loggedHits.Add(hit))
                {
                    MelonLogger.Msg($"[ImageProbe] {hit}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[JacketProbe][{source}] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static object? ResolveFirstMemberValue(object owner, IEnumerable<string> memberNames)
        {
            Type type = owner.GetType();
            foreach (string memberName in memberNames)
            {
                object? value = TryGetMemberValue(owner, type, memberName);
                if (value is not null)
                {
                    return value;
                }
            }

            return null;
        }

        private static string TryGetSceneName(object? gameObject)
        {
            if (gameObject is null)
            {
                return string.Empty;
            }

            object? sceneObj = TryGetPropertyValue(gameObject, "scene");
            if (sceneObj is null)
            {
                return string.Empty;
            }

            object? sceneName = TryGetPropertyValue(sceneObj, "name");
            return sceneName?.ToString() ?? string.Empty;
        }

        private static string BuildTransformPath(object transform)
        {
            string current = GetObjectName(transform);
            object? cursor = transform;
            for (int depth = 0; depth < 32; depth++)
            {
                object? parent = TryGetPropertyValue(cursor!, "parent");
                if (parent is null)
                {
                    break;
                }

                string parentName = GetObjectName(parent);
                current = $"{parentName}/{current}";
                cursor = parent;
            }

            return current;
        }

        private static string GetObjectName(object instance)
        {
            object? nameValue = TryGetPropertyValue(instance, "name");
            if (nameValue is not null)
            {
                return nameValue.ToString() ?? string.Empty;
            }

            return instance.GetType().Name;
        }

        private static object? TryGetPropertyValue(object instance, string propertyName)
        {
            try
            {
                Type type = instance.GetType();
                PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return property?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }
    }
}
