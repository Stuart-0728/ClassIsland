// Copyright (c) edge-tts contributors. See Assets/OpenSourceLicenses/edge-tts-LGPLv3.txt.
// Token interoperability based on rany2/edge-tts 7.3.1 drm.py (LGPL-3.0).
// https://github.com/rany2/edge-tts/blob/master/src/edge_tts/drm.py
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClassIsland.Services.SpeechService;

internal static class EdgeTtsToken
{
    private static readonly uint[] Initial = [
        0x6A09E667u, 0xBB67AE85u, 0x3C6EF372u, 0xA54FF53Au, 0x510E527Fu, 0x9B05688Cu, 0x1F83D9ABu, 0x5BE0CD19u];
    private static readonly uint[] Seed = [
        0xFD9FBEA2u, 0x91243CE7u, 0xBE499094u, 0xCD2E1E0Au, 0x6C006F49u, 0x04BD35ABu, 0xFEB0A7E4u, 0x03EFFBD2u];
    private static readonly uint[] SeedSchedule = [
        0xEB24BF9Eu, 0x1558691Fu, 0xA0C09EE2u, 0x0CFA32BAu, 0xA622EE8Cu, 0xD1DD9491u, 0xC0DD25A0u, 0xF9E70AB6u, 0xF0C31573u, 0x7058CE4Eu, 0x7188E606u, 0x00E5AB33u, 0xD258B060u, 0x72766564u];
    private static readonly uint[] RoundConstants = [
        0x428A2F98u, 0x71374491u, 0xB5C0FBCFu, 0xE9B5DBA5u, 0x3956C25Bu, 0x59F111F1u, 0x923F82A4u, 0xAB1C5ED5u, 0xD807AA98u, 0x12835B01u, 0x243185BEu, 0x550C7DC3u, 0x72BE5D74u, 0x80DEB1FEu, 0x9BDC06A7u, 0xC19BF174u, 0xE49B69C1u, 0xEFBE4786u, 0x0FC19DC6u, 0x240CA1CCu, 0x2DE92C6Fu, 0x4A7484AAu, 0x5CB0A9DCu, 0x76F988DAu, 0x983E5152u, 0xA831C66Du, 0xB00327C8u, 0xBF597FC7u, 0xC6E00BF3u, 0xD5A79147u, 0x06CA6351u, 0x14292967u, 0x27B70A85u, 0x2E1B2138u, 0x4D2C6DFCu, 0x53380D13u, 0x650A7354u, 0x766A0ABBu, 0x81C2C92Eu, 0x92722C85u, 0xA2BFE8A1u, 0xA81A664Bu, 0xC24B8B70u, 0xC76C51A3u, 0xD192E819u, 0xD6990624u, 0xF40E3585u, 0x106AA070u, 0x19A4C116u, 0x1E376C08u, 0x2748774Cu, 0x34B0BCB5u, 0x391C0CB3u, 0x4ED8AA4Au, 0x5B9CCA4Fu, 0x682E6FF3u, 0x748F82EEu, 0x78A5636Fu, 0x84C87814u, 0x8CC70208u, 0x90BEFFFAu, 0xA4506CEBu, 0xBEF9A3F7u, 0xC67178F2u];
    private static readonly (uint Factor, uint Offset)[] Coefficients = [
        (0xE82E91B1u, 0xB32700D0u), (0x44DE24C6u, 0x88DFF51Du), (0x3BEB65BCu, 0x29514038u), (0xC89C5933u, 0xB328CBDFu), (0x8D1EC211u, 0xD405B4D5u), (0x314EF5D5u, 0x26E55F3Cu), (0xE92491B1u, 0x607801C6u), (0xF0A5B9D5u, 0x82B2C1FBu), (0x06064CF3u, 0xFA51A3DAu), (0xDA79FA60u, 0x207CC464u), (0xBED20560u, 0x3C3C74F3u), (0x89F9C9F8u, 0xD2D14B61u), (0x35FA38C6u, 0xBB04A2C1u), (0xCB49E3C6u, 0xC5626167u)];

    private static uint Rotate16(uint x) => (x << 16) | (x >> 16);
    private static uint Sigma0(uint x) => ((x >> 7) | (x << 25)) ^ ((x >> 18) | (x << 14)) ^ (x >> 3);
    private static uint Sigma1(uint x) => ((x >> 17) | (x << 15)) ^ ((x >> 19) | (x << 13)) ^ (x >> 10);
    private static uint Ror(uint x, int n) => (x >> n) | (x << (32 - n));
    private static void Expand(List<uint> words, int first = 16)
    {
        unchecked { for (var i = first; i < 64; i++) words.Add(Sigma1(words[i-2]) + words[i-7] + Sigma0(words[i-15]) + words[i-16]); }
    }
    private static uint[] Rounds(uint[] state, List<uint> words, int first = 0)
    {
        var a=state[0]; var b=state[1]; var c=state[2]; var d=state[3]; var e=state[4]; var f=state[5]; var g=state[6]; var h=state[7];
        unchecked
        {
            for (var i=first; i<64; i++)
            {
                var k=first==14 && i==14 ? 0xC9DC06A7u : RoundConstants[i];
                var t1=h+(Ror(e,6)^Ror(e,11)^Ror(e,25))+(g^(e&(f^g)))+k+words[i];
                var t2=(Ror(a,2)^Ror(a,13)^Ror(a,22))+((a&b)|(c&(a|b)));
                h=g; g=f; f=e; e=d+t1; d=c; c=b; b=a; a=t1+t2;
            }
        }
        return [a,b,c,d,e,f,g,h];
    }
    private static uint[] InitialState(byte[] data)
    {
        var words=Enumerable.Repeat(0u,14).ToList();
        words.Add((uint)(data[0]<<16 | data[1]<<8 | data[2]));
        words.Add(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(3,4)));
        unchecked
        {
            for(var i=16;i<30;i++)
            {
                var x=SeedSchedule[i-16];
                if(i-2>=14) x+=Sigma1(words[i-2])^(i==16?0x000B92C0u:0u);
                if(i-7>=14) x+=words[i-7];
                if(i-15>=14) x+=Sigma0(words[i-15])^(i==29?0x059C0B80u:0u);
                words.Add(x);
            }
            words.Add(Sigma1(words[28])+words[23]+Sigma0(words[15])+words[14]+0xC9DC06A7u-RoundConstants[14]);
            Expand(words,31);
            var working=Rounds(Seed,words,14);
            return Initial.Select((x,i)=>x+working[i]).ToArray();
        }
    }
    private static byte[] Digest(byte[] data)
    {
        var length=57+data.Length;
        var zeros=((55-length)%64+64)%64;
        var padded=new byte[data.Length-7+1+zeros+8];
        data.AsSpan(7).CopyTo(padded); padded[data.Length-7]=0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(padded.Length-8),(ulong)length*8);
        var state=InitialState(data);
        unchecked
        {
            for(var offset=0;offset<padded.Length;offset+=64)
            {
                var words=new List<uint>(64);
                for(var i=0;i<16;i++) words.Add(BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(offset+i*4,4)));
                Expand(words); var working=Rounds(state,words);
                state=state.Select((x,i)=>x+working[i]).ToArray();
            }
        }
        var digest=new byte[32];
        for(var i=0;i<8;i++) BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i*4),state[i]);
        return digest;
    }
    private static byte[] Checksum(byte[] data, byte[] digest)
    {
        unchecked
        {
            var keyA=BinaryPrimitives.ReadUInt32LittleEndian(digest)|1u;
            var keyB=BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4))|1u;
            var factors=new[]{keyA*0x82893933u,keyB*0x82893933u};
            var terms=Coefficients.Select((x,i)=>x.Factor*factors[i%2]).ToList();
            var offsets=Coefficients.Select(x=>x.Offset).ToList();
            var padded=new byte[((data.Length+1+7)/8)*8]; data.CopyTo(padded,1);
            for(var i=0;i<padded.Length/4;i++)
            {
                var word=BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(i*4));
                var term=word*(i%2==0?keyA:keyB);
                var offset=Rotate16(word)*(i%2==0?0xEF0569FBu:0xC31713DBu);
                if(i==0){term+=0xF2F90B1Au*factors[0];offset+=0x0B1A0000u;}
                terms.Add(term);offsets.Add(offset);
            }
            uint swapAcc=0,swapLast=0,state=0,acc=0;
            for(var i=0;i<terms.Count;i+=2)
            {
                var t=Rotate16(offsets[i]+terms[i]); var q=t*0x76F50F34u;
                swapAcc+=Rotate16(q)*0xC3EFEA97u+q;
                t=Rotate16(offsets[i+1]+terms[i+1]);q=t*0x1881CE3Cu;
                swapLast=Rotate16(q)*0x35BD1EC9u+q;swapAcc+=swapLast;
                state=state*keyA+terms[i];state=Rotate16(state)*0xCF98B111u;
                state=Rotate16(state)*0x87085B9Fu;state=Rotate16(state)*0x12CEB96Du;state=Rotate16(state)*0x257E1D83u;acc+=state;
                t=state*keyB+terms[i+1];t=Rotate16(t)*0xA27416F5u;t=Rotate16(t)*0xD38396FFu;
                state=Rotate16(Rotate16(t)*0x7C932B89u)*0xBFA49F69u;acc+=state;
            }
            var result=new byte[8];BinaryPrimitives.WriteUInt32LittleEndian(result,swapLast^state);BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4),swapAcc^acc);return result;
        }
    }
    internal static string Generate(DateTimeOffset now)
    {
        var ticks=now.UtcDateTime.ToFileTimeUtc(); ticks-=ticks%3_000_000_000L;
        var data=Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture)+"6A5AA1D4EAFF4E9FB37E23D68491D6F4");
        var digest=Digest(data);
        return Convert.ToHexString(Checksum(data,digest))+"-"+Convert.ToHexString(digest);
    }
}
