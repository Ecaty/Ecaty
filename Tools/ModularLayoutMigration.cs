using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class ModularLayoutMigration
{
    static void Folder(string path) {
        if(AssetDatabase.IsValidFolder(path))return;
        var parent=Path.GetDirectoryName(path).Replace('\\','/');
        Folder(parent);
        if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))))throw new IOException("Could not create "+path);
    }
    public static object Run(string staging) {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Stop Play and finish compiling before migration.");
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(staging,"migration.json")));
        const string reportPath="Verification/modular-layout-migration.json";
        var report=File.Exists(reportPath)?JObject.Parse(File.ReadAllText(reportPath)):new JObject{["passed"]=false,["moves"]=new JArray(),["patched"]=new JArray()};
        if((bool)report["passed"])throw new InvalidOperationException("Migration already applied; use the layout verification instead.");
        int alreadyMoved=((JArray)report["moves"]).Count;
        report.Remove("error");
        foreach(var row in plan["assets"]) {
            string path=AssetDatabase.GUIDToAssetPath((string)row["guid"]);
            if(string.IsNullOrEmpty(path))throw new InvalidOperationException("GUID missing since planning: "+row["from"]);
            if(row["sha256"].Type!=JTokenType.Null) {
                using(var hash=System.Security.Cryptography.SHA256.Create()) {
                    string actual=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
                    if(actual!=(string)row["sha256"])throw new InvalidOperationException("File changed since planning: "+path);
                }
            }
        }
        EditorApplication.LockReloadAssemblies();
        AssetDatabase.DisallowAutoRefresh();
        try {
            foreach(var row in plan["moves"].Skip(alreadyMoved)) {
                string from=(string)row["from"],to=(string)row["to"];
                Folder(Path.GetDirectoryName(to).Replace('\\','/'));
                string error=AssetDatabase.MoveAsset(from,to);
                if(!string.IsNullOrEmpty(error))throw new IOException(from+" -> "+to+": "+error);
                ((JArray)report["moves"]).Add(to);
                File.WriteAllText(reportPath,report.ToString());
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            string files=Path.Combine(staging,"files");
            foreach(string source in Directory.GetFiles(files,"*",SearchOption.AllDirectories)) {
                string dest=source.Substring(files.Length+1).Replace('\\','/');
                Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(source,dest,true);
                ((JArray)report["patched"]).Add(dest);
            }
            report["passed"]=true;
            return new { moved=((JArray)report["moves"]).Count,patched=((JArray)report["patched"]).Count };
        } catch(Exception e){report["error"]=e.ToString();throw;}
        finally {
            Directory.CreateDirectory("Verification");File.WriteAllText("Verification/modular-layout-migration.json",report.ToString());
            AssetDatabase.AllowAutoRefresh();EditorApplication.UnlockReloadAssemblies();AssetDatabase.Refresh();
        }
    }
}
