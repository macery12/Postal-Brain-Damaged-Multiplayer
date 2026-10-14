using Hyperstrange.PBD;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PBDMP.Plugin;

// A raycast receiver only: it has no movement, explosion or damage simulation.
[Il2CppImplements(typeof(IPissable))]
public sealed class UrineProjectileProxy : MonoBehaviour
{
    private uint _id;
    public UrineProjectileProxy(IntPtr pointer):base(pointer) {}
    public void Piss(PissData data)=>SharedWorld.Current?.RequestProjectileUrine(_id,data);
    [HideFromIl2Cpp]
    internal static void Attach(GameObject root,uint id,Projectile prefab)
    {
        if(!ClassInjector.IsTypeRegisteredInIl2Cpp<UrineProjectileProxy>()) ClassInjector.RegisterTypeInIl2Cpp<UrineProjectileProxy>();
        root.AddComponent<UrineProjectileProxy>()._id=id;
        var original=prefab.GetComponentInChildren<Collider>(true);
        if(original!=null) root.layer=original.gameObject.layer;
        var collider=root.AddComponent<SphereCollider>();collider.radius=original?.TryCast<SphereCollider>()?.radius??0.4f;
        if(Player.Instance?.Collider!=null) Physics.IgnoreCollision(collider,Player.Instance.Collider,true);
    }
}
