"""Audit serialized particle collision/trigger/sub-emitter modules without distributing game assets."""
import argparse,collections,json,re
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-dir',type=Path,required=True)
parser.add_argument('--additional-asset',action='append',default=[],help='Additional data file, e.g. level15. Repeat for multiple files.')
args=parser.parse_args()
root=args.game_dir.expanduser().resolve()/'POSTAL Brain Damaged_Data'
assets=list(dict.fromkeys(['sharedassets0.assets',*args.additional_asset]))
for filename in assets:
 if not re.fullmatch(r'(sharedassets[0-9]+\.assets|level[0-9]+)',filename) or not (root/filename).is_file():
  parser.error('Expected an existing serialized game data filename: '+filename)
try:
 import UnityPy
except ImportError:
 parser.exit(1,'Install UnityPy in your Python environment to run this optional asset audit.\n')
names=collections.defaultdict(list);report=[]
for filename in assets:
 env=UnityPy.load(str(root/filename)); objects={o.path_id:o for o in env.objects}; gos={};ts={};ps={}
 for pid,obj in objects.items():
  if obj.type.name=='GameObject':gos[pid]=obj.read_typetree()
  elif obj.type.name=='Transform':ts[pid]=obj.read_typetree()
  elif obj.type.name=='ParticleSystem':ps[pid]=obj.read_typetree()
 children=collections.defaultdict(list);got={};gop=collections.defaultdict(list)
 for pid,t in ts.items():got[t['m_GameObject']['m_PathID']]=pid;children[t['m_Father']['m_PathID']].append(pid)
 for pid,p in ps.items():gop[p['m_GameObject']['m_PathID']].append(pid)
 def descendants(tid):
  yield tid
  for child in children[tid]:yield from descendants(child)
 for gid,go in gos.items():
  if gid not in got:continue
  ids=[pid for t in descendants(got[gid]) for pid in gop[ts[t]['m_GameObject']['m_PathID']]]
  safe=all((not ps[pid].get('CollisionModule',{}).get('enabled',False) or
            ps[pid].get('CollisionModule',{}).get('colliderForce',0)==0 and not ps[pid].get('CollisionModule',{}).get('collisionMessages',False)) and
           not ps[pid].get('TriggerModule',{}).get('enabled',False) and
           (not ps[pid].get('SubModule',{}).get('enabled',False) or all(sub.get('emitter',{}).get('m_PathID',0)==0 or
            sub.get('emitter',{}).get('m_FileID',0)==0 and sub['emitter']['m_PathID'] in ids for sub in ps[pid].get('SubModule',{}).get('subEmitters',[]))) and
           ps[pid].get('stopAction',0)==0 for pid in ids)
  names[go['m_Name']].append((safe,bool(ids)))
  if ids:report.append({'file':filename,'name':go['m_Name'],'safe':safe,'particles':len(ids)})
 print(filename,len(ps),'particles',flush=True)
accepted=sorted(n for n,rows in names.items() if all(s for s,p in rows) and any(p for s,p in rows))
repo=Path(__file__).resolve().parents[1]
out=repo/'.local';out.mkdir(exist_ok=True)
(out/'visual-particle-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
output=repo/'src'/'PBDMP.Plugin'/'AuditedVisualSources.cs'
output.write_text('namespace PBDMP.Plugin;\ninternal static class AuditedVisualSources\n{\n    internal static readonly HashSet<string> Names=new(StringComparer.Ordinal)\n    {\n'+''.join('        '+json.dumps(n)+',\n' for n in accepted)+'    };\n}\n',encoding='utf-8')
print('Safe unambiguous particle source names:',len(accepted))
