using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly List<(NativePresentation Visual,Vector3 Velocity,float Until)> _breakDebris=new();
    internal bool IsSharedSpawner(Component c)=>_catalogue?.Key(c) is {} key && _catalogue.SpawnerKeys.Contains(key);
    internal bool AllowPlatformCarry(Hyperstrange.PBD.MovingPlatformTrigger platform)=>!Client || !Active;
    internal bool SuppressRestoredBreak(Breaker breaker)=>Active && RestoringNative && _catalogue!.BreakOwners.ContainsKey(breaker.Pointer);
    internal void SceneryBroke(Breaker breaker,Vector3 position,Vector3 velocity)
    {
        if(Client || !Active || RestoringNative || Applying || !_catalogue!.BreakOwners.TryGetValue(breaker.Pointer,out var key)) return;
        SendMissionBreak(key,true);
        Send(new(WorldKind.SceneryEvent,_epoch,1,Key:key,Flags:1,Position:Net(position),Velocity:Net(Vector3.ClampMagnitude(velocity,100))));
    }
    private void PlaySceneryEvent(WorldFrame f)
    {
        if(f.Flags==2)
        {
            if(!_posters.TryGetValue(f.Key,out var poster)) throw new InvalidDataException("Unknown poster presentation.");
            ShowPosterCollection(poster);return;
        }
        if(f.Flags!=1 || !_catalogue!.MissionBreakKeys.Contains(f.Key)) throw new InvalidDataException("Unknown live destruction event.");
        var c=_catalogue.Components[f.Key];
        var breaker=c.TryCast<BreakableObject>()?._breaker??c.TryCast<CollisionBreakableObject>()?._breaker??
            c.TryCast<FallingPlatform>()?._breaker??c.TryCast<WireWallGate>()?._breaker;
        if(breaker==null) return;
        breaker._soundEvent?.PlayOneShot(Unity(f.Position));
        if(breaker._partParticle!=null)
        {
            try
            {
                var effect=new NativePresentation(breaker._partParticle.gameObject);
                effect.Move(Unity(f.Position),Quaternion.identity);_effects.Add((effect,Time.unscaledTime+4));
            }
            catch(Exception error) {Plugin.Logger.LogWarning("Native smash presentation unavailable: "+breaker._partParticle.name+" / "+error.Message);}
        }
        if(breaker._rigidbodies==null) return;
        foreach(var body in breaker._rigidbodies.Take(24))
        {
            if(body==null || GameplayFragment(body)) continue;
            NativePresentation visual;
            try {visual=new NativePresentation(body.gameObject,geometryOnly:true);}
            catch(InvalidOperationException error) {Plugin.Logger.LogWarning("Cosmetic debris source needs an audit: "+body.name+" / "+error.Message);continue;}
            visual.Move(body.position,body.rotation);
            _breakDebris.Add((visual,Unity(f.Velocity)+UnityEngine.Random.insideUnitSphere*2,Time.unscaledTime+4));
        }
    }
    internal static bool GameplayFragment(Rigidbody body)
    {
        var player=Hyperstrange.PBD.Player.Instance;
        return body.GetComponentsInChildren<Collider>(true).Any(c=>!c.isTrigger && player!=null &&
            !Physics.GetIgnoreLayerCollision(c.gameObject.layer,player.Collider.gameObject.layer));
    }
    private void TickBreakDebris()
    {
        if(_life.Frozen) return;
        var dt=Mathf.Min(Time.deltaTime,.05f);
        for(var i=_breakDebris.Count-1;i>=0;i--)
        {
            var piece=_breakDebris[i];
            if(!piece.Visual.Alive || Time.unscaledTime>=piece.Until) {piece.Visual.Dispose();_breakDebris.RemoveAt(i);continue;}
            var velocity=piece.Velocity+Physics.gravity*dt;var origin=piece.Visual.Root.transform.position;
            var step=velocity*dt;
            if(step.sqrMagnitude>0 && Physics.Raycast(origin,step.normalized,out var hit,step.magnitude+.03f,~(1<<Hyperstrange.PBD.Player.Instance.gameObject.layer),QueryTriggerInteraction.Ignore))
            {piece.Visual.Root.transform.position=hit.point;velocity=Vector3.zero;}
            else piece.Visual.Root.transform.position+=step;
            piece.Visual.Tick(dt);_breakDebris[i]=(piece.Visual,velocity,piece.Until);
        }
    }
}
