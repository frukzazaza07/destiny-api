// Offline visual QA: render each actual exported GLB in Chrome, not an artist's mockup.
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const root = fileURLToPath(new URL('../', import.meta.url));
const output = `${root}/.tmp/model-previews`;
await mkdir(output, { recursive: true });
const html = `<!doctype html><html><head><script type="importmap">{"imports":{"three":"/vendor/three.module.js","three/webgpu":"/vendor/three.webgpu.js"}}</script></head>
<body style="margin:0"><script type="module">
import * as T from 'three';
import { GLTFLoader } from '/GLTFLoader.js';
const renderer = new T.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});renderer.setSize(1200,900);document.body.append(renderer.domElement);
renderer.setClearColor('#e5ded1');
const camera=new T.PerspectiveCamera(40,1200/900,.01,150),scene=new T.Scene();
scene.add(new T.HemisphereLight('#fff5df','#8e8270',2.4));
const key=new T.DirectionalLight('#ffffff',3);key.position.set(4,9,8);scene.add(key);
let model,mixer;
window.showModel=async(name,animation)=>{
 if(model)scene.remove(model);
 const asset=await new GLTFLoader().loadAsync('/models/'+name+'.glb');model=asset.scene;scene.add(model);
 if(animation){mixer=new T.AnimationMixer(model);mixer.clipAction(asset.animations.find(c=>c.name===animation)).play();mixer.setTime(.8);}
 const bounds=new T.Box3().setFromObject(model),center=bounds.getCenter(new T.Vector3()),size=bounds.getSize(new T.Vector3());
 if(name==='shop'){camera.position.set(25,26,32);camera.lookAt(0,0,0);}
 else if(name==='tarot-back'){camera.position.set(.2,.17,.68);camera.lookAt(center);}
 else{camera.position.copy(center).add(new T.Vector3(2.1,1,4.2));camera.lookAt(center);}
 renderer.render(scene,camera);
 return {name,clips:asset.animations.map(c=>c.name),triangles:renderer.info.render.triangles,drawCalls:renderer.info.render.calls,bounds:size.toArray()};
};
</script></body></html>`;
const server=createServer(async(request,response)=>{
 try{
  let path;
  if(request.url==='/'){response.setHeader('Content-Type','text/html');response.end(html);return;}
  if(request.url==='/GLTFLoader.js')path='node_modules/three/examples/jsm/loaders/GLTFLoader.js';
  else if(request.url==='/utils/BufferGeometryUtils.js')path='node_modules/three/examples/jsm/utils/BufferGeometryUtils.js';
  else if(request.url==='/utils/SkeletonUtils.js')path='node_modules/three/examples/jsm/utils/SkeletonUtils.js';
  else if(/^\/vendor\/three[\w.]*\.js$/.test(request.url))path='node_modules/three/build/'+request.url.split('/').pop();
  else if(/^\/models\/(shop|advisor|visitor|tarot-back)\.glb$/.test(request.url))path='public/models/destiny-shop/initial/'+request.url.split('/').pop();
  else {response.writeHead(404).end();return;}
  response.setHeader('Content-Type',path.endsWith('.js')?'text/javascript':'model/gltf-binary');response.end(await readFile(root+path));
 }catch(error){response.writeHead(500).end(String(error));}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const browser=await chromium.launch({channel:'chrome',headless:true});
try{
 const page=await browser.newPage({viewport:{width:1200,height:900}});
 const errors=[];page.on('pageerror',error=>{errors.push(error.message);console.error(error.message);});
 page.on('response',response=>{if(response.status()>=400)console.error(response.status(),response.url());});
 await page.goto('http://127.0.0.1:'+server.address().port);
 await page.waitForFunction(()=>Boolean(window.showModel));
 for(const [name,clip]of [['shop'],...['idle','seated','greeting','listening','shuffling','dealing','reveal','result'].map(clip=>['advisor',clip]),['visitor','idle'],['visitor','walk'],['tarot-back']]){
  console.log(await page.evaluate(([name,clip])=>window.showModel(name,clip),[name,clip]));
  await page.screenshot({path:output+'/'+name+(clip?'-'+clip:'')+'.png'});
 }
 if(errors.length)throw Error(errors.join('\n'));
 console.log('Model previews: '+output);
}finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
