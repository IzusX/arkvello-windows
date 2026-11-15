/* Design study only. No filesystem operations, storage or network access. */
const $ = selector => document.querySelector(selector);
const query = new URLSearchParams(location.search);
const systemTheme = matchMedia('(prefers-color-scheme: dark)');
const feedback = message => { $('#feedback').textContent = message; };
function fitStudy() {
  const top = $('#stage').getBoundingClientRect().top + scrollY;
  document.documentElement.style.setProperty('--available-height', `${Math.max(160, innerHeight - top - 12)}px`);
}
window.addEventListener('resize', fitStudy);

const files = [
  ['..', '', '', '', 'folder'],
  ['assets', '', '14.11.2026 18:42', 'Папка', 'folder'],
  ['docs', '', '14.11.2026 18:40', 'Папка', 'folder'],
  ['release', '', '15.11.2026 10:08', 'Папка', 'folder'],
  ['src', '', '14.11.2026 21:16', 'Папка', 'folder'],
  ['Arkvello.sln', '4 216', '14.11.2026 18:32', 'SLN', 'file'],
  ['CHANGELOG.md', '8 192', '15.11.2026 09:54', 'Markdown', 'file'],
  ['DESIGN.md', '12 480', '15.11.2026 10:12', 'Markdown', 'file'],
  ['LICENSE.txt', '1 084', '14.11.2026 18:32', 'Текст', 'file'],
  ['README.md', '3 846', '15.11.2026 09:50', 'Markdown', 'file'],
  ['release.7z', '18 472 960', '15.11.2026 10:08', '7z', 'file'],
  ['version.txt', '24', '14.11.2026 18:32', 'Текст', 'file']
];
const backupFiles = [files[0], ['2026-11-13.7z', '17 309 696', '13.11.2026 22:10', '7z', 'file'], ['2026-11-14.7z', '18 194 432', '14.11.2026 22:08', '7z', 'file']];
function applyTheme() {
  const choice = $('#theme').value;
  document.documentElement.dataset.theme = choice === 'system' ? (systemTheme.matches ? 'dark' : 'light') : choice;
}
function showSurface(surface) {
  $('#surface').value = surface;
  $('#manager').hidden = surface !== 'main';
  $('#archive').hidden = surface !== 'archive';
  $('#dialog-state-control').hidden = surface !== 'archive';
  fitStudy();
}
function makePanel(number) {
  const panel = document.createElement('section');
  panel.className = `panel${number ? ' inactive' : ''}`;
  panel.setAttribute('aria-label', number ? 'Правая панель' : 'Левая панель');
  panel.innerHTML = `<div class="pathbar"><button type="button" aria-label="На уровень вверх"><svg><use href="#up"/></svg></button><div class="path-field"><svg aria-hidden="true"><use href="#location"/></svg><input aria-label="Путь ${number ? 'правой' : 'левой'} панели" spellcheck="false" list="history-${number}"><datalist id="history-${number}"><option value="C:\\Users\\Alex\\Projects\\Arkvello\\"><option value="D:\\Backups\\"></datalist></div></div><div class="file-scroll"><table class="file-table" aria-label="Пример списка файлов"><colgroup><col class="name"><col class="size"><col class="date"><col class="kind"></colgroup><thead><tr><th scope="col" aria-sort="ascending">Имя<span class="sort" aria-hidden="true"></span></th><th scope="col" class="number">Размер</th><th scope="col">Изменён</th><th scope="col">Тип</th></tr></thead><tbody></tbody></table></div><div class="panel-status"><span class="count"></span><span class="selection"></span></div>`;
  panel.querySelector('.pathbar input').value = number ? 'D:\\Backups\\' : 'C:\\Users\\Alex\\Projects\\Arkvello\\';
  let data = number ? backupFiles : files;
  if ($('#list-state').value === 'empty') data = [files[0]];
  if ($('#list-state').value === 'long' && !number) data = files.map((file, i) => i === 7 ? ['Спецификация интерфейса Arkvello для Windows — итоговая версия с дополнениями и замечаниями.md', ...file.slice(1)] : file);
  data.forEach((file, index) => {
    const row = document.createElement('tr');
    row.tabIndex = index === 0 ? 0 : -1;
    row.setAttribute('aria-selected', String(number ? index === 1 : index === 10));
    const name = document.createElement('td');
    name.innerHTML = `<div class="file-name"><svg class="${file[4]}" aria-hidden="true"><use href="#${file[4]}"/></svg><span></span></div>`;
    name.querySelector('span').textContent = file[0];
    name.title = file[0];
    row.append(name);
    file.slice(1, 4).forEach((value, cell) => {
      const td = document.createElement('td');
      td.textContent = value;
      if (cell === 0) td.className = 'number';
      row.append(td);
    });
    const selectRow = () => {
      panel.querySelectorAll('tbody tr').forEach(other => {
        other.setAttribute('aria-selected', String(other === row));
        other.tabIndex = other === row ? 0 : -1;
      });
      panel.querySelector('.selection').textContent = index ? `Выделено: 1${file[1] ? ` · ${file[1]} байт` : ''}` : 'Нет выделенных файлов';
    };
    row.addEventListener('click', () => { selectRow(); row.focus(); });
    row.addEventListener('keydown', event => {
      if (!['ArrowDown', 'ArrowUp', 'Home', 'End', ' '].includes(event.key)) return;
      event.preventDefault();
      const rows = [...panel.querySelectorAll('tbody tr')];
      const next = event.key === 'Home' ? rows[0] : event.key === 'End' ? rows.at(-1) : event.key === 'ArrowDown' ? row.nextElementSibling : event.key === 'ArrowUp' ? row.previousElementSibling : row;
      (next || row).click();
    });
    panel.querySelector('tbody').append(row);
  });
  panel.querySelector('.count').textContent = number ? `Объектов: ${data.length - 1}` : $('#list-state').value === 'empty' ? 'Объектов: 0' : 'Папок: 4 · Файлов: 7';
  panel.querySelector('.selection').textContent = data.length > 1 ? number ? 'Выделено: 1 · 17 309 696 байт' : 'Выделено: 1 · 18 472 960 байт' : 'Нет выделенных файлов';
  panel.addEventListener('focusin', () => {
    document.querySelectorAll('.panel').forEach(other => other.classList.toggle('inactive', other !== panel));
  });
  panel.querySelector('.pathbar button').addEventListener('click', () => feedback('Переходы по файловой системе в макете не выполняются. Поле пути доступно для редактирования.'));
  return panel;
}
function renderPanels() {
  $('#panels').replaceChildren(makePanel(0));
  if ($('#two-panels').checked) $('#panels').append(makePanel(1));
}
$('#theme').addEventListener('change', applyTheme);
systemTheme.addEventListener('change', applyTheme);
$('#surface').addEventListener('change', () => showSurface($('#surface').value));
$('#scale').addEventListener('change', () => document.documentElement.style.setProperty('--scale', $('#scale').value));
$('#two-panels').addEventListener('change', renderPanels);
$('#list-state').addEventListener('change', renderPanels);
$('#dialog-state').addEventListener('change', () => {
  const invalid = $('#dialog-state').value === 'password-error';
  $('#password-error').hidden = !invalid;
  $('#password-again').setAttribute('aria-invalid', String(invalid));
  if (invalid) $('#password-again').setAttribute('aria-describedby', 'password-error');
  else $('#password-again').removeAttribute('aria-describedby');
});
$('#add').addEventListener('click', () => { showSurface('archive'); $('#archive-name').focus(); });
$('#archive-cancel').addEventListener('click', () => { showSurface('main'); $('#add').focus(); });
$('#archive-form').addEventListener('submit', event => {
  event.preventDefault();
  feedback('Это визуальное исследование: архив не создаётся. Зависимости форматов, проверка паролей и оценка памяти появятся только в нативном прототипе.');
});
$('#show-password').addEventListener('change', event => {
  ['#password', '#password-again'].forEach(selector => { $(selector).type = event.target.checked ? 'text' : 'password'; });
});
$('#options-open').addEventListener('click', () => $('#options-dialog').showModal());
document.querySelectorAll('[data-override]').forEach(input => input.addEventListener('change', () => {
  document.getElementById(input.dataset.override).disabled = !input.checked;
}));
document.querySelectorAll('[data-command]').forEach(button => button.addEventListener('click', () => feedback(`«${button.dataset.command}»: команда сохраняется в приложении; в макете операция не выполняется.`)));
document.querySelectorAll('.menubar button').forEach(button => button.addEventListener('click', () => feedback(`Меню «${button.textContent}» сохраняет команды оригинала. В этом исследовании показано его оформление без раскрытия.`)));
document.addEventListener('keydown', event => {
  if (event.key === 'F9' && !$('#options-dialog').open) {
    event.preventDefault();
    $('#two-panels').checked = !$('#two-panels').checked;
    renderPanels();
  }
  if (event.key === 'Escape' && !$('#options-dialog').open && !$('#archive').hidden) $('#archive-cancel').click();
});
for (const id of ['theme', 'scale', 'surface', 'list-state']) {
  const value = query.get(id);
  if (value && [...document.getElementById(id).options].some(option => option.value === value)) document.getElementById(id).value = value;
}
$('#two-panels').checked = query.get('panels') === '2';
document.documentElement.style.setProperty('--scale', $('#scale').value);
applyTheme();
showSurface($('#surface').value);
renderPanels();
