using System.Text;

namespace PBDMP.Network;

public enum WorldKind : byte
{
    Manifest = 1, ManifestAck, SnapshotBegin, SnapshotEnd, SnapshotAck,
    Enemy, Door, Projectile, Loot, Encounter, Impact, IncomingHit, WeaponEffect, InteractionResult, ActorState, ActorEffect,
    UrineState, MissionState, Scenery, SceneryEvent, Mechanism, Sequence, PlayerEffect, Survival,
    Fire = 32, Melee, Interact, Trigger, Defense, Reflect, Special, ClientEffect, SurvivalRequest
}

public readonly record struct NetVector(float X, float Y, float Z)
{
    public static readonly NetVector Zero = new();
}

// All identities are scoped by travel epoch. Key maps authored catalogues; Asset identifies local assets.
// Values are bounded typed-adapter data, never code, native pointers or arbitrary serialized objects.
public sealed record WorldFrame(WorldKind Kind, uint Epoch, ulong Sequence, uint Entity = 0, uint Revision = 0,
    string Key = "", string Asset = "", byte Actor = 0, uint Life = 0, uint Flags = 0,
    NetVector Position = default, NetVector Rotation = default, NetVector Velocity = default,
    float Value = 0, float Value2 = 0, float[]? Values = null, string[]? Data = null);

public static class WorldProtocol
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static bool FromClient(WorldKind kind) => kind is WorldKind.ManifestAck or WorldKind.SnapshotAck || (byte)kind >= 32;
    public static bool Durable(WorldFrame frame)=>frame.Kind switch
    {
        WorldKind.UrineState=>frame.Flags is 0 or 4 or 16 or 32,
        WorldKind.MissionState=>frame.Flags is not (256 or 512),
        WorldKind.Enemy or WorldKind.Door or WorldKind.Loot or WorldKind.Encounter or WorldKind.ActorState or
            WorldKind.Scenery or WorldKind.Mechanism or WorldKind.Sequence=>true,
        _=>false
    };
    public static void Validate(WorldFrame frame)
    {
        if (!Enum.IsDefined(typeof(WorldKind), frame.Kind) || frame.Epoch == 0 || frame.Sequence == 0 || frame.Actor > 2 ||
            frame.Key == null || frame.Asset == null || Utf8.GetByteCount(frame.Key) > 256 || Utf8.GetByteCount(frame.Asset) > 256 ||
            !Valid(frame.Position) || !Valid(frame.Rotation) || !Valid(frame.Velocity) ||
            !Valid(frame.Value) || !Valid(frame.Value2) || frame.Values is { Length: > 32 } ||
            frame.Values?.Any(v=>!Valid(v)) == true || frame.Data is { Length: > 32 } ||
            frame.Data?.Any(t=>t==null || Utf8.GetByteCount(t)>256) == true)
            throw new InvalidDataException($"Invalid world frame: kind={frame.Kind}, entity={frame.Entity}, key={frame.Key}, asset={frame.Asset}, flags={frame.Flags}, position={frame.Position}, value={frame.Value}/{frame.Value2}, values=[{string.Join(',',frame.Values??Array.Empty<float>())}], dataCount={frame.Data?.Length??0}.");
    }
    private static bool Valid(float value) => float.IsFinite(value) && Math.Abs(value) <= 1_000_000f;
    private static bool Valid(NetVector vector) => Valid(vector.X) && Valid(vector.Y) && Valid(vector.Z);
    internal static void Write(BinaryWriter writer, WorldFrame frame)
    {
        Validate(frame);
        writer.Write((byte)frame.Kind); writer.Write(frame.Epoch); writer.Write(frame.Sequence);
        writer.Write(frame.Entity); writer.Write(frame.Revision); Text(writer,frame.Key); Text(writer,frame.Asset);
        writer.Write(frame.Actor); writer.Write(frame.Life); writer.Write(frame.Flags);
        Vector(writer,frame.Position); Vector(writer,frame.Rotation); Vector(writer,frame.Velocity);
        writer.Write(frame.Value); writer.Write(frame.Value2); writer.Write((byte)(frame.Values?.Length ?? 0));
        if (frame.Values != null) foreach (var value in frame.Values) writer.Write(value);
        writer.Write((byte)(frame.Data?.Length ?? 0));
        if (frame.Data != null) foreach (var value in frame.Data) Text(writer,value);
    }
    internal static WorldFrame Read(BinaryReader reader)
    {
        var kind = (WorldKind)reader.ReadByte(); var epoch = reader.ReadUInt32(); var sequence = reader.ReadUInt64();
        var entity = reader.ReadUInt32(); var revision = reader.ReadUInt32(); var key = Text(reader); var asset = Text(reader);
        var actor = reader.ReadByte(); var life = reader.ReadUInt32(); var flags = reader.ReadUInt32();
        var position = Vector(reader); var rotation = Vector(reader); var velocity = Vector(reader);
        var value = reader.ReadSingle(); var value2 = reader.ReadSingle(); var count = reader.ReadByte();
        if (count > 32) throw new InvalidDataException("World values too long.");
        var values = new float[count]; for (var i=0;i<count;i++) values[i] = reader.ReadSingle();
        var textCount=reader.ReadByte(); if (textCount>32) throw new InvalidDataException("World data too long.");
        var texts=new string[textCount]; for (var i=0;i<textCount;i++) texts[i]=Text(reader);
        var frame = new WorldFrame(kind,epoch,sequence,entity,revision,key,asset,actor,life,flags,position,rotation,velocity,value,value2,values,texts);
        Validate(frame); return frame;
    }
    private static void Vector(BinaryWriter writer, NetVector value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    private static NetVector Vector(BinaryReader reader) => new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
    private static void Text(BinaryWriter writer, string value)
    { var bytes = Utf8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    private static string Text(BinaryReader reader)
    {
        var length=reader.ReadUInt16(); if (length>256) throw new InvalidDataException("World text too long.");
        var bytes=reader.ReadBytes(length); if (bytes.Length!=length) throw new EndOfStreamException("Truncated world text.");
        return Utf8.GetString(bytes);
    }
}
