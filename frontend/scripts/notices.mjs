import { readFile, writeFile } from 'node:fs/promises';
const packages = ['liquid-glass-react', 'react', 'react-dom', 'lucide-react'];
const notices = await Promise.all(packages.map(async name => `=== ${name} ===\n${await readFile(new URL(`../node_modules/${name}/LICENSE`, import.meta.url), 'utf8')}`));
await writeFile(new URL('../dist/THIRD-PARTY-LICENSES.txt', import.meta.url), notices.join('\n\n'));
