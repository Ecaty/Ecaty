"""Copy the installed local-AI pack beside an existing Windows player. No Unity changes."""
import argparse,pathlib,shutil
root=pathlib.Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('player',type=pathlib.Path,help='Existing Windows .exe');args=parser.parse_args()
exe=args.player.resolve()
if not exe.is_file() or exe.suffix.lower()!='.exe':parser.error('Choose an existing Windows player executable')
source=root/'LocalData/AI';dest=exe.parent/'LocalAI'
if not (source/'config.json').exists():parser.error('Run setup.py first')
if dest==source or source in dest.parents:parser.error('Destination must be outside the source AI pack')
for name in ('llama','whisper','models'):
 shutil.copytree(source/name,dest/name,dirs_exist_ok=True)
for name in ('config.json','UPSTREAM.json','MODEL-LICENSE.txt','WHISPER-LICENSE.txt'):
 shutil.copy2(source/name,dest/name)
print('Local AI pack staged:',dest)
