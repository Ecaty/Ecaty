"""Install pinned Windows local-AI sidecars/weights into ignored LocalData/AI. No accounts."""
import concurrent.futures,hashlib,json,pathlib,urllib.request,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
DEST=ROOT/'LocalData/AI'
HEADERS={'User-Agent':'Ecaty-local-ai-setup'}
def get_json(url):return json.load(urllib.request.urlopen(urllib.request.Request(url,headers=HEADERS)))
def digest_file(path):
 h=hashlib.sha256()
 with path.open('rb') as file:
  for block in iter(lambda:file.read(4*1024*1024),b''):h.update(block)
 return h.hexdigest()
def download(url,path,expected=None):
 path.parent.mkdir(parents=True,exist_ok=True)
 if not path.exists() or (expected and digest_file(path)!=expected):
  partial=path.with_suffix(path.suffix+'.part')
  with urllib.request.urlopen(urllib.request.Request(url,headers=HEADERS),timeout=120) as src,partial.open('wb') as dst:
   while True:
    block=src.read(4*1024*1024)
    if not block:break
    dst.write(block)
  partial.replace(path)
 digest=digest_file(path)
 if expected and digest!=expected:raise ValueError('Checksum mismatch: '+path.name)
 print('Ready:',path.name,flush=True);return digest
def runtime(repo,tag,name,folder):
 release=get_json('https://api.github.com/repos/'+repo+'/releases/tags/'+tag)
 asset=next(a for a in release['assets'] if a['name']==name)
 digest=download(asset['browser_download_url'],DEST/'downloads'/name,asset['digest'].removeprefix('sha256:'))
 target=DEST/folder;target.mkdir(parents=True,exist_ok=True)
 with zipfile.ZipFile(DEST/'downloads'/name) as archive:
  for entry in archive.infolist():
   out=(target/entry.filename).resolve()
   if not out.is_relative_to(target.resolve()):raise ValueError('Unsafe archive path')
  archive.extractall(target)
 download('https://raw.githubusercontent.com/'+repo+'/'+release['target_commitish']+'/LICENSE',target/'LICENSE.txt')
 return {'repository':repo,'release':tag,'asset':name,'sha256':digest}
def main():
 DEST.mkdir(parents=True,exist_ok=True)
 repo='unsloth/Qwen3.5-4B-GGUF';revision='e87f176479d0855a907a41277aca2f8ee7a09523'
 info=get_json('https://huggingface.co/api/models/'+repo+'/revision/'+revision+'?blobs=true')
 files={x['rfilename']:x for x in info['siblings']}
 def model(name):
  item=files[name];return download('https://huggingface.co/'+repo+'/resolve/'+revision+'/'+name,DEST/'models'/name,item.get('lfs',{}).get('sha256'))
 tasks={}
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
  tasks['llama']=pool.submit(runtime,'ggml-org/llama.cpp','b12177','llama-b12177-bin-win-vulkan-x64.zip','llama')
  tasks['whisper']=pool.submit(runtime,'ggml-org/whisper.cpp','b5454','whisper-bin-x64.zip','whisper')
  tasks['model']=pool.submit(model,'Qwen3.5-4B-Q4_K_M.gguf')
  tasks['projector']=pool.submit(model,'mmproj-F16.gguf')
  results={k:f.result() for k,f in tasks.items()}
 whisper_revision='5359861c739e955e79d9a303bcbc70fb988958b1'
 whisper_repo=get_json('https://huggingface.co/api/models/ggerganov/whisper.cpp/revision/'+whisper_revision+'?blobs=true')
 bank=next(x for x in whisper_repo['siblings'] if x['rfilename']=='ggml-base.bin')
 results['speechModel']=download('https://huggingface.co/ggerganov/whisper.cpp/resolve/'+whisper_revision+'/ggml-base.bin',DEST/'models/ggml-base.bin',bank.get('lfs',{}).get('sha256'))
 for repo_name,revision_name,target in [('Qwen/Qwen3.5-4B','main','MODEL-LICENSE.txt'),('ggml-org/whisper.cpp','b5454','WHISPER-LICENSE.txt')]:
  url=('https://huggingface.co/'+repo_name+'/resolve/'+revision_name+'/LICENSE') if repo_name.startswith('Qwen/') else ('https://raw.githubusercontent.com/'+repo_name+'/'+revision_name+'/LICENSE')
  download(url,DEST/target)
 llama=next((DEST/'llama').rglob('llama-server.exe'));whisper=next((DEST/'whisper').rglob('whisper-server.exe'))
 config={'llamaExecutable':str(llama.relative_to(DEST)),'whisperExecutable':str(whisper.relative_to(DEST)),'model':'models/Qwen3.5-4B-Q4_K_M.gguf','projector':'models/mmproj-F16.gguf','speechModel':'models/ggml-base.bin','modelId':'ecaty-local','llamaPort':18080,'whisperPort':18081,'contextSize':32768,'gpuLayers':99,'threads':6}
 if not (DEST/'config.json').exists():(DEST/'config.json').write_text(json.dumps(config,indent=2),encoding='utf-8')
 (DEST/'UPSTREAM.json').write_text(json.dumps({'runtimes':results,'model':{'repository':repo,'revision':revision,'license':'Apache-2.0'},'whisperModel':{'repository':'ggerganov/whisper.cpp','revision':whisper_revision}},indent=2),encoding='utf-8')
 fixture=ROOT/'Logs/LocalAiFixture/jfk.wav'
 download('https://raw.githubusercontent.com/ggml-org/whisper.cpp/b5454/samples/jfk.wav',fixture,'59dfb9a4acb36fe2a2affc14bacbee2920ff435cb13cc314a08c13f66ba7860e')
 print('Installed local AI:',DEST,flush=True)
if __name__=='__main__':main()
