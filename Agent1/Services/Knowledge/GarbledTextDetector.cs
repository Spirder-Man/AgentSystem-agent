using System;
using System.Collections.Generic;

namespace Agent1.Services
{
    /// <summary>
    /// [#5 FIX] 乱码块检测器 — 知识库入库管道的确定性守门员。
    ///
    /// 背景：部分国标 PDF 使用自定义字体编码，提取后产出
    /// "书书书!!!!!"#$%&amp;'(..." 形态的乱码块，污染 BM25/向量检索
    /// （日志中此类块以高分挤占正常法规条文的召回位）。
    ///
    /// 三条纯确定性规则（无概率判断），命中任一即拒收：
    ///   ① 同一非数字字符连续重复 ≥4 次（"书书书书"、"!!!!"；
    ///      数字豁免，避免 "10000m³" 这类合法容积值误杀）
    ///   ② 非中文/字母/数字/常用标点字符占比 &gt; 40%
    ///   ③ 有效中文字符占比 &lt; 20%（本知识库全部为中文法规文本）
    /// </summary>
    public static class GarbledTextDetector
    {
        // 中文法规文本常用标点（规则②的白名单一部分）
        private static readonly HashSet<char> CommonPunctuation = new()
        {
            '，', '。', '；', '：', '、', '！', '？', '（', '）', '《', '》',
            '【', '】', '“', '”', '‘', '’', '—', '…', '·', '±',
            ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '<', '>',
            '"', '\'', '-', '_', '/', '\\', '%', '‰', '℃', '°', '§',
            '～', '~', '＝', '=', '＋', '+', '×', '÷', '≤', '≥', '≠', '&',
        };

        /// <summary>
        /// 判定文本块是否为乱码。
        /// </summary>
        /// <param name="text">待检测的分块内容</param>
        /// <param name="reason">命中的规则描述（未命中时为空串）</param>
        public static bool IsGarbled(string text, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "空白块";
                return true;
            }

            // ── 规则①：同一非数字字符连续重复 ≥4 ──
            int run = 1;
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i] == text[i - 1] && !char.IsDigit(text[i]) && !char.IsWhiteSpace(text[i]))
                {
                    run++;
                    if (run >= 4)
                    {
                        reason = $"同字符连续重复≥4次('{text[i]}')";
                        return true;
                    }
                }
                else
                {
                    run = 1;
                }
            }

            // ── 字符构成统计（空白不计入分母）──
            int total = 0, chinese = 0, valid = 0;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c)) continue;
                total++;

                bool isChinese = c >= 0x4E00 && c <= 0x9FFF;
                if (isChinese) chinese++;
                if (isChinese || char.IsLetterOrDigit(c) || CommonPunctuation.Contains(c))
                    valid++;
            }

            if (total == 0)
            {
                reason = "空白块";
                return true;
            }

            // ── 规则②：异常字符占比 > 40% ──
            double invalidRatio = 1.0 - (double)valid / total;
            if (invalidRatio > 0.4)
            {
                reason = $"异常字符占比{invalidRatio:P0}>40%";
                return true;
            }

            // ── 规则③：中文占比 < 20% ──
            double chineseRatio = (double)chinese / total;
            if (chineseRatio < 0.2)
            {
                reason = $"中文占比{chineseRatio:P0}<20%";
                return true;
            }

            return false;
        }
    }
}
