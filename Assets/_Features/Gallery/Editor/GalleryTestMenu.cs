#if ECATY_CAP_FLEXALON
using UnityEditor;
using UnityEngine;
namespace Ecaty.Editor
{
    [InitializeOnLoad]
    public static class GalleryTestMenu
    {
        const string Pending="Ecaty.GalleryTests.Pending";
        static bool running;
        static GalleryTestMenu(){EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false))EditorApplication.delayCall+=Run;};}
        [MenuItem("Runtime/Tests/Gallery scenarios")]
        public static async void Run()
        {
            if(running)return;
            if(!EditorApplication.isPlaying){SessionState.SetBool(Pending,true);EditorApplication.isPlaying=true;return;}
            bool ownPlay=SessionState.GetBool(Pending,false);SessionState.SetBool(Pending,false);running=true;
            try{var report=await GalleryScenarioSuite.Run();Debug.Log("Gallery scenarios: "+report);}
            catch(System.Exception e){Debug.LogException(e);}
            finally{running=false;if(ownPlay)EditorApplication.isPlaying=false;}
        }
    }
}
#endif
