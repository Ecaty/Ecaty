#if ECATY_CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Ecaty.Editor
{
    /// <summary>Local developer artifacts from the actual camera. No model call or user chat capture.</summary>
    public sealed class GalleryRunArtifacts
    {
        readonly string folder;readonly JArray images=new();readonly JArray errors=new();
        public string Folder=>folder;public int Count=>images.Count;
        public GalleryRunArtifacts(string directory=null)
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../Tests/Results"));folder=Path.GetFullPath(directory??Path.Combine(root,DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff"),"Gallery"));
            if(!folder.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Artifacts must stay inside Tests/Results");Directory.CreateDirectory(folder);
        }
        public void Capture(CrystalWorldGallery gallery,string name)
        {
            RenderTexture target=null;Texture2D image=null;var previous=RenderTexture.active;
            try{
                var camera=gallery.ViewCamera;int width=Mathf.Clamp(camera.pixelWidth,320,1280),height=Mathf.Max(180,Mathf.RoundToInt(width/Mathf.Max(.1f,camera.aspect)));
                target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("Camera capture request unavailable");RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target;image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string file=name+".png";File.WriteAllBytes(Path.Combine(folder,file),image.EncodeToPNG());images.Add(new JObject{["name"]=name,["file"]=file,["frame"]=Time.frameCount,["time"]=Time.unscaledTime});
            }catch(Exception error){errors.Add(name+": "+error.Message);}
            finally{RenderTexture.active=previous;if(image)UnityEngine.Object.DestroyImmediate(image);if(target)RenderTexture.ReleaseTemporary(target);}
        }
        public void Finish(JObject report)
        {
            report["artifactDirectory"]=folder;report["images"]=images;report["captureErrors"]=errors;File.WriteAllText(Path.Combine(folder,"report.json"),report.ToString());
            var html=new StringBuilder("<!doctype html><meta charset=utf-8><title>Gallery test</title><style>body{font:16px system-ui;background:#17282d;color:#e0eee9;max-width:1100px;margin:32px auto;padding:0 20px}img{max-width:100%;border-radius:12px}figure{margin:24px 0}figcaption{margin:8px 0}details{margin:12px 0;padding:14px;background:#203338;border-radius:8px}summary{cursor:pointer;font-weight:600}a{color:#b5d5ca}.success{color:#9cddad}.error{color:#ffac9c}.error-panel{border-left:4px solid #ffac9c;background:#432f30;padding:12px}.success-panel{border-left:4px solid #9cddad;padding-left:12px}li{margin:6px 0}pre{white-space:pre-wrap}</style>");
            bool passed=(bool?)report["passed"]==true;html.Append("<h1 class='").Append(passed?"success":"error").Append("'>Gallery: ").Append(passed?"passed":"failed").Append("</h1><p><a href=report.json>JSON result</a></p><details open><summary>Gallery</summary>");
            var checks=(JArray)report["checks"];
            foreach(bool success in new[]{false,true}){var rows=checks.Where(c=>(bool)c["passed"]==success).ToArray();if(rows.Length==0)continue;html.Append("<section class='").Append(success?"success-panel":"error-panel").Append("'><h2 class='").Append(success?"success":"error").Append("'>").Append(success?"Passed checks":"Failed checks").Append(" (").Append(rows.Length).Append(")</h2><ul>");foreach(var check in rows)html.Append("<li>").Append(WebUtility.HtmlEncode((string)check["name"])).Append("</li>");html.Append("</ul></section>");}
            if(report["failure"]!=null)html.Append("<pre class=error>").Append(WebUtility.HtmlEncode((string)report["failure"])).Append("</pre>");
            foreach(var picture in images)html.Append("<figure><figcaption>").Append(WebUtility.HtmlEncode((string)picture["name"])).Append(" · frame ").Append((int)picture["frame"]).Append("</figcaption><a href='").Append((string)picture["file"]).Append("'><img loading=lazy src='").Append((string)picture["file"]).Append("'></a></figure>");
            html.Append("</details>");File.WriteAllText(Path.Combine(folder,"index.html"),html.ToString());
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../Tests/Results"));string relative=Path.GetRelativePath(root,Path.Combine(folder,"index.html")).Replace('\\','/');File.WriteAllText(Path.Combine(root,"LatestGallery.html"),"<!doctype html><meta http-equiv=refresh content='0;url="+relative+"'><a href='"+relative+"'>Latest gallery report</a>");
        }
    }
}
#endif
