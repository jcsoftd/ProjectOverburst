using System;
using System.IO;
using UnityEngine;

namespace Overburst.Persistence
{
    public static class AccountWorldLevelCommands
    {
        public const int MaximumStage = 10;
        public static int EligibleStage(int characterLevel) => Mathf.Clamp(characterLevel / 10 + 1, 1, MaximumStage);
        public static int RequiredLevel(int stage) => stage == 1 ? 1 : (stage - 1) * 10;
        public static int MinimumMonsterLevel(int stage) => (stage - 1) * 10 + 1;
        public static int MaximumMonsterLevel(int stage) => stage * 10;

        public static void Validate(AccountSnapshot state)
        {
            if (state == null || state.unlockedWorldLevel < 1 || state.unlockedWorldLevel > MaximumStage
                || state.appliedWorldLevel < 1 || state.appliedWorldLevel > state.unlockedWorldLevel)
                throw new InvalidDataException("Invalid character world level.");
        }

        public static bool Unlock(AccountSnapshot state)
        {
            Validate(state);
            int eligible = EligibleStage(state.level);
            if (eligible <= state.unlockedWorldLevel) return false;
            state.unlockedWorldLevel = eligible;
            return true;
        }

        public static bool Apply(AccountSnapshot state, int stage)
        {
            Validate(state);
            if (stage < 1 || stage > state.unlockedWorldLevel)
                throw new InvalidOperationException("해금한 단계만 적용할 수 있습니다.");
            if (state.appliedWorldLevel == stage) return false;
            state.appliedWorldLevel = stage;
            return true;
        }
    }
}
