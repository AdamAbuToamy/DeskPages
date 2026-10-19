(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const root = $('desktop');
  const folder = '<svg viewBox="0 0 40 38" aria-hidden="true"><path fill="#dfa92a" d="M3 7h13l4 4h17v23H3z"/><path fill="#ffdc74" d="M3 12h34v22H3z"/><path fill="#f5c84c" d="M3 15h34v19H3z"/></svg>';
  const documentIcon = (color, glyph) => `<svg viewBox="0 0 40 38" aria-hidden="true"><path fill="#f8fcff" stroke="#b7cee7" d="M9 2h16l8 8v26H9z"/><path fill="#d7e9f8" d="M25 2v9h8"/><rect x="5" y="18" width="23" height="14" rx="2" fill="${color}"/><text x="16.5" y="28" fill="white" text-anchor="middle" font-size="9" font-family="Segoe UI,sans-serif">${glyph}</text></svg>`;
  let pages, files, current, chosenPage, selected, dragged, edgeTimer, hudTimer, token = 0, replaying = false;
  const basePath = 'C:\\Users\\Demo\\Desktop\\';
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const initialFiles = () => [
    { id:'readme',name:'Read me.txt',page:1,pinned:true,icon:documentIcon('#527bb3','TXT') },
    { id:'projects',name:'Projects',page:1,icon:folder },
    { id:'notes',name:'Build notes.txt',page:1,icon:documentIcon('#527bb3','TXT') },
    { id:'photo',name:'Reference.png',page:1,icon:documentIcon('#4b9d87','IMG') },
    { id:'parts',name:'Parts list.pdf',page:1,icon:documentIcon('#c94a4a','PDF') },
    { id:'code',name:'Code editor',page:1,shortcut:true,icon:documentIcon('#007acc','&lt;/&gt;') },
    { id:'work',name:'Work files',page:2,icon:folder },
    { id:'brief',name:'Project brief.pdf',page:2,icon:documentIcon('#c94a4a','PDF') },
    { id:'course',name:'Course notes',page:3,icon:folder },
    { id:'exercise',name:'Exercise.txt',page:3,icon:documentIcon('#527bb3','TXT') },
  ];
  const visible = page => files.filter(file => file.pinned || file.page === page);
  const say = (text, file) => {
    $('demo-action').textContent = text;
    $('path-label').textContent = file ? `${basePath}${file.name} — same original path` : `Example files stay in ${basePath}`;
  };
  function showHud() {
    clearTimeout(hudTimer);
    $('hud-number').textContent = String(pages.findIndex(page => page.id === current) + 1);
    $('hud').classList.toggle('visible', $('number-enabled').checked);
    hudTimer = setTimeout(() => $('hud').classList.remove('visible'), 1100);
  }
  function renderIcons() {
    const layer = document.createElement('div'); layer.className = 'icon-layer';
    visible(current).forEach(file => {
      const item = document.createElement('button'); item.type = 'button'; item.draggable = true; item.className = `file-icon${selected === file.id ? ' selected' : ''}`; item.dataset.file = file.id;
      item.setAttribute('aria-label', `${file.name}${file.pinned ? ', visible on every page' : ''}`);
      item.innerHTML = file.icon;
      const label = document.createElement('span'); label.textContent = file.name; item.append(label);
      if (file.shortcut) { const marker = document.createElement('span'); marker.className = 'shortcut'; marker.textContent = '↗'; item.append(marker); }
      if (file.pinned) { const marker = document.createElement('span'); marker.className = 'pinned'; marker.textContent = '◆'; marker.title = 'Shown on every page'; item.append(marker); }
      item.addEventListener('click', () => { selected = file.id; layer.querySelectorAll('.file-icon').forEach(icon => icon.classList.toggle('selected',icon === item)); say(`${file.name}${file.pinned ? ' is shown on every page.' : ' selected. Drag it to an outer edge.'}`,file); });
      item.addEventListener('dragstart', event => {
        if (!$('drag-enabled').checked || replaying) { event.preventDefault(); return; }
        selected = dragged = file.id; event.dataTransfer.setData('text/plain',file.id); event.dataTransfer.effectAllowed = 'move'; root.classList.add('dragging'); say('Hold the file at an outer edge. Drop it after the page changes.',file);
      });
      item.addEventListener('dragend', finishDrag);
      layer.append(item);
    });
    return layer;
  }
  function renderPanels() {
    const page = pages.find(item => item.id === current);
    $('page-label').textContent = `${page.name} · Page ${pages.indexOf(page)+1}`;
    $('page-list').replaceChildren(); $('pagination').replaceChildren();
    pages.forEach((item,index) => {
      const row = document.createElement('button'); row.type = 'button'; row.className = `page-row${item.id === chosenPage ? ' active' : ''}`; row.setAttribute('aria-label',`Select ${item.name} desktop page`);
      const badge = document.createElement('span'); badge.className = 'badge'; badge.textContent = String(index+1);
      const text = document.createElement('span'); const title = document.createElement('strong'); title.textContent = item.name;
      const note = document.createElement('small'); note.textContent = item.id === current ? 'Current page' : `${visible(item.id).length} items`; text.append(title,note);row.append(badge,text);
      row.addEventListener('click',()=>{ chosenPage = item.id; renderPanels(); });
      row.addEventListener('dblclick',()=>openPage(item.id)); $('page-list').append(row);
      const dot = document.createElement('button'); dot.type = 'button'; dot.textContent = String(index+1);dot.setAttribute('aria-label',`Show ${item.name} desktop`);dot.setAttribute('aria-pressed',String(item.id === current));dot.addEventListener('click',()=>switchPage(item.id));$('pagination').append(dot);
    });
  }
  function switchPage(id, reason = '') {
    if (id === current || !pages.some(page=>page.id === id)) return;
    const previous = current;
    const direction = pages.findIndex(page=>page.id === id) > pages.findIndex(page=>page.id === previous) ? 1 : -1;
    current = chosenPage = id;
    const old = $('icon-stage').lastElementChild;
    const layer = renderIcons(); $('icon-stage').append(layer);
    const duration = !reduced && $('animation-enabled').checked ? 420 : 0;
    if (old) {
      const leave = old.animate([{transform:'translateX(0)'},{transform:`translateX(${-100*direction}%)`}],{duration,easing:'cubic-bezier(.2,.7,.2,1)',fill:'forwards'});
      leave.onfinish=()=>{if(!dragged)old.remove();else old.dataset.retired='true';};
    }
    layer.animate([{transform:`translateX(${100*direction}%)`},{transform:'translateX(0)'}],{duration,easing:'cubic-bezier(.2,.7,.2,1)'});
    renderPanels();showHud();say(reason || `${pages.find(page=>page.id===id).name}: different icons, same wallpaper, taskbar and open windows.`);
  }
  function step(direction) { const index=pages.findIndex(page=>page.id===current);switchPage(pages[(index+direction+pages.length)%pages.length].id); }
  function openPage(id) { switchPage(id);$('page-panel').hidden=true; }
  function finishDrag() {
    clearTimeout(edgeTimer); dragged=null; root.classList.remove('dragging');root.querySelectorAll('.edge-zone').forEach(edge=>edge.classList.remove('armed'));root.querySelectorAll('[data-retired]').forEach(layer=>layer.remove());
  }
  function dropFile(event) {
    if (!dragged) return; event.preventDefault();
    const file=files.find(item=>item.id===dragged); if(!file)return finishDrag();
    if(file.pinned) {say('This file is pinned on every page; its original path is unchanged.',file);}
    else {file.page=current; say(`Moved ${file.name} to ${pages.find(page=>page.id===current).name}. Its path did not change.`,file);}
    selected=file.id;finishDrag();$('icon-stage').replaceChildren(renderIcons());renderPanels();
  }
  root.querySelectorAll('.edge-zone').forEach(edge=>{
    edge.addEventListener('dragover',event=>{
      if(!dragged || !$('drag-enabled').checked)return;event.preventDefault();event.dataTransfer.dropEffect='move';
      if(edge.classList.contains('armed'))return;edge.classList.add('armed');edgeTimer=setTimeout(()=>{step(Number(edge.dataset.edge));},320);
    });
    edge.addEventListener('dragleave',()=>{clearTimeout(edgeTimer);edge.classList.remove('armed');});edge.addEventListener('drop',dropFile);
  });
  $('icon-stage').addEventListener('dragover',event=>{if(dragged)event.preventDefault();});$('icon-stage').addEventListener('drop',dropFile);
  root.addEventListener('keydown',event=>{if(replaying || event.target.closest('button,input,select'))return;if(event.key==='ArrowRight'||event.key==='ArrowLeft'){event.preventDefault();step(event.key==='ArrowRight'?1:-1);}});
  let start;
  root.addEventListener('pointerdown',event=>{if(event.pointerType==='touch'&&!event.target.closest('.native-panel,button'))start={x:event.clientX,y:event.clientY};});
  root.addEventListener('pointerup',event=>{if(start&&$('swipe-enabled').checked&&!replaying){const dx=event.clientX-start.x,dy=event.clientY-start.y;if(Math.abs(dx)>55&&Math.abs(dx)>Math.abs(dy))step(dx<0?1:-1);}start=null;});
  $('previous').addEventListener('click',()=>{if(!replaying)step(-1);});$('next').addEventListener('click',()=>{if(!replaying)step(1);});
  $('open-page').addEventListener('click',()=>openPage(chosenPage));
  $('add-page').addEventListener('click',()=>{const id=Math.max(...pages.map(page=>page.id))+1;pages.push({id,name:'New page'});chosenPage=id;renderPanels();say('Added New page. It starts empty, just like in DeskPages.');});
  $('tray-button').addEventListener('click',()=>{$('settings-panel').hidden=true;$('page-panel').hidden=!$('page-panel').hidden;});$('close-panel').addEventListener('click',()=>{$('page-panel').hidden=true;});
  $('open-settings').addEventListener('click',()=>{$('page-panel').hidden=true;$('settings-panel').hidden=false;});$('close-settings').addEventListener('click',()=>{$('settings-panel').hidden=true;});
  const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  function reset() {token++;replaying=false;$('replay').disabled=false;$('replay').textContent='▶ Watch file move';finishDrag();pages=[{id:1,name:'Main'},{id:2,name:'Work'},{id:3,name:'Study'}];files=initialFiles();current=chosenPage=1;selected=null;$('demo-cursor').hidden=true;$('settings-panel').hidden=true;$('page-panel').hidden=false;$('icon-stage').replaceChildren(renderIcons());renderPanels();say('Open a page from the panel, or try the arrows.');}
  $('reset-demo').addEventListener('click',reset);
  $('replay').addEventListener('click',async()=>{
    reset();const run=token;replaying=true;$('replay').disabled=true;$('replay').textContent='Playing…';$('page-panel').hidden=true;
    const file=files.find(item=>item.id==='notes');selected=file.id;$('icon-stage').replaceChildren(renderIcons());say('1. Pick up Build notes.txt on Main.',file);
    const item=root.querySelector('[data-file="notes"]');const area=root.getBoundingClientRect(),rect=item.getBoundingClientRect();const cursor=$('demo-cursor');cursor.style.left=`${rect.left-area.left+20}px`;cursor.style.top=`${rect.top-area.top+15}px`;cursor.hidden=false;
    await pause(900);if(run!==token)return;cursor.style.left=`${root.clientWidth-30}px`;cursor.style.top='150px';say('2. Hold it at the outer right edge.',file);
    await pause(reduced?200:1050);if(run!==token)return;const edge=root.querySelector('.edge-right');edge.classList.add('armed');await pause(340);if(run!==token)return;switchPage(2,'3. Work opens. Keep holding the file until you drop it.');
    await pause(650);if(run!==token)return;cursor.style.left='70px';cursor.style.top='220px';await pause(reduced?200:1050);if(run!==token)return;file.page=2;selected=file.id;$('icon-stage').replaceChildren(renderIcons());renderPanels();cursor.hidden=true;edge.classList.remove('armed');say('4. Dropped on Work. Same file, same original path.',file);replaying=false;$('replay').disabled=false;$('replay').textContent='▶ Watch file move';
  });
  document.querySelectorAll('[data-screenshot-language]').forEach(button=>button.addEventListener('click',()=>{const lang=button.dataset.screenshotLanguage;$('pages-screenshot').src=`assets/ui/pages-${lang}.png`;$('settings-screenshot').src=`assets/ui/settings-${lang}.png`;document.querySelectorAll('[data-screenshot-language]').forEach(other=>other.setAttribute('aria-pressed',String(other===button)));}));
  reset();
})();
