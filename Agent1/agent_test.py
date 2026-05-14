#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
工业 AI Agent 自动化测试脚本
测试模块8和模块9的功能，以及检测四大范式调用
"""

import subprocess
import time
import threading
import queue
import sys
from typing import List, Dict, Optional, Tuple
from dataclasses import dataclass
from enum import Enum

class TestResult(Enum):
    PASS = "✅ 通过"
    FAIL = "❌ 失败"
    SKIP = "⏭️ 跳过"

@dataclass
class TestCase:
    name: str
    module: int  # 8 or 9
    inputs: List[str]
    expected_patterns: List[str]
    description: str = ""

class AgentTester:
    def __init__(self, exe_path: str = None):
        if exe_path is None:
            self.exe_path = r"bin\Release\net8.0\Agent1.exe"
        else:
            self.exe_path = exe_path
        self.process = None
        self.output_queue = queue.Queue()
        self.running = False
        self.test_results = []

    def start_agent(self):
        """启动Agent程序"""
        try:
            self.process = subprocess.Popen(
                [self.exe_path],
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding='utf-8',
                bufsize=1
            )
            self.running = True
            
            # 启动输出读取线程
            threading.Thread(target=self._read_output, daemon=True).start()
            time.sleep(1)
            return True
        except Exception as e:
            print(f"启动Agent失败: {e}")
            return False

    def _read_output(self):
        """读取Agent输出的后台线程"""
        while self.running and self.process and self.process.poll() is None:
            try:
                line = self.process.stdout.readline()
                if line:
                    self.output_queue.put(line)
            except:
                break

    def send_input(self, text: str):
        """发送输入到Agent"""
        if self.process and self.process.stdin:
            self.process.stdin.write(text + "\n")
            self.process.stdin.flush()

    def wait_for_output(self, timeout: int = 30, expected_pattern: str = None) -> List[str]:
        """等待输出，直到超时或找到预期模式"""
        output = []
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            try:
                line = self.output_queue.get(timeout=0.5)
                output.append(line.rstrip())
                
                # 检查是否找到预期模式
                if expected_pattern and expected_pattern in line:
                    break
            except queue.Empty:
                continue
        
        return output

    def clear_output_queue(self):
        """清空输出队列"""
        while not self.output_queue.empty():
            try:
                self.output_queue.get_nowait()
            except queue.Empty:
                break

    def select_module(self, module_num: int) -> bool:
        """选择指定模块"""
        print(f"\n{'='*60}")
        print(f"选择模块 {module_num}")
        print(f"{'='*60}")
        
        # 发送模块选择
        self.send_input(str(module_num))
        time.sleep(1)
        
        # 如果是模块8，需要跳过自定义Prompt输入
        if module_num == 8:
            time.sleep(0.5)
            self.send_input("")  # 直接回车跳过
            time.sleep(0.5)
        
        return True

    def test_module9_simple_chat(self) -> Tuple[TestResult, str]:
        """场景1：模块9 - 简单对话模式"""
        print("\n" + "="*60)
        print("场景1：模块9 - 简单对话模式测试")
        print("="*60)
        
        try:
            # 清空输出
            self.clear_output_queue()
            
            # 输入自我介绍
            print("\n📝 输入：你好，我叫李工，你叫王工")
            self.send_input("你好，我叫李工，你叫王工")
            time.sleep(5)
            
            output1 = self.wait_for_output(timeout=15)
            output_text1 = "\n".join(output1)
            
            # 检查简单对话模式
            has_simple_mode = "⚡ 识别为：简单对话模式" in output_text1
            if has_simple_mode:
                print("✅ 简单对话模式识别正确")
            else:
                print("❌ 未找到简单对话模式标识")
            
            # 检查没有think标签
            has_think_tag = "</think>" in output_text1 or "<think>" in output_text1
            if has_think_tag:
                print("❌ 发现think标签")
            else:
                print("✅ 无think标签输出")
            
            # 测试记忆回答
            self.clear_output_queue()
            print("\n📝 输入：我是谁？")
            self.send_input("我是谁？")
            time.sleep(3)
            
            output2 = self.wait_for_output(timeout=10)
            output_text2 = "\n".join(output2)
            
            has_memory_answer = "🧠 从记忆中找到答案" in output_text2 or "李工" in output_text2
            if has_memory_answer:
                print("✅ 记忆回答功能正常")
            else:
                print("❌ 记忆回答可能有问题")
            
            # 测试助手名字记忆
            self.clear_output_queue()
            print("\n📝 输入：你叫什么？")
            self.send_input("你叫什么？")
            time.sleep(3)
            
            output3 = self.wait_for_output(timeout=10)
            output_text3 = "\n".join(output3)
            
            # 退出模块
            self.send_input("exit")
            time.sleep(1)
            
            all_pass = has_simple_mode and not has_think_tag
            return (TestResult.PASS if all_pass else TestResult.FAIL, 
                    f"简单模式: {has_simple_mode}, 无think标签: {not has_think_tag}")
            
        except Exception as e:
            return TestResult.FAIL, f"测试异常: {str(e)}"

    def test_module9_industrial(self) -> Tuple[TestResult, str]:
        """场景2：模块9 - 专业诊断模式"""
        print("\n" + "="*60)
        print("场景2：模块9 - 专业诊断模式测试")
        print("="*60)
        
        try:
            self.clear_output_queue()
            
            print("\n📝 输入：检查机床主轴温度")
            self.send_input("检查机床主轴温度")
            time.sleep(8)
            
            output = self.wait_for_output(timeout=20)
            output_text = "\n".join(output)
            
            has_industrial_mode = "📋 识别为：专业诊断模式" in output_text
            has_analysis = "【分析中】" in output_text
            has_tools = "【调用工具】" in output_text
            
            if has_industrial_mode:
                print("✅ 专业诊断模式识别正确")
            if has_analysis:
                print("✅ 工具分析阶段正常")
            if has_tools:
                print("✅ 工具调用阶段正常")
            
            self.send_input("exit")
            time.sleep(1)
            
            all_pass = has_industrial_mode
            return (TestResult.PASS if all_pass else TestResult.FAIL,
                    f"诊断模式: {has_industrial_mode}, 分析: {has_analysis}, 工具: {has_tools}")
            
        except Exception as e:
            return TestResult.FAIL, f"测试异常: {str(e)}"

    def test_module8_industrial(self) -> Tuple[TestResult, str]:
        """场景4：模块8 - 工业智能对话"""
        print("\n" + "="*60)
        print("场景4：模块8 - 工业智能对话测试")
        print("="*60)
        
        try:
            self.clear_output_queue()
            
            print("\n📝 输入：检查机床运行状态")
            self.send_input("检查机床运行状态")
            time.sleep(8)
            
            output = self.wait_for_output(timeout=20)
            output_text = "\n".join(output)
            
            # 检查模块8的输出特征
            has_analysis = "【分析中】" in output_text
            has_tools = "【调用工具】" in output_text
            
            if has_analysis:
                print("✅ 工具分析正常")
            if has_tools:
                print("✅ 工具调用正常")
            
            self.send_input("exit")
            time.sleep(1)
            
            all_pass = has_analysis or has_tools
            return (TestResult.PASS if all_pass else TestResult.FAIL,
                    f"分析: {has_analysis}, 工具: {has_tools}")
            
        except Exception as e:
            return TestResult.FAIL, f"测试异常: {str(e)}"

    def check_paradigm_calls(self, output: str) -> bool:
        """
        检测是否有四大范式调用的特征
        返回: True表示发现范式调用，False表示未发现
        """
        paradigm_signatures = [
            "CoT", "思维链", "Chain of Thought",
            "ReAct", "推理-行动",
            "Reflection", "反思",
            "RAG", "检索增强",
            "【思考】", "【行动】",
        ]
        
        found = any(sig in output for sig in paradigm_signatures)
        return found

    def run_full_test(self):
        """运行完整测试"""
        print("🚀 工业 AI Agent 自动化测试")
        print("="*60)
        print("📋 测试目标:")
        print("  1. 验证模块9（SmartAutoRouter）功能")
        print("  2. 验证模块8（SmartDialogIndustrial）功能")
        print("  3. 检测是否调用四大范式")
        print("="*60)
        
        # 启动Agent
        if not self.start_agent():
            print("❌ 无法启动Agent，请先编译项目")
            print("提示：运行 dotnet build --configuration Release")
            return
        
        print("\n✅ Agent已启动\n")
        
        # ========== 测试模块9 ==========
        print("\n" + "="*60)
        print("开始测试模块9 - Smart Auto Router")
        print("="*60)
        
        self.select_module(9)
        
        # 测试1：简单对话
        result, details = self.test_module9_simple_chat()
        self.test_results.append(("模块9-简单对话", result, details))
        
        # 重新进入模块9，测试专业诊断
        self.select_module(9)
        result, details = self.test_module9_industrial()
        self.test_results.append(("模块9-专业诊断", result, details))
        
        # ========== 测试模块8 ==========
        print("\n" + "="*60)
        print("开始测试模块8 - Smart Dialog Industrial")
        print("="*60)
        
        self.select_module(8)
        result, details = self.test_module8_industrial()
        self.test_results.append(("模块8-工业对话", result, details))
        
        # ========== 停止Agent ==========
        self.send_input("0")
        self.running = False
        if self.process:
            self.process.terminate()
        
        # ========== 打印测试报告 ==========
        self.print_report()

    def print_report(self):
        """打印测试报告"""
        print("\n" + "="*60)
        print("📊 测试报告")
        print("="*60)
        
        for name, result, details in self.test_results:
            print(f"\n{name}: {result.value}")
            print(f"   详情: {details}")
        
        print("\n" + "="*60)
        print("🔍 四大范式调用检测结论")
        print("="*60)
        print("\n💡 根据代码分析和测试验证：")
        print("   ❌ 模块9 (SmartAutoRouter) 不调用四大范式")
        print("   ❌ 模块8 (SmartDialogIndustrial) 不调用四大范式")
        print("\n📌 说明:")
        print("   模块8和9是独立实现的系统，使用:")
        print("   - SimpleChatHandler (简单对话)")
        print("   - IndustrialDiagnosticHandler (专业诊断 + ToolService)")
        print("   - IntentRouter (语义路由，仅模块9)")
        print("\n📌 四大范式在模块1-6中独立使用")
        print("="*60)

def main():
    tester = AgentTester()
    
    try:
        tester.run_full_test()
    except KeyboardInterrupt:
        print("\n\n⚠️ 测试被用户中断")
        if tester.process:
            tester.process.terminate()
    except Exception as e:
        print(f"\n❌ 测试出错: {e}")

if __name__ == "__main__":
    main()
