/* Stage 2 design study. Synthetic data only; no filesystem, registry or network APIs. */
const $ = s => document.querySelector(s);
const params = new URLSearchParams(location.search);
const osTheme = matchMedia('(prefers-color-scheme: dark)');
const say = text => { $('#feedback').textContent = text; };
function fitStudy(){const top=$('#stage').getBoundingClientRect().top;$('#stage').style.height=`${Math.max(260,(innerHeight-top-38)/Number($('#scale').value))}px`;}
window.addEventListener('resize',fitStudy);
const paths = {
  add:'M12 4v16M4 12h16', extract:'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
  test:'m4 12 5 5L20 6', copy:'M8 8h12v13H8zM4 16V3h12', move:'M3 12h17m-5-5 5 5-5 5M4 5v14',
  delete:'M4 6h16M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7',
  info:'M12 11v6m0-10v.1M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
  up:'M12 21V4m-7 7 7-7 7 7', back:'M20 12H4m7-7-7 7 7 7', forward:'M4 12h16m-7-7 7 7-7 7',
  refresh:'M20 9a8 8 0 1 0 0 7m0-13v6h-6', location:'M3 3h18v13H3zM8 21h8m-4-5v5',
  folder:'M2 6V4h8l2 3h10v14H2z', file:'M5 2h9l5 5v15H5zM14 2v6h5',
  archive:'M5 2h14v20H5zM12 2v3m0 2v2m0 2v2m0 2v5m-2-3h4',
  more:'M5 12h.1m6.9 0h.1m6.9 0h.1', close:'m6 6 12 12M6 18 18 6',
  view:'M3 4h18v16H3zM3 9h18M8 9v11', sort:'M5 4v16m-3-3 3 3 3-3M12 5h9m-9 6h6m-6 6h3',
  chevron:'m8 5 7 7-7 7'
};
function icon(name) { return `<svg aria-hidden="true" class="${name}" viewBox="0 0 24 24"><path d="${paths[name] || paths.file}"/></svg>`; }
function btn(label, name, action, extra='') { return `<button type="button" data-action="${action}" title="${label}" ${extra}>${icon(name)}<span class="toolbar-label">${label}</span></button>`; }
function field(label, control, id) { return `<div class="field"><label for="${id}">${label}</label>${control.replace('ID',id)}</div>`; }
function input(label,id,value='',type='text') { return field(label,`<input id="ID" type="${type}" value="${value}" ${type==='password'?'autocomplete="off"':''}>`,id); }
function select(label,id,values) { return field(label,`<select id="ID">${values.map(v=>`<option>${v}</option>`).join('')}</select>`,id); }
function check(label,id,checked=false) { return `<label class="check"><input type="checkbox" id="${id}" ${checked?'checked':''}>${label}</label>`; }
function browse(label,id,value) { return field(label,`<div class="input-browse"><input id="ID" value="${value}" spellcheck="false"><button type="button" data-action="browse" aria-label="Обзор — ${label}">…</button></div>`,id); }
const files = [
  ['..','','','На уровень вверх','up'],['Документы','','15.11.2026 18:42','Папка','folder'],
  ['Исходники','','15.11.2026 16:15','Папка','folder'],['Фотографии','','14.11.2026 10:08','Папка','folder'],
  ['Arkvello-26.06.7z','18,4 МБ','16.11.2026 09:24','Архив 7z','archive'],
  ['Документы.zip','1,2 МБ','15.11.2026 22:10','Архив ZIP','archive'],
  ['README.md','3,8 КБ','15.11.2026 18:40','Markdown','file'],
  ['Инструкция.pdf','526 КБ','15.11.2026 14:32','Документ PDF','file'],
  ['Заметки.txt','2,1 КБ','14.11.2026 11:07','Текстовый документ','file'],
  ['Контрольные суммы.sha256','412 байт','14.11.2026 11:06','SHA-256','file'],
  ['Сборка Windows.cmd','1,3 КБ','13.11.2026 10:08','Сценарий Windows','file']
];
const menus = {
  'Файл':[['Открыть','Enter','open'],['Открыть внутри','Ctrl+PgDn','inside'],['Открыть внутри *','','inside-one'],['Открыть внутри #','','inside-parser'],['Открыть снаружи','Shift+Enter','outside'],null,['Просмотр','F3','viewer'],['Редактор','F4','editor'],['Сравнить…','','diff'],null,['Переименовать','F2','rename'],['Копировать в…','F5','copy'],['Переместить в…','F6','move'],['Удалить','Del','delete'],['Создать папку','F7','new-folder'],['Создать файл…','','new-file'],['Комментарий…','','comment'],['Ссылка…','','link'],['Альтернативные потоки…','','streams'],null,['Разбить файл…','','split'],['Объединить файлы…','','combine'],['CRC / SHA','','hash'],['Свойства','Alt+Enter','info'],null,['Контроль версий (при настройке)','','version-control'],['Выход','Alt+F4','exit']],
  'Правка':[['Выделить всё','Ctrl+A','select-all'],['Снять выделение','','deselect-all'],['Инвертировать выделение','','invert'],['Выделить по маске…','+','select-mask'],['Снять по маске…','−','deselect-mask'],['Выделить по типу','','select-type'],['Снять по типу','','deselect-type'],null,['Вырезать','Ctrl+X','cut'],['Копировать','Ctrl+C','clipboard-copy'],['Вставить','Ctrl+V','paste']],
  'Вид':[['Крупные значки','','mode-large'],['Мелкие значки','','mode-small'],['Список','','mode-list'],['Таблица','','mode-details'],null,['Сортировка и столбцы…','','columns'],['Время: точность / UTC…','','time'],['Плоский вид','','flat'],['Две панели','F9','two'],['Панели инструментов…','','toolbars'],null,['Корень','','root'],['На уровень вверх','Backspace','parent'],['История папок','Alt+F12','history'],['Обновить','Ctrl+R','refresh'],['Автообновление','','auto-refresh']],
  'Избранное':[['Добавить в избранное…','','favorite-add'],...Array.from({length:10},(_,n)=>[`Закладка ${n}`,`Alt+${n}`,'bookmark'])],
  'Сервис':[['Добавить в архив…','','add'],['Распаковать…','','extract'],['Проверить архив','','test'],null,['Тест производительности','','benchmark'],['Тест производительности: полный','','benchmark-total'],['Временные файлы…','','temp'],['Параметры…','','settings']],
  'Справка':[['Содержание','F1','help'],['О программе Arkvello','','about']]
};
let activePanel=0, opener=null, currentSurface='main', parentSurface='progress';
let paused=false;
let menuOpener=null;
function theme() {
  const t=$('#theme').value; document.documentElement.dataset.theme=t==='system'?(osTheme.matches?'dark':'light'):t;
  if(t!=='contrast') {
    const dark=document.documentElement.dataset.theme==='dark';
    const hex=$('#accent').value, rgb=[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16));
    const mix=(base,a)=>`rgb(${rgb.map(v=>Math.round(v*a+base*(1-a))).join(',')})`;
    document.documentElement.style.setProperty('--nav',mix(dark?32:243,dark?.14:.05));
    document.documentElement.style.setProperty('--address',mix(dark?40:232,dark?.12:.05));
  } else { document.documentElement.style.removeProperty('--nav'); document.documentElement.style.removeProperty('--address'); }
}
function updateStatus(panel) {
  const chosen=[...panel.querySelectorAll('tr[aria-selected=true]')].filter(r=>r.dataset.index!=='0');
  $('#selected').textContent=chosen.length===1?`Выбрано: 1 · ${chosen[0].dataset.size || 'папка'}`:`Выбрано: ${chosen.length}`;
  $('#total').textContent=$('#state').value==='normal'||$('#state').value==='long'?'Папок: 3 · Файлов: 7':$('#state').value==='empty'?'Объектов: 0':'Список недоступен';
}
function makePanel(n) {
  const panel=document.createElement('section'); panel.className=`panel${n?' inactive':''}`; panel.setAttribute('aria-label',n?'Правая панель':'Левая панель');
  panel.innerHTML=`<div class="pathbar"><button class="icon-only" type="button" data-action="back" aria-label="Назад">${icon('back')}</button><button class="icon-only" type="button" data-action="forward" aria-label="Вперёд">${icon('forward')}</button><button class="icon-only" type="button" data-action="parent" aria-label="На уровень вверх">${icon('up')}</button><button class="icon-only" type="button" data-action="refresh" aria-label="Обновить">${icon('refresh')}</button><div class="address">${icon('location')}<input aria-label="Путь ${n?'правой':'левой'} панели" value="${n?'D:\\Резервные копии':'C:\\Users\\User\\Downloads'}" spellcheck="false" list="history-${n}"><button class="icon-only" data-action="history" type="button" aria-label="История папок">${icon('chevron')}</button></div><datalist id="history-${n}"><option value="C:\\Users\\User\\Downloads"><option value="D:\\Резервные копии"></datalist></div><div class="commandbar" aria-label="Команды ${n?'правой':'левой'} панели">${btn('Добавить','add','add')}${btn('Распаковать','extract','extract')}${btn('Проверить','test','test')}<span class="separator"></span>${btn('Копировать','copy','copy')}${btn('Переместить','move','move')}${btn('Удалить','delete','delete')}<span class="spacer"></span>${btn('Вид','view','view-menu')}${btn('Ещё','more','more-menu')}</div><div class="file-scroll"><table class="file-table" role="grid" aria-multiselectable="true" aria-label="${n?'Правая':'Левая'} файловая панель"><colgroup><col><col><col><col></colgroup><thead><tr><th aria-sort="ascending"><button data-action="sort-name" type="button">Имя</button></th><th><button data-action="sort-date" type="button">Дата изменения</button></th><th><button data-action="sort-type" type="button">Тип</button></th><th class="number"><button data-action="sort-size" type="button">Размер</button></th></tr></thead><tbody></tbody></table></div>`;
  const state=$('#state').value;
  if(['loading','empty','error'].includes(state)) {
    panel.querySelector('.file-scroll').innerHTML=`<div class="empty-state" role="status">${{loading:'Чтение содержимого…',empty:'В этой папке нет файлов.',error:'Нет доступа к папке. Проверьте путь и права доступа.'}[state]}${state==='error'?'<br><button data-action="retry" type="button">Повторить</button>':''}</div>`;
  } else {
    let anchor=4;
    files.forEach((f,i)=>{
      const row=document.createElement('tr'); row.dataset.index=i; row.dataset.size=f[1]; row.tabIndex=i===4?0:-1;
      row.setAttribute('aria-selected',String(i===4));
      const name=state==='long'&&i===6?'Спецификация интерфейса Arkvello для Windows — итоговая редакция с замечаниями и дополнительными материалами.md':f[0];
      row.innerHTML=`<td><div class="filename">${icon(f[4])}<span></span></div></td><td>${f[2]}</td><td>${f[3]}</td><td class="number">${f[1]}</td>`;
      row.querySelector('.filename span').textContent=name; row.querySelector('td').title=name;
      function choose(e={}) {
        const rows=[...panel.querySelectorAll('tbody tr')];
        if($('#alternative').checked) {
          if(e.ctrlKey||e.toggle) row.setAttribute('aria-selected',String(row.getAttribute('aria-selected')!=='true'));
        } else if(e.shiftKey) {
          rows.forEach((r,j)=>r.setAttribute('aria-selected',String(j>=Math.min(anchor,i)&&j<=Math.max(anchor,i)&&j!==0)));
        } else if(e.ctrlKey) { row.setAttribute('aria-selected',String(row.getAttribute('aria-selected')!=='true')); anchor=i; }
        else { rows.forEach(r=>r.setAttribute('aria-selected',String(r===row&&i!==0))); anchor=i; }
        rows.forEach(r=>r.tabIndex=r===row?0:-1); row.focus(); updateStatus(panel);
      }
      row.addEventListener('click',choose);
      row.addEventListener('dblclick',()=>action(i===4||i===5?'inside':'open',row));
      row.addEventListener('contextmenu',e=>{e.preventDefault();choose(e);showMenu(menus['Файл'],row,e.clientX,e.clientY);});
      row.addEventListener('keydown',e=>{
        const rows=[...panel.querySelectorAll('tbody tr')];
        if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)) {
          e.preventDefault(); const j=e.key==='Home'?0:e.key==='End'?rows.length-1:Math.max(0,Math.min(rows.length-1,i+(e.key==='ArrowDown'?1:-1)));
          if($('#alternative').checked || e.ctrlKey) { rows.forEach(r=>r.tabIndex=-1);rows[j].tabIndex=0;rows[j].focus(); }
          else rows[j].dispatchEvent(new MouseEvent('click',{shiftKey:e.shiftKey}));
        } else if(e.key==='Insert'||e.key===' ') {e.preventDefault();choose({toggle:true,ctrlKey:true});}
        else if(e.key==='Enter') {e.preventDefault();action('open',row);}
      });
      panel.querySelector('tbody').append(row);
    });
  }
  panel.addEventListener('focusin',()=>{activePanel=n;document.querySelectorAll('.panel').forEach(p=>p.classList.toggle('inactive',p!==panel));updateStatus(panel);});
  panel.querySelector('.address input').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();say('Путь в макете изменён. Переход по реальной файловой системе будет реализован в приложении.');}if(e.key==='Escape'){e.target.blur();panel.querySelector('tr[tabindex="0"]')?.focus();}});
  return panel;
}
function renderPanels() {$('#panels').replaceChildren(makePanel(0));if($('#two').checked)$('#panels').append(makePanel(1));$('#panels').classList.toggle('two',$('#two').checked);activePanel=0;updateStatus($('#panels').firstElementChild);}
function showMenu(items,target,x,y) {
  menuOpener=target;
  const p=$('#popup');p.replaceChildren();
  items.forEach(item=>{if(!item){p.append(document.createElement('hr'));return;}const b=document.createElement('button');b.className='popup-row';b.type='button';b.innerHTML='<span></span><small></small>';b.firstElementChild.textContent=item[0];b.lastElementChild.textContent=item[1];b.onclick=()=>{p.hidePopover();action(item[2],target);};p.append(b);});
  p.showPopover();const r=target.getBoundingClientRect();p.style.left=`${Math.max(8,Math.min(x??r.left,innerWidth-p.offsetWidth-8))}px`;p.style.top=`${Math.max(8,Math.min(y??r.bottom,innerHeight-p.offsetHeight-8))}px`;p.querySelector('button')?.focus();
}
function tabbed(tabs) {return `<div class="tabs" role="tablist">${tabs.map((t,i)=>`<button type="button" role="tab" id="tab-${t[0]}" aria-controls="pane-${t[0]}" aria-selected="${!i}" tabindex="${i?-1:0}" data-tab="${t[0]}">${t[1]}</button>`).join('')}</div>${tabs.map((t,i)=>`<section role="tabpanel" aria-labelledby="tab-${t[0]}" id="pane-${t[0]}" ${i?'hidden':''}>${t[2]}</section>`).join('')}`;}
function addContent() {
  return browse('Архив','archive-path','C:\\Users\\User\\Downloads\\Документы.7z')+tabbed([
    ['compression','Сжатие',`<div class="columns"><div>${select('Формат архива','format',['7z','zip','tar','wim'])}${select('Уровень сжатия','level',['Обычный','Без сжатия','Скоростной','Быстрый','Максимальный','Ультра'])}${select('Метод сжатия','method',['LZMA2','LZMA','PPMd','BZip2'])}${select('Размер словаря','dictionary',['64 МБ','16 МБ','32 МБ','128 МБ'])}${select('Размер слова / порядок','word',['32','64','128'])}</div><div>${select('Размер непрерывного блока','solid',['Непрерывный','Без непрерывного блока','1 ГБ','4 ГБ'])}${select('Число потоков CPU','threads',['Автоматически (16)','1','2','4','8','16'])}${select('Ограничение памяти','ram',['80%','50%','4 ГБ','8 ГБ'])}${input('Размер томов, байт','volumes','','text')}<p class="hint">Пустое значение — один архив. Допускаются размеры, например 100M.</p></div></div><div class="summary"><span>Память при сжатии: 704 МБ</span><span>При распаковке: 65 МБ</span><span>Доступно потоков: 16</span></div>`],
    ['update','Обновление',`${select('Режим обновления','update',['Добавить и заменить','Обновить и добавить','Обновить существующие','Синхронизировать'])}${select('Режим путей','path-mode',['Относительные пути','Полные пути','Абсолютные пути'])}${check('Создать SFX-архив','sfx')}${check('Сжимать открытые для записи файлы','shared')}${check('Удалять исходные файлы после сжатия','delete-source')}<p class="hint">Доступность параметров и возможность изменения архива зависят от формата. Существующие файлы не удаляются до подтверждённого результата операции.</p>`],
    ['encryption','Шифрование',`<div class="columns"><div>${input('Пароль','password-new','','password')}${input('Повторите пароль','password-repeat','','password')}${check('Показать пароль','show-new-password')}</div><div>${select('Метод шифрования','encryption',['AES-256'])}${check('Шифровать имена файлов','encrypt-names')}<p class="hint">Пароль не сохраняется в параметрах приложения. Доступные методы и ограничения определяет формат.</p></div></div><p id="password-error" class="error-text" role="alert" hidden>Пароли не совпадают. Проверьте повторный ввод.</p>`],
    ['metadata','Метаданные',`<h2>Ссылки и NTFS</h2>${check('Сохранять символические ссылки','symlinks')}${check('Сохранять жёсткие ссылки','hardlinks')}${check('Сохранять альтернативные потоки','ads')}${check('Сохранять права доступа','security')}<h2>Временные метки</h2><p class="hint">Флажок слева включает явное переопределение. Без него действует настройка формата; значение справа сохраняется отдельно.</p>${[['precision','Точность времени'],['mtime','Время изменения'],['ctime','Время создания'],['atime','Время доступа'],['ztime','Время архива — по последнему файлу']].map(([id,label])=>`<div class="override"><input type="checkbox" aria-label="Переопределить: ${label}" data-override="${id}"><label>${label}${id==='precision'?`<select id="${id}" disabled><option>По умолчанию</option><option>1 нс</option><option>100 нс</option><option>1 с</option><option>2 с</option></select>`:`<input id="${id}" type="checkbox" ${id==='mtime'?'checked':''} disabled aria-label="Сохранять: ${label}">`}</label></div>`).join('')}${check('Не изменять время доступа исходных файлов','preserve-atime')}`],
    ['advanced','Дополнительно',`${input('Параметры кодека','raw-parameters')}<p class="hint">Поле сохраняет возможность передать исходные параметры 7-Zip. Неизвестные свойства не отбрасываются интерфейсом. Их проверяет выбранный метод.</p><p class="hint">В этом макете списки форматов и значений сокращены. Приложение получит полный набор из служб C++; эти примеры не задают новые ограничения.</p>`]
  ]);
}
function extractContent(){return `${browse('Распаковать в','destination','C:\\Users\\User\\Downloads\\Документы')}${check('Отдельная папка с именем','use-output-name',true)}${input('Имя папки','output-name','Документы')}<div class="columns"><div>${select('Режим путей','extract-path-mode',['Полные пути','Текущие пути','Без путей'])}${select('При совпадении имён','overwrite-policy',['Спрашивать','Заменять без запроса','Пропускать','Переименовать извлекаемый файл','Переименовать существующий файл'])}${check('Убирать повторение корневой папки','eliminate-duplicate',true)}</div><div>${input('Пароль, если нужен','extract-password','','password')}${check('Показать пароль','show-extract-password')}${check('Восстанавливать права доступа','restore-security')}</div></div><p class="hint">Настройка метки зоны Windows находится в «Параметры → Интеграция». При извлечении выделенных элементов сохраняется их точная принадлежность открытому архиву.</p>`;}
function progressContent(){return `<p class="progress-title" id="progress-status">Распаковка файлов · 42%</p><p class="wrap">Документы.7z → C:\\Users\\User\\Downloads\\Документы</p><progress value="42" max="100" aria-label="Выполнено 42 процента"></progress><p class="secondary wrap">Текущий файл: Материалы\\Спецификация интерфейса.pdf</p><dl class="metrics"><div><dt>Обработано</dt><dd>42 из 100 файлов</dd></div><div><dt>Прошло</dt><dd>00:12</dd></div><div><dt>Скорость</dt><dd>48 МБ/с</dd></div><div><dt>Осталось</dt><dd>00:17</dd></div><div><dt>Размер</dt><dd>576 МБ / 1,37 ГБ</dd></div><div><dt>Сжатый размер</dt><dd>188 МБ</dd></div></dl><details><summary>Журнал операции</summary><p class="hint">Условный пример. Файл обрабатывается; сообщений об ошибках пока нет.</p></details><div class="choice-buttons"><button type="button" data-action="overwrite">Пример конфликта</button><button type="button" data-action="password">Запрос пароля</button><button type="button" data-action="memory">Запрос памяти</button><button type="button" data-action="result">Пример результата</button></div>`;}
function overwriteContent(){return `<p class="notice">В папке назначения уже существует файл <strong>Спецификация.pdf</strong>.</p><div class="conflict-grid"><section><h2>Существующий файл</h2><p>526 КБ</p><p class="hint">Изменён 14.11.2026, 10:08</p><p class="secondary wrap">C:\\Users\\User\\Downloads\\Документы\\Спецификация.pdf</p></section><section><h2>Файл из архива</h2><p>548 КБ</p><p class="hint">Изменён 16.11.2026, 09:24</p><p class="secondary wrap">Документы.7z\\Спецификация.pdf</p></section></div><div class="choice-buttons"><button type="button" data-action="replace">Заменить</button><button type="button" data-action="replace-all">Заменить все</button><button type="button" data-action="skip">Пропустить</button><button type="button" data-action="skip-all">Пропустить все</button><button type="button" data-action="auto-rename">Автопереименование</button></div>`;}
function passwordContent(){return `<p class="notice wrap">Для открытия Документы.7z нужен пароль.</p>${input('Пароль','request-password','','password')}${check('Показать пароль','show-request-password')}<p class="error-text" id="request-password-error" role="alert" hidden>Не удалось открыть архив. Возможны неверный пароль или повреждение данных. Проверьте пароль и повторите попытку.</p><p class="hint">Введите любое непустое значение, чтобы вернуться к условной операции. Это не проверка архива.</p><button type="button" data-action="password-error">Показать пример ошибки</button>`;}
function memoryContent(){return `<p class="notice">Для распаковки архива нужно больше памяти, чем разрешено настройкой.</p><dl class="metrics"><div><dt>Запрошено</dt><dd>6 ГБ</dd></div><div><dt>Лимит</dt><dd>4 ГБ</dd></div><div><dt>Доступно системе</dt><dd>8 ГБ</dd></div></dl>${input('Новый лимит для следующих операций, ГБ','memory-limit','6','number')}${check('Повторять выбранное действие в текущей операции','memory-remember')}${check('Сохранить новый лимит в параметрах','memory-save')}<p class="hint">Это отдельные решения: разрешение текущей операции и сохранение настройки. Точная доступность определяется исходным запросом службы.</p><div class="choice-buttons"><button type="button" data-action="memory-allow">Разрешить</button><button type="button" data-action="memory-skip">Пропустить архив</button></div>`;}
function resultContent(outcome='warning'){const cancelled=outcome==='cancel';return `<p class="progress-title">${cancelled?'Операция отменена':'Завершено с предупреждениями'}</p><p class="hint">${cancelled?'Могли остаться уже записанные файлы. Отмена не означает откат всей операции.':'Успешно: 98 файлов. Пропущено: 1. Ошибок: 1. Полного результата нет.'}</p><div class="log" tabindex="0" aria-label="Сообщения операции">${cancelled?'Обработка остановлена по запросу пользователя.':'Пропущено: Материалы\\Черновик.txt — выбран пропуск.<br>Ошибка: Закрытый файл.pdf — отказано в доступе.'}</div><div class="choice-buttons"><button type="button" data-action="copy-result">Копировать сообщения</button><button type="button" data-action="open-output">Открыть папку результата</button></div>`;}
const settingsPages = {
  appearance:['Оформление',()=>`${select('Тема приложения','app-theme',['Системная','Светлая','Тёмная'])}<p class="hint">Акцент берётся из персонализации Windows. Выделение файлов остаётся нейтральным. Контрастные темы системы имеют приоритет.</p>${check('Показывать текст команд','toolbar-text',true)}${check('Крупные кнопки панели инструментов','large-toolbar')}`],
  list:['Список и поведение',()=>`${check('Альтернативное выделение','setting-alternative')}${check('Выделять строку целиком','full-row',true)}${check('Показывать линии сетки','gridlines')}${check('Один щелчок для открытия','single-click')}${check('Системное контекстное меню','system-context',true)}${check('Показывать значки файлов','file-icons',true)}${check('Показывать строку перехода «..»','show-dots',true)}${check('Разрешить большие страницы памяти','large-pages')}${check('Переопределить лимит памяти распаковки','global-memory-override')}${input('Лимит памяти распаковки, ГБ','global-memory','4','number')}<p class="hint">Параметры показывают организацию раздела. Исходные значения по умолчанию и зависимости переносятся из модели C++.</p>`],
  folders:['Рабочая папка',()=>`${select('Рабочая папка для временных файлов','workfolder',['Системная временная папка','Текущая папка','Указанная папка'])}${browse('Папка','workfolder-path','D:\\Temp')}${check('Использовать только для съёмных носителей','removable-only')}`],
  apps:['Внешние программы',()=>`${browse('Программа просмотра','viewer','')}${browse('Редактор','editor','')}${browse('Программа сравнения','diff','')}<p class="hint">Сохраняются шаблоны команд и исходные правила запуска. Условные команды контроля версий остаются доступны при прежних условиях настройки.</p>`],
  integration:['Интеграция',()=>`${check('Интеграция с Проводником','shell-registration')}${check('Интеграция 32-разрядной оболочки (при наличии компонента)','shell32')}${check('Каскадное меню Arkvello','cascade',true)}${check('Значки в контекстном меню','shell-icons',true)}${check('Убирать повторение корневой папки при быстрой распаковке','shell-duplicate',true)}${select('Метка зоны Windows при распаковке','zone',['Исходное поведение','Не распространять','Применять ко всем','Только выбранные типы'])}<details><summary>Команды контекстного меню</summary>${['Открыть / открыть как','Распаковать… / здесь / в папку','Проверить','Добавить… / в 7z / в ZIP','Архивировать и отправить по почте','Вычислить / создать / проверить контрольные суммы'].map((s,i)=>check(s,`shell-command-${i}`,true)).join('')}</details><p class="hint">В макете настройки не применяются. Регистрация, точные политики зоны и расположение команд Windows 11 требуют отдельной реализации и проверки.</p>`],
  associations:['Типы файлов',()=>`<p class="hint">Назначения хранятся отдельно от исходного 7-Zip. Изменение системных приложений по умолчанию выполняется поддерживаемым Windows способом.</p>${['7z','ZIP','RAR','TAR','GZIP','WIM'].map((s,i)=>check(s,`association-${i}`)).join('')}<p class="hint">В приложении — полный перечень форматов, состояния текущего пользователя и системы, выбор всех и снятие выбора.</p>`],
  language:['Язык',()=>`${select('Язык интерфейса','language',['Системный','Русский','English'])}<p class="hint">Тексты, подсказки, сообщения операций и быстрые клавиши переводятся согласованно. Длинные переводы могут переноситься; значения параметров не обрезаются без доступа к полному тексту.</p>`]
};
function settingsContent(){return `<div class="settings"><nav class="settings-nav" aria-label="Разделы параметров">${Object.entries(settingsPages).map(([id,[label]],i)=>`<button type="button" data-settings="${id}" ${i?'':'aria-current="page"'}>${label}</button>`).join('')}</nav><div class="settings-content">${Object.entries(settingsPages).map(([id,[label,body]],i)=>`<section id="settings-${id}" ${i?'hidden':''}><h2>${label}</h2>${body()}</section>`).join('')}</div></div>`;}
function footer(html){$('#sheet-footer').innerHTML=html;}
function show(surface,target,outcome){
  if(surface==='main'){closeSheet();return;}
  if(!$('#sheet').open)opener=target||document.activeElement;
  if(['overwrite','password','memory'].includes(surface))parentSurface=currentSurface==='progress'?'progress':'main';
  currentSurface=surface;$('#surface').value=surface;
  const titles={add:'Добавить в архив',extract:'Распаковать',progress:'Распаковка',overwrite:'Заменить файл?',password:'Пароль архива',memory:'Недостаточно разрешённой памяти',result:'Результат операции',settings:'Параметры'};
  $('#sheet-title').textContent=titles[surface];$('#sheet-context').textContent=surface==='add'?'Документы · 12 файлов · 24,8 МБ (условный пример)':surface==='extract'?'Документы.7z · все файлы (условный пример)':surface==='settings'?'Arkvello для Windows': 'Условный пример состояния операции';
  $('#sheet').style.width=surface==='settings'?'min(860px, calc(100vw / var(--scale) - 32px))':surface==='add'?'min(820px, calc(100vw / var(--scale) - 32px))':'min(680px, calc(100vw / var(--scale) - 32px))';
  $('#sheet-content').innerHTML={add:addContent,extract:extractContent,progress:progressContent,overwrite:overwriteContent,password:passwordContent,memory:memoryContent,result:()=>resultContent(outcome),settings:settingsContent}[surface]();
  const cancel='<button type="button" data-action="cancel-sheet">Отмена</button>';
  const help='<button type="button" class="help" data-action="help">Справка</button>';
  if(surface==='progress'){paused=false;footer('<button type="button" class="help" data-action="background">В фоновый режим</button><button type="button" data-action="pause" id="pause">Пауза</button><button type="button" data-action="cancel-operation">Отменить</button>');}
  else if(['overwrite','memory'].includes(surface))footer('<button type="button" data-action="cancel-operation">Отменить операцию</button>');
  else if(surface==='result')footer('<button class="primary" type="button" data-action="close-result">Закрыть</button>');
  else footer(`${help}<button class="primary" type="submit">${{add:'Добавить',extract:'Распаковать',password:'Продолжить',settings:'Сохранить'}[surface]}</button>${cancel}`);
  if(surface==='memory')$('#memory-limit').disabled=true;
  if(surface==='settings')$('#global-memory').disabled=true;
  if(!$('#sheet').open)$('#sheet').showModal();
  const focus={add:'#archive-path',extract:'#destination',password:'#request-password'}[surface];
  (focus?$(focus):$('#sheet-content button, #sheet-footer button'))?.focus();
}
function closeSheet(){if($('#sheet').open)$('#sheet').close();$('#surface').value='main';currentSurface='main';if(opener?.isConnected)opener.focus();}
function action(id,target){
  if(['add','extract','progress','overwrite','password','memory','result','settings'].includes(id)){show(id,target);return;}
  if(id==='cancel-sheet'){if(currentSurface==='password'){show('result',target,'cancel');}else closeSheet();return;}
  if(id==='close-result'){closeSheet();return;}
  if(id==='cancel-operation'){show('result',target,'cancel');say('Макет отмены: остановка подтверждена условно. В приложении результат появляется только после ответа службы.');return;}
  if(id==='pause'){paused=!paused;$('#pause').textContent=paused?'Продолжить':'Пауза';$('#progress-status').textContent=paused?'Приостановлено · 42%':'Распаковка файлов · 42%';return;}
  if(id==='background'){say('Макет: понижение приоритета без закрытия окна. В приложении — согласованная с C++ службой семантика Background.');return;}
  if(id==='password-error'){$('#request-password-error').hidden=false;$('#request-password').setAttribute('aria-invalid','true');$('#request-password').setAttribute('aria-describedby','request-password-error');$('#request-password').focus();return;}
  if(['replace','replace-all','skip','skip-all','auto-rename','memory-allow','memory-skip'].includes(id)){show(parentSurface==='main'?'progress':parentSurface,target);say(`Решение «${target?.textContent || id}» принято только в макете.`);return;}
  if(id==='two'){$('#two').checked=!$('#two').checked;renderPanels();return;}
  if(id==='view-menu'){showMenu(menus['Вид'],target);return;}
  if(id==='more-menu'){showMenu(menus['Файл'],target);return;}
  if(id==='hash'){showMenu(['Все методы','CRC32','CRC64','XXH64','MD5','SHA1','SHA256','SHA384','SHA512','SHA3-256','BLAKE2sp','Создать файл контрольных сумм…','Проверить файл контрольных сумм…'].map(v=>[v,'','hash-demo']),target);return;}
  if(id==='version-control'){showMenu(['Edit','Commit','Revert','Diff'].map(v=>[v,'','version-demo']),target);return;}
  const panel=$('#panels').children[activePanel];
  if(['select-all','deselect-all','invert'].includes(id)){panel.querySelectorAll('tbody tr').forEach(r=>r.setAttribute('aria-selected',String(r.dataset.index!=='0'&&(id==='select-all'||id==='invert'&&r.getAttribute('aria-selected')!=='true'))));updateStatus(panel);return;}
  if(id==='rename'){const row=panel.querySelector('tbody tr[tabindex="0"]');if(row?.dataset.index!=='0'&&row){const span=row.querySelector('.filename span');const original=span.textContent;const edit=document.createElement('input');edit.value=original;edit.setAttribute('aria-label','Новое имя');span.replaceWith(edit);edit.focus();edit.select();edit.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();span.textContent=e.key==='Enter'&&edit.value?edit.value:original;edit.replaceWith(span);row.focus();say('Имя изменено только в условном списке.');}});}return;}
  if(id==='inside'){panel.querySelector('.address input').value+='\\Документы.7z\\';say('Пример пути внутри архива. Содержимое списка условное; архив не открыт.');return;}
  if(id==='retry'){$('#state').value='normal';renderPanels();return;}
  if(id.startsWith('sort-')){const th=target.closest('th');th.setAttribute('aria-sort',th.getAttribute('aria-sort')==='ascending'?'descending':'ascending');say('Показано переключение направления. Сортировка по данным служб будет реализована в приложении.');return;}
  say(`«${target?.textContent?.trim()||id}»: точка входа предусмотрена. Это макет; действие с файлами, системные диалоги и дополнительные окна здесь не реализованы.`);
}
Object.keys(menus).forEach(label=>{const b=document.createElement('button');b.type='button';b.textContent=label;b.setAttribute('aria-haspopup','true');b.onclick=()=>showMenu(menus[label],b);$('#menubar').append(b);});
document.addEventListener('click',e=>{const a=e.target.closest('[data-action]');if(a)action(a.dataset.action,a);const t=e.target.closest('[data-tab]');if(t){document.querySelectorAll('[role=tab]').forEach(b=>{const selected=b===t;b.setAttribute('aria-selected',selected);b.tabIndex=selected?0:-1;});document.querySelectorAll('[role=tabpanel]').forEach(p=>p.hidden=p.id!==`pane-${t.dataset.tab}`);}const s=e.target.closest('[data-settings]');if(s){document.querySelectorAll('[data-settings]').forEach(b=>{if(b===s)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');});Object.keys(settingsPages).forEach(id=>$(`#settings-${id}`).hidden=id!==s.dataset.settings);}});
document.addEventListener('change',e=>{if(e.target.matches('[data-override]'))$(`#${e.target.dataset.override}`).disabled=!e.target.checked;
  const sets={'show-new-password':['password-new','password-repeat'],'show-extract-password':['extract-password'],'show-request-password':['request-password']};
  if(sets[e.target.id])sets[e.target.id].forEach(id=>$(`#${id}`).type=e.target.checked?'text':'password');
  if(e.target.id==='memory-save')$('#memory-limit').disabled=!e.target.checked;
  if(e.target.id==='global-memory-override')$('#global-memory').disabled=!e.target.checked;
  if(e.target.id==='use-output-name')$('#output-name').disabled=!e.target.checked;
});
document.addEventListener('keydown',e=>{
  if($('#popup').matches(':popover-open')&&['ArrowDown','ArrowUp','Home','End','Escape'].includes(e.key)){
    e.preventDefault();const buttons=[...$('#popup').querySelectorAll('button')];const i=buttons.indexOf(document.activeElement);
    if(e.key==='Escape'){$('#popup').hidePopover();menuOpener?.focus();return;}
    const j=e.key==='Home'?0:e.key==='End'?buttons.length-1:(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;buttons[j]?.focus();return;
  }
  if(e.target.matches('[role=tab]')&&['ArrowLeft','ArrowRight','Home','End'].includes(e.key)){e.preventDefault();const tabs=[...document.querySelectorAll('[role=tab]')],i=tabs.indexOf(e.target),j=e.key==='Home'?0:e.key==='End'?tabs.length-1:(i+(e.key==='ArrowRight'?1:-1)+tabs.length)%tabs.length;tabs[j].click();tabs[j].focus();return;}
  if($('#sheet').open)return;
  if(e.key==='F9'){e.preventDefault();action('two');}
  if(e.key==='F2'){e.preventDefault();action('rename');}
  if(e.ctrlKey&&e.key==='a'&&!e.target.matches('input,textarea')){e.preventDefault();action('select-all');}
  if(e.altKey&&e.key.toLowerCase()==='d'){e.preventDefault();$('#panels').children[activePanel].querySelector('.address input').focus();}
});
$('#sheet-form').addEventListener('submit',e=>{e.preventDefault();if(currentSurface==='add'){
  const invalid=$('#password-new').value!==$('#password-repeat').value;$('#password-error').hidden=!invalid;
  if(invalid){$('#tab-encryption').click();$('#password-repeat').setAttribute('aria-invalid','true');$('#password-repeat').setAttribute('aria-describedby','password-error');$('#password-repeat').focus();return;}
  show('progress');say('Запуск условной операции. Архив не создаётся.');
}else if(currentSurface==='extract'){show('progress');say('Запуск условной распаковки. Файлы не изменяются.');}
else if(currentSurface==='password'){if(!$('#request-password').value){action('password-error');return;}show('progress');}
else if(currentSurface==='settings'){say('В макете параметры не сохраняются и не применяются к Windows.');closeSheet();}});
$('#close-sheet').innerHTML=icon('close');$('#close-sheet').onclick=()=>{if(['progress','overwrite','memory','password'].includes(currentSurface))action('cancel-operation');else closeSheet();};
$('#sheet').addEventListener('cancel',e=>{e.preventDefault();$('#close-sheet').click();});
$('#theme').value=params.get('theme')||'system';$('#scale').value=params.get('scale')||'1';$('#state').value=params.get('state')||'normal';$('#two').checked=params.get('two')==='1';
$('#theme').onchange=theme;$('#accent').oninput=theme;osTheme.addEventListener('change',theme);
$('#scale').onchange=()=>{document.documentElement.style.setProperty('--scale',$('#scale').value);fitStudy();};
$('#state').onchange=renderPanels;$('#two').onchange=renderPanels;$('#surface').onchange=e=>show(e.target.value,e.target);
theme();$('#scale').onchange();renderPanels();if(params.get('surface'))show(params.get('surface'),$('#surface'));
