using System;
using UnityEngine;

/// <summary>
/// 몬스터 피격 추가음 묶음 (2026-09-30 청음 결정).
/// 몬스터가 맞으면 공용 피격음(Stab 01~03)이 항상 나고, 그 위에 이 묶음의 세트 하나가 얹힌다.
/// 세트는 타격마다 번갈아(바로 앞 세트는 피해서) 고른다. 세트 = 동시에 겹쳐 내는 층 목록.
/// </summary>
[CreateAssetMenu(menuName = "OVERBURST/Audio/Monster Hit Sfx Bundle")]
public sealed class MonsterHitSfxBundle : ScriptableObject
{
    [Serializable]
    public sealed class Layer
    {
        public AudioClip clip;
        [Tooltip("공용 피격음(1.0) 기준 볼륨. 1을 넘으면 같은 클립을 한 번 더 겹쳐 증폭한다(최대 2).")]
        [Range(0f, 2f)] public float volume = 1f;
    }

    [Serializable]
    public sealed class Set
    {
        public string name;
        public Layer[] layers = Array.Empty<Layer>();
    }

    public Set[] sets = Array.Empty<Set>();
    [Tooltip("치명타·처치일 때 대신 쓰는 묶음. 비어 있으면 이 묶음 그대로.")]
    public MonsterHitSfxBundle criticalBundle;

    [NonSerialized] private int lastSet = -1;

    public Set PickSet()
    {
        if (sets == null || sets.Length == 0) return null;
        int count = sets.Length;
        int pick = UnityEngine.Random.Range(0, count);
        if (count > 1 && pick == lastSet) pick = (pick + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
        lastSet = pick;
        return sets[pick];
    }
}
