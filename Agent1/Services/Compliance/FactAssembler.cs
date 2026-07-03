using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 双通道解耦架构 — 确定性事实渲染器。
    /// 纯 C# 模板引擎，根据 ExtractedFacts 渲染输出文本。
    /// 不走 LLM，确保法规引用 100% 确定性。
    /// </summary>
    public static class FactAssembler
    {
        /// <summary>
        /// 渲染确定性事实输出。
        /// </summary>
        /// <param name="facts">从工具结果提取的结构化事实</param>
        /// <returns>格式化的事实输出文本</returns>
        public static string Build(ExtractedFacts facts)
        {
            if (facts == null || !facts.HasAnyToolResult)
                return BuildNoResult();

            var sb = new StringBuilder();
            sb.AppendLine("━━━ 查询结果（基于数据库/知识库验证）━━━");
            sb.AppendLine();

            // 1. 危险类别
            foreach (var (substance, category) in facts.HazardCategories)
            {
                var regs = facts.RegulationRefs.Count > 0
                    ? $" [法规依据: {string.Join("; ", facts.GetUniqueRegulations())}]"
                    : "";
                sb.AppendLine($"「{substance}」危险类别: {category}{regs}");
            }

            // 2. 储存兼容性
            foreach (var (pair, verdict) in facts.ComplianceVerdicts)
            {
                var subs = pair.Split('|');
                var a = subs.Length > 0 ? subs[0] : "?";
                var b = subs.Length > 1 ? subs[1] : "?";

                var regs = facts.RegulationRefs.Count > 0
                    ? $" [法规依据: {string.Join("; ", facts.GetUniqueRegulations())}]"
                    : "";
                sb.AppendLine($"「{a}」与「{b}」: {verdict}{regs}");
            }

            // 3. 安全距离
            foreach (var (facility, distance) in facts.SafetyDistances)
            {
                var regs = facts.RegulationRefs.Count > 0
                    ? $" [法规依据: {string.Join("; ", facts.GetUniqueRegulations())}]"
                    : "";
                sb.AppendLine($"{facility}的安全距离: {distance}{regs}");
            }

            // 4. 重大危险源临界量
            foreach (var (substance, threshold) in facts.Thresholds)
            {
                var regs = facts.RegulationRefs.Count > 0
                    ? $" [法规依据: {string.Join("; ", facts.GetUniqueRegulations())}]"
                    : "";
                sb.AppendLine($"「{substance}」重大危险源临界量: {threshold}{regs}");
            }

            // 5. 法规版本
            foreach (var (standard, version) in facts.RegulationVersions)
            {
                sb.AppendLine($"{standard} 现行版本: {version}");
            }

            // 6. 化学品属性
            foreach (var (substance, props) in facts.ChemicalProperties)
            {
                sb.AppendLine($"「{substance}」属性: {props}");
            }

            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            return sb.ToString();
        }

        /// <summary>工具未触发或数据不足时的标准化输出</summary>
        public static string BuildNoResult()
        {
            return
                "━━━ 查询结果 ━━━\n\n" +
                "基于现有资料无法给出确定结论，建议联系安环部门人工确认。\n" +
                "已检索到的相关法规参考: 当前知识库中未找到匹配的法规条款。\n\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
        }
    }
}
