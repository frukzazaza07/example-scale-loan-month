import { onUnmounted, ref, shallowRef, watch, type Ref } from 'vue';
import { api } from '../api';
export function usePoll<T>(load:(signal:AbortSignal) => Promise<T>, interval = 3000) {
  const data = shallowRef<T>(); const error = ref(''); const loading = ref(true); const updatedAt = ref<string>();
  let timer:ReturnType<typeof setTimeout>; let controller:AbortController | undefined; let stopped = false; let failures = 0;
  async function refresh() {
    clearTimeout(timer); controller?.abort(); controller = new AbortController(); const current = controller;
    try { const value = await load(current.signal); if (!stopped && !current.signal.aborted) { data.value=value; error.value=''; updatedAt.value=new Date().toISOString(); failures=0; } }
    catch (e) { if (!current.signal.aborted && !stopped) { error.value=e instanceof Error ? e.message : 'Service unavailable'; failures++; } }
    finally { if (!current.signal.aborted && !stopped) { loading.value=false; timer=setTimeout(refresh,Math.min(30000,interval*2**Math.min(failures,4))); } }
  }
  void refresh(); onUnmounted(() => {stopped=true;clearTimeout(timer);controller?.abort()});
  return {data,error,loading,updatedAt,refresh};
}
export function useResource<T>(path:Ref<string>) { const state=usePoll<T>(signal=>api<T>(path.value,{signal})); watch(path,()=>{state.data.value=undefined;void state.refresh()}); return state; }
export const useLoans = useResource;
export const usePayments = useResource;
export const useSimulator = useResource;
export const useMetrics = () => usePoll(signal=>api<import('../api').Metric>('/metrics',{signal}));
