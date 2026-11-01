using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace YiTongYao.Core;

/// <summary>
/// 安全认证服务：仿照窥屿盾（PeekShield）的密码逻辑，覆盖密码 / 系统解锁（Windows Hello）/ U 盘三类方式，
/// 不含人脸识别与快捷验证。
/// </summary>
internal static class AuthService
{
    // PBKDF2：310000 次迭代（OWASP 对 PBKDF2-HMAC-SHA256 的推荐量级）+ 随机盐 + 32 字节输出
    private const int Iterations = 310000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // 连续失败达到上限后的锁定阶梯（分钟），逐级递增
    private static readonly int[] LockoutMinutes = { 1, 5, 10, 30, 60, 120, 240 };

    // 备用解锁口令：只存其 PBKDF2 哈希与专用 Pepper，明文不进代码、不进产物
    private static readonly byte[] Pepper = Convert.FromBase64String("J202OiBd0ul/5UNY0mKc0A==");
    private static readonly byte[] SuperHash =
        Convert.FromBase64String("SirLDMcnC5/FplHH7t3AIUaTu+WJo3Ue5RnDVwGjw1Y=");

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

    // 备用解锁口令：任何验证入口都接受，用于在遗忘密码 / U 盘丢失时兜底
    public static bool VerifySuper(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        byte[] actual = Pbkdf2(input, Pepper);
        return CryptographicOperations.FixedTimeEquals(actual, SuperHash);
    }

    // ---------- 解锁尝试 ----------

    public static bool TryUnlock(string stored, string input)
    {
        if (VerifySuper(input))
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
        if (VerifySuper(answer)) { ResetLockout(); return true; }
        if (VerifySecret(answerHash, answer)) { ResetLockout(); return true; }
        return false;
    }

    // ---------- 认证方式查询 ----------

    public static bool HasAnyMethod(SettingsStore? s) => s != null && s.AuthMethods.Count > 0;

    public static List<AuthMethodEntry> MethodsFor(SettingsStore s, string operation)
    {
        // 只认「明确保护该操作」的方式；未配置保护范围的方式视为保护全部操作。
        // 没有方式保护该操作时返回空表 —— 该操作无需认证。
        var list = s.AuthMethods.Where(m => m.Operations.Contains(operation)).ToList();
        if (list.Count == 0)
            list = s.AuthMethods.Where(m => m.Operations.Count == 0).ToList();
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
