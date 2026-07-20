import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import crypto from 'node:crypto';

const __dirname = dirname(fileURLToPath(import.meta.url));
const STORE_PATH = join(__dirname, 'tasks.json');

// ── 任务状态枚举 ──
// pending   → 等待用户确认
// confirmed → 用户已点击确认
// rejected  → 用户已拒绝
// expired   → 超时未确认
const VALID_STATUSES = ['pending', 'confirmed', 'rejected', 'expired'];

/**
 * 加载所有任务（不存在则返回空数组）
 */
function loadTasks() {
  try {
    if (!existsSync(STORE_PATH)) return [];
    const raw = readFileSync(STORE_PATH, 'utf-8');
    return JSON.parse(raw);
  } catch {
    return [];
  }
}

/**
 * 保存任务列表
 */
function saveTasks(tasks) {
  writeFileSync(STORE_PATH, JSON.stringify(tasks, null, 2), 'utf-8');
}

/**
 * 创建新任务
 * @param {{ title: string, summary: string, nextStep: string, actions: Array<{label: string, actionId: string}> }} params
 * @returns {{ taskId: string, token: string }}
 */
export function createTask({ title, summary, nextStep, actions }) {
  const tasks = loadTasks();
  const taskId = crypto.randomUUID().slice(0, 8);
  const token = crypto.randomUUID().replace(/-/g, '').slice(0, 16);

  const task = {
    taskId,
    token,
    title,
    summary,
    nextStep,
    actions: actions.map(a => ({ ...a, status: 'pending' })),
    overallStatus: 'pending',
    confirmedAction: null,
    createdAt: new Date().toISOString(),
    confirmedAt: null,
  };

  tasks.push(task);
  saveTasks(tasks);
  return { taskId, token };
}

/**
 * 确认任务中的某个动作
 * @returns {{ success: boolean, task?: object, error?: string }}
 */
export function confirmAction(taskId, actionId, token) {
  const tasks = loadTasks();
  const task = tasks.find(t => t.taskId === taskId);
  if (!task) return { success: false, error: '任务不存在或已过期' };
  if (task.token !== token) return { success: false, error: 'Token 无效' };
  if (task.overallStatus !== 'pending') return { success: false, error: `任务已${task.overallStatus === 'confirmed' ? '确认' : '过期'}` };

  const action = task.actions.find(a => a.actionId === actionId);
  if (!action) return { success: false, error: '未知操作' };

  action.status = 'confirmed';
  task.overallStatus = 'confirmed';
  task.confirmedAction = actionId;
  task.confirmedAt = new Date().toISOString();

  saveTasks(tasks);
  return { success: true, task };
}

/**
 * 查询任务状态
 */
export function getTaskStatus(taskId) {
  const tasks = loadTasks();
  const task = tasks.find(t => t.taskId === taskId);
  if (!task) return null;
  return {
    taskId: task.taskId,
    title: task.title,
    overallStatus: task.overallStatus,
    confirmedAction: task.confirmedAction,
    actions: task.actions.map(a => ({ label: a.label, actionId: a.actionId, status: a.status })),
    createdAt: task.createdAt,
    confirmedAt: task.confirmedAt,
  };
}

/**
 * 列出所有待处理任务
 */
export function listPendingTasks() {
  return loadTasks()
    .filter(t => t.overallStatus === 'pending')
    .map(t => ({
      taskId: t.taskId,
      title: t.title,
      createdAt: t.createdAt,
      actions: t.actions.map(a => a.label),
    }));
}

/**
 * 清理过期任务（超过指定小时）
 * @param {number} maxAgeHours 默认 24 小时
 */
export function cleanupExpired(maxAgeHours = 24) {
  const tasks = loadTasks();
  const cutoff = Date.now() - maxAgeHours * 3600 * 1000;
  const valid = tasks.filter(t => {
    if (t.overallStatus === 'pending' && new Date(t.createdAt).getTime() < cutoff) {
      t.overallStatus = 'expired';
    }
    return new Date(t.createdAt).getTime() > cutoff || t.overallStatus !== 'pending';
  });
  saveTasks(valid);
  return tasks.length - valid.length;
}


