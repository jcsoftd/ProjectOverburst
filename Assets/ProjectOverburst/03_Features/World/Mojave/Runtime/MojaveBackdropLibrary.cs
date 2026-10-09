using UnityEngine;

namespace Overburst.Mojave
{
    [CreateAssetMenu(menuName="Overburst/Mojave/Backdrop library")]
    public sealed class MojaveBackdropLibrary : ScriptableObject
    {
        public MojavePatch[] tiles;
        public bool preserveAuthoredEnvironment;
        // Opt in for libraries whose candidates have already been curated and separated.
        public bool useAllNaturalCandidates;
    }
}
