# AI会话记忆机制

## 会话历史存储

多轮对话需要上下文，模型才能理解"你之前说的"指的是什么。这些上下文（历史消息）由谁来维护、存在哪里？答案因 API 而异。

## 三大 API 如何管理对话历史

### Chat Completions API（OpenAI / Azure / DeepSeek / Qwen）

**谁管历史：你（客户端）**

每次请求必须把完整的历史消息全部发给服务端，服务本身是**无状态**的。

### Responses API（OpenAI / Azure OpenAI）

**谁管历史：服务端**

每次请求只需发送**新消息 + 上一次响应的 ID**，服务端自动维护完整会话树。

### Messages API（Anthropic Claude）

**谁管历史：你（客户端）**

与 Chat Completions API 类似，每次必须携带完整历史。

## 国内主流模型的协议兼容情况

| 模型 / 厂商 | Chat Completions API | Responses API | Messages API |
|------------|---------------------|---------------|--------------|
| DeepSeek | ✅ 支持 | ❌ 不支持 | ❌ 不支持 |
| Qwen | ✅ 支持 | ❌ 不支持 | ❌ 不支持 |
| Claude | ❌ 不支持 | ❌ 不支持 | ✅ 原生支持 |

## SessionService 设计

在Agent1项目中，SessionService负责管理会话历史，支持：
- 会话创建与存储
- 对话历史追加
- 格式化历史输出
- 上下文摘要
- 历史清空
