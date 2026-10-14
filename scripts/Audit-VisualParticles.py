"""Read local serialized particle modules; produce an original-source allowlist, not game assets."""
import argparse,json,pathlib,collections

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-dir',type=pathlib.Path,required=True,
                    help='Game installation containing POSTAL Brain Damaged.exe')
args=parser.parse_args()
repo=pathlib.Path(__file__).resolve().parents[1]
source=args.game_dir.expanduser().resolve()/'POSTAL Brain Damaged_Data'/'sharedassets0.assets'
if not source.is_file():
    parser.error('The supplied game directory does not contain sharedassets0.assets.')
try:
    import UnityPy
except ImportError:
    parser.exit(1,'Install UnityPy in your Python environment before running this optional research tool.\n')
env=UnityPy.load(str(source))
objects={obj.path_id:obj for obj in env.objects}
trees={}
def read(pid):
    if pid not in trees: trees[pid]=objects[pid].read_typetree()
    return trees[pid]
gameobjects={}
transforms={}
particles={}
for pid,obj in objects.items():
    if obj.type.name=='GameObject': gameobjects[pid]=read(pid)
    elif obj.type.name=='Transform': transforms[pid]=read(pid)
    elif obj.type.name=='ParticleSystem': particles[pid]=read(pid)
children=collections.defaultdict(list)
go_transform={}
for pid,t in transforms.items():
    go_transform[t['m_GameObject']['m_PathID']]=pid
    children[t['m_Father']['m_PathID']].append(pid)
go_particles=collections.defaultdict(list)
for pid,p in particles.items(): go_particles[p['m_GameObject']['m_PathID']].append(pid)
def descendants(tid):
    yield tid
    for child in children[tid]: yield from descendants(child)
audit=[]
by_name=collections.defaultdict(list)
for gid,go in gameobjects.items():
    tid=go_transform.get(gid)
    if tid is None: continue
    ids=[pid for child in descendants(tid) for pid in go_particles[transforms[child]['m_GameObject']['m_PathID']]]
    # Native sparks may bounce visually. They cannot push world bodies or send
    # collision callbacks; every gameplay component is stripped from the clone.
    safe=all((not particles[pid].get('CollisionModule',{}).get('enabled',False) or
              particles[pid].get('CollisionModule',{}).get('colliderForce',0)==0 and
              not particles[pid].get('CollisionModule',{}).get('collisionMessages',False)) and
             not particles[pid].get('TriggerModule',{}).get('enabled',False) and
             (not particles[pid].get('SubModule',{}).get('enabled',False) or
              all(sub.get('emitter',{}).get('m_PathID',0)==0 or
                  sub.get('emitter',{}).get('m_FileID',0)==0 and sub['emitter']['m_PathID'] in ids
                  for sub in particles[pid].get('SubModule',{}).get('subEmitters',[]))) and
             particles[pid].get('stopAction',0)==0 for pid in ids)
    by_name[go['m_Name']].append((safe,bool(ids)))
    if ids: audit.append({'name':go['m_Name'],'id':gid,'particles':ids,'safe':safe})
names=sorted(name for name,results in by_name.items() if all(safe for safe,_ in results) and any(has_particles for _,has_particles in results))
output=repo/'src'/'PBDMP.Plugin'/'AuditedVisualSources.cs'
output.write_text('namespace PBDMP.Plugin;\ninternal static class AuditedVisualSources\n{\n    internal static readonly HashSet<string> Names = new(StringComparer.Ordinal)\n    {\n'+
    ''.join('        '+json.dumps(name,ensure_ascii=True)+',\n' for name in names)+'    };\n}\n',encoding='utf-8')
(repo/'.local').mkdir(exist_ok=True)
(repo/'.local'/'visual-particle-audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
print(f'Audited {len(particles)} particle systems; {len(names)} unambiguous source names accepted; details in .local/visual-particle-audit.json')
