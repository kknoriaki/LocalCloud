// Local PDF decoder assets: no CDN or Internet dependency in production.
import {cpSync,mkdirSync} from 'node:fs';import {fileURLToPath} from 'node:url';
for(const name of ['cmaps','standard_fonts','wasm']){const target=new URL('./public/pdf-assets/'+name+'/',import.meta.url);mkdirSync(target,{recursive:true});cpSync(fileURLToPath(new URL('./node_modules/pdfjs-dist/'+name+'/',import.meta.url)),fileURLToPath(target),{recursive:true});}
