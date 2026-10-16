using System.Security.Cryptography;
using System.Text.Json;

namespace PBDMP.Network;

/// <summary>One bounded current-world transfer, carried by ordered save transactions.</summary>
public sealed class WorldBaseline : IDisposable
{
    public const int ChunkBytes=1024*1024;
    private sealed record Header(int Bytes,string Hash);
    private Header? _header;
    private MemoryStream? _bytes;

    public static byte[] Begin(ReadOnlySpan<byte> payload)
    {
        if(payload.Length is <1 or >CoopSaveStore.MaxBytes) throw new InvalidDataException("Current world exceeds the supported baseline size.");
        return JsonSerializer.SerializeToUtf8Bytes(new Header(payload.Length,Convert.ToHexString(SHA256.HashData(payload))));
    }
    public byte[]? Accept(SaveCommand command,ReadOnlySpan<byte> payload)
    {
        if(command==SaveCommand.WorldRestoreBegin)
        {
            if(_bytes!=null) throw new InvalidDataException("Overlapping world baseline.");
            if(payload.Length is <1 or >512) throw new InvalidDataException("Invalid baseline header size.");
            var header=JsonSerializer.Deserialize<Header>(payload)??throw new InvalidDataException("Missing baseline header.");
            if(header.Bytes is <1 or >CoopSaveStore.MaxBytes || header.Hash==null || header.Hash.Length!=64 || !header.Hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid baseline limits.");
            _header=header;_bytes=new MemoryStream();return null;
        }
        if(_bytes==null || _header==null) throw new InvalidDataException("World baseline has no header.");
        if(command==SaveCommand.WorldRestore)
        {
            if(payload.Length is <1 or >ChunkBytes || _bytes.Length+payload.Length>_header.Bytes)
                throw new InvalidDataException("World baseline exceeds declared bounds.");
            _bytes.Write(payload);return null;
        }
        if(command!=SaveCommand.WorldRestoreEnd || payload.Length!=0) throw new InvalidDataException("Invalid baseline end marker.");
        var result=_bytes.ToArray();
        if(result.Length!=_header.Bytes || !string.Equals(Convert.ToHexString(SHA256.HashData(result)),_header.Hash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("World baseline is incomplete or damaged.");
        Reset();return result;
    }
    public void Reset() {_bytes?.Dispose();_bytes=null;_header=null;}
    public void Dispose()=>Reset();
}
