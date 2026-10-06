import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./tests/browser',timeout:60000,expect:{timeout:20000},workers:1,use:{baseURL:process.env.SMOKE_URL??'http://localhost:8080',headless:true,screenshot:'only-on-failure',trace:'retain-on-failure',viewport:{width:1440,height:1000}},reporter:[['list'],['html',{open:'never'}]]});
