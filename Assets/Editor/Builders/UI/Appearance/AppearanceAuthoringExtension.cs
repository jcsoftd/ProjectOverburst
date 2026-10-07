using System.Collections.Generic;
using Overburst.Appearance;
using UnityEngine;

// Optional authoring modules are discovered through TypeCache. Core assets never reference their types.
public abstract class AppearanceAuthoringExtension
{
    public abstract void Prepare(CharacterAppearanceCatalog catalog);
    public abstract void CreateUI(Transform surface,AppearanceCustomizationPanel panel);
    public virtual void CheckAssets(AppearanceCustomizationPanel panel,List<string> checks){}
    public virtual void CheckPreview(AppearanceCustomizationPanel panel,List<string> checks){}
}
