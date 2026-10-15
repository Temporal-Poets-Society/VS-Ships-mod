// Execute the actual standalone preview script with a small DOM/canvas adapter.
// This is a control/syntax smoke check; PNG review covers the actual appearance.
const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert');
const html=fs.readFileSync(path.join(__dirname,'../docs/prototype/two-bay.html'),'utf8');
const entry=fs.readFileSync(path.join(__dirname,'../tools/gridship/viewer.html'),'utf8');
assert(!entry.includes('__BLUEPRINT_DATA__'),'Entry page must not be an unfilled template');
assert(entry.includes('../../docs/prototype/two-bay.html'),'Entry points to the compiled preview');
assert(!html.includes('__BLUEPRINT_DATA__'),'Compiled preview has its ship data');
const script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
let draws=0;
const context=new Proxy({}, {get(t,key){if(key in t)return t[key];return (...args)=>{for(const v of args)if(typeof v==='number')assert(Number.isFinite(v),`Non-finite canvas argument to ${key}`);draws++;};},set(t,key,value){t[key]=value;return true;}});
function element(){return {value:'',checked:false,textContent:'',className:'',children:[],append(...items){this.children.push(...items);},replaceChildren(){this.children=[];},getContext(){return context;},clientWidth:1000,clientHeight:570,setPointerCapture(){},getBoundingClientRect(){return {left:0,top:0};},click(){if(this.onclick)this.onclick();}};}
const elements=Object.fromEntries(ids.map(id=>[id,element()]));
elements.view.value='cutaway';elements.texture.value='wood';elements['rib-width'].value='slim';elements.ghosts.checked=true;
const window={};
vm.runInNewContext(script,{window,document:{getElementById(id){assert(id in elements,`Missing element ${id}`);return elements[id];},createElement:element},
 devicePixelRatio:1,ResizeObserver:class{observe(){}},setTimeout,URL,Blob,console},{timeout:30000});
const p=window.hansaPreview;
assert(p&&draws>0,'Preview initializes and draws');
assert(p.state.changes['cell:6:6:-2'].variant==='hatch','Default hatch exists');
const originalIds=p.data.cells.map(c=>c.id).join('|');
elements['rib-width'].value='full';elements['rib-width'].onchange();
assert.strictEqual(p.data.cells.map(c=>c.id).join('|'),originalIds,'Visible width preserves IDs');
const rib=p.data.cells.find(c=>c.role==='rib').id;p.select(rib);
elements.destroy.click();assert(!p.solid(rib));assert(elements.summary.textContent.includes('1 missing required'));
elements.plank.click();assert(p.solid(rib));assert(elements.summary.textContent.includes('0 missing required'));
const hull=p.data.cells.find(c=>c.allowed.includes('window')).id;p.select(hull);
elements.window.click();assert(p.solid(hull));assert(p.state.changes[hull].variant==='window');
elements.port.click();elements.toggle.click();assert(!p.solid(hull));elements.toggle.click();assert(p.solid(hull));
p.select('cell:6:6:-2');elements.toggle.click();assert(!p.solid('cell:6:6:-2'));
elements['move-stair'].click();assert.strictEqual(p.state.stairX,6);
assert([1,2,3].every(k=>p.solid(`cell:2:6:${k}`)&&!p.solid(`cell:6:6:${k}`)));
elements['blocked-stair'].click();assert.strictEqual(p.state.stairX,6);assert(elements.message.textContent.includes('Rejected'));
elements.view.value='section';elements.view.onchange();
elements.reset.click();assert.strictEqual(p.state.stairX,2);assert(p.solid('cell:6:6:-2'));
console.log(`Preview controls passed; ${draws} finite canvas operations checked.`);
