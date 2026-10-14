using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using Hyperstrange.Base;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed class NativeAssetGraph
{
    internal readonly Dictionary<string,ProjectileModel> Models=new();
    internal readonly Dictionary<IntPtr,string> ModelKeys=new();
    internal readonly Dictionary<string,string> ModelOrigins=new();
    private readonly Dictionary<string,string> _originAliases=new();
    internal readonly Dictionary<string,WeaponAction.FxModule> Effects=new();
    private readonly Dictionary<IntPtr,string> _effectKeys=new();
    private readonly HashSet<IntPtr> _visited=new();
    private static readonly MethodInfo CastMethod=typeof(Il2CppObjectBase).GetMethods().Single(m=>m.Name=="Cast" && m.IsGenericMethodDefinition);
    internal static object Actual(Il2CppObjectBase value)
    {
        if(value.GetType().IsGenericType) return value;
        var name=value.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName;
        var type=typeof(Npc).Assembly.GetType(name);
        return type==null?value:CastMethod.MakeGenericMethod(type).Invoke(value,null)!;
    }
    internal static object Cast(Il2CppObjectBase value,Type type)=>CastMethod.MakeGenericMethod(type).Invoke(value,null)!;
    internal void Build()
    {
        foreach(var weapon in Resources.FindObjectsOfTypeAll<WeaponId>())
        {
            if(weapon.Category!=DLCCategory.NONE || weapon.PlayerWeaponPrefab==null) continue;
            if(weapon.PlayerWeaponModel!=null) Walk(weapon.PlayerWeaponModel,weapon.ID,"model",0);
            var components=weapon.PlayerWeaponPrefab.GetComponentsInChildren<PlayerWeapon>(true);
            foreach(var component in components) Walk(component,weapon.ID,"weapon",0);
        }
        foreach(var npc in Resources.FindObjectsOfTypeAll<NpcId>())
        {
            if(npc.NpcModel!=null) Walk(npc.NpcModel,npc.ID,npc.name+"/npcmodel",0);
            if(npc._npcPrefab!=null)
                foreach(var component in npc._npcPrefab.GetComponentsInChildren<MonoBehaviour>(true))
                    if(component.GetIl2CppType().FullName.Contains("Hyperstrange.PBD")) Walk(component,npc.ID,npc.name+"/"+component.GetIl2CppType().Name,0);
        }
        var weapons=Resources.FindObjectsOfTypeAll<WeaponId>().Where(w=>w.Category==DLCCategory.NONE).ToArray();
        foreach(var weapon in weapons)
        {
            var original=weapons.FirstOrDefault(w=>w.name==weapon.name.Replace("_Akimbo","",StringComparison.Ordinal));
            if(original!=null) _originAliases[weapon.ID]=original.ID;
        }
        Plugin.Logger.LogInfo($"Native asset graph: {Models.Count} projectile models and {Effects.Count} weapon FX sources.");
    }
    private void Walk(Il2CppObjectBase value,string asset,string path,int depth)
    {
        if(depth>4 || !_visited.Add(value.Pointer)) return;
        var actual=Actual(value);
        foreach(var property in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
        {
            if((!property.Name.StartsWith('_') && property.Name!="projectileModel") || property.GetIndexParameters().Length!=0) continue;
            if(!typeof(ProjectileModel).IsAssignableFrom(property.PropertyType) && !typeof(WeaponAction).IsAssignableFrom(property.PropertyType) &&
                property.PropertyType!=typeof(WeaponAction.FxModule) && !typeof(PlayerWeaponModel).IsAssignableFrom(property.PropertyType) &&
                !property.PropertyType.Name.StartsWith("FireMode") &&
                !(property.PropertyType.Namespace==typeof(Npc).Namespace && property.PropertyType.Name.EndsWith("Properties",StringComparison.Ordinal))) continue;
            var child=property.GetValue(actual) as Il2CppObjectBase; if(child==null) continue;
            var childPath=path+"/"+property.Name;
            var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset+"/"+childPath)));
            if(child is ProjectileModel model) {Models[key]=model;ModelKeys[model.Pointer]=key;ModelOrigins[key]=asset;}
            else if(child is WeaponAction.FxModule effect) {Effects[key]=effect;_effectKeys[effect.Pointer]=key;}
            else Walk(child,asset,childPath,depth+1);
        }
    }
    internal string Bind(ProjectileModel model,string origin)
    {
        if(ModelKeys.TryGetValue(model.Pointer,out var key)) return key;
        var kind=model.GetIl2CppType().FullName; var prefab=model.Prefab?.name;
        var candidates=Models.Where(p=>AllowsOrigin(p.Key,origin) && p.Value.GetIl2CppType().FullName==kind && p.Value.Prefab?.name==prefab)
            .OrderBy(p=>Mathf.Abs(p.Value.Speed-model.Speed)+Mathf.Abs(p.Value.Damage-model.Damage)).ToArray();
        if(candidates.Length==0) throw new InvalidOperationException("No audited native projectile model binding: "+kind+"/"+prefab);
        ModelKeys[model.Pointer]=candidates[0].Key; return candidates[0].Key;
    }
    internal bool AllowsOrigin(string key,string origin)=>ModelOrigins[key]==origin || _originAliases.GetValueOrDefault(origin,origin)==_originAliases.GetValueOrDefault(ModelOrigins[key],ModelOrigins[key]);
    internal string? Bind(WeaponAction.FxModule effect)
    {
        if(_effectKeys.TryGetValue(effect.Pointer,out var key)) return key;
        var match=Effects.FirstOrDefault(p=>p.Value._fireEvent?.name==effect._fireEvent?.name && p.Value._chargeEvent?.name==effect._chargeEvent?.name &&
            p.Value._fireChargeFullEvent?.name==effect._fireChargeFullEvent?.name && p.Value._muzzleFlash?.name==effect._muzzleFlash?.name);
        if(match.Key==null) return null;
        _effectKeys[effect.Pointer]=match.Key;return match.Key;
    }
}
