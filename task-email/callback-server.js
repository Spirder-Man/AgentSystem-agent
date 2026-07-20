import http from 'node:http';
import { confirmAction, getTaskStatus } from './task-store.js';

const PORT = parseInt(process.env.CALLBACK_PORT || '9876', 10);

/**
 * 生成确认成功 HTML 页面
 */
function successPage(task) {
  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>确认成功 — Agent1</title>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: 'Microsoft YaHei', -apple-system, sans-serif;
    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    min-height: 100vh; display: flex; align-items: center; justify-content: center;
  }
  .card {
    background: white; border-radius: 16px; padding: 48px;
    max-width: 480px; width: 90%; text-align: center;
    box-shadow: 0 20px 60px rgba(0,0,0,0.15);
  }
  .icon { font-size: 64px; margin-bottom: 16px; }
  h1 { font-size: 24px; color: #2d3748; margin-bottom: 8px; }
  .task-title { color: #667eea; font-weight: 600; margin-bottom: 16px; }
  .detail {
    background: #f7fafc; border-radius: 8px; padding: 16px;
    text-align: left; margin: 16px 0; font-size: 14px; color: #4a5568;
  }
  .action-badge {
    display: inline-block; background: #48bb78; color: white;
    padding: 4px 12px; border-radius: 20px; font-size: 13px; margin-top: 8px;
  }
  .footer { margin-top: 24px; color: #a0aec0; font-size: 12px; }
</style>
</head>
<body>
<div class="card">
  <div class="icon">✅</div>
  <h1>操作已确认</h1>
  <p class="task-title">${escapeHtml(task.title)}</p>
  <div class="detail">
    <strong>确认动作：</strong>${escapeHtml(task.confirmedAction || '—')}
    <br><strong>确认时间：</strong>${new Date(task.confirmedAt).toLocaleString('zh-CN')}
  </div>
  <div class="action-badge">Agent1 将继续执行下一步</div>
  <div class="footer">
    Agent1 Task Email System · 可以关闭此页面
  </div>
</div>
</body>
</html>`;
}

/**
 * 错误页面
 */
function errorPage(message) {
  return `<!DOCTYPE html>
<html lang="zh-CN">
<head><meta charset="UTF-8"><title>确认失败</title>
<style>
  body { font-family: 'Microsoft YaHei', sans-serif; display: flex;
    align-items: center; justify-content: center; min-height: 100vh;
    background: #fef2f2; }
  .card { background: white; border-radius: 12px; padding: 40px; text-align: center;
    box-shadow: 0 4px 12px rgba(0,0,0,0.1); }
  .icon { font-size: 48px; } h1 { color: #dc3545; margin: 8px 0; }
  p { color: #6b7280; }
</style>
</head>
<body>
<div class="card">
  <div class="icon">❌</div>
  <h1>确认失败</h1>
  <p>${escapeHtml(message)}</p>
</div>
</body>
</html>`;
}

function escapeHtml(str) {
  return String(str).replace(/[&<>"']/g, m =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
}

/**
 * 启动 HTTP 回调服务
 */
export function startCallbackServer() {
  const server = http.createServer((req, res) => {
    // CORS
    res.setHeader('Access-Control-Allow-Origin', '*');

    const url = new URL(req.url, `http://localhost:${PORT}`);

    // GET /confirm/:taskId/:actionId?token=xxx
    const confirmMatch = url.pathname.match(/^\/confirm\/([a-zA-Z0-9-]+)\/([a-zA-Z0-9-]+)$/);
    if (confirmMatch && req.method === 'GET') {
      const taskId = confirmMatch[1];
      const actionId = confirmMatch[2];
      const token = url.searchParams.get('token') || '';

      const result = confirmAction(taskId, actionId, token);

      res.writeHead(result.success ? 200 : 400, { 'Content-Type': 'text/html; charset=utf-8' });
      res.end(result.success ? successPage(result.task) : errorPage(result.error));
      return;
    }

    // GET /status/:taskId
    const statusMatch = url.pathname.match(/^\/status\/([a-zA-Z0-9-]+)$/);
    if (statusMatch && req.method === 'GET') {
      const taskId = statusMatch[1];
      const task = getTaskStatus(taskId);
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify(task || { error: '任务不存在' }));
      return;
    }

    // 404
    res.writeHead(404, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end('<h1>404 Not Found</h1>');
  });

  server.listen(PORT, () => {
    console.error(`[task-email] HTTP 回调服务已启动: http://localhost:${PORT}`);
  });

  return server;
}
