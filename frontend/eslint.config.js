import vue from 'eslint-plugin-vue';
import ts from 'typescript-eslint';
export default [ { ignores: ['dist/**', 'node_modules/**', 'playwright-report/**', 'test-results/**'] }, ...ts.configs.recommended, ...vue.configs['flat/essential'], { files: ['src/views/*.vue'], rules: { 'vue/multi-word-component-names': 'off' } }, { files: ['**/*.vue'], languageOptions: { parserOptions: { parser: ts.parser, extraFileExtensions: ['.vue'] } } } ];
