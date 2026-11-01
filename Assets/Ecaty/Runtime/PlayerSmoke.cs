using Ecaty.Flecs;
using System;
using System.IO;
using UnityEngine;

namespace Ecaty
{
    /// <summary>Opt-in standalone-player verification; inactive in normal use.</summary>
    public sealed class PlayerSmoke : MonoBehaviour
    {
        private int frames;
        private ulong testEntity;
        private Quaternion startRotation;
        private FlecsWorld host;
        private void Update()
        {
            try
            {
                frames++;
                if (frames == 2)
                {
                    host = GetComponent<FlecsWorld>();
                    if (!UnityEngine.Object.FindFirstObjectByType<Ecaty.Agents.UI.AgentChatPanel>()) throw new Exception("Chat panel did not mount");
                    var icons = Resources.Load<Font>("MaterialSymbolsRounded");
                    if (!icons || !icons.HasCharacter('\ue163')) throw new Exception("Bundled icon font missing in player");
                    if (host.Session == null || host.ViewCount != 4) throw new Exception("Hello world not running");
                    host.Session.RunScript("RuntimeCheck { Position: {2,0,0} Rotation: {0,0,0} Spin: {0,90,0} Primitive: {1} }");
                    host.SyncViews();
                    int count=host.Session.ReadViews(out var views);
                    for(int i=0;i<count;i++) if(host.Session.EntityName(views[i].Entity)=="RuntimeCheck") testEntity=views[i].Entity;
                    if(testEntity==0) throw new Exception("Runtime entity missing");
                    startRotation=host.FindView(testEntity).transform.localRotation;
                }
                if (frames < 80) return;
                var view=host.FindView(testEntity);
                if(!view || view.transform.localPosition.x!=2 || Quaternion.Angle(startRotation,view.transform.localRotation)<0.01f)
                    throw new Exception("Player Update/native system/Unity presentation did not advance");
                if (!host.Session.Query("Presented").Contains("frame")) throw new Exception("Player presentation readback missing");
                Finish(true,"80 advancing frames; native Script/rotation, Presented readback, mounted chat panel and bundled Google icons");
            }
            catch(Exception ex) { Finish(false,ex.ToString()); }
        }
        private void Finish(bool success,string detail)
        {
            enabled=false;
            var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-flecsSmokeResult");
            if(index>=0 && index+1<args.Length)
                File.WriteAllText(args[index+1],JsonUtility.ToJson(new Result{passed=success,frames=frames,detail=detail},true));
            Debug.Log("FLECS_PLAYER_SMOKE="+(success?"PASS":"FAIL")+" "+detail);
            Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Result { public bool passed; public int frames; public string detail; }
    }
}
