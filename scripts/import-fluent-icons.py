"""Refresh the small vendored Fluent subset; network is only needed here."""
import json
import pathlib
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor

root = pathlib.Path(__file__).resolve().parents[1] / 'src/Arkvello.WinUI/Assets/Fluent'
root.mkdir(parents=True, exist_ok=True)

def get(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'Arkvello-icon-import'})
    return urllib.request.urlopen(request, timeout=30).read()

manifest_path = root / 'manifest.json'
commit = (json.loads(manifest_path.read_text(encoding='utf-8'))['commit'] if manifest_path.exists()
          else json.loads(get('https://api.github.com/repos/microsoft/fluentui-system-icons/commits/main'))['sha'])
base = f'https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{commit}/'
icons = {'Add': ('Add', 20), 'Extract': ('Folder Arrow Up', 20),
         'Test': ('Shield Checkmark', 20), 'Copy': ('Copy', 20),
         'Move': ('Folder Arrow Right', 20), 'Delete': ('Delete', 20),
         'View': ('Grid', 20), 'Back': ('Arrow Left', 20), 'Parent': ('Arrow Up', 20),
         'Refresh': ('Arrow Clockwise', 20), 'Folder': ('Folder', 20),
         'File': ('Document', 20), 'FolderLarge': ('Folder', 48), 'FileLarge': ('Document', 48)}

def fetch(item):
    key, (name, size) = item
    path = f'assets/{name}/SVG/ic_fluent_{name.lower().replace(" ", "_")}_{size}_regular.svg'
    data = get(base + urllib.parse.quote(path))
    svg = ET.fromstring(data)
    paths = svg.findall('{http://www.w3.org/2000/svg}path')
    assert len(paths) == 1 and svg.attrib['viewBox'] == f'0 0 {size} {size}', key
    (root / f'{key}.svg').write_bytes(data)
    return key, {'path': path, 'size': size, 'data': paths[0].attrib['d']}

manifest = {'repository': 'https://github.com/microsoft/fluentui-system-icons', 'commit': commit,
            'icons': dict(ThreadPoolExecutor(max_workers=6).map(fetch, icons.items()))}
resources = ['<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">']
for key, icon in manifest['icons'].items():
    resources.append(f'  <PathIconSource x:Key="Fluent{key}" Data="{icon["data"]}"/>')
resources.append('</ResourceDictionary>')
(root / 'Icons.xaml').write_text('\n'.join(resources) + '\n', encoding='utf-8')
code = ['// Generated from the pinned Fluent SVGs; see Assets/Fluent/manifest.json.',
        'namespace Arkvello.Controls;', 'internal static class FluentItemPaths', '{']
for key in ['Folder', 'File', 'FolderLarge', 'FileLarge']:
    code.append(f'    public const string {key} = "{manifest["icons"][key]["data"]}";')
code.append('}')
(root.parents[1] / 'Controls/FluentItemPaths.cs').write_text('\n'.join(code) + '\n', encoding='utf-8')
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
for name in ['LICENSE', 'NOTICE']:
    (root / name).write_bytes(get(base + name))
print(f'Imported {len(icons)} Fluent Regular icons at {commit}')
