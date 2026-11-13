using System;
using System.Collections.Generic;
using System.Text;

namespace YiTongYao.Core;

/// <summary>
/// 二维码（QR Code）编码器 —— 标准兼容的精简实现，无第三方依赖，保证离线可构建。
/// 支持：字节模式（0100）、纠错等级 L / M、版本 1 ~ 10（按内容长度自动选择最小可用版本）。
/// 输出为模块矩阵（true = 深色），渲染时另加 4 模块静默区。
/// </summary>
internal static class QrEncoder
{
    /// <summary>扫码载荷前缀，移动端据此识别是否为易通遥的连接二维码。</summary>
    public const string PayloadPrefix = "YTY1";

    private const int MinVersion = 1;
    private const int MaxVersion = 10;
    private const int EccLow = 0;
    private const int EccMedium = 1;

    // 每块纠错码字数 [version][ecc]
    private static readonly int[][] EccCodewordsPerBlock =
    {
        new[] { 0, 0, 0, 0 },      // 0（占位，版本从 1 开始）
        new[] { 7, 10, 13, 17 },   // 1
        new[] { 10, 16, 22, 28 },  // 2
        new[] { 15, 26, 18, 22 },  // 3
        new[] { 20, 18, 26, 16 },  // 4
        new[] { 26, 24, 18, 22 },  // 5
        new[] { 18, 16, 24, 28 },  // 6
        new[] { 20, 18, 18, 26 },  // 7
        new[] { 24, 22, 22, 26 },  // 8
        new[] { 30, 22, 20, 24 },  // 9
        new[] { 18, 26, 24, 28 }   // 10
    };

    // 纠错块数 [version][ecc]
    private static readonly int[][] NumEcBlocks =
    {
        new[] { 0, 0, 0, 0 },      // 0（占位）
        new[] { 1, 1, 1, 1 },  // 1
        new[] { 1, 1, 1, 1 },  // 2
        new[] { 1, 1, 2, 2 },  // 3
        new[] { 1, 2, 2, 4 },  // 4
        new[] { 1, 2, 2, 2 },  // 5
        new[] { 2, 4, 4, 4 },  // 6
        new[] { 2, 4, 2, 4 },  // 7
        new[] { 2, 2, 4, 4 },  // 8
        new[] { 2, 3, 4, 4 },  // 9
        new[] { 2, 4, 6, 6 }   // 10
    };

    // 对齐图案中心坐标 [version]
    private static readonly int[][] AlignmentPositions =
    {
        new int[0],                // 0（占位）
        new int[0],             // 1
        new[] { 6, 18 },        // 2
        new[] { 6, 22 },        // 3
        new[] { 6, 26 },        // 4
        new[] { 6, 30 },        // 5
        new[] { 6, 34 },        // 6
        new[] { 6, 22, 38 },    // 7
        new[] { 6, 24, 42 },    // 8
        new[] { 6, 26, 46 },    // 9
        new[] { 6, 28, 50 }     // 10
    };

    // ---- GF(256) 查表（本原多项式 0x11D，本原元 2）----
    private static readonly byte[] ExpTable = new byte[512];
    private static readonly byte[] LogTable = new byte[256];

    static QrEncoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            ExpTable[i] = (byte)x;
            LogTable[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) ExpTable[i] = ExpTable[i - 255];
    }

    private static byte GfMul(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        int z = LogTable[a] + LogTable[b];
        return ExpTable[z];
    }

    /// <summary>构造扫码载荷：<c>YTY1|IP|端口|配对码[|蓝牙MAC]</c>。</summary>
    public static string BuildPayload(string ip, int port, string pairCode, string btMac = "")
    {
        string mac = (btMac ?? string.Empty).Trim();
        return mac.Length == 0
            ? string.Format("{0}|{1}|{2}|{3}", PayloadPrefix, ip, port, pairCode)
            : string.Format("{0}|{1}|{2}|{3}|{4}", PayloadPrefix, ip, port, pairCode, mac);
    }

    /// <summary>将文本编码为二维码矩阵（true = 深色模块）。</summary>
    public static bool[][] Encode(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text ?? string.Empty);
        int version = ChooseVersion(data.Length);
        if (version < 0) throw new ArgumentException("内容过长，无法生成二维码。");

        byte[] codewords = BuildCodewords(data, version);
        return BuildMatrix(codewords, version);
    }

    /// <summary>渲染为可显示位图包含的矩阵（已含 4 模块静默区）。</summary>
    public static bool[][] EncodeWithQuietZone(string text)
    {
        bool[][] core = Encode(text);
        int size = core.Length;
        int full = size + 8;
        bool[][] full1 = new bool[full][];
        for (int r = 0; r < full; r++)
        {
            full1[r] = new bool[full];
            for (int c = 0; c < full; c++)
            {
                bool inside = r >= 4 && r < 4 + size && c >= 4 && c < 4 + size;
                full1[r][c] = inside && core[r - 4][c - 4];
            }
        }
        return full1;
    }

    /// <summary>调试用：导出文本编码后的完整码字序列（十六进制）。</summary>
    public static string DumpCodewords(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text ?? string.Empty);
        int version = ChooseVersion(data.Length);
        byte[] cw = BuildCodewords(data, version);
        var sb = new StringBuilder();
        sb.Append("version=").Append(version).Append('\n');
        foreach (byte b in cw) sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().Trim();
    }

    private static int ChooseVersion(int dataLen)
    {
        for (int v = MinVersion; v <= MaxVersion; v++)
        {
            int capacityBits = DataCodewords(v, EccLow) * 8;
            int needBits = 4 + CharCountBits(v) + dataLen * 8;
            if (capacityBits >= needBits) return v;
        }
        return -1;
    }

    private static int DataCodewords(int version, int ecc)
    {
        int raw = NumRawDataModules(version) / 8;
        return raw - EccCodewordsPerBlock[version][ecc] * NumEcBlocks[version][ecc];
    }

    private static int NumRawDataModules(int ver)
    {
        int result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            int numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    private static int CharCountBits(int version)
    {
        return version < 10 ? 8 : 16;
    }

    private static byte[] BuildCodewords(byte[] data, int version)
    {
        int ecc = EccLow;
        int numBlocks = NumEcBlocks[version][ecc];
        int blockEccLen = EccCodewordsPerBlock[version][ecc];
        int rawCodewords = NumRawDataModules(version) / 8;
        int numShortBlocks = numBlocks - rawCodewords % numBlocks;
        int shortBlockLen = rawCodewords / numBlocks;

        // ---- 比特流 ----
        var bits = new List<bool>(rawCodewords * 8);
        AppendBits(bits, 0x4, 4);                       // 字节模式
        AppendBits(bits, data.Length, CharCountBits(version));
        foreach (byte b in data) AppendBits(bits, b, 8);

        int capacityBits = DataCodewords(version, ecc) * 8;
        AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
        AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
            AppendBits(bits, (byte)pad, 8);

        var dataCodewords = new byte[capacityBits / 8];
        for (int i = 0; i < dataCodewords.Length; i++)
        {
            int val = 0;
            for (int k = 0; k < 8; k++) val = (val << 1) | (bits[i * 8 + k] ? 1 : 0);
            dataCodewords[i] = (byte)val;
        }

        // ---- 分块 + RS ----
        var blocks = new List<byte[]>();
        byte[] gen = RsGenerator(blockEccLen);
        int offset = 0;
        for (int i = 0; i < numBlocks; i++)
        {
            int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
            var dat = new byte[shortBlockLen + 1];
            Array.Copy(dataCodewords, offset, dat, 0, datLen);
            offset += datLen;
            byte[] eccBytes = RsRemainder(dat, datLen, ToHighFirst(gen), blockEccLen);
            Array.Copy(eccBytes, 0, dat, shortBlockLen - blockEccLen + 1, blockEccLen);
            blocks.Add(dat);
        }

        // ---- 交织（数据码字 + 纠错码字，按索引外层、块内层）----
        var result = new List<byte>(rawCodewords);
        for (int j = 0; j < blocks[0].Length; j++)
        {
            if (j == shortBlockLen - blockEccLen) continue;   // 短块的填充位
            for (int k = 0; k < blocks.Count; k++)
                result.Add(blocks[k][j]);
        }
        return result.ToArray();
    }

    private static void AppendBits(List<bool> bits, int value, int len)
    {
        for (int i = len - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    // ---- Reed–Solomon over GF(256) ----

    /// <summary>生成多项式系数，低次在前，长度 degree+1，最高次系数为 1。</summary>
    private static byte[] RsGenerator(int degree)
    {
        var g = new byte[degree + 1];
        g[0] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = degree; j >= 1; j--)
                g[j] = (byte)(g[j - 1] ^ GfMul(g[j], (byte)root));
            g[0] = GfMul(g[0], (byte)root);
            root = GfMul((byte)root, 2);
        }
        return g;
    }

    /// <summary>把「低次在前」的系数数组反转为「高次在前」。</summary>
    private static byte[] ToHighFirst(byte[] lowFirst)
    {
        var high = new byte[lowFirst.Length];
        for (int i = 0; i < lowFirst.Length; i++)
            high[i] = lowFirst[lowFirst.Length - 1 - i];
        return high;
    }

    /// <summary>
    /// 多项式长除法取模：计算 (data(x) * x^degree) mod gen(x)。
    /// data 与 gen 均为「高次在前」排列，返回余式系数（高次在前，长度 degree）。
    /// </summary>
    private static byte[] RsRemainder(byte[] data, int dataLen, byte[] gen, int degree)
    {
        var buf = new byte[dataLen + degree];
        Array.Copy(data, 0, buf, 0, dataLen);

        for (int i = 0; i < dataLen; i++)
        {
            int coef = buf[i] & 0xFF;
            if (coef == 0) continue;
            for (int j = 0; j <= degree; j++)
                buf[i + j] ^= GfMul(gen[j], (byte)coef);
        }

        var result = new byte[degree];
        Array.Copy(buf, dataLen, result, 0, degree);
        return result;
    }

    // ---- 矩阵构造 ----

    private static bool[][] BuildMatrix(byte[] codewords, int version)
    {
        int size = version * 4 + 17;
        var modules = new bool[size][];
        var isFunc = new bool[size][];
        for (int i = 0; i < size; i++)
        {
            modules[i] = new bool[size];
            isFunc[i] = new bool[size];
        }

        // 定时图案（先画，随后由定位图案覆盖其端点）
        for (int i = 0; i < size; i++)
        {
            SetFunc(modules, isFunc, size, 6, i, i % 2 == 0);
            SetFunc(modules, isFunc, size, i, 6, i % 2 == 0);
        }

        // 定位图案 + 分隔符
        DrawFinder(modules, isFunc, size, 3, 3);
        DrawFinder(modules, isFunc, size, size - 4, 3);
        DrawFinder(modules, isFunc, size, 3, size - 4);

        // 对齐图案（跳过与定位图案重叠的三角）
        int[] align = AlignmentPositions[version];
        int last = align.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            for (int j = 0; j <= last; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)) continue;
                DrawAlignment(modules, isFunc, size, align[i], align[j]);
            }
        }

        // 格式信息区（先占位，选掩码后写入真值）
        DrawFormatBits(modules, isFunc, size, 0);
        if (version >= 7) DrawVersionBits(modules, isFunc, size, version);

        // 数据码字放置（之字形）
        int bitIndex = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int c = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int r = upward ? size - 1 - vert : vert;
                    if (!isFunc[r][c] && bitIndex < codewords.Length * 8)
                    {
                        modules[r][c] = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        bitIndex++;
                    }
                }
            }
        }

        // 选最优掩码
        int best = 0;
        long minPenalty = long.MaxValue;
        for (int m = 0; m < 8; m++)
        {
            ApplyMask(modules, isFunc, size, m);
            DrawFormatBits(modules, isFunc, size, m);
            long p = PenaltyScore(modules, size);
            if (p < minPenalty) { minPenalty = p; best = m; }
            ApplyMask(modules, isFunc, size, m);   // 撤销
        }
        ApplyMask(modules, isFunc, size, best);
        DrawFormatBits(modules, isFunc, size, best);

        return modules;
    }

    private static void SetFunc(bool[][] modules, bool[][] isFunc, int size, int row, int col, bool dark)
    {
        if (row < 0 || row >= size || col < 0 || col >= size) return;
        modules[row][col] = dark;
        isFunc[row][col] = true;
    }

    private static void DrawFinder(bool[][] modules, bool[][] isFunc, int size, int rc, int cc)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                SetFunc(modules, isFunc, size, rc + dy, cc + dx, dist != 2 && dist != 4);
            }
        }
    }

    private static void DrawAlignment(bool[][] modules, bool[][] isFunc, int size, int rc, int cc)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                SetFunc(modules, isFunc, size, rc + dy, cc + dx, dist != 1);
            }
    }

    private static void DrawFormatBits(bool[][] modules, bool[][] isFunc, int size, int mask)
    {
        // 纠错等级 L 的格式指示位为 01
        int data = (0x01 << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | (rem & 0x3FF)) ^ 0x5412;

        for (int i = 0; i <= 5; i++) SetFunc(modules, isFunc, size, i, 8, GetBit(bits, i));
        SetFunc(modules, isFunc, size, 7, 8, GetBit(bits, 6));
        SetFunc(modules, isFunc, size, 8, 8, GetBit(bits, 7));
        SetFunc(modules, isFunc, size, 8, 7, GetBit(bits, 8));
        for (int i = 9; i < 15; i++) SetFunc(modules, isFunc, size, 8, 14 - i, GetBit(bits, i));

        for (int i = 0; i < 8; i++) SetFunc(modules, isFunc, size, 8, size - 1 - i, GetBit(bits, i));
        for (int i = 8; i < 15; i++) SetFunc(modules, isFunc, size, size - 15 + i, 8, GetBit(bits, i));
        SetFunc(modules, isFunc, size, size - 8, 8, true);   // 固定深色模块
    }

    private static void DrawVersionBits(bool[][] modules, bool[][] isFunc, int size, int version)
    {
        int rem = version;
        for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        int bits = (version << 12) | (rem & 0xFFF);
        for (int i = 0; i < 18; i++)
        {
            bool bit = ((bits >> i) & 1) != 0;
            int a = size - 11 + i % 3;
            int b = i / 3;
            SetFunc(modules, isFunc, size, a, b, bit);
            SetFunc(modules, isFunc, size, b, a, bit);
        }
    }

    private static bool GetBit(int value, int index)
    {
        return ((value >> index) & 1) != 0;
    }

    private static void ApplyMask(bool[][] modules, bool[][] isFunc, int size, int mask)
    {
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (isFunc[r][c]) continue;
                modules[r][c] ^= MaskBit(mask, r, c);
            }
        }
    }

    private static bool MaskBit(int mask, int row, int col)
    {
        switch (mask)
        {
            case 0: return (row + col) % 2 == 0;
            case 1: return row % 2 == 0;
            case 2: return col % 3 == 0;
            case 3: return (row + col) % 3 == 0;
            case 4: return (row / 2 + col / 3) % 2 == 0;
            case 5: return (row * col) % 2 + (row * col) % 3 == 0;
            case 6: return ((row * col) % 2 + (row * col) % 3) % 2 == 0;
            case 7: return ((row + col) % 2 + (row * col) % 3) % 2 == 0;
            default: return false;
        }
    }

    private static long PenaltyScore(bool[][] modules, int size)
    {
        long penalty = 0;

        // 规则 1：同色连续 5 个以上
        for (int r = 0; r < size; r++)
        {
            int runColor = modules[r][0] ? 1 : 0, runLen = 1;
            for (int c = 1; c < size; c++)
            {
                int cur = modules[r][c] ? 1 : 0;
                if (cur == runColor) { runLen++; }
                else
                {
                    if (runLen >= 5) penalty += 3 + (runLen - 5);
                    runColor = cur; runLen = 1;
                }
            }
            if (runLen >= 5) penalty += 3 + (runLen - 5);
        }
        for (int c = 0; c < size; c++)
        {
            int runColor = modules[0][c] ? 1 : 0, runLen = 1;
            for (int r = 1; r < size; r++)
        {
                int cur = modules[r][c] ? 1 : 0;
                if (cur == runColor) { runLen++; }
                else
                {
                    if (runLen >= 5) penalty += 3 + (runLen - 5);
                    runColor = cur; runLen = 1;
                }
            }
            if (runLen >= 5) penalty += 3 + (runLen - 5);
        }

        // 规则 2：2x2 同色块
        for (int r = 0; r + 1 < size; r++)
            for (int c = 0; c + 1 < size; c++)
            {
                bool v = modules[r][c];
                if (v == modules[r][c + 1] && v == modules[r + 1][c] && v == modules[r + 1][c + 1])
                    penalty += 3;
            }

        // 规则 3：类似定位图案的 1:1:3:1:1（含 4 模块浅色边距）
        const int patternA = 0x5D0;   // 10111010000
        const int patternB = 0x05D;   // 00001011101
        for (int r = 0; r < size; r++)
            for (int c = 0; c + 11 <= size; c++)
            {
                int bits = 0;
                for (int k = 0; k < 11; k++) bits = (bits << 1) | (modules[r][c + k] ? 1 : 0);
                if (bits == patternA || bits == patternB) penalty += 40;
            }
        for (int c = 0; c < size; c++)
            for (int r = 0; r + 11 <= size; r++)
            {
                int bits = 0;
                for (int k = 0; k < 11; k++) bits = (bits << 1) | (modules[r + k][c] ? 1 : 0);
                if (bits == patternA || bits == patternB) penalty += 40;
            }

        // 规则 4：深色比例偏离 50%
        int dark = 0;
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                if (modules[r][c]) dark++;
        int total = size * size;
        penalty += Math.Abs(dark * 20 - total * 10) / total * 10;

        return penalty;
    }
}
