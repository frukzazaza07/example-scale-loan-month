export function money(minor: string | undefined | null): string {
  if (minor === undefined || minor === null) return 'Unavailable';
  const amount = BigInt(minor); const sign = amount < 0n ? '-' : ''; const positive = amount < 0n ? -amount : amount;
  const whole = (positive / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${sign}${whole}.${(positive % 100n).toString().padStart(2,'0')} THB`;
}
export const sumMinor = (...values:string[]) => values.reduce((sum,v) => sum+BigInt(v),0n).toString();
export const remaining = (interest:string,principal:string,paidInterest:string,paidPrincipal:string) => (BigInt(interest)+BigInt(principal)-BigInt(paidInterest)-BigInt(paidPrincipal)).toString();
export function time(value:string | null | undefined):string { return value ? new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Bangkok',dateStyle:'medium',timeStyle:'medium'}).format(new Date(value)) : 'Not observed'; }
