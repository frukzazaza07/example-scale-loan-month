<script setup lang="ts">
import {computed} from 'vue';
const props=defineProps<{status:string}>();
const tone=computed(()=> /^(Applied|Matched|Healthy|Completed|Active|SUCCEEDED|Created|Received|Paid)/.test(props.status)?'good':/^(Conflict|FAILED|Missing|Failed|Unavailable|AmountConflict|OutcomeConflict)/.test(props.status)?'bad':'warn');
</script>
<template><span class="badge" :class="tone"><span class="status-dot"/>{{status.replace(/([a-z])([A-Z])/g,'$1 $2')}}</span></template>
