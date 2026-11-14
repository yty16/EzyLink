using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace YiTongYao.Core;

/// <summary>安全认证服务。</summary>
internal static class AuthService
{
    // PBKDF2：310000 次迭代（OWASP 对 PBKDF2-HMAC-SHA256 的推荐量级）+ 随机盐 + 32 字节输出
    private const int Iterations = 310000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // 连续失败达到上限后的锁定阶梯（分钟），逐级递增
    private static readonly int[] LockoutMinutes = { 1, 5, 10, 30, 60, 120, 240 };

    public static SettingsStore? Settings { get; set; }

    // ---------- 会话 ----------

    public static bool SessionUnlocked { get; private set; }
    public static bool SessionSystem { get; private set; }
    public static bool SessionUsb { get; private set; }
    private static DateTime _unlockAt = DateTime.MinValue;

    public static void ResetSession()
    {
        SessionUnlocked = false;
        SessionSystem = false;
        SessionUsb = false;
        _unlockAt = DateTime.MinValue;
    }

    public static void SetSessionUnlocked()
    {
        SessionUnlocked = true;
        _unlockAt = DateTime.UtcNow;
    }

    public static void SetSystemUnlocked()
    {
        ResetLockout();
        SessionUnlocked = true;
        SessionSystem = true;
        _unlockAt = DateTime.UtcNow;
    }

    public static void SetUsbUnlocked()
    {
        ResetLockout();
        SessionUnlocked = true;
        SessionUsb = true;
        _unlockAt = DateTime.UtcNow;
    }

    public static void RefreshSession()
    {
        if (SessionUnlocked) _unlockAt = DateTime.UtcNow;
    }

    public static bool IsSessionActive(int ttlMinutes)
    {
        if (!SessionUnlocked) return false;
        if (ttlMinutes <= 0) return true;
        return (DateTime.UtcNow - _unlockAt).TotalMinutes < ttlMinutes;
    }

    // ---------- 哈希与校验 ----------

    public static string HashSecret(string secret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Pbkdf2(secret, salt);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public static bool VerifySecret(string stored, string input)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(input)) return false;
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expected = Convert.FromBase64String(parts[1]);
        }
        catch
        {
            return false;
        }
        byte[] actual = Pbkdf2(input, salt);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    // Crockford Base32 字母表
    private const string RecoveryAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int RecoveryBytes = 20;

    // 生成本机一次性恢复码（160 位，分组便于抄写）
    public static string CreateRecoveryCode()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(RecoveryBytes);
        int acc = 0, bits = 0;
        var sb = new StringBuilder();
        foreach (byte b in raw)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(RecoveryAlphabet[(acc >> bits) & 31]);
                acc &= (1 << bits) - 1;
            }
        }
        if (bits > 0) sb.Append(RecoveryAlphabet[(acc << (5 - bits)) & 31]);
        string s = sb.ToString();
        return string.Join("-", Enumerable.Range(0, (s.Length + 3) / 4)
            .Select(i => s.Substring(i * 4, Math.Min(4, s.Length - i * 4))));
    }

    // 恢复码输入归一化
    public static string NormalizeRecoveryCode(string input)
    {
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            char u = char.ToUpperInvariant(c);
            if (u == 'O') u = '0';
            else if (u == 'I' || u == 'L') u = '1';
            if (RecoveryAlphabet.IndexOf(u) >= 0) sb.Append(u);
        }
        return sb.ToString();
    }

    // 写入恢复码哈希；明文不落盘
    public static void SaveRecoveryCode(SettingsStore s, string code)
    {
        s.RecoveryCodeHash = HashSecret(NormalizeRecoveryCode(code));
        s.RecoveryCodeShown = true;
        s.Save();
    }

    // 校验本机恢复码
    public static bool VerifyRecoveryCode(string input)
    {
        var stored = Settings?.RecoveryCodeHash;
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(input)) return false;
        return VerifySecret(stored, NormalizeRecoveryCode(input));
    }

    // ---------- 解锁尝试 ----------

    public static bool TryUnlock(string stored, string input)
    {
        if (VerifyRecoveryCode(input))
        {
            ResetLockout();
            SetSessionUnlocked();
            return true;
        }
        if (IsLocked(out _)) return false;
        if (VerifySecret(stored, input))
        {
            ResetLockout();
            SetSessionUnlocked();
            return true;
        }
        return false;
    }

    public static bool TryRecover(string answerHash, string answer)
    {
        if (string.IsNullOrEmpty(answerHash)) return false;
        if (VerifyRecoveryCode(answer)) { ResetLockout(); return true; }
        if (VerifySecret(answerHash, answer)) { ResetLockout(); return true; }
        return false;
    }

    // ---------- 认证方式查询 ----------

    public static bool HasAnyMethod(SettingsStore? s) => s != null && s.AuthMethods.Count > 0;

    // 主密码：第一条密码类认证方式
    public static AuthMethodEntry? Master(SettingsStore s)
        => s.AuthMethods.FirstOrDefault(m => m.Kind == AuthMethodKind.Password && m.Name == "主密码")
           ?? s.AuthMethods.FirstOrDefault(m => m.Kind == AuthMethodKind.Password);

    // 主密码已授权的操作
    public static HashSet<string> MasterOperations(SettingsStore s)
        => Master(s) is { } m
            ? m.Operations.Where(AuthOperation.All.Contains).ToHashSet()
            : new HashSet<string>();

    // 补齐主密码授权，清理无效操作；返回是否改动
    public static bool NormalizeOperations(SettingsStore s)
    {
        bool changed = false;
        var master = Master(s);
        if (master == null) return false;

        var union = MasterOperations(s);
        foreach (var m in s.AuthMethods)
            foreach (var op in m.Operations)
                if (AuthOperation.All.Contains(op)) union.Add(op);
        foreach (var op in AuthOperation.All)
            if (union.Contains(op) && !master.Operations.Contains(op))
            { master.Operations.Add(op); changed = true; }

        foreach (var m in s.AuthMethods)
        {
            var clean = m.Operations.Where(AuthOperation.All.Contains).Distinct().ToList();
            if (!clean.SequenceEqual(m.Operations)) { m.Operations = clean; changed = true; }
        }
        return changed;
    }

    public static List<AuthMethodEntry> MethodsFor(SettingsStore s, string operation)
    {
        // 无主密码时保留既有配置
        var master = Master(s);
        if (master == null)
        {
            var legacy = s.AuthMethods.Where(m => m.Operations.Contains(operation)).ToList();
            if (legacy.Count == 0)
                legacy = s.AuthMethods.Where(m => m.Operations.Count == 0).ToList();
            return legacy;
        }
        // 主密码未授权的操作不受保护，且主密码始终可用于已授权的操作
        if (!master.Operations.Contains(operation)) return new List<AuthMethodEntry>();
        var list = s.AuthMethods.Where(m => m.Operations.Contains(operation)).ToList();
        if (!list.Contains(master)) list.Insert(0, master);
        return list;
    }

    public static bool NeedsAuth(SettingsStore s, string operation)
        => MethodsFor(s, operation).Count > 0;

    public static bool RequireTwoFactors(SettingsStore s, List<AuthMethodEntry> methods)
        => s.TwoFactorEnabled
           && methods.Select(m => AuthMethodKinds.Category(m.Kind)).Distinct().Count() >= 2;

    // ---------- 失败锁定 ----------

    public static bool IsLocked(out int remainingMinutes)
    {
        remainingMinutes = 0;
        var diff = GetLockRemaining();
        if (diff <= TimeSpan.Zero)
        {
            if (Settings != null && Settings.PasswordFailedAttempts >= 5)
            {
                Settings.PasswordFailedAttempts = 0;
                Settings.PasswordLockoutUntil = DateTime.MinValue;
                Settings.Save();
            }
            return false;
        }
        remainingMinutes = (int)Math.Ceiling(diff.TotalMinutes);
        return true;
    }

    public static TimeSpan GetLockRemaining()
    {
        if (Settings == null) return TimeSpan.Zero;
        var until = Settings.PasswordLockoutUntil;
        if (until.Kind != DateTimeKind.Utc) until = until.ToUniversalTime();
        var diff = until - DateTime.UtcNow;
        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
    }

    public static void RecordFailure(out int remainingAttempts, out int lockoutMinutes)
    {
        remainingAttempts = 0;
        lockoutMinutes = 0;
        if (Settings == null) return;
        Settings.PasswordFailedAttempts++;
        if (Settings.PasswordFailedAttempts < 5)
        {
            remainingAttempts = 5 - Settings.PasswordFailedAttempts;
            Settings.Save();
            return;
        }
        int level = Math.Min(Settings.PasswordLockoutLevel, LockoutMinutes.Length - 1);
        lockoutMinutes = LockoutMinutes[level];
        Settings.PasswordLockoutUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
        Settings.PasswordLockoutLevel++;
        remainingAttempts = 0;
        Settings.Save();
    }

    public static void ResetLockout()
    {
        if (Settings == null) return;
        if (Settings.PasswordFailedAttempts == 0 &&
            Settings.PasswordLockoutUntil <= DateTime.MinValue &&
            Settings.PasswordLockoutLevel == 0) return;
        Settings.PasswordFailedAttempts = 0;
        Settings.PasswordLockoutUntil = DateTime.MinValue;
        Settings.PasswordLockoutLevel = 0;
        Settings.Save();
    }

    private static byte[] Pbkdf2(string value, byte[] salt)
    {
        using var d = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(value), salt, Iterations, HashAlgorithmName.SHA256);
        return d.GetBytes(HashBytes);
    }
}
