using System;
using Overburst.Persistence;

// One disposable training account, projected through the production inventory/equipment path.
public sealed class TutorialAccountScope : IDisposable
{
    private readonly AccountGameplaySession original;
    private readonly AccountSnapshot before;
    private readonly AccountGameplaySession training;
    private bool restored;
    public const string FireGemId = "item.elementgem.fire.common";
    public AccountGameplaySession Training => training;
    public AccountGameplaySession Original => original;

    public TutorialAccountScope(AccountSnapshot loadout)
    {
        original = AccountGameplaySession.Current ?? throw new InvalidOperationException("계정이 준비되지 않았습니다.");
        if (original.IsEditing || original.NeedsProjectionRecovery || !WorldSessionState.IsHideout)
            throw new InvalidOperationException("마을에서 계정 준비를 마친 뒤 훈련할 수 있습니다.");
        CurrencyPickupBatch.DrainPending();
        if (Overburst.Quests.QuestSession.Current != null && !Overburst.Quests.QuestSession.Current.DrainPending())
            throw new InvalidOperationException("의뢰 진행을 먼저 저장해야 합니다.");
        if (PlayerProgression.Current != null && !PlayerProgression.Current.FlushPendingExperience())
            throw new InvalidOperationException("경험치를 먼저 저장해야 합니다.");
        if (!original.FlushPendingSave()) throw new InvalidOperationException("계정 저장에 실패했습니다. 훈련을 시작하지 않았습니다.");
        before = original.Read();
        AccountInvariants.Validate(loadout, original.ContentRegistry);
        training = new AccountGameplaySession(original.Owner, original.ContentRegistry,
            EasySaveAccountStore.CreateMemoryOnly(), loadout);
        original.Detach();
        try
        {
            AccountGameplayProjection.Restore(loadout, original.Owner, original.ContentRegistry);
            training.Attach();
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public void Restore()
    {
        if (restored) return;
        if (AccountGameplaySession.Current != training && AccountGameplaySession.Current != null
            && AccountGameplaySession.Current != original) throw new InvalidOperationException("다른 계정이 연결되어 훈련 반환을 중단했습니다.");
        training.Detach();
        AccountGameplayProjection.Restore(before, original.Owner, original.ContentRegistry);
        original.Attach();
        restored = true;
    }
    public void Dispose() => Restore();
}
