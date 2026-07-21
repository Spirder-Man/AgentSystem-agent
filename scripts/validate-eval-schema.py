#!/usr/bin/env python3
"""
JSON Schema 验证 — Pre-deploy / Post-deploy 输出标准化检查

用途：验证 pre-deploy-check.sh --json 和 post-deploy-eval.sh 
      产生的 JSON 是否符合统一 schema，确保 Grafana 可统一消费。

用法：python3 scripts/validate-eval-schema.py <json_file>
      返回 0 = 通过，非0 = 格式不符

设计原则（对齐测试分层）：
  - Pre-deploy 输出：基础设施+API连通性+安全中间件+FC就绪
  - Post-deploy 输出：评测指标+趋势对比+幻觉监控
  - 两者共享 schema 顶层结构（run_id/environment/timestamp/verdict）
"""

import json
import sys
import os

# ── 统一顶层 Schema ──
REQUIRED_TOP_KEYS = {"run_id", "environment", "timestamp", "verdict"}
VALID_VERDICTS = {"pass", "fail"}
VALID_ENVIRONMENTS = {"staging", "production", "development"}

# ── Pre-deploy 必需字段 ──
PRE_DEPLOY_KEYS = {"infrastructure", "api_connectivity", "security", "fc_readiness"}
PRE_DEPLOY_INFRA_KEYS = {"postgresql", "llama_cpp", "embedding"}

# ── Post-deploy 预期字段 ──
POST_DEPLOY_KEYS = {"model_version", "conclusionAccuracy", "total"}
POST_DEPLOY_COMPARISON_KEYS = {"conclusion_accuracy", "hallucination_rate"}


def validate_top_level(data: dict) -> list[str]:
    """验证顶层字段"""
    errors = []
    missing = REQUIRED_TOP_KEYS - set(data.keys())
    if missing:
        errors.append(f"缺少顶层字段: {missing}")
    if data.get("verdict") not in VALID_VERDICTS:
        errors.append(f"无效 verdict: {data.get('verdict')} (允许: {VALID_VERDICTS})")
    if data.get("environment") not in VALID_ENVIRONMENTS:
        errors.append(f"无效 environment: {data.get('environment')} (允许: {VALID_ENVIRONMENTS})")
    return errors


def validate_pre_deploy(data: dict) -> list[str]:
    """验证 Pre-deploy 输出"""
    errors = []
    if "infrastructure" in data:
        infra = data["infrastructure"]
        for key in PRE_DEPLOY_INFRA_KEYS:
            if key not in infra:
                errors.append(f"infrastructure 缺少: {key}")
            elif not isinstance(infra[key], bool):
                errors.append(f"infrastructure.{key} 应为 boolean，实际: {type(infra[key])}")
    if "fc_readiness" in data:
        fc = data["fc_readiness"]
        if "ok" not in fc:
            errors.append("fc_readiness 缺少 ok 字段")
    return errors


def validate_post_deploy(data: dict) -> list[str]:
    """验证 Post-deploy 输出"""
    errors = []
    # 模型版本
    if "model_version" not in data:
        errors.append("Post-deploy 输出缺少 model_version")
    # 评测指标
    for key in ["conclusionAccuracy", "total"]:
        if key in data:
            val = data[key]
            if not isinstance(val, (int, float)):
                errors.append(f"{key} 应为数字，实际: {type(val)}")
    # 对比数据
    if "comparison" in data:
        comp = data["comparison"]
        for key in POST_DEPLOY_COMPARISON_KEYS:
            if key in comp:
                for sub in ["previous", "current"]:
                    if sub not in comp[key]:
                        errors.append(f"comparison.{key} 缺少 {sub}")
    # 告警字段
    if "alerts" in data:
        alerts = data["alerts"]
        if not isinstance(alerts, str):
            errors.append(f"alerts 应为字符串，实际: {type(alerts)}")
    return errors


def main():
    if len(sys.argv) < 2:
        print("用法: python3 validate-eval-schema.py <json_file>")
        sys.exit(2)

    filepath = sys.argv[1]
    if not os.path.exists(filepath):
        print(f"[FAIL] 文件不存在: {filepath}")
        sys.exit(1)

    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            data = json.load(f)
    except json.JSONDecodeError as e:
        print(f"[FAIL] JSON 解析失败: {e}")
        sys.exit(1)

    all_errors = []
    all_errors.extend(validate_top_level(data))

    # 自动判断 Pre-deploy 还是 Post-deploy
    if "infrastructure" in data:
        all_errors.extend(validate_pre_deploy(data))
    if "model_version" in data or "conclusionAccuracy" in data:
        all_errors.extend(validate_post_deploy(data))

    if all_errors:
        print(f"[FAIL] Schema 验证失败 ({len(all_errors)} 个错误):")
        for err in all_errors:
            print(f"  - {err}")
        sys.exit(1)
    else:
        report_type = "Pre-deploy" if "infrastructure" in data else "Post-deploy"
        print(f"[PASS] {report_type} JSON schema 验证通过")
        sys.exit(0)


if __name__ == "__main__":
    main()
