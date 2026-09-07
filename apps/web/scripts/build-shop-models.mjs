// Original Destiny Shop model source. Run with `npm run models:build`.
// All geometry is authored here; no downloaded artwork, textures or model data.
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import * as T from 'three';
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { mergeGeometries, mergeVertices } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

// GLTFExporter uses FileReader for its binary buffers, even without textures.
globalThis.FileReader = class {
  readAsArrayBuffer(blob) { blob.arrayBuffer().then(result => { this.result = result; this.onloadend?.(); }); }
};
const output = fileURLToPath(new URL('../public/models/destiny-shop/initial/', import.meta.url));
await mkdir(output, { recursive: true });
const materials = {};
function material(name, color, roughness = .65, metalness = 0, glow = false) {
  const m = new T.MeshStandardMaterial({ name, color, roughness, metalness });
  if (glow) { m.emissive.set(color); m.emissiveIntensity = .8; }
  materials[name] = m;
  return m;
}
const teak = material('Oiled teak', '#70442e', .52);
const dark = material('Dark walnut', '#30271f', .65);
const woodLight = material('End grain', '#ae7750', .66);
const brass = material('Brushed brass', '#d6b16c', .34, .65);
const jade = material('Jade woven silk', '#337b70', .89);
const cream = material('Warm linen', '#e3d3b7', .92);
const wall = material('Lime plaster', '#87948a', .94);
const midnight = material('Ink blue', '#293d50', .85);
const rose = material('Mulberry velvet', '#70384d', .96);
const skin = material('Warm skin', '#bd865f', .85);
const hair = material('Espresso hair', '#251e1b', .8);
const eyeWhite = material('Eye whites', '#eae0d3', .55);
const iris = material('Brown iris', '#3c2920', .38);
const leaf = material('Deep leaf', '#326047', .82);
const leafLight = material('Young leaf', '#60855c', .8);
const ceramic = material('Glazed celadon', '#6c9e90', .28);
const glow = material('Warm lantern diffuser', '#ffda91', .68, 0, true);

function group(parent, name, position = [0, 0, 0]) {
  const g = new T.Group(); g.name = name; g.position.set(...position); parent?.add(g); return g;
}
function mesh(parent, geometry, mat, position = [0, 0, 0], rotation = [0, 0, 0], scale = [1, 1, 1]) {
  const m = new T.Mesh(geometry, mat); m.position.set(...position); m.rotation.set(...rotation); m.scale.set(...scale); parent.add(m); return m;
}
function box(p, size, mat, pos, radius = .025) {
  return mesh(p, radius ? new RoundedBoxGeometry(...size, 1, Math.min(radius, Math.min(...size) / 3)) : new T.BoxGeometry(...size), mat, pos);
}
function ellipsoid(p, size, mat, pos, rotation = [0, 0, 0]) {
  return mesh(p, new T.SphereGeometry(1, 12, 8), mat, pos, rotation, size);
}
function lathe(p, profile, mat, pos = [0, 0, 0], segments = 20) {
  return mesh(p, new T.LatheGeometry(profile.map(([r,y]) => new T.Vector2(r,y)), segments), mat, pos);
}
function rod(p, a, b, radius, mat, topRadius = radius) {
  const start = new T.Vector3(...a), end = new T.Vector3(...b);
  const m = mesh(p, new T.CylinderGeometry(topRadius, radius, start.distanceTo(end), 10), mat, start.clone().add(end).multiplyScalar(.5).toArray());
  m.quaternion.setFromUnitVectors(new T.Vector3(0,1,0), end.sub(start).normalize()); return m;
}
function ring(p, r, tube, mat, pos, rot = [Math.PI/2, 0, 0]) {
  return mesh(p, new T.TorusGeometry(r,tube,6,24),mat,pos,rot);
}
function curve(p, points, radius, mat) {
  return mesh(p,new T.TubeGeometry(new T.CatmullRomCurve3(points.map(v=>new T.Vector3(...v))),10,radius,5,false),mat);
}

// Bake static parts into one mesh per material. Articulated groups remain separate.
function batch(root) {
  root.updateMatrixWorld(true);
  const inverse = root.matrixWorld.clone().invert(), buckets = new Map();
  root.traverse(o => {
    if (!o.isMesh) return;
    const geo = o.geometry.index ? o.geometry.toNonIndexed() : o.geometry.clone();
    geo.applyMatrix4(new T.Matrix4().multiplyMatrices(inverse,o.matrixWorld));
    geo.deleteAttribute('uv');
    if (!buckets.has(o.material)) buckets.set(o.material,[]);
    buckets.get(o.material).push(geo);
  });
  root.clear();
  for(const [mat, geometries] of buckets) {
    const unindexed = mergeGeometries(geometries);
    const merged = mergeVertices(unindexed);
    unindexed.dispose();
    const m = new T.Mesh(merged,mat); m.name = `${root.name}_${mat.name.replaceAll(' ','_')}`; root.add(m);
    geometries.forEach(g=>g.dispose());
  }
}

function chair(parent, pos, rotation = 0) {
  const c = group(parent,'Carved_armchair',pos); c.rotation.y = rotation;
  c.scale.setScalar(.8);
  for(const x of [-.46,.46]) for(const z of [-.37,.37]) {
    lathe(c,[[.045,0],[.065,.07],[.04,.16],[.055,.5],[.07,.63]],teak,[x,0,z],12);
  }
  box(c,[1.12,.15,.98],teak,[0,.62,0],.05);
  box(c,[.99,.18,.85],jade,[0,.75,0],.08);
  for(const x of [-.5,.5]) {
    rod(c,[x,.65,.38],[x,1.62,.42],.052,teak);
    curve(c,[[x,.96,-.38],[x,1.02,0],[x,1.07,.38]],.055,teak);
    rod(c,[x,.68,-.32],[x,.97,-.32],.035,brass);
  }
  box(c,[1.04,.83,.12],teak,[0,1.24,.44],.045);
  box(c,[.9,.65,.08],jade,[0,1.24,.365],.055);
  for(const x of [-.27,0,.27]) ellipsoid(c,[.025,.025,.013],brass,[x,1.27,.318]);
  curve(c,[[-.53,1.63,.43],[0,1.76,.43],[.53,1.63,.43]],.055,teak);
}
function table(parent,pos) {
  const t = group(parent,'Tarot_consultation_table',pos);
  t.scale.y = .68;
  box(t,[4.3,.2,2.7],teak,[0,1.39,0],.09);
  box(t,[4.22,.035,2.62],brass,[0,1.505,0],.07);
  box(t,[4.06,.025,2.46],rose,[0,1.535,0],.09);
  box(t,[3.95,.33,.11],teak,[0,1.12,1.22]);
  for(const x of [-1.72,1.72]) for(const z of [-.97,.97]) {
    lathe(t,[[.085,0],[.14,.08],[.08,.2],[.075,.75],[.13,.92],[.13,1.35]],teak,[x,0,z],16);
    ring(t,.115,.018,brass,[x,.16,z]);
  }
  // Sewn border and repeating brass diamond inlay.
  for(const z of [-1.13,1.13]) rod(t,[-1.9,1.555,z],[1.9,1.555,z],.008,brass);
  for(let x=-1.7;x<=1.7;x+=.34) box(t,[.08,.02,.08],brass,[x,1.08,1.285],0).rotation.y=Math.PI/4;
  for(const x of [-1.65,1.65]) {
    lathe(t,[[.16,0],[.16,.025],[.055,.05],[.045,.2],[.11,.22]],brass,[x,1.56,-.8]);
    lathe(t,[[.065,0],[.065,.23]],cream,[x,1.78,-.8]);
    ellipsoid(t,[.025,.065,.025],glow,[x,2.04,-.8]);
  }
}
function plant(parent,pos,scale=1) {
  const p=group(parent,'Ceramic_planter',pos); p.scale.setScalar(scale);
  lathe(p,[[.23,0],[.3,.08],[.35,.45],[.31,.58],[.34,.61],[.27,.63],[.26,.55]],ceramic);
  for(let i=0;i<7;i++) {
    const angle=i*2.4, x=Math.cos(angle)*.48, z=Math.sin(angle)*.48, y=.95+(i%3)*.18;
    curve(p,[[0,.52,0],[x*.4,y*.85,z*.4],[x,y,z]],.015,leaf);
    const l=ellipsoid(p,[.13,.37,.025],i%2?leaf:leafLight,[x,y,z],[.5,angle,.45]);
    l.rotation.z=x>0?-.6:.6;
  }
}
function lantern(parent,pos) {
  const l=group(parent,'Woven_lantern',pos);
  lathe(l,[[.16,-.35],[.36,-.2],[.39,0],[.32,.3],[.16,.39]],glow);
  for(let i=0;i<12;i++) {
    const a=i*Math.PI/6;
    curve(l,[[Math.cos(a)*.16,-.35,Math.sin(a)*.16],[Math.cos(a)*.39,0,Math.sin(a)*.39],[Math.cos(a)*.16,.39,Math.sin(a)*.16]],.013,teak);
  }
  ring(l,.16,.027,brass,[0,-.35,0]); ring(l,.16,.027,brass,[0,.39,0]);
  rod(l,[0,.4,0],[0,1.2,0],.014,brass);
}
function doorway(parent,z) {
  const g=group(parent,'Teak_portal',[0,0,z]);
  for(const x of [-2.45,2.45]) {
    box(g,[.3,4.5,.38],teak,[x,2.25,0]);
    box(g,[.48,.2,.54],brass,[x,.15,0]);
    for(let y=.5;y<4.4;y+=.24) box(g,[.015,.1,.4],woodLight,[x+.12,y,0],0);
  }
  box(g,[5.2,.28,.44],teak,[0,4.45,0]);
  curve(g,[[-2.65,4.44,0],[-1.6,4.82,0],[0,5.16,0],[1.6,4.82,0],[2.65,4.44,0]],.075,brass);
  for(let x=-2.1;x<=2.1;x+=.3) rod(g,[x,4.08,0],[x,4.38,0],.02,woodLight);
}
function cabinet(parent,pos,rotation=0) {
  const g=group(parent,'Display_cabinet',pos);g.rotation.y=rotation;
  box(g,[2.1,2.9,.6],dark,[0,1.5,0]);
  for(const y of [.12,1.04,1.97,2.95]) box(g,[2.25,.12,.72],teak,[0,y,.05]);
  for(const x of [-1.04,1.04]) box(g,[.12,3,.75],teak,[x,1.5,.05]);
  for(let i=0;i<9;i++) box(g,[.13,.5+(i%3)*.09,.25],i%2?rose:midnight,[-.83+i*.19,.44,.25]);
  lathe(g,[[.12,0],[.23,.15],[.21,.38],[.09,.49],[.09,.57]],ceramic,[-.5,1.1,.1]);
  lathe(g,[[.13,0],[.18,.15],[.11,.3],[.075,.34]],brass,[.43,2.04,.1]);
}

function environment() {
  const root=group(null,'Destiny_shop');
  const collision=group(root,'Architecture_collision');
  for(const [s,p] of [ [[32,.14,28],[0,-.1,0]], [[.24,4.8,28],[-16,2.4,0]],[[.24,4.8,28],[16,2.4,0]],[[32,4.8,.24],[0,2.4,-14]],[[11.6,4.8,.2],[-10.2,2.4,5.1]],[[11.6,4.8,.2],[10.2,2.4,5.1]],[[11.4,4.8,.2],[-10.3,2.4,-4.3]],[[11.4,4.8,.2],[10.3,2.4,-4.3]] ]) box(collision,s,p[1]<0?teak:wall,p,0);
  batch(collision); collision.children.forEach(m=>m.userData.cameraCollision=true);
  const art=group(root,'Shop_furnishings');
  // Floor boards, jade carpet and thin woven border. No overhead obstruction of the follow camera.
  for(let x=-15.5;x<16;x+=1) rod(art,[x,.001,-14],[x,.001,14],.012,dark);
  box(art,[4.2,.018,27.4],jade,[0,.015,0],0);
  for(const x of [-2.05,2.05]) rod(art,[x,.032,-13.6],[x,.032,13.6],.022,brass);
  for(let z=-13;z<13;z+=.7) for(const x of [-1.93,1.93]) box(art,[.075,.012,.22],cream,[x,.037,z],0);
  doorway(art,5.05);doorway(art,-4.35);
  // Wainscot, narrow timber battens, and woven lattice panels.
  for(const z of [5.1,-4.3,-13.8]) for(const side of [-1,1]) {
    for(let x=5;x<15.7;x+=.55) {
      box(art,[.075,1.3,.12],teak,[side*x,.7,z+.16],0);
      box(art,[.04,2.4,.04],woodLight,[side*x,2.9,z+.15],0);
    }
    for(const y of [.15,1.37,4.38]) box(art,[11,.1,.14],teak,[side*10,y,z+.18]);
  }
  const desk=group(art,'Reception_desk',[-4.9,0,7.8]);
  box(desk,[4.9,1.35,1.1],teak,[0,.7,0],.09);
  box(desk,[5.12,.13,1.3],cream,[0,1.42,0],.055);
  for(let x=-2.25;x<2.3;x+=.15) box(desk,[.055,1.12,.08],woodLight,[x,.72,.58]);
  box(desk,[1,.45,.03],jade,[0,.88,.64]);
  // Small brass desk bell, ledger, and flower vase.
  lathe(desk,[[.11,0],[.12,.03],[.1,.09],[.02,.13]],brass,[1.7,1.49,.25]);
  box(desk,[.62,.08,.43],midnight,[0,1.5,.1]);
  plant(art,[-7.7,0,8.1],1.1);
  for(const [x,z] of [[11.8,11],[-12,11],[9.8,-12],[-9.8,-12]]) plant(art,[x,0,z],1.5);
  for(const [x,z] of [[-5.8,3.7],[5.8,3.7],[-5.8,-5.5],[5.8,-5.5]]) lantern(art,[x,3.55,z]);
  for(const [x,mat] of [[-7.2,rose],[7.2,jade],[-11.7,midnight],[11.7,ceramic]]) {
    const station=group(art,'Future_service_station',[x,0,Math.abs(x)>10?-1.8:.4]);
    box(station,[2.7,.17,1.8],teak,[0,1.1,0],.07);
    box(station,[2.55,.025,1.65],mat,[0,1.2,0]);
    for(const a of [-1.1,1.1]) for(const b of [-.6,.6]) rod(station,[a,0,b],[a,1.05,b],.07,teak);
    box(station,[1.8,.45,.08],dark,[0,1.65,-.7]);
    ring(station,.22,.02,brass,[0,2.17,-.65],[0,0,0]);
    chair(station,[0,0,-1.35]);
  }
  table(art,[0,0,-9.9]);chair(art,[0,0,-11.85],Math.PI);chair(art,[0,0,-7.95]);
  cabinet(art,[-12.7,0,-12.8]);cabinet(art,[12.7,0,-12.8]);
  const bench=group(art,'Reception_bench',[8,0,10]);
  box(bench,[3.7,.18,1.1],teak,[0,.62,0],.07);box(bench,[3.5,.22,.93],jade,[0,.79,0],.09);
  for(const x of [-1.6,1.6]) box(bench,[.15,.65,.85],teak,[x,.3,0]);
  box(bench,[3.7,.9,.14],teak,[0,1.27,.46]);
  for(let x=-1.5;x<1.6;x+=.25) rod(bench,[x,.9,.38],[x,1.62,.38],.025,woodLight);
  // A secular sunburst artwork above the advisor.
  box(art,[4.1,2.45,.12],dark,[0,3,-13.78],.05);
  box(art,[3.85,2.2,.03],midnight,[0,3,-13.7]);
  ring(art,.65,.026,brass,[0,3,-13.66],[0,0,0]);
  for(let i=0;i<24;i++) {const a=i*Math.PI/12;rod(art,[Math.cos(a)*.75,3+Math.sin(a)*.75,-13.65],[Math.cos(a)*.95,3+Math.sin(a)*.95,-13.65],.015,brass);}
  batch(art);
  return {root,clips:[]};
}

function character(advisor) {
  const root=group(null,advisor?'Advisor':'Visitor');
  const torso=group(root,'Torso',[0,1.1,0]);
  const body=group(torso,'Clothing');
  // Shaped silhouette with a neck, fitted shoulders and fabric layers.
  const shirt=advisor?jade:cream;
  lathe(body,[[.2,-.24],[.27,-.12],[.25,.08],[.33,.35],[.27,.48],[.105,.51]],shirt,[0,0,0],24).scale.z=.64;
  if(advisor) {
    // Contemporary silk blouse and tailored trousers, without ceremonial dress or sacred symbols.
    box(body,[.43,.13,.29],rose,[0,-.23,0],.04);
    const sash=box(body,[.1,.57,.03],brass,[.12,.19,.18]);sash.rotation.z=-.24;
  } else {
    box(body,[.48,.18,.3],midnight,[0,-.2,0],.05);
    for(const x of [-.08,.08]) box(body,[.1,.1,.014],teak,[x,.25,.188]);
    // Small crossbody satchel and strap.
    curve(body,[[-.22,.43,-.13],[-.12,.1,-.21],[.21,-.1,-.22]],.022,teak);
    box(body,[.25,.3,.13],teak,[.22,-.12,-.19],.04);
    box(body,[.08,.05,.012],brass,[.22,-.08,-.26]);
  }
  lathe(body,[[.08,0],[.09,.14]],skin,[0,.46,0]); batch(body);
  const head=group(torso,'Head',[0,.7,0]);
  mesh(head,new T.SphereGeometry(1,24,18),skin,[0,0,0],[0,0,0],[.205,.27,.185]);
  for(const side of [-1,1]) {
    ellipsoid(head,[.035,.059,.028],skin,[side*.204,-.025,0]);
    ellipsoid(head,[.05,.027,.012],eyeWhite,[side*.078,.021,.171]);
    ellipsoid(head,[.022,.023,.008],iris,[side*.078,.021,.184]);
    ellipsoid(head,[.008,.008,.004],eyeWhite,[side*.072,.027,.192]);
    curve(head,[[side*.037,.071,.174],[side*.081,.084,.18],[side*.123,.07,.164]],.009,hair);
    if(advisor) ring(head,.029,.008,brass,[side*.216,-.082,0],[0,Math.PI/2,0]);
  }
  ellipsoid(head,[.027,.052,.041],skin,[0,-.021,.184]);
  curve(head,[[-.051,-.106,.166],[0,-.115,.178],[.051,-.106,.166]],.006,rose);
  // Sculpted hair cap and overlapping locks, with a bun for the advisor.
  mesh(head,new T.SphereGeometry(1,20,12,0,Math.PI*2,0,Math.PI*.54),hair,[0,.06,-.028],[0,0,0],[.218,.239,.194]);
  for(let i=0;i<5;i++) ellipsoid(head,[.075,.12,.048],hair,[-.14+i*.065,.16+(i%2)*.02,.1],[0,0,-.5]);
  if(advisor) {ellipsoid(head,[.16,.18,.13],hair,[0,.18,-.19]);ring(head,.11,.012,brass,[0,.21,-.23],[.6,0,0]);}
  batch(head);
  const pivots=[head];
  for(const [side,label] of [[-1,'Left'],[1,'Right']]) {
    const arm=group(torso,`${label}Arm`,[side*.29,.34,0]);
    const upper=group(arm,'Sleeve');
    rod(upper,[0,0,0],[side*.045,-.28,0],.09,shirt,.108);batch(upper);
    const forearm=group(arm,`${label}Forearm`,[side*.045,-.28,0]);
    rod(forearm,[0,0,0],[0,-.23,0],.047,skin,.062);
    ellipsoid(forearm,[.053,.085,.028],skin,[0,-.275,.009]);
    for(let f=0;f<4;f++) rod(forearm,[-.033+f*.022,-.3,.017],[-.033+f*.022,-.354,.019],.009,skin);
    ellipsoid(forearm,[.021,.041,.02],skin,[side*.055,-.263,.015],[0,0,side*.35]);
    if(advisor) ring(forearm,.052,.01,brass,[0,-.18,0]);
    batch(forearm);pivots.push(arm,forearm);
    const leg=group(root,`${label}Leg`,[side*.13,.86,0]);
    const thigh=group(leg,'Trouser_thigh');
    rod(thigh,[0,0,0],[0,-.42,0],.085,advisor?rose:midnight,.105);batch(thigh);
    const knee=group(leg,`${label}Knee`,[0,-.42,0]);
    ellipsoid(knee,[.085,.085,.085],advisor?rose:midnight,[0,0,0]);
    rod(knee,[0,0,0],[0,-.31,0],.075,advisor?rose:midnight,.085);
    ellipsoid(knee,[.095,.07,.175],dark,[0,-.37,.055]);batch(knee);pivots.push(leg,knee);
  }
  // Node-rig animation: rigid articulated meshes, no runtime generated body parts.
  const times=[0,.5,1,1.5,2];
  function clip(name,poses) {
    const tracks=[];
    for(const [node,values] of Object.entries(poses)) {
      const quats=values.flatMap(v=>new T.Quaternion().setFromEuler(new T.Euler(...v)).toArray());
      tracks.push(new T.QuaternionKeyframeTrack(`${node}.quaternion`,times,quats));
    }
    return new T.AnimationClip(name,2,tracks);
  }
  const repeat=v=>Array.from({length:5},()=>v);
  const idle={Head:[[0,-.035,0],[.012,0,0],[0,.035,0],[.012,0,0],[0,-.035,0]]};
  const seated={LeftLeg:repeat([-1,0,0]),RightLeg:repeat([-1,0,0]),LeftKnee:repeat([1,0,0]),RightKnee:repeat([1,0,0]),LeftArm:repeat([-.35,0,.1]),RightArm:repeat([-.35,0,-.1]),LeftForearm:repeat([-1,0,0]),RightForearm:repeat([-1,0,0])};
  const clips=advisor?[
    clip('idle',{...seated,...idle}),
    clip('seated',seated),
    clip('greeting',{...seated,Head:[[0,0,0],[.12,0,0],[.2,0,0],[.12,0,0],[0,0,0]],RightArm:[[0,0,-.1],[-.6,0,-.7],[-.7,0,-.85],[-.6,0,-.7],[0,0,-.1]]}),
    clip('listening',{...seated,Head:[[.03,0,0],[.13,-.04,0],[.03,0,0],[.13,.04,0],[.03,0,0]]}),
    clip('shuffling',{...seated,LeftArm:[[-.7,0,-.2],[-.8,0,-.4],[-.7,0,-.2],[-.8,0,-.4],[-.7,0,-.2]],RightArm:[[-.8,0,.4],[-.7,0,.2],[-.8,0,.4],[-.7,0,.2],[-.8,0,.4]]}),
    clip('dealing',{...seated,RightArm:[[-.3,0,0],[-1.1,.1,-.2],[-.3,0,0],[-1.1,-.1,-.2],[-.3,0,0]]}),
    clip('reveal',{...seated,LeftArm:[[-.35,0,.1],[-.9,0,.35],[-.9,0,.35],[-.9,0,.35],[-.35,0,.1]]}),
    clip('result',{...seated,...idle,RightForearm:[[-1,0,0],[-1.3,.25,0],[-1,0,0],[-1.3,-.25,0],[-1,0,0]]}),
  ]:[clip('idle',idle),clip('walk',{LeftLeg:[[-.5,0,0],[0,0,0],[.5,0,0],[0,0,0],[-.5,0,0]],RightLeg:[[.5,0,0],[0,0,0],[-.5,0,0],[0,0,0],[.5,0,0]],LeftArm:[[.4,0,0],[0,0,0],[-.4,0,0],[0,0,0],[.4,0,0]],RightArm:[[-.4,0,0],[0,0,0],[.4,0,0],[0,0,0],[-.4,0,0]]})];
  return {root,clips};
}

function card() {
  const root=group(null,'Anonymous_tarot_back');
  box(root,[.22,.36,.014],cream,[0,0,0],0);
  mesh(root,new T.PlaneGeometry(.202,.34),midnight,[0,0,.008]);
  for(const x of [-.087,.087]) mesh(root,new T.PlaneGeometry(.004,.3),brass,[x,0,.009]);
  for(const y of [-.15,.15]) mesh(root,new T.PlaneGeometry(.174,.004),brass,[0,y,.009]);
  mesh(root,new T.RingGeometry(.041,.047,24),brass,[0,0,.009]);
  for(let i=0;i<8;i++){const a=i*Math.PI/4;mesh(root,new T.PlaneGeometry(.015,.004),brass,[Math.cos(a)*.059,Math.sin(a)*.059,.009],[0,0,a]);}
  batch(root);return {root,clips:[]};
}

const register=[];
for(const [filename,make] of [['shop',environment],['advisor',()=>character(true)],['visitor',()=>character(false)],['tarot-back',card]]) {
  const {root,clips}=make();
  root.userData={source:'scripts/build-shop-models.mjs',provenance:'Original geometry authored for this repository',revision:1};
  const data=await new GLTFExporter().parseAsync(root,{binary:true,animations:clips,onlyVisible:true});
  await writeFile(`${output}/${filename}.glb`,Buffer.from(data));
  let triangles=0,drawCalls=0;
  root.traverse(o=>{if(o.isMesh){triangles+=(o.geometry.index?.count??o.geometry.attributes.position.count)/3;drawCalls++;}});
  register.push({file:`${filename}.glb`,bytes:data.byteLength,triangles,drawCalls,animations:clips.map(c=>c.name)});
  console.log(`${filename}: ${(data.byteLength/1024).toFixed(0)} KB, ${triangles} triangles, ${drawCalls} meshes, ${clips.length} clips`);
}
await writeFile(`${output}/manifest.json`,JSON.stringify({revision:1,source:'scripts/build-shop-models.mjs',assets:register},null,2)+'\n');
