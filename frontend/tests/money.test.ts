import {expect,test} from 'vitest';import {money,remaining} from '../src/money';
test('formats satang exactly beyond floating point precision',()=>{expect(money('9007199254740993')).toBe('90,071,992,547,409.93 THB');expect(money('1')).toBe('0.01 THB');expect(money('-1')).toBe('-0.01 THB')});
test('remaining installments use integer arithmetic',()=>{expect(remaining('100000','800000','100000','800000')).toBe('0')});
