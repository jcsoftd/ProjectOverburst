using UnityEditor;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private MonsterTunerSaveReview saveReview;
        private void OpenSaveReview(bool closing)
        {
            if (session == null) return;
            CloseSaveReview();
            saveReview = MonsterTunerSaveReview.Create(this, session, catalog);
            // Native close/save waits for this Toolkit review, just as it waited for the previous confirmation dialog.
            if (closing) saveReview.ShowModalUtility(); else saveReview.ShowUtility();
        }
        private void CloseSaveReview() { if (saveReview != null) saveReview.Close(); saveReview = null; }
        internal string CommitReview(MonsterTunerSession reviewed, string fingerprint)
        {
            if (reviewed != session) return "선택 몬스터가 바뀌었습니다. 현재 몬스터의 저장 검토를 다시 여세요.";
            if (MonsterTunerSaveReview.Fingerprint(session) != fingerprint) return "검토 이후 편집이 바뀌었습니다. 검증 / 내역을 새로고침하세요.";
            var errors = MonsterTunerWriter.Validate(session, catalog);
            if (errors.Count > 0) { SetStatus(errors[0], true); return errors[0]; }
            if (!session.Dirty) return "저장할 변경이 없습니다.";
            var result = MonsterTunerWriter.Save(session, catalog); SetStatus(result.Message, !result.Success);
            if (!result.Success) return result.Message;
            ReleaseThumbnail(session.definitionGuid);
            stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader(); RenderNow();
            return null;
        }
    }
}
