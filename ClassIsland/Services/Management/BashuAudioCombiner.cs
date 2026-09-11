using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace ClassIsland.Services.Management;

public static class BashuAudioCombiner
{
    public static bool TryGetDataChunk(ReadOnlySpan<byte> wav, out int dataOffset, out int dataLength)
    {
        dataOffset = 44;
        dataLength = wav.Length - 44;
        if (wav.Length < 44) return false;
        if (wav[0] != (byte)'R' || wav[1] != (byte)'I' || wav[2] != (byte)'F' || wav[3] != (byte)'F') return false;
        if (wav[8] != (byte)'W' || wav[9] != (byte)'A' || wav[10] != (byte)'V' || wav[11] != (byte)'E') return false;

        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var isData = wav[pos] == (byte)'d' && wav[pos + 1] == (byte)'a' && wav[pos + 2] == (byte)'t' && wav[pos + 3] == (byte)'a';
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(pos + 4, 4));
            if (isData)
            {
                dataOffset = pos + 8;
                dataLength = Math.Min(chunkSize > 0 ? chunkSize : (wav.Length - dataOffset), wav.Length - dataOffset);
                return dataLength > 0;
            }
            if (chunkSize <= 0 || pos + 8 + chunkSize > wav.Length)
            {
                break;
            }
            pos += 8 + chunkSize;
        }

        dataOffset = 44;
        dataLength = Math.Max(0, wav.Length - 44);
        return dataLength > 0;
    }

    public static byte[] CombinePcmWav(IReadOnlyList<byte[]> wavList)
    {
        if (wavList.Count == 0) return Array.Empty<byte>();
        if (wavList.Count == 1) return wavList[0];

        var pcmParts = new List<(byte[] Buffer, int Offset, int Length)>();
        var totalPcmBytes = 0;
        foreach (var wav in wavList)
        {
            if (TryGetDataChunk(wav, out var dataOffset, out var dataLength) && dataLength > 0)
            {
                pcmParts.Add((wav, dataOffset, dataLength));
                totalPcmBytes += dataLength;
            }
        }

        if (pcmParts.Count == 0) return wavList[0];
        if (pcmParts.Count == 1) return pcmParts[0].Buffer;

        var firstWav = pcmParts[0].Buffer;
        var firstOffset = pcmParts[0].Offset;
        var result = new byte[firstOffset + totalPcmBytes];
        Array.Copy(firstWav, 0, result, 0, firstOffset);

        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), result.Length - 8);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(firstOffset - 4, 4), totalPcmBytes);

        var currentOffset = firstOffset;
        foreach (var part in pcmParts)
        {
            Array.Copy(part.Buffer, part.Offset, result, currentOffset, part.Length);
            currentOffset += part.Length;
        }
        return result;
    }
}
