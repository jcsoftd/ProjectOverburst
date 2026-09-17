using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AttackGeometryData))]
public sealed class AttackGeometryDataDrawer : PropertyDrawer
{
    internal static readonly string[] SerializedPropertyNames =
    {
        "rangeMultiplier",
        "angleMultiplier",
        "widthMultiplier",
        "vfxScaleMultiplier",
        "overrideForwardOffset"
    };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        int lineCount = 1;
        if (property.isExpanded)
        {
            lineCount += SerializedPropertyNames.Length;
            SerializedProperty overrideForwardOffset =
                property.FindPropertyRelative("overrideForwardOffset");
            if (overrideForwardOffset != null && overrideForwardOffset.boolValue)
                lineCount++;
        }

        return lineCount * EditorGUIUtility.singleLineHeight
            + (lineCount - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect line = NextLine(ref position);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        for (int i = 0; i < SerializedPropertyNames.Length; i++)
        {
            SerializedProperty child = property.FindPropertyRelative(SerializedPropertyNames[i]);
            DrawChild(NextLine(ref position), child, SerializedPropertyNames[i]);
        }

        SerializedProperty overrideForwardOffset =
            property.FindPropertyRelative("overrideForwardOffset");
        if (overrideForwardOffset != null && overrideForwardOffset.boolValue)
        {
            SerializedProperty forwardOffset = property.FindPropertyRelative("forwardOffset");
            DrawChild(
                NextLine(ref position),
                forwardOffset,
                "forwardOffset");
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static Rect NextLine(ref Rect position)
    {
        Rect line = new Rect(
            position.x,
            position.y,
            position.width,
            EditorGUIUtility.singleLineHeight);
        position.y += EditorGUIUtility.singleLineHeight
            + EditorGUIUtility.standardVerticalSpacing;
        return line;
    }

    private static void DrawChild(Rect position, SerializedProperty child, string propertyName)
    {
        if (child != null)
        {
            EditorGUI.PropertyField(position, child);
            return;
        }

        EditorGUI.HelpBox(position, "Missing serialized field: " + propertyName, MessageType.Error);
    }
}

[CustomPropertyDrawer(typeof(AttackImpactData))]
public sealed class AttackImpactDataDrawer : PropertyDrawer
{
    internal static readonly string[] SerializedPropertyNames =
    {
        "damageMultiplier",
        "knockbackMultiplier",
        "hitStunMultiplier",
        "knockbackReactionDuration",
        "overrideTargetReaction",
        "triggersOnHitEffects",
        "airborneImpulse",
        "airborneStunDuration"
    };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        int lineCount = property.isExpanded ? SerializedPropertyNames.Length + 1 : 1;
        return lineCount * EditorGUIUtility.singleLineHeight
            + (lineCount - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect line = TakeLine(ref position);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < SerializedPropertyNames.Length; i++)
            {
                SerializedProperty child =
                    property.FindPropertyRelative(SerializedPropertyNames[i]);
                DrawChild(
                    TakeLine(ref position),
                    child,
                    SerializedPropertyNames[i]);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static Rect TakeLine(ref Rect position)
    {
        Rect line = new Rect(
            position.x,
            position.y,
            position.width,
            EditorGUIUtility.singleLineHeight);
        position.y += EditorGUIUtility.singleLineHeight
            + EditorGUIUtility.standardVerticalSpacing;
        return line;
    }

    private static void DrawChild(Rect position, SerializedProperty child, string propertyName)
    {
        if (child != null)
        {
            EditorGUI.PropertyField(position, child);
            return;
        }

        EditorGUI.HelpBox(position, "Missing serialized field: " + propertyName, MessageType.Error);
    }
}

public static class WeaponDataPropertyDrawerValidation
{
    private const string ComboAssetPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Combos/OneHandSwordPrimaryCombo.asset";

    public static void RunOnceFromCommandLine()
    {
        MeleeComboDefinition combo =
            AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(ComboAssetPath);
        if (combo == null)
            throw new System.InvalidOperationException($"Combo asset not found: {ComboAssetPath}");

        SerializedObject serializedCombo = new SerializedObject(combo);
        SerializedProperty steps = Require(serializedCombo.FindProperty("steps"), "steps");
        if (steps.arraySize == 0)
            throw new System.InvalidOperationException("Combo has no steps.");

        SerializedProperty phases = Require(
            steps.GetArrayElementAtIndex(0).FindPropertyRelative("attackPhases"),
            "steps[0].attackPhases");
        if (phases.arraySize == 0)
            throw new System.InvalidOperationException("First combo step has no attack phases.");

        SerializedProperty phase = phases.GetArrayElementAtIndex(0);
        ValidateChildren(
            Require(phase.FindPropertyRelative("geometry"), "geometry"),
            AttackGeometryDataDrawer.SerializedPropertyNames);
        Require(
            phase.FindPropertyRelative("geometry").FindPropertyRelative("forwardOffset"),
            "geometry.forwardOffset");
        ValidateChildren(
            Require(phase.FindPropertyRelative("impact"), "impact"),
            AttackImpactDataDrawer.SerializedPropertyNames);

        Debug.Log("[WeaponDataPropertyDrawerValidation] PASS");
    }

    private static void ValidateChildren(SerializedProperty parent, string[] childNames)
    {
        for (int i = 0; i < childNames.Length; i++)
            Require(parent.FindPropertyRelative(childNames[i]), $"{parent.propertyPath}.{childNames[i]}");
    }

    private static SerializedProperty Require(SerializedProperty property, string path)
    {
        if (property == null)
            throw new System.InvalidOperationException($"Serialized property not found: {path}");
        return property;
    }
}
