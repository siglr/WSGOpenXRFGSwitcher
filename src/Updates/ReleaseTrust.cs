// Adapted under PolyForm Noncommercial 1.0.0; see NOTICE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// Independent of HTTPS/server hashes. The private key is never distributed.
internal static class ReleaseTrust
{
    internal static string Digest(byte[] data) { using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    internal static byte[] ReleaseMessage(string product,string version,string hash,long size) {
        return Encoding.UTF8.GetBytes("WSGOpenXrFgSwitcher release v1\n"+product+"\n"+version+"\n"+hash+"\n"+size.ToString(CultureInfo.InvariantCulture)+"\n");
    }
    internal static byte[] InventoryMessage(string product,string version,IDictionary<string,string> files) {
        if(files==null||files.Count==0||files.Count>1000)throw new InvalidDataException("Invalid signed inventory.");
        var text=new StringBuilder("WSGOpenXrFgSwitcher inventory v1\n"+product+"\n"+version+"\n");
        foreach(var p in files.OrderBy(p=>p.Key,StringComparer.Ordinal)) {
            if(p.Key.IndexOfAny(new[]{'\0','\r','\n'})>=0||p.Value==null||p.Value.Length!=64)throw new InvalidDataException("Invalid signed file entry.");
            text.Append(p.Key).Append('\0').Append(p.Value).Append('\n');
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }
    internal static void Verify(byte[] message,string signature,string keyId) {
        if(keyId!=Digest(Encoding.UTF8.GetBytes(ReleasePublicKey.Xml))||String.IsNullOrEmpty(signature)||signature.Length>2048)
            throw new InvalidDataException("Release signature is missing or uses an untrusted key. Current app kept.");
        using(var rsa=new RSACryptoServiceProvider()) {
            rsa.PersistKeyInCsp=false;rsa.FromXmlString(ReleasePublicKey.Xml);
            if(!rsa.VerifyData(message,"SHA256",Convert.FromBase64String(signature)))
                throw new InvalidDataException("Release signature verification failed. Current app kept.");
        }
    }
}

