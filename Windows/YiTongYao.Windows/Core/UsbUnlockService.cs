using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace YiTongYao.Core;

public sealed class UsbVolume
{
    public string Label { get; set; } = "";
    public string RootPath { get; set; } = "";
}

// U 盘解锁（卷序列号 / 令牌文件）
internal static class UsbUnlockService
{
    private const string TokenFileName = ".yitongyao_usb";

    public static bool IsSupported() => true;

    public static string GenerateToken() => Guid.NewGuid().ToString("N");

    public static string HashToken(string token)
    {
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var sb = new StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    public static List<UsbVolume> ListCandidateVolumes()
    {
        var list = new List<UsbVolume>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            if (d.DriveType != DriveType.Removable) continue;
            var root = d.RootDirectory.FullName;
            var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? root.TrimEnd('\\') : d.VolumeLabel;
            list.Add(new UsbVolume { Label = label, RootPath = root });
        }
        return list;
    }

    public static bool WriteToken(string rootPath, string token)
    {
        try
        {
            var path = Path.Combine(rootPath, TokenFileName);
            File.WriteAllText(path, token);
            File.SetAttributes(path, FileAttributes.Hidden);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadToken(string rootPath)
    {
        try
        {
            var path = Path.Combine(rootPath, TokenFileName);
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool TryUnlock(string expectedHash, bool fileMode)
    {
        if (string.IsNullOrEmpty(expectedHash)) return false;
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            var root = d.RootDirectory.FullName;
            if (fileMode)
            {
                var tok = ReadToken(root);
                if (tok != null && HashToken(tok) == expectedHash) return true;
            }
            else
            {
                var serial = GetVolumeSerial(root);
                if (serial != null && HashToken(serial) == expectedHash) return true;
            }
        }
        return false;
    }

    public static string? GetVolumeSerial(string rootPath)
    {
        try
        {
            var sbVol = new StringBuilder(256);
            uint serial = 0;
            uint maxCompLen = 0, fileSysFlags = 0;
            var sbFs = new StringBuilder(256);
            if (GetVolumeInformation(rootPath, sbVol, 256, ref serial, ref maxCompLen,
                    ref fileSysFlags, sbFs, 256))
                return serial.ToString("X8");
        }
        catch { }
        try
        {
            var d = new DriveInfo(rootPath);
            return (d.VolumeLabel ?? "") + "|" + rootPath;
        }
        catch { }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string lpRootPathName,
        StringBuilder lpVolumeNameBuffer, int nVolumeNameSize, ref uint lpVolumeSerialNumber,
        ref uint lpMaximumComponentLength, ref uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer, int nFileSystemNameSize);
}
