#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Overburst.Appearance.AnimationPreview
{
    [Serializable] public sealed class AppearanceAnimationExclusion
    {
        public string animationId,clipName,category,assetGuid,assetPath,excludedAtUtc;
        public long localFileId;
    }
    [Serializable] public sealed class AppearanceAnimationExclusionDocument
    {
        public int version=1;
        public List<AppearanceAnimationExclusion> excluded=new List<AppearanceAnimationExclusion>();
    }
    // Developer selection list. It never deletes source clips or changes account/gameplay data.
    public sealed class AppearanceAnimationExclusions
    {
        public const string FileName="appearance-animation-exclusions.json";
        public readonly string Path;
        public AppearanceAnimationExclusionDocument Document {get;private set;}
        public AppearanceAnimationExclusions(string path=null)
        {
            string isolated=Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
            Path=path??System.IO.Path.Combine(string.IsNullOrEmpty(isolated)?Application.persistentDataPath:isolated,"Developer",FileName);
            Document=File.Exists(Path)?JsonUtility.FromJson<AppearanceAnimationExclusionDocument>(File.ReadAllText(Path)):new AppearanceAnimationExclusionDocument();
            if(Document==null||Document.version!=1||Document.excluded==null)throw new InvalidDataException("애니메이션 제외 목록을 읽을 수 없습니다.");
        }
        public bool Contains(string id)=>Document.excluded.Any(x=>x.animationId==id);
        public void Exclude(AppearanceAnimationOption option)
        {
            if(option==null||Contains(option.id))return;
            var item=new AppearanceAnimationExclusion{animationId=option.id,clipName=option.displayName,category=option.category,
                assetGuid=option.assetGuid,assetPath=option.assetPath,localFileId=option.localFileId,excludedAtUtc=DateTime.UtcNow.ToString("O")};
            Document.excluded.Add(item);
            try{Save();}catch{Document.excluded.Remove(item);throw;}
        }
        public void Restore(string id)
        {
            int index=Document.excluded.FindIndex(x=>x.animationId==id);if(index<0)return;
            var item=Document.excluded[index];Document.excluded.RemoveAt(index);
            try{Save();}catch{Document.excluded.Insert(index,item);throw;}
        }
        private void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            string pending=Path+".pending";File.WriteAllText(pending,JsonUtility.ToJson(Document,true));
            if(File.Exists(Path))File.Replace(pending,Path,Path+".backup");else File.Move(pending,Path);
        }
        public string Json()=>JsonUtility.ToJson(Document,true);
    }
}
#endif
