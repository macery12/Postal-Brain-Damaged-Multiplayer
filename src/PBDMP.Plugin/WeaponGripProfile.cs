using UnityEngine;

namespace PBDMP.Plugin;

// Authored pickup-space profiles. Never infer a weapon's forward direction from its largest dimension.
internal sealed record WeaponGripProfile(string Family,float Length,Vector3 GeometryRotation,Vector3 Grip,Vector3 Support,
    Vector3 Hold,bool TwoHands=true,bool LeftHanded=false,string NeutralNode="",bool Saw=false,bool Bow=false)
{
    private static readonly string[] Names={"WEAPON_Pistol","WEAPON_Shotgun","WEAPON_MeatShotgun","WEAPON_MachineGun","WEAPON_BubbleGumMachineGun","WEAPON_LightningGun","WEAPON_RocketLauncher","WEAPON_NuclearSyringe","WEAPON_GatlingGun","WEAPON_CatCanon","WEAPON_Shovel","WEAPON_DildoBow","WEAPON_Hook","WEAPON_PissGun","WEAPON_UmDrill"};
    private static readonly Dictionary<string,WeaponGripProfile> Profiles=Names.ToDictionary(n=>n,Create,StringComparer.Ordinal);
    private static readonly WeaponGripProfile Fallback=Create("");
    internal static WeaponGripProfile For(string name)=>Profiles.TryGetValue(name,out var profile)?profile:Fallback;
    private static WeaponGripProfile Create(string name)=>name switch
    {
        "WEAPON_Pistol"=>new("machinegun",.42f,Vector3.zero,new(.5f,.22f,.2f),new(.48f,.2f,.3f),new(.18f,1.24f,.34f)),
        "WEAPON_Shotgun"=>new("shotgun",.87f,Vector3.zero,new(.5f,.25f,.33f),new(.5f,.27f,.7f),new(.18f,1.22f,.29f)),
        "WEAPON_MeatShotgun"=>new("shotgun",.83f,Vector3.zero,new(.5f,.24f,.26f),new(.5f,.3f,.7f),new(.18f,1.2f,.32f)),
        "WEAPON_MachineGun"=>new("machinegun",.78f,Vector3.zero,new(.5f,.2f,.29f),new(.5f,.3f,.65f),new(.18f,1.23f,.31f)),
        "WEAPON_BubbleGumMachineGun"=>new("machinegun",.8f,Vector3.zero,new(.5f,.16f,.25f),new(.5f,.24f,.65f),new(.18f,1.2f,.33f)),
        "WEAPON_LightningGun"=>new("machinegun",.8f,Vector3.zero,new(.5f,.2f,.29f),new(.5f,.25f,.66f),new(.18f,1.2f,.32f)),
        "WEAPON_RocketLauncher"=>new("rocketlauncher",1.0f,new(0,180,0),new(.5f,.16f,.41f),new(.5f,.2f,.72f),new(.22f,1.28f,.25f)),
        "WEAPON_NuclearSyringe"=>new("rocketlauncher",.93f,Vector3.zero,new(.5f,.2f,.28f),new(.5f,.27f,.66f),new(.18f,1.17f,.34f)),
        "WEAPON_GatlingGun"=>new("machinegun",.98f,Vector3.zero,new(.5f,.2f,.27f),new(.42f,.65f,.49f),new(.2f,1.04f,.34f)),
        "WEAPON_CatCanon"=>new("rocketlauncher",.86f,Vector3.zero,new(.5f,.18f,.25f),new(.5f,.27f,.6f),new(.18f,1.16f,.35f)),
        "WEAPON_Shovel"=>new("shotgun",.95f,new(90,0,0),new(.5f,.5f,.14f),new(.5f,.5f,.44f),new(.2f,1.07f,.34f),NeutralNode:"Shovel",Saw:true),
        "WEAPON_DildoBow"=>new("machinegun",.95f,Vector3.zero,new(.5f,.47f,.46f),new(.5f,.47f,.08f),new(-.18f,1.25f,.5f),LeftHanded:true,NeutralNode:"Bow",Bow:true),
        "WEAPON_Hook"=>new("machinegun",.46f,new(90,0,0),new(.5f,.22f,.25f),new(.5f,.2f,.1f),new(.18f,1.2f,.4f),TwoHands:false),
        // These optional installed-game extras use explicit profiles but are not campaign acceptance targets.
        "WEAPON_PissGun"=>new("machinegun",.7f,Vector3.zero,new(.5f,.22f,.3f),new(.5f,.27f,.63f),new(.18f,1.2f,.32f)),
        "WEAPON_UmDrill"=>new("shotgun",.9f,Vector3.zero,new(.5f,.22f,.22f),new(.5f,.24f,.48f),new(.2f,1.07f,.34f),NeutralNode:"Player_UmDrilla",Saw:true),
        _=>new("machinegun",.75f,Vector3.zero,new(.5f,.2f,.25f),new(.5f,.25f,.65f),new(.18f,1.2f,.32f))
    };
}
