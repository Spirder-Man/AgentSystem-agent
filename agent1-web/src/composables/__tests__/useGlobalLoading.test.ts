/**
 * P2-10b: useGlobalLoading 组合式函数单元测试
 *
 * 验证: start/stop 状态切换/消息传递/loading ref 响应式
 */
import { describe, it, expect, vi } from 'vitest';
import { useGlobalLoading } from '@/composables/useGlobalLoading';

// useGlobalLoading 底层使用模块级 ref，需要直接测试
describe('useGlobalLoading', () => {
  it('start 应设置 loading 为 true', () => {
    const { start, loading } = useGlobalLoading();

    expect(loading.value).toBe(false);
    start('测试消息');
    expect(loading.value).toBe(true);
  });

  it('start 应设置消息内容', () => {
    const { start, message } = useGlobalLoading();

    start('正在加载数据…');
    expect(message.value).toBe('正在加载数据…');
  });

  it('start 默认消息应为"处理中，请稍候…"', () => {
    const { start, message } = useGlobalLoading();

    start();
    expect(message.value).toBe('处理中，请稍候…');
  });

  it('stop 应清除 loading 和 message', () => {
    const { start, stop, loading, message } = useGlobalLoading();

    start('处理中…');
    expect(loading.value).toBe(true);
    expect(message.value).toBe('处理中…');

    stop();
    expect(loading.value).toBe(false);
    expect(message.value).toBe('');
  });

  it('多次调用 start/stop 状态应正确切换', () => {
    const { start, stop, loading } = useGlobalLoading();

    start('A');
    expect(loading.value).toBe(true);
    stop();
    expect(loading.value).toBe(false);

    start('B');
    expect(loading.value).toBe(true);
    stop();
    expect(loading.value).toBe(false);
  });

  it('同一实例多次调用返回相同 ref 引用', () => {
    const a = useGlobalLoading();
    const b = useGlobalLoading();

    // 共享模块级 ref，所以 a.start() 影响 b.loading
    a.start('from a');
    expect(b.loading.value).toBe(true);
    expect(b.message.value).toBe('from a');

    b.stop();
    expect(a.loading.value).toBe(false);
  });

  it('loading 应为 ref 类型（响应式）', () => {
    const { loading } = useGlobalLoading();

    expect(loading).toBeDefined();
    expect(typeof loading.value).toBe('boolean');
  });
});
