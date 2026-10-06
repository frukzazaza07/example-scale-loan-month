<script setup lang="ts">
import {onMounted,onUnmounted,ref,watch} from 'vue';
import {init,type ECharts} from 'echarts/core';
import {LineChart} from 'echarts/charts';
import {GridComponent,TooltipComponent,LegendComponent} from 'echarts/components';
import {CanvasRenderer} from 'echarts/renderers';
import {use} from 'echarts/core';
use([LineChart,GridComponent,TooltipComponent,LegendComponent,CanvasRenderer]);
const props=defineProps<{samples:{at:string;rate:number|null;queue:number|null;replicas:number|null}[]}>();
const element=ref<HTMLDivElement>();let chart:ECharts|undefined;let observer:ResizeObserver;
function render(){chart?.setOption({color:['#26826c','#c78340','#7c81bc'],tooltip:{trigger:'axis'},legend:{bottom:0,textStyle:{color:'#73847e'}},grid:{left:48,right:36,top:20,bottom:55},xAxis:{type:'category',boundaryGap:false,data:props.samples.map(s=>new Date(s.at).toLocaleTimeString()),axisLine:{lineStyle:{color:'#dfe7e1'}},axisLabel:{color:'#8b9992'}},yAxis:[{type:'value',name:'RPS / messages',splitLine:{lineStyle:{color:'#edf1ec'}},axisLabel:{color:'#8b9992'}},{type:'value',name:'Replicas',min:0,splitLine:{show:false}}],series:[{name:'Accepted RPS',type:'line',smooth:true,showSymbol:false,data:props.samples.map(s=>s.rate)},{name:'Ready messages',type:'line',smooth:false,showSymbol:false,data:props.samples.map(s=>s.queue)},{name:'Worker replicas',type:'line',step:'end',yAxisIndex:1,showSymbol:false,data:props.samples.map(s=>s.replicas)}]})}
onMounted(()=>{if(element.value){chart=init(element.value);observer=new ResizeObserver(()=>chart?.resize());observer.observe(element.value);render()}});watch(()=>props.samples,render,{deep:true});onUnmounted(()=>{observer?.disconnect();chart?.dispose()});
</script>
<template><div ref="element" class="chart" role="img" aria-label="Observed request rate, payment queue depth and worker replicas over time"/></template>
