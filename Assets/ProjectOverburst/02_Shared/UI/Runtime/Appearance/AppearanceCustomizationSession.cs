using System;
using Overburst.Persistence;

namespace Overburst.Appearance
{
    public enum AppearancePreviewBody { Underwear, Equipment, Nude }
    public enum AppearanceFraming { Face, UpperBody, FullBody }

    // This session contains appearance values, never inventory/loadout or animation commands.
    public sealed class AppearanceCustomizationSession
    {
        private readonly CharacterAppearanceCatalog catalog;
        private readonly CharacterAppearanceSnapshot displayedOriginal;
        public CharacterAppearanceSnapshot CommittedAtOpen {get;}
        public CharacterAppearanceSnapshot Draft {get;private set;}
        public AppearancePreviewBody PreviewBody {get;private set;} = AppearancePreviewBody.Equipment;
        private AppearancePreviewBody developerReturnBody;
        private string developerReturnEquipmentId;
        public string EquipmentExampleId {get;private set;}
        public bool HeadgearVisible {get;private set;}=true;
        public void ToggleHeadgear(){HeadgearVisible=!HeadgearVisible;Changed?.Invoke();}
        public AppearanceFraming Framing {get;set;} = AppearanceFraming.FullBody;
        public bool UsedFallback {get;}
        public bool HasAppearanceChanges => !Draft.Equals(displayedOriginal) || UsedFallback;
        public event Action Changed;

        public AppearanceCustomizationSession(CharacterAppearanceCatalog catalog, CharacterAppearanceSnapshot saved)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            CommittedAtOpen = saved?.Copy();
            displayedOriginal = catalog.ResolveForDisplay(saved,out bool fallback);
            UsedFallback=fallback;
            Draft=displayedOriginal.Copy();
            EquipmentExampleId=catalog.equipmentExamples[0].id;
        }

        public void Edit(Action<CharacterAppearanceSnapshot> edit)
        {
            if (edit==null) throw new ArgumentNullException(nameof(edit));
            var next=Draft.Copy();edit(next);catalog.Validate(next);Draft=next;
            Changed?.Invoke();
        }
        public void ResetAppearance() {Draft=catalog.defaultAppearance.Copy();catalog.Validate(Draft);Changed?.Invoke();}
        public CharacterAppearanceSnapshot OriginalForComparison => displayedOriginal.Copy();
        public void ShowUnderwear() {PreviewBody=AppearancePreviewBody.Underwear;EquipmentExampleId=null;Changed?.Invoke();}
        public void ShowEquipment(string id)
        {
            CharacterAppearanceCatalog.Required(catalog.equipmentExamples,id,x=>x.id);
            EquipmentExampleId=id;PreviewBody=AppearancePreviewBody.Equipment;Changed?.Invoke();
        }
        // Developer extension calls this; no developer-state field exists in the account DTO.
        public void SetDeveloperNude(bool enabled)
        {
            if(enabled)
            {
                if(PreviewBody==AppearancePreviewBody.Nude)return;
                developerReturnBody=PreviewBody;developerReturnEquipmentId=EquipmentExampleId;
                PreviewBody=AppearancePreviewBody.Nude;EquipmentExampleId=null;
            }
            else
            {
                if(PreviewBody!=AppearancePreviewBody.Nude)return;
                PreviewBody=developerReturnBody;EquipmentExampleId=developerReturnEquipmentId;
            }
            Changed?.Invoke();
        }
        public CharacterAppearanceSnapshot CandidateForSave() {catalog.Validate(Draft);return Draft.Copy();}
    }
}

