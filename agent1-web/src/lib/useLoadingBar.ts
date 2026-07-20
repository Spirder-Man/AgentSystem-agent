import { ref, readonly } from 'vue';

const visible = ref(false);
const message = ref('');
let timer: ReturnType<typeof setTimeout> | null = null;

export function useLoadingBar() {
  function start(msg?: string) {
    message.value = msg || '';
    visible.value = true;
    if (timer) clearTimeout(timer);
    timer = setTimeout(reset, 30_000);
  }

  function stop() {
    visible.value = false;
    message.value = '';
    if (timer) { clearTimeout(timer); timer = null; }
  }

  function reset() {
    visible.value = false;
    message.value = '';
    if (timer) { clearTimeout(timer); timer = null; }
  }

  return { visible: readonly(visible), message: readonly(message), start, stop };
}
