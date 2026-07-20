import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema } from '@modelcontextprotocol/sdk/types.js';
import nodemailer from 'nodemailer';
import { readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createTask, getTaskStatus, listPendingTasks, cleanupExpired } from './task-store.js';
import { startCallbackServer } from './callback-server.js';

// ── 加载 .env 文件（如果存在） ──
const __dirname = dirname(fileURLToPath(import.meta.url));
const envPath = join(__dirname, '.env');
if (existsSync(envPath)) {
  const envContent = readFileSync(envPath, 'utf-8');
  for (const line of envContent.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) continue;
    const eqIdx = trimmed.indexOf('=');
    if (eqIdx === -1) continue;
    const key = trimmed.slice(0, eqIdx).trim();
    const val = trimmed.slice(eqIdx + 1).trim();
    if (!process.env[key]) {
      process.env[key] = val;
    }
  }
  console.error('[task-email] 已加载 .env 配置文件');
}

// ── 配置（从环境变量读取） ──
const SMTP_HOST = process.env.SMTP_HOST || 'smtp.qq.com';
const SMTP_PORT = parseInt(process.env.SMTP_PORT || '587', 10);
const SMTP_USER = (process.env.SMTP_USER || '').trim();
const SMTP_PASS = (process.env.SMTP_PASS || '').trim();
const SMTP_TO = (process.env.SMTP_TO || process.env.SMTP_USER || '').trim();
const CALLBACK_PORT = parseInt(process.env.CALLBACK_PORT || '9876', 10);
const CALLBACK_HOST = process.env.CALLBACK_HOST || `http://localhost:${CALLBACK_PORT}`;

// ── 邮件发送器 ──
let transporter = null;

function getTransporter() {
  if (transporter) return transporter;
  if (!SMTP_USER || !SMTP_PASS) {
    console.error('[task-email] ⚠️ SMTP 未配置 (SMTP_USER / SMTP_PASS 环境变量缺失)，邮件发送功能不可用');
    return null;
  }
  transporter = nodemailer.createTransport({
    host: SMTP_HOST,
    port: SMTP_PORT,
    secure: SMTP_PORT === 465,
    auth: { user: SMTP_USER, pass: SMTP_PASS },
  });
  return transporter;
}

/**
 * 生成 HTML 邮件正文
 */
function buildEmailHtml({ title, summary, nextStep, actions, confirmUrls }) {
  const actionButtons = actions.map((action, i) => {
    const colorMap = ['#667eea', '#48bb78', '#ed8936', '#e53e3e'];
    const color = colorMap[i % colorMap.length];
    return `
      <a href="${confirmUrls[i]}" 
         style="display:inline-block;background:${color};color:white;padding:12px 24px;
                border-radius:8px;text-decoration:none;font-size:15px;font-weight:600;
                margin:6px 8px;">
        ${action.label}
      </a>`;
  }).join('');

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head><meta charset="UTF-8"></head>
<body style="font-family:'Microsoft YaHei',-apple-system,sans-serif;max-width:600px;margin:0 auto;">
  <div style="background:linear-gradient(135deg,#667eea,#764ba2);color:white;padding:20px 24px;border-radius:12px 12px 0 0;">
    <h2 style="margin:0;">🤖 Agent1 — 任务完成通知</h2>
  </div>
  <div style="border:1px solid #e2e8f0;border-top:none;padding:24px;border-radius:0 0 12px 12px;">
    <h3 style="color:#2d3748;margin-top:0;">${escapeHtml(title)}</h3>
    <div style="background:#f7fafc;border-radius:8px;padding:16px;margin:12px 0;">
      <pre style="white-space:pre-wrap;font-family:inherit;margin:0;color:#4a5568;line-height:1.6;">${escapeHtml(summary)}</pre>
    </div>
    ${nextStep ? `<p style="color:#718096;"><strong>📌 下一步：</strong>${escapeHtml(nextStep)}</p>` : ''}
    <div style="text-align:center;margin:24px 0 8px;">
      <p style="color:#718096;margin-bottom:12px;">👇 请点击下方按钮确认并继续：</p>
      ${actionButtons}
    </div>
    <hr style="border:none;border-top:1px solid #e2e8f0;margin:20px 0;">
    <p style="color:#a0aec0;font-size:12px;">
      发送时间: ${new Date().toLocaleString('zh-CN')}<br>
      Agent1 Task Email System · 此邮件由 AI 助手自动发送
    </p>
  </div>
</body>
</html>`;
}

function escapeHtml(str) {
  return String(str).replace(/[&<>"']/g, m =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
}

/**
 * 发送任务通知邮件
 */
async function sendTaskEmail(params) {
  const transport = getTransporter();
  if (!transport) {
    return { success: false, error: 'SMTP 未配置。请设置 SMTP_USER 和 SMTP_PASS 环境变量。' };
  }
  if (!SMTP_TO) {
    return { success: false, error: '收件人未配置。请设置 SMTP_TO 环境变量。' };
  }

  const { title, summary, nextStep, actions } = params;

  // 创建任务记录
  const { taskId, token } = createTask({ title, summary, nextStep, actions });

  // 生成确认链接
  const confirmUrls = actions.map(a =>
    `${CALLBACK_HOST}/confirm/${taskId}/${a.actionId}?token=${token}`
  );

  // 构建并发送邮件
  const html = buildEmailHtml({ title, summary, nextStep, actions, confirmUrls });

  try {
    await transport.sendMail({
      from: `"Agent1 任务助手" <${SMTP_USER}>`,
      to: SMTP_TO,
      subject: `[Agent1] ✅ 任务完成: ${title}`,
      html,
    });

    return {
      success: true,
      taskId,
      message: `邮件已发送至 ${SMTP_TO}`,
      actions: actions.map((a, i) => ({
        label: a.label,
        actionId: a.actionId,
        confirmUrl: confirmUrls[i],
      })),
    };
  } catch (err) {
    return { success: false, error: `邮件发送失败: ${err.message}` };
  }
}

// ── MCP Server ──
const server = new Server(
  { name: 'task-email-mcp', version: '1.0.0' },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: [
    {
      name: 'send_task_notification',
      description:
        '任务完成后发送邮件通知给用户。邮件中包含确认按钮，用户点击后可继续下一步。\n' +
        '参数：title(任务标题)、summary(任务摘要，支持多行)、nextStep(下一步描述，可选)、' +
        'actions(操作按钮数组，每项含 label 和 actionId)。\n' +
        '典型用法：任务完成后调用此工具，然后使用 wait_for_confirmation 等待用户确认。',
      inputSchema: {
        type: 'object',
        properties: {
          title: { type: 'string', description: '任务标题（会显示在邮件主题和标题中）' },
          summary: { type: 'string', description: '任务完成摘要，描述完成了什么、改了什么' },
          nextStep: { type: 'string', description: '下一步任务描述（可选），告诉用户接下来要做什么' },
          actions: {
            type: 'array',
            description: '操作按钮列表。至少提供一个。',
            items: {
              type: 'object',
              properties: {
                label: { type: 'string', description: '按钮显示文字，如"确认并继续"、"跳过"、"查看详情"' },
                actionId: { type: 'string', description: '动作标识符，如 confirm、skip、reject' },
              },
              required: ['label', 'actionId'],
            },
          },
        },
        required: ['title', 'summary', 'actions'],
      },
    },
    {
      name: 'check_task_status',
      description: '查询某个任务的确认状态。用户点击邮件中按钮后状态会变为 confirmed。',
      inputSchema: {
        type: 'object',
        properties: {
          taskId: { type: 'string', description: 'send_task_notification 返回的 taskId' },
        },
        required: ['taskId'],
      },
    },
    {
      name: 'wait_for_confirmation',
      description:
        '等待用户通过邮件确认。会轮询检查直到用户点击按钮或超时。\n' +
        '默认每 5 秒检查一次，最长等待 5 分钟。',
      inputSchema: {
        type: 'object',
        properties: {
          taskId: { type: 'string', description: 'send_task_notification 返回的 taskId' },
          timeoutSeconds: { type: 'number', description: '超时秒数，默认 300（5分钟），最大 1800（30分钟）' },
          pollIntervalSeconds: { type: 'number', description: '轮询间隔秒数，默认 5，最小 3' },
        },
        required: ['taskId'],
      },
    },
    {
      name: 'list_pending_tasks',
      description: '列出所有等待用户确认的未完成任务。',
      inputSchema: { type: 'object', properties: {} },
    },
    {
      name: 'cleanup_expired_tasks',
      description: '清理超过指定小时数的过期任务。',
      inputSchema: {
        type: 'object',
        properties: {
          maxAgeHours: { type: 'number', description: '最大保留小时数，默认 24' },
        },
      },
    },
  ],
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  switch (name) {
    // ── send_task_notification ──
    case 'send_task_notification': {
      const { title, summary, nextStep, actions } = args;
      if (!title || !summary || !actions || actions.length === 0) {
        return {
          content: [{ type: 'text', text: '❌ 缺少必要参数：title、summary、actions（至少一个按钮）' }],
        };
      }

      const result = await sendTaskEmail({ title, summary, nextStep: nextStep || '', actions });

      if (!result.success) {
        return { content: [{ type: 'text', text: `❌ ${result.error}` }] };
      }

      const lines = [
        '📧 **邮件已发送！**',
        '',
        `| 项目 | 内容 |`,
        `|------|------|`,
        `| 收件人 | ${SMTP_TO} |`,
        `| Task ID | \`${result.taskId}\` |`,
        `| 任务标题 | ${title} |`,
        '',
        '**📎 确认链接：**',
        ...result.actions.map(a => `- **${a.label}**: [${a.confirmUrl}](${a.confirmUrl})`),
        '',
        '💡 用户点击邮件中的按钮后，使用 `wait_for_confirmation` 或 `check_task_status` 检查状态。',
        '',
        `\`\`\`json
${JSON.stringify({ taskId: result.taskId, actions: result.actions.map(a => ({ label: a.label, actionId: a.actionId })) }, null, 2)}
\`\`\``,
      ];

      return { content: [{ type: 'text', text: lines.join('\n') }] };
    }

    // ── check_task_status ──
    case 'check_task_status': {
      const task = getTaskStatus(args.taskId);
      if (!task) {
        return { content: [{ type: 'text', text: `⚠️ 任务 \`${args.taskId}\` 不存在或已过期` }] };
      }

      const emoji = task.overallStatus === 'confirmed' ? '✅' : '⏳';
      const lines = [
        `${emoji} **任务状态: ${task.overallStatus}**`,
        '',
        `| 字段 | 值 |`,
        `|------|-----|`,
        `| Task ID | \`${task.taskId}\` |`,
        `| 标题 | ${task.title} |`,
        `| 状态 | ${task.overallStatus} |`,
        `| 确认动作 | ${task.confirmedAction || '—'} |`,
        `| 创建时间 | ${task.createdAt} |`,
        `| 确认时间 | ${task.confirmedAt || '—'} |`,
        '',
        '**操作按钮：**',
        ...task.actions.map(a => `- ${a.label} (\`${a.actionId}\`): ${a.status}`),
      ];

      return { content: [{ type: 'text', text: lines.join('\n') }] };
    }

    // ── wait_for_confirmation ──
    case 'wait_for_confirmation': {
      const taskId = args.taskId;
      const timeoutMs = Math.min(
        Math.max(parseInt(args.timeoutSeconds) || 300, 10),
        1800
      ) * 1000;
      const pollMs = Math.max(parseInt(args.pollIntervalSeconds) || 5, 3) * 1000;

      const startTime = Date.now();
      let lastStatus = 'pending';

      while (Date.now() - startTime < timeoutMs) {
        const task = getTaskStatus(taskId);
        if (!task) {
          return { content: [{ type: 'text', text: `⚠️ 任务 \`${taskId}\` 不存在或已过期` }] };
        }

        if (task.overallStatus === 'confirmed') {
          return {
            content: [{
              type: 'text',
              text: [
                '✅ **用户已确认！继续执行下一步。**',
                '',
                `- 确认动作: **${task.confirmedAction}**`,
                `- 确认时间: ${task.confirmedAt}`,
              ].join('\n'),
            }],
          };
        }

        if (task.overallStatus !== lastStatus) {
          lastStatus = task.overallStatus;
          console.error(`[task-email] 任务 ${taskId} 状态变更: ${lastStatus}`);
        }

        // 等待 pollMs
        await new Promise(resolve => setTimeout(resolve, pollMs));
      }

      return {
        content: [{
          type: 'text',
          text: [
            '⏰ **等待超时** — 用户未在指定时间内确认。',
            '',
            `- Task ID: \`${taskId}\``,
            `- 超时时间: ${timeoutMs / 1000}s`,
            '',
            '💡 你可以稍后使用 `check_task_status` 再次检查，或发送新的通知。',
          ].join('\n'),
        }],
      };
    }

    // ── list_pending_tasks ──
    case 'list_pending_tasks': {
      const pending = listPendingTasks();
      if (pending.length === 0) {
        return { content: [{ type: 'text', text: '📭 目前没有待确认的任务。' }] };
      }

      const lines = [
        `📋 **待确认任务列表 (${pending.length} 个)**`,
        '',
        '| Task ID | 标题 | 创建时间 | 操作 |',
        '|---------|------|----------|------|',
        ...pending.map(t =>
          `| \`${t.taskId}\` | ${t.title} | ${t.createdAt} | ${t.actions.join(', ')} |`
        ),
      ];

      return { content: [{ type: 'text', text: lines.join('\n') }] };
    }

    // ── cleanup_expired_tasks ──
    case 'cleanup_expired_tasks': {
      const maxAge = parseInt(args.maxAgeHours) || 24;
      const cleaned = cleanupExpired(maxAge);
      return {
        content: [{
          type: 'text',
          text: cleaned > 0
            ? `🧹 已清理 ${cleaned} 个过期任务（超过 ${maxAge} 小时）`
            : `✨ 没有需要清理的过期任务（阈值: ${maxAge} 小时）`,
        }],
      };
    }

    default:
      throw new Error(`未知工具: ${name}`);
  }
});

// ── 启动 ──
// 1. 启动 HTTP 回调服务
let httpServer = null;
try {
  httpServer = startCallbackServer();
} catch (err) {
  console.error(`[task-email] ⚠️ HTTP 回调服务启动失败: ${err.message}`);
  console.error('[task-email] 邮件中的确认链接将无法使用，但邮件发送功能正常。');
}

// 2. 连接 MCP transport
const transport = new StdioServerTransport();
await server.connect(transport);
console.error('[task-email] MCP Server 已就绪');

// 优雅退出
process.on('SIGINT', () => {
  if (httpServer) httpServer.close();
  process.exit(0);
});
process.on('SIGTERM', () => {
  if (httpServer) httpServer.close();
  process.exit(0);
});
