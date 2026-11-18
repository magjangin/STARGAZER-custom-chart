using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        private sealed class AreaInjectorMetadata
        {
            public ConstructorInfo? LayerCtor { get; init; }
            public MethodInfo? NotesAddMethod { get; set; }
        }

        private sealed class BeatInfoInjectorMetadata
        {
            public ConstructorInfo? DefaultCtor { get; init; }
            public FieldInfo? BeatSplitField { get; init; }
            public PropertyInfo? BeatSplitProp { get; init; }
            public FieldInfo? BeatIndexField { get; init; }
            public PropertyInfo? BeatIndexProp { get; init; }

            public bool CanWriteDirect =>
                (BeatSplitField is not null || BeatSplitProp is not null)
                && (BeatIndexField is not null || BeatIndexProp is not null);

            public void WriteDirect(object beatInfo, int split, int index)
            {
                if (BeatSplitField is not null) BeatSplitField.SetValue(beatInfo, split);
                else BeatSplitProp!.SetValue(beatInfo, split);

                if (BeatIndexField is not null) BeatIndexField.SetValue(beatInfo, index);
                else BeatIndexProp!.SetValue(beatInfo, index);
            }
        }

        private sealed class NoteInjectorMetadata
        {
            public ConstructorInfo? Ctor3 { get; init; }
            public PropertyInfo? PropertyProp { get; init; }
            public FieldInfo? PropertyField { get; init; }

            public bool CanSetProperty =>
                (PropertyProp is not null && PropertyProp.CanWrite) || PropertyField is not null;

            public void SetProperty(object note, object propValue)
            {
                if (PropertyProp is not null && PropertyProp.CanWrite) PropertyProp.SetValue(note, propValue);
                else PropertyField?.SetValue(note, propValue);
            }
        }

        private sealed class NotePropertyInjectorMetadata
        {
            public ConstructorInfo? DefaultCtor { get; init; }
            public PropertyInfo? ExprProp { get; init; }
            public FieldInfo? ExprField { get; init; }
            public PropertyInfo? LinkedProp { get; init; }
            public FieldInfo? LinkedField { get; init; }
            public object? LinkedNone { get; init; }
            public object? LinkedStart { get; init; }
            public object? LinkedEnd { get; init; }

            public void Apply(object propObj, object? templateExprHolder, object linkedEnumVal)
            {
                if (templateExprHolder is not null)
                {
                    if (ExprProp is not null && ExprProp.CanWrite) ExprProp.SetValue(propObj, templateExprHolder);
                    else if (ExprField is not null) ExprField.SetValue(propObj, templateExprHolder);
                }

                if (LinkedProp is not null && LinkedProp.CanWrite) LinkedProp.SetValue(propObj, linkedEnumVal);
                else if (LinkedField is not null) LinkedField.SetValue(propObj, linkedEnumVal);
            }
        }

        private static readonly ConcurrentDictionary<(Type AreaType, Type LayerType), AreaInjectorMetadata> AreaMetaCache =
            new ConcurrentDictionary<(Type, Type), AreaInjectorMetadata>();

        private static readonly ConcurrentDictionary<Type, BeatInfoInjectorMetadata> BeatInfoMetaCache =
            new ConcurrentDictionary<Type, BeatInfoInjectorMetadata>();

        private static readonly ConcurrentDictionary<Type, NoteInjectorMetadata> NoteMetaCache =
            new ConcurrentDictionary<Type, NoteInjectorMetadata>();

        private static readonly ConcurrentDictionary<Type, NotePropertyInjectorMetadata> NotePropertyMetaCache =
            new ConcurrentDictionary<Type, NotePropertyInjectorMetadata>();
    }
}
