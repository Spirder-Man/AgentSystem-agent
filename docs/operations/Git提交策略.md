# Git 提交策略

> **不只是 `git commit -m "fix"`。** 本项目已配置 commitlint（Conventional Commits）+ husky 卡口，但规范只检查格式，策略回答的是：什么情况用什么类型、一个 commit 放多少内容、什么时候拆什么时候合、多分支怎么同步、什么东西绝对不能提交。

---

## 一、先搞清楚：你的改动属于哪种性质？

在写 commit message 之前，先问自己：**这次改动的"动机"是什么？**

| 动机 | 类型 | 本项目真实示例 |
|------|------|---------------|
| 修了一个 Bug | `fix` | `fix: 哈希链断裂因 IsDirty 误判导致审计记录丢失` |
| 新增了一个功能 | `feat` | `feat: 知识库双层表架构（Phase 1-6）` |
| 只改文档/注释 | `docs` | `docs: Bug-032 v2 回马枪修复结论 + 编译缓存陷阱教训` |
| 重构代码，行为不变 | `refactor` | `refactor: 提取 FactAssembler 为独立服务` |
| 改了 CI/构建/依赖 | `ci` / `chore` | `ci: 前端 Vitest 接入 CI 邮件告警` |
| 只加测试 | `test` | `test: ComplianceFactExtractor 边界场景覆盖` |

**关键判断：行为变了吗？**
- 行为变了 → `fix` 或 `feat`
- 行为没变 → `refactor`、`docs`、`test`、`chore`

---

## 二、commit message 三段式模板

```
<type>(<scope>): <subject>
# ← 空一行
<body>
# ← 空一行
<footer>
```

### subject（必填）

一句话说清楚做了什么。中文 15-50 字，不以句号结尾。

```
✅ fix(Bug-032-v2): FC=Required 违约检测提升为 HasAnyToolResult 之前独立最高优先级闸门
❌ fix bug
❌ Fix: 修复了问题
❌ fix:修了
```

### body（强烈建议）

**解释"为什么"这样做**，不是"做了什么"——diff 已经说了做了什么。三个月后的自己（或同事）会感谢你。

```
✅ v1 修复中 toolCalls.Count==0 检查放在 else 分支内，当 HasAnyToolResult==true
   （存在缓存工具结果）时代码走第一个分支直接 return，FC 违约检测被完全绕过。
   v2 将 toolCalls.Count==0 提升为 ApplyDecoupledPipeline 方法内第一道独立闸门。

❌ 修了一个问题                    ← 废话
❌ 改了 AgentDialog.cs 第 810 行   ← diff 就能看到
```

### footer（按需）

- 关联 Issue：`Closes #42`
- Breaking Change：`BREAKING CHANGE: xxx`
- 关联 Bug 编号：`Bug-032`

---

## 三、scope 怎么用？

| scope | 适用场景 |
|-------|---------|
| `(Bug-XXX)` | 对应 Bug知识库 中的编号 |
| `(test)` | 测试基础设施改动（不改被测代码） |
| `(ci)` | CI/CD 流水线改动 |
| 不加 scope | 一般功能开发/修复，模块名 diff 就能看出来 |

**原则：scope 提供 diff 里看不出的上下文时才加。**

---

## 四、一个 commit 放多少？拆还是合？

### 必须拆（一个 commit 只做一件事）

| 场景 | 原因 |
|------|------|
| 改了 .cs + 改了 .vue | 后端和前端分开，方便独立 revert |
| 修 Bug + 顺手改了文档 | Bug 修复和文档是两件事，分开 trace |
| 重构 + 加新功能 | 重构不应夹带行为变更 |

### 可以合

| 场景 | 原因 |
|------|------|
| 改了一个函数 + 它的单元测试 | 测试和实现是原子绑定 |
| 加了新 Controller + 对应的 DTO/Model | 同一个功能的不同层 |

**判断标准：如果将来要 revert 这个 commit，会不会后悔？** 会 → 拆开。

---

## 五、分支策略

```
master / main   ← 生产就绪（Gitee 默认 master，GitHub 默认 main）
  └── develop   ← 集成测试
        ├── feature/partner-dev        ← 合作开发
        └── linux原生编译模型llama.cpp ← 特定编译环境适配
```

| 场景 | 提交到 |
|------|--------|
| 紧急 Bug 修复 | 从 `master` 切 `hotfix/xxx`，修完 merge 回 `master` + `develop` |
| 日常功能开发 | 从 `develop` 切 `feature/xxx`，修完 merge 回 `develop` |
| 文档/配置/清理 | 直接在对应分支改，然后同步到其他活跃分支 |
| 不影响其他环境的编译适配 | 在 `linux原生编译模型llama.cpp` 上改 |

**⚠️ 双远程陷阱：** 本项目同时推送 Gitee（`origin`）和 GitHub（`github`）。`master` 和 `main` 是两个独立分支，任何修改必须同步到另一个。当前用 `merge` 同步，不用 `rebase`。

每次推送后检查：
```bash
git push origin <branch>    # Gitee
git push github <branch>    # GitHub
```

---

## 六、绝对不能提交的东西

> **这是用真实事故换来的教训。**

### 2026-07-20 事故回顾：GitHub 语言统计 JS 占 52.2%

**现象：** 项目明明是 .NET C# 为主，GitHub 却显示 JavaScript 52.2%、C# 仅 35%。

**排查过程：**

```bash
# 1. 怀疑有大型 JS 文件被追踪
git ls-files -- '*.js'                 # 只有 9 个，都不大

# 2. 怀疑 node_modules 被提交
git ls-files | grep node_modules       # 空的，排除

# 3. 用上传脚本到远程排查大文件
find . -name "*.js" -type f -exec ls -lh {} \; | sort -k5 -hr | head -20
```

**根因发现：**

| 元凶 | 大小 | 路径 |
|------|------|------|
| element-plus.js | 2.6 MB | `agent1-web/.vite/deps/` |
| 其他 Vite 预构建产物 | ~8 MB | `agent1-web/.vite/deps/` |
| 评测报告 JSON | ~1.7 MB | `logs/linux/eval_report*.json` |
| 知识库文件 | 若干 MB | `knowledgebase/` |
| 构建统计 | 若干 KB | `.CodeCounter/` |

**共计 1331 个不该追踪的文件。**

**为什么 `.gitignore` 没拦住？**

`.gitignore` 从第 79 行起存在编码损坏（全是 null 字节），Git 将其识别为二进制文件，后续添加的规则全部失效。同时，这些文件在 `.gitignore` 配置之前就已经被 `git add` 提交过——一旦文件被 Git 追踪，`.gitignore` 就管不了它了。

### 修复步骤（完整记录）

```bash
# Step 1：重写 .gitignore，修复编码损坏 + 补充遗漏规则
# 新增规则：
#   agent1-web/.vite/
#   agent1-web/dist/
#   logs/linux/eval_report*.json
#   .CodeCounter/
#   knowledgebase/

# Step 2：从 Git 索引移除已追踪的构建产物（文件本身保留在本地）
git rm --cached -r agent1-web/.vite/
git rm --cached -r knowledgebase/
git rm --cached logs/linux/eval_report*.json
git rm --cached -r .CodeCounter/

# Step 3：提交
git commit -m "chore: 清理 .gitignore 编码损坏并移除构建产物追踪 — 修复 GitHub 语言统计 JS 52% 问题"

# Step 4：推送到双远程所有 5 个分支
# master、main、develop、feature/partner-dev、linux原生编译模型llama.cpp
```

### 事后检查清单

**每次 `git add` 前自问：**

- [ ] 这是编译/构建产物吗？（`.vite/`、`dist/`、`bin/`、`obj/`）→ **不要提交**
- [ ] 这是运行时日志/报告吗？（`logs/`、`*.log`、`eval_report*.json`）→ **不要提交**
- [ ] 这是本地配置文件吗？（`.env`、含密码的 `appsettings.json` 修改）→ **不要提交**
- [ ] 这是 IDE 临时文件吗？（`.vs/`、`.idea/`、`*.suo`）→ **不要提交**
- [ ] `.gitignore` 文件本身有没有损坏？（`file .gitignore` 应显示 UTF-8 text，不是 data）→ **每次修改后检查**

### 底线规则

```
构建产物、日志、运行时数据、本地配置 → 一律不进仓库
.gitignore 防"未追踪"、git rm --cached 清"已追踪"
```

---

## 七、commitlint 卡口

项目通过 husky 在 `agent1-web/` 下配置了 `@commitlint/config-conventional`，以下写法会被拒绝：

```
❌ fix bug                               ← 没有冒号
❌ Fix: 修复了问题                       ← type 首字母大写
❌ fix:修复了问题                        ← 冒号后没空格
❌ fix: 修                               ← subject 太短
❌ docs: readme v4.4                     ← README 全大写触发 subject-case 规则，用小写
```

### 本地预检

```bash
# 检查最近的 commit
cd agent1-web && npx commitlint --from HEAD~1

# 如果被拦但确实需要绕过（如紧急 hotfix）
git commit --no-verify   # 跳过 husky 钩子，谨慎使用
```

---

## 八、Git 忽略文件三种方式速查

| 方法 | 适用场景 | 本项目对应 |
|------|---------|-----------|
| `.gitignore` | 团队共享规则，忽略未追踪文件 | `agent1-web/.vite/`、`logs/linux/` |
| `.git/info/exclude` | 个人本地文件，不影响团队 | 你本地调试创建的临时脚本 |
| `--skip-worktree` | 已追踪文件，只想保留本地修改 | `appsettings.json` 本地数据库连接串 |

### 文件是否已被追踪？先判断

```bash
git ls-files --error-unmatch path/to/file
# 有输出 → 已追踪（.gitignore 管不了，用 git rm --cached 或 --skip-worktree）
# 报错   → 未追踪（用 .gitignore 即可）
```

---

## 九、GitHub 语言统计刷新

**统计不是实时的。** GitHub Linguist 有缓存，push 后通常需要 **几小时到 24 小时** 才会刷新。

如果确认修复已推送但统计未变，耐心等待即可。可以在 commit 中加 `.gitattributes` 强制指定语言：

```
# .gitattributes（如需）
*.js linguist-vendored     # 标记为第三方/构建产物，不计入统计
```

---

## 十、速查表

| 我想… | 做法 |
|-------|------|
| 修一个 Bug | `git commit -m "fix: xxx"` |
| 加一个新功能 | `git commit -m "feat: xxx"` |
| 只改了文档 | `git commit -m "docs: xxx"` |
| 重构，行为不变 | `git commit -m "refactor: xxx"` |
| 改了 CI 配置 | `git commit -m "ci: xxx"` |
| 清理/杂务 | `git commit -m "chore: xxx"` |
| 把大改动拆成多个 commit | `git add -p` 逐块暂存 |
| 预检 commit 会不会被拦 | `cd agent1-web && npx commitlint --from HEAD~1` |
| 从仓库移除已追踪的构建产物 | `git rm --cached -r 路径` → 更新 `.gitignore` → 提交 |
| 保护本地配置不被提交 | `git update-index --skip-worktree appsettings.json` |
| 检查 `.gitignore` 是否损坏 | `file .gitignore`（应显示 UTF-8 text） |
| 检查还有哪些大文件被追踪 | `git ls-files | xargs ls -lh 2>/dev/null | sort -k5 -hr | head -20` |
| 同步所有分支到双远程 | 逐个 checkout → merge → `git push origin` → `git push github` |

---

> **三条铁律：**
>
> 1. **一个 commit 只做一件事**——将来 revert 不后悔
> 2. **subject 说做了什么，body 说为什么**——三个月后的自己还能看懂
> 3. **构建产物、日志、本地配置一律不进仓库**——2026-07-20 的 JS 52% 事故就是血的教训
