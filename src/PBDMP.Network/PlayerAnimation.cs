namespace PBDMP.Network;

public enum PlayerMotion : byte { Grounded, Airborne, Sliding }
[Flags]
public enum PlayerActions : ushort
{
    None=0, Firing=1, Alternate=2, Melee=4, Loop=8, Blocking=16, Shielding=32, Charging=64, Kicking=128, LeftHand=256, SawRunning=512
}
// Presentation only. These flags never authorize combat or alter player movement.
public readonly record struct PlayerAnimation(PlayerMotion Motion=PlayerMotion.Grounded,
    PlayerActions Actions=PlayerActions.None, uint Sequence=0, float Phase=0, float Charge=0)
{
    public bool Has(PlayerActions action)=>(Actions&action)!=0;
    internal static void Validate(PlayerAnimation value)
    {
        if(value.Motion>PlayerMotion.Sliding || (value.Actions & ~(PlayerActions)1023)!=0 ||
            !float.IsFinite(value.Phase) || value.Phase is <0 or >1 ||
            !float.IsFinite(value.Charge) || value.Charge is <0 or >100)
            throw new InvalidDataException("Invalid player animation.");
    }
}
