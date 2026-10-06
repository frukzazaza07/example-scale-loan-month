<script setup lang="ts">
import type {Quantile} from '../api';
defineProps<{acceptance:Quantile|undefined;application:Quantile|undefined}>();
function display(value:Quantile|undefined){const n=Number(value?.[0]?.value[1]??NaN);return Number.isFinite(n)?n.toFixed(3)+' seconds':'Unavailable: no observations'}
</script>
<template><div class="two-col"><div class="stat-card"><span>Durable acceptance latency · p95</span><strong class="small-stat">{{display(acceptance)}}</strong><small>Observed Prometheus histogram · last minute</small></div><div class="stat-card"><span>Receipt-to-application latency · p95</span><strong class="small-stat">{{display(application)}}</strong><small>Observed worker histogram · last minute</small></div></div></template>
