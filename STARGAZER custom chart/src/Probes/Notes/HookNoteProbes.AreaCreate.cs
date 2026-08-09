using System;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void TryRunAreaCreationTest(PropertyInfo areasProp, object areasValue, object templateArea)
        {
            try
            {
                bool areasWritable = areasProp.CanWrite && areasProp.SetMethod is not null;
                string areasTypeName = areasValue.GetType().FullName ?? areasValue.GetType().Name;
                int countBefore = TryGetCollectionCount(areasValue) ?? -1;

                Type areaType = templateArea.GetType();
                object? newArea = TryInstantiateAreaViaLayerConstructor(areaType, templateArea)
                    ?? InstantiateIl2CppObject(areaType);
                if (newArea is null)
                {
                    MelonLogger.Warning($"[AreaCreateTest] areasWritable={areasWritable} areasType={areasTypeName} countBefore={countBefore} Area 인스턴스 생성 실패");
                    return;
                }

                bool bpmCopied = TrySetValueByNameCandidates(newArea, new[] { "areabpm" }, TryGetMemberValue(templateArea, areaType, "AreaBPM"));
                bool lengthCopied = TrySetValueByNameCandidates(newArea, new[] { "length" }, TryGetMemberValue(templateArea, areaType, "length"));
                bool layerCopied = TrySetValueByNameCandidates(newArea, new[] { "targetlayer" }, TryGetMemberValue(templateArea, areaType, "TargetLayer"));

                bool added = TryAddToNotesCollection(areasValue, newArea);
                int countAfter = TryGetCollectionCount(areasValue) ?? -1;

                MelonLogger.Msg($"[AreaCreateTest] areasWritable={areasWritable} areasType={areasTypeName} instantiated=True bpmCopied={bpmCopied} lengthCopied={lengthCopied} layerCopied={layerCopied} added={added} countBefore={countBefore} countAfter={countAfter}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AreaCreateTest] 실패: {ex.GetType().Name} {ex.Message}");
            }
        }

        private static object? TryInstantiateAreaViaLayerConstructor(Type areaType, object templateArea)
        {
            object? owningLayer = TryGetMemberValue(templateArea, areaType, "TargetLayer");
            if (owningLayer is null)
                return null;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (ConstructorInfo ctor in areaType.GetConstructors(flags))
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.Name.Contains("Layer"))
                {
                    try
                    {
                        object area = ctor.Invoke(new[] { owningLayer });
                        // 마디마다 불리므로 한 번만 남긴다(2026-08-09 실측: 곡 하나에 73줄).
                        if (LogOnce("AreaCreateTest.AreaLayerCtor"))
                        {
                            MelonLogger.Msg("[AreaCreateTest] Area(Layer) 생성자로 Area를 성공적으로 생성했습니다. (처음 1회만 기록)");
                        }

                        return area;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[AreaCreateTest] Area(Layer) 생성자 호출에 실패했습니다: {ex.Message}");
                        return null;
                    }
                }
            }

            return null;
        }
    }
}
