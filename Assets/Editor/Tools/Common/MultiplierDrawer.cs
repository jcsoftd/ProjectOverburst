using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(MultiplierAttribute))]
public sealed class MultiplierDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Float)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        MultiplierAttribute settings = (MultiplierAttribute)attribute;
        EditorGUI.BeginProperty(position, label, property);
        float percent = property.floatValue * 100f;
        percent = EditorGUI.FloatField(position, label, percent);
        property.floatValue = Mathf.Max(settings.Minimum, percent / 100f);
        EditorGUI.EndProperty();
    }
}
