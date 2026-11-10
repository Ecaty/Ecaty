using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class ResourceMigration
{
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
    public static object Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before moving resources");
        var rows=JArray.Parse(File.ReadAllText("Verification/resource-move-manifest.json"));
        foreach(var row in rows){string old=(string)row["oldPath"],next=(string)row["newPath"],guid=(string)row["guid"];if(!old.StartsWith("Assets/Ecaty/Resources/",StringComparison.Ordinal)||!next.StartsWith("Assets/Ecaty/Resources/",StringComparison.Ordinal))throw new Exception("Move outside product Resources");if(AssetDatabase.AssetPathToGUID(old)!=guid)throw new Exception("Source GUID mismatch: "+old);if(!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(next)))throw new Exception("Destination exists: "+next);}
        // Folder creation/moves must enter the asset database before batching file imports.
        foreach(var row in rows.Where(row=>AssetDatabase.IsValidFolder((string)row["oldPath"]))){string old=(string)row["oldPath"],next=(string)row["newPath"];Folder(next.Substring(0,next.LastIndexOf('/')));string error=AssetDatabase.MoveAsset(old,next);if(!string.IsNullOrEmpty(error))throw new Exception(error);}
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var files=rows.Where(row=>!AssetDatabase.IsValidFolder((string)row["newPath"])).ToArray();
        foreach(var row in files){string next=(string)row["newPath"];Folder(next.Substring(0,next.LastIndexOf('/')));}
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.StartAssetEditing();
        try{foreach(var row in files){string error=AssetDatabase.MoveAsset((string)row["oldPath"],(string)row["newPath"]);if(!string.IsNullOrEmpty(error))throw new Exception(error);}}
        finally{AssetDatabase.StopAssetEditing();AssetDatabase.Refresh();}
        foreach(var row in rows){string next=(string)row["newPath"];if(AssetDatabase.AssetPathToGUID(next)!=(string)row["guid"])throw new Exception("GUID changed: "+next);if(next.EndsWith("/GalleryLayout.asset")||next.EndsWith("/Theme.asset")){var asset=AssetDatabase.LoadMainAssetAtPath(next);asset.name=Path.GetFileNameWithoutExtension(next);EditorUtility.SetDirty(asset);}}
        AssetDatabase.SaveAssets();var report=new JObject{["passed"]=true,["moves"]=rows.Count,["preservedGuids"]=rows.Count};File.WriteAllText("Verification/resource-migration-checks.json",report.ToString());return report;
    }
}
