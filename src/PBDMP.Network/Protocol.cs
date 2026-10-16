using System.Buffers.Binary;
using System.Text;

namespace PBDMP.Network;

public enum MessageKind : byte { Hello = 1, Welcome = 2, Pose = 3, Reject = 4, Travel = 5, Ready = 6, World = 7, Save = 8 }

// Immutable snapshots can cross worker/main-thread boundaries without Unity objects.
public sealed record PlayerPose(string Level, bool Ready, float X, float Y, float Z, float Yaw, float Height,
    string Weapon = "", float Pitch = 0f, uint ShotSequence = 0, bool Alive = true, uint Life = 0, uint Epoch = 0, PlayerCarrier? Carrier = null)
{
    public static readonly PlayerPose Empty = new("", false, 0, 0, 0, 0, 1.8f);
}

public enum TravelAction : byte { Menu, Load, Restart }
public sealed record InventoryEntry(string Id, int Quantity);
public sealed record JoinLoadout(string CurrentWeapon, string LastWeapon, IReadOnlyList<InventoryEntry> Items,
    float X, float Y, float Z, float Yaw);
public sealed record TravelState(uint Epoch, TravelAction Action, string Level, int Difficulty,
    bool HostReady, bool Paused, bool Released, bool Joining, JoinLoadout? Loadout = null, string StoryVideo = "", float StoryTime = 0, bool StoryWaiting = false, bool StoryEnding = false, bool HostMenuPaused = false);
public sealed record ClientReady(uint Epoch, bool Ready);
public sealed record Packet(MessageKind Kind, int Version = Protocol.Version, string Text = "", PlayerPose? Pose = null,
    TravelState? Travel = null, ClientReady? Ready = null, WorldFrame? World = null, SavePage? Save = null);

public static class Protocol
{
    public const int Version = 10;
    public const int MaxFrameBytes = 8192;
    public const int MaxInventoryItems = 64;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encode(Packet packet)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Utf8, true);
        writer.Write((byte)packet.Kind);
        switch (packet.Kind)
        {
            case MessageKind.Save:
                SaveProtocol.Write(writer, packet.Save ?? throw new InvalidDataException("Missing save page."));
                break;
            case MessageKind.World:
                WorldProtocol.Write(writer, packet.World ?? throw new InvalidDataException("Missing world frame."));
                break;
            case MessageKind.Hello:
            case MessageKind.Welcome:
                writer.Write(packet.Version);
                WriteText(writer, packet.Text);
                break;
            case MessageKind.Reject:
                WriteText(writer, packet.Text);
                break;
            case MessageKind.Pose:
                var pose = packet.Pose ?? throw new InvalidDataException("Missing pose.");
                Validate(pose);
                WriteText(writer, pose.Level);
                writer.Write(pose.Ready);
                writer.Write(pose.X); writer.Write(pose.Y); writer.Write(pose.Z);
                writer.Write(pose.Yaw); writer.Write(pose.Height);
                WriteText(writer, pose.Weapon);
                writer.Write(pose.Pitch);
                writer.Write(pose.ShotSequence);
                writer.Write(pose.Alive); writer.Write(pose.Life); writer.Write(pose.Epoch);
                writer.Write(pose.Carrier!=null);
                if(pose.Carrier is {} carrier)
                {WriteText(writer,carrier.Key);writer.Write(carrier.Offset.X);writer.Write(carrier.Offset.Y);writer.Write(carrier.Offset.Z);}
                break;
            case MessageKind.Travel:
                var travel = packet.Travel ?? throw new InvalidDataException("Missing travel state.");
                Validate(travel);
                writer.Write(travel.Epoch); writer.Write((byte)travel.Action); WriteText(writer, travel.Level);
                writer.Write(travel.Difficulty); writer.Write(travel.HostReady); writer.Write(travel.Paused);
                writer.Write(travel.Released); writer.Write(travel.Joining); writer.Write(travel.Loadout != null);
                if (travel.Loadout is { } loadout)
                {
                    WriteText(writer, loadout.CurrentWeapon); WriteText(writer, loadout.LastWeapon);
                    writer.Write((byte)loadout.Items.Count);
                    foreach (var item in loadout.Items) { WriteText(writer, item.Id); writer.Write(item.Quantity); }
                    writer.Write(loadout.X); writer.Write(loadout.Y); writer.Write(loadout.Z); writer.Write(loadout.Yaw);
                }
                WriteText(writer,travel.StoryVideo);writer.Write(travel.StoryTime);writer.Write(travel.StoryWaiting);writer.Write(travel.StoryEnding);writer.Write(travel.HostMenuPaused);
                break;
            case MessageKind.Ready:
                var ready = packet.Ready ?? throw new InvalidDataException("Missing ready state.");
                writer.Write(ready.Epoch); writer.Write(ready.Ready);
                break;
            default: throw new InvalidDataException("Unknown message kind.");
        }
        if (data.Length > MaxFrameBytes) throw new InvalidDataException("Frame too large.");
        return data.ToArray();
    }

    public static Packet Decode(byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaxFrameBytes) throw new InvalidDataException("Invalid frame size.");
        using var data = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(data, Utf8);
        var kind = (MessageKind)reader.ReadByte();
        var packet = kind switch
        {
            MessageKind.Save => new(kind, Save: SaveProtocol.Read(reader)),
            MessageKind.World => new Packet(kind, World: WorldProtocol.Read(reader)),
            MessageKind.Hello or MessageKind.Welcome => new Packet(kind, reader.ReadInt32(), ReadText(reader)),
            MessageKind.Reject => new Packet(kind, Text: ReadText(reader)),
            MessageKind.Pose => new Packet(kind, Pose: new PlayerPose(ReadText(reader), ReadReady(reader),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                ReadText(reader), reader.ReadSingle(), reader.ReadUInt32(), ReadReady(reader), reader.ReadUInt32(), reader.ReadUInt32(), ReadCarrier(reader))),
            MessageKind.Travel => new Packet(kind, Travel: ReadTravel(reader)),
            MessageKind.Ready => new Packet(kind, Ready: new ClientReady(reader.ReadUInt32(), ReadReady(reader))),
            _ => throw new InvalidDataException("Unknown message kind.")
        };
        if (data.Position != data.Length) throw new InvalidDataException("Trailing frame data.");
        if (packet.Pose != null) Validate(packet.Pose);
        if (packet.Travel != null) Validate(packet.Travel);
        return packet;
    }

    private static PlayerCarrier? ReadCarrier(BinaryReader reader) => ReadReady(reader)
        ? new(ReadText(reader),new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle())) : null;

    private static TravelState ReadTravel(BinaryReader reader)
    {
        var epoch = reader.ReadUInt32(); var action = (TravelAction)reader.ReadByte(); var level = ReadText(reader);
        var difficulty = reader.ReadInt32(); var hostReady = ReadReady(reader); var paused = ReadReady(reader);
        var released = ReadReady(reader); var joining = ReadReady(reader);
        JoinLoadout? loadout = null;
        if (ReadReady(reader))
        {
            var current = ReadText(reader); var last = ReadText(reader); var count = reader.ReadByte();
            if (count > MaxInventoryItems) throw new InvalidDataException("Inventory too large.");
            var items = new InventoryEntry[count];
            for (var i = 0; i < count; i++) items[i] = new(ReadText(reader), reader.ReadInt32());
            loadout = new(current, last, items, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        return new(epoch, action, level, difficulty, hostReady, paused, released, joining, loadout,ReadText(reader),reader.ReadSingle(),ReadReady(reader),ReadReady(reader),ReadReady(reader));
    }

    public static void Validate(TravelState state)
    {
        if(state.HostMenuPaused && !state.Paused) throw new InvalidDataException("Host menu pause must hold shared gameplay.");
        if(state.StoryVideo==null || state.StoryVideo.Length>128 || state.StoryVideo.Any(char.IsControl) || !float.IsFinite(state.StoryTime) || state.StoryTime<0 || state.StoryTime>86400 || state.StoryWaiting && state.StoryVideo.Length==0) throw new InvalidDataException("Invalid story state.");
        if (state.Epoch == 0 || !Enum.IsDefined(typeof(TravelAction), state.Action) || state.Difficulty is < 0 or > 15 ||
            state.Level.Length > 64 || (state.Action != TravelAction.Menu && string.IsNullOrEmpty(state.Level)) ||
            (state.Action == TravelAction.Menu && state.Level != "") || (state.Released && !state.HostReady) ||
            (state.Loadout != null && !state.Joining)) throw new InvalidDataException("Invalid travel state.");
        if (state.Loadout is not { } loadout) return;
        if (loadout.Items.Count > MaxInventoryItems || loadout.CurrentWeapon.Length > 64 || loadout.LastWeapon.Length > 64 ||
            !float.IsFinite(loadout.X) || !float.IsFinite(loadout.Y) || !float.IsFinite(loadout.Z) || !float.IsFinite(loadout.Yaw) ||
            Math.Abs(loadout.X) > 1_000_000 || Math.Abs(loadout.Y) > 1_000_000 || Math.Abs(loadout.Z) > 1_000_000)
            throw new InvalidDataException("Invalid join loadout.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in loadout.Items)
            if (string.IsNullOrEmpty(item.Id) || item.Id.Length > 64 || item.Quantity is < 0 or > 1_000_000 || !ids.Add(item.Id))
                throw new InvalidDataException("Invalid inventory entry.");
    }

    public static async Task WriteAsync(Stream stream, Packet packet, CancellationToken token)
    {
        var bytes = Encode(packet);
        var frame = new byte[bytes.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(frame, bytes.Length);
        bytes.CopyTo(frame, 4);
        await stream.WriteAsync(frame.AsMemory(), token).ConfigureAwait(false);
        ProtocolTraffic.Sent(frame.Length);
    }

    public static async Task<Packet> ReadAsync(Stream stream, CancellationToken token)
    {
        var lengthBytes = new byte[4];
        await ReadExactly(stream, lengthBytes, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is < 1 or > MaxFrameBytes) throw new InvalidDataException("Invalid frame size.");
        var bytes = new byte[length];
        await ReadExactly(stream, bytes, token).ConfigureAwait(false);
        var packet=Decode(bytes);
        ProtocolTraffic.Received(length+4);
        return packet;
    }

    private static async Task ReadExactly(Stream stream, byte[] bytes, CancellationToken token)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Peer disconnected.");
            offset += read;
        }
    }

    private static bool ReadReady(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false, 1 => true, _ => throw new InvalidDataException("Invalid ready flag.")
    };

    private static void WriteText(BinaryWriter writer, string value)
    {
        var bytes = Utf8.GetBytes(value);
        if (bytes.Length > 256) throw new InvalidDataException("Text too long.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadText(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        if (length > 256) throw new InvalidDataException("Text too long.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Truncated text.");
        return Utf8.GetString(bytes);
    }

    private static void Validate(PlayerPose pose)
    {
        PlayerCarrier.Validate(pose.Carrier);
        if (!float.IsFinite(pose.X) || !float.IsFinite(pose.Y) || !float.IsFinite(pose.Z) ||
            !float.IsFinite(pose.Yaw) || !float.IsFinite(pose.Height) || !float.IsFinite(pose.Pitch) ||
            Math.Abs(pose.X) > 1_000_000 || Math.Abs(pose.Y) > 1_000_000 || Math.Abs(pose.Z) > 1_000_000 ||
            pose.Height is < 0.1f or > 10f || Math.Abs(pose.Pitch) > 90f || pose.Weapon.Length > 64 ||
            (pose.Ready && string.IsNullOrEmpty(pose.Level)))
            throw new InvalidDataException("Invalid pose values.");
    }
}
