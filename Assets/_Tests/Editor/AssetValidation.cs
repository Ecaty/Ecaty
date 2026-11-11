using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
#if ODIN_VALIDATOR
using Sirenix.OdinInspector.Editor.Validation;
using Sirenix.OdinValidator.Editor;

#endif
namespace Ecaty.Editor
{
    public static class AssetValidation
    {
        [MenuItem("Runtime/Tests/Asset validation")]
        public static void Menu()=>UnityEngine.Debug.Log(Run());
        public static object Run()
        {
#if ODIN_VALIDATOR
            var references=JArray.Parse(File.ReadAllText("Tests/Fixtures/resource-load-manifest.json"));
            var paths=references.Where(r=>r["composition"]!=null||(string)r["key"]=="Workshop/UI/WorkshopDock").Select(r=>(string)r["path"]).Append(AssetDatabase.GetAssetPath(RuntimeComposition.Current)).Append(GalleryConfigScript.AssetPath).Distinct().OrderBy(p=>p).ToArray();
            var rows=new JArray();int validations=0;
            foreach(string path in paths)using(var session=new ValidationSession("Project configuration",new[]{ValidationItem.FromAssetPath(path)}))
            {
                foreach(var result in session.ValidateEverythingEnumerator(openClosedScenes:false,showProgressBar:false)){
                    validations++;string severity=result.ResultType.ToString();if(severity=="Valid"||severity=="Ignore")continue;
                    rows.Add(new JObject{["asset"]=path,["path"]=result.Path,["severity"]=severity,["message"]=result.Message,["validator"]=result.ValidatorType?.FullName});}
            }
            var report=new JObject{["installed"]=true,["passed"]=!rows.Any(r=>(string)r["severity"]=="Error"),["validations"]=validations,["assets"]=new JArray(paths),["results"]=rows};
#else
            var report=new JObject{["installed"]=false,["passed"]=true,["skipped"]=true,["reason"]="Odin Validator is not imported"};
#endif
            File.WriteAllText("Verification/asset-validation-checks.json",report.ToString());return report;
        }
    }
}
