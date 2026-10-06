import {defineStore} from 'pinia';
import {ref} from 'vue';
import {api,post} from '../api';
export const useAuth = defineStore('auth',()=>{
  const role=ref<'Admin'|'Observer'|null>(null); const ready=ref(false);
  async function initialize(){try{role.value=(await api<{role:'Admin'|'Observer'}>('/auth/me')).role}catch{role.value=null}finally{ready.value=true}}
  async function login(selected:'Admin'|'Observer',password:string){const result=await post<{role:'Admin'|'Observer'}>('/auth/login',{role:selected,password});role.value=result.role}
  async function logout(){await post('/auth/logout');role.value=null}
  return {role,ready,initialize,login,logout};
});
