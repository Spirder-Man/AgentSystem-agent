
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""手动测试脚本，只运行简单的测试"""

import subprocess
import time
import threading
import queue

exe_path = r"bin\Release\net8.0\Agent1.exe"

def read_output(proc, q):
    while True:
        line = proc.stdout.readline()
        if not line:
            break
        q.put(line.rstrip())

try:
    proc = subprocess.Popen(
        [exe_path],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding='utf-8',
        bufsize=1
    )
    
    q = queue.Queue()
    t = threading.Thread(target=read_output, args=(proc, q), daemon=True)
    t.start()
    
    time.sleep(1.5)
    
    # 收集启动输出
    print("=== 启动输出 ===")
    while not q.empty():
        print(q.get())
    
    # 发送7，进入统一对话模块
    print("\n=== 发送7 ===")
    proc.stdin.write("7\n")
    proc.stdin.flush()
    time.sleep(1.5)
    
    while not q.empty():
        print(q.get())
    
    # 发送测试输入
    print("\n=== 发送'你好' ===")
    proc.stdin.write("你好\n")
    proc.stdin.flush()
    time.sleep(3)
    
    while not q.empty():
        print(q.get())
    
    # 发送exit
    print("\n=== 发送exit ===")
    proc.stdin.write("exit\n")
    proc.stdin.flush()
    time.sleep(1)
    
    while not q.empty():
        print(q.get())
    
    # 发送0退出
    print("\n=== 发送0 ===")
    proc.stdin.write("0\n")
    proc.stdin.flush()
    time.sleep(0.5)
    
    proc.terminate()
except Exception as e:
    print(f"错误: {e}")

