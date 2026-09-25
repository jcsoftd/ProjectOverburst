using UnityEngine;

public enum SwordDistortionVersion { PressureWave, BladeTrail }

[CreateAssetMenu(menuName = "OVERBURST/Weapons/One Hand Sword Distortion Style")]
public sealed class OneHandSwordDistortionStyle : ScriptableObject
{
    [InspectorName("한손검 비교 버전")]
    public SwordDistortionVersion version;
}
