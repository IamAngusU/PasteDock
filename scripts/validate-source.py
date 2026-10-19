#!/usr/bin/env python3
"""Validate source structure without claiming C# compilation or Windows execution.

Python 3.10+, standard library only. Run from any directory. The report records
checks actually performed and explicitly lists validation that remains unrun.
"""
from __future__ import annotations
import argparse
import json
import platform
import re
import shutil
import sys
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
XAML = '{http://schemas.microsoft.com/winfx/2006/xaml}'
EVENTS = {
    'Click', 'Closing', 'Closed', 'Loaded', 'Unloaded', 'Opened', 'SizeChanged',
    'PreviewKeyDown', 'KeyDown', 'SelectionChanged', 'MouseDoubleClick',
    'IsVisibleChanged', 'DragDelta', 'PreviewMouseWheel', 'PreviewMouseLeftButtonDown',
    'PreviewMouseMove', 'PreviewMouseLeftButtonUp', 'LostMouseCapture',
}


def mask_literals(text: str) -> str:
    """Mask comments and string/character literals, including interpolated strings.

    This is a deliberately small lexical scanner, not a C# parser. Interpolation
    expression internals are skipped along with their enclosing string. Raw string
    literals are not used by this project and fail explicitly rather than guessed.
    """
    out = list(text)
    n = len(text)

    def quoted(i: int, quote: str, verbatim: bool, interpolated: bool) -> int:
        j = i + 1
        while j < n:
            if text[j] == quote:
                if verbatim and j + 1 < n and text[j + 1] == quote:
                    j += 2
                    continue
                return j + 1
            if not verbatim and text[j] == '\\':
                j += 2
                continue
            if interpolated and text[j] == '{':
                if j + 1 < n and text[j + 1] == '{':
                    j += 2
                    continue
                depth = 1
                j += 1
                while j < n and depth:
                    if text[j] in ('"', "'"):
                        q = text[j]
                        j = quoted(j, q, j > 0 and text[j - 1] == '@', False)
                        continue
                    depth += (text[j] == '{') - (text[j] == '}')
                    j += 1
                if depth:
                    raise ValueError('Unterminated interpolation expression')
                continue
            j += 1
        raise ValueError('Unterminated literal')

    i = 0
    while i < n:
        end = None
        if text.startswith('//', i):
            end = text.find('\n', i)
            if end == -1:
                end = n
        elif text.startswith('/*', i):
            end = text.find('*/', i + 2)
            if end == -1:
                raise ValueError('Unterminated block comment')
            end += 2
        elif text.startswith('"""', i):
            raise ValueError('Raw string literal requires a compiler-grade lexer')
        elif text[i] in ('"', "'"):
            prefix = text[max(0, i - 2):i]
            verbatim = text[i] == '"' and (prefix.endswith('@') or prefix.endswith('@$'))
            interpolated = text[i] == '"' and (prefix.endswith('$') or prefix.endswith('$@'))
            end = quoted(i, text[i], verbatim, interpolated)
        if end is not None:
            for k in range(i, end):
                if out[k] != '\n':
                    out[k] = ' '
            i = end
        else:
            i += 1
    return ''.join(out)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, default=ROOT / 'docs/static-validation.json')
    args = parser.parse_args()
    failures: list[str] = []
    checks: list[dict] = []

    def check(name: str, count: int, errors: list[str], scope: str) -> None:
        checks.append({'name': name, 'status': 'failed' if errors else 'passed',
                       'checked_count': count, 'scope': scope, 'errors': errors})
        failures.extend(errors)

    files = [p for p in ROOT.rglob('*') if p.is_file() and not any(part in {'bin', 'obj', 'artifacts', '.tools', '.git', '__pycache__'} for part in p.relative_to(ROOT).parts)]
    xml_files = [p for p in files if p.suffix in {'.xaml', '.csproj', '.props', '.slnx', '.manifest'}]
    trees: dict[Path, ET.Element] = {}
    errors = []
    for path in xml_files:
        try:
            trees[path] = ET.parse(path).getroot()
        except ET.ParseError as exc:
            errors.append(f'{path.relative_to(ROOT)}: {exc}')
    check('XML well-formedness', len(xml_files), errors, 'XML syntax only; not WPF schema compilation')

    errors = []
    refs = 0
    for path, tree in trees.items():
        for element in tree.iter():
            tag = element.tag.split('}')[-1]
            value = (element.get('Include') if tag == 'ProjectReference' else
                     element.get('Path') if tag == 'Project' and path.suffix == '.slnx' else
                     element.get('Source') if tag == 'ResourceDictionary' else None)
            if value and not value.startswith(('pack:', 'http:', 'https:')):
                refs += 1
                if not (path.parent / value.replace('\\', '/')).exists():
                    errors.append(f'{path.relative_to(ROOT)}: missing reference {value}')
    check('Project and resource-dictionary files', refs, errors, 'Relative file existence')

    errors = []
    cs_files = [p for p in files if p.suffix == '.cs']
    masked: dict[Path, str] = {}
    for path in cs_files:
        try:
            source = mask_literals(path.read_text(encoding='utf-8'))
            masked[path] = source
            stack: list[tuple[str, int]] = []
            line = 1
            for char in source:
                if char == '\n':
                    line += 1
                elif char in '([{':
                    stack.append((char, line))
                elif char in ')]}':
                    if not stack or stack[-1][0] != {')': '(', ']': '[', '}': '{'}[char]:
                        raise ValueError(f'mismatched delimiter at line {line}')
                    stack.pop()
            if stack:
                raise ValueError(f'unclosed delimiters: {stack}')
        except ValueError as exc:
            errors.append(f'{path.relative_to(ROOT)}: {exc}')
    check('C# outer delimiter balance', len(cs_files), errors,
          'Comments/literals masked; interpolation internals skipped; not type checking or compilation')

    errors = []
    event_count = 0
    for path, tree in trees.items():
        if path.suffix != '.xaml' or not tree.get(XAML + 'Class'):
            continue
        class_name = tree.get(XAML + 'Class', '')
        code_path = Path(str(path) + '.cs')
        if not code_path.exists():
            errors.append(f'{path.relative_to(ROOT)}: missing code-behind')
            continue
        source = masked.get(code_path, code_path.read_text(encoding='utf-8-sig'))
        namespace, _, name = class_name.rpartition('.')
        if not re.search(r'\bpartial\s+class\s+' + re.escape(name) + r'\b', source) or f'namespace {namespace}' not in source:
            errors.append(f'{path.relative_to(ROOT)}: x:Class mismatch')
        for element in tree.iter():
            for attr, handler in element.attrib.items():
                if attr in EVENTS:
                    event_count += 1
                    if not re.search(r'\b' + re.escape(handler) + r'\s*\(', source):
                        errors.append(f'{path.relative_to(ROOT)}: missing event handler {handler}')
    check('XAML code-behind and declared events', event_count, errors, 'Matching class names and handler method names; not delegate signature checking')

    keys: set[str] = set()
    for path, tree in trees.items():
        if path.suffix == '.xaml':
            keys.update(e.get(XAML + 'Key') for e in tree.iter() if e.get(XAML + 'Key'))
    errors = []
    resource_count = 0
    element_count = 0
    for path, tree in trees.items():
        if path.suffix != '.xaml':
            continue
        names = {e.get(XAML + 'Name') or e.get('Name') for e in tree.iter()}
        source = path.read_text(encoding='utf-8')
        for key in re.findall(r'\{(?:StaticResource|DynamicResource)\s+([^{}\s,]+)\s*\}', source):
            resource_count += 1
            if key not in keys:
                errors.append(f'{path.relative_to(ROOT)}: unknown resource key {key}')
        for name in re.findall(r'\bElementName\s*=\s*([^,}\s]+)', source):
            element_count += 1
            if name not in names:
                errors.append(f'{path.relative_to(ROOT)}: unknown ElementName {name}')
    check('XAML resource and named-element references', resource_count + element_count, errors,
          'Aggregate resource-key existence and per-file names; not runtime resource scope/binding validation')

    light = ROOT / 'src/PasteDock.Desktop/Themes/Tokens.Light.xaml'
    dark = ROOT / 'src/PasteDock.Desktop/Themes/Tokens.Dark.xaml'
    theme_sets = [{e.get(XAML + 'Key') for e in trees[p].iter() if e.get(XAML + 'Key')} for p in (light, dark)]
    check('Theme token parity', len(theme_sets[0]),
          [] if theme_sets[0] == theme_sets[1] else [f'Theme key mismatch: {sorted(theme_sets[0] ^ theme_sets[1])}'],
          'Light and dark token dictionaries declare the same keys')

    test_source = '\n'.join(p.read_text(encoding='utf-8-sig') for p in sorted((ROOT / 'tests/PasteDock.Core.Tests').glob('*.cs')))
    cases = re.findall(r'^\s*Test(?:Async)?\("([^"\n]+)"', test_source, re.M)
    duplicates = [name for name, count in Counter(cases).items() if count > 1]
    check('Named test declarations', len(cases), [f'Duplicate test: {name}' for name in duplicates],
          'Declared test names only; no test has been executed by this validator')

    errors = []
    for path, source in masked.items():
        if 'tests' in path.relative_to(ROOT).parts:
            continue
        for pattern in [r'\bHttpClient\b', r'\bWebClient\b', r'\bTcpClient\b', r'\bBinaryFormatter\b', r'\bProcess\s*\.\s*Start\s*\(']:
            if re.search(pattern, source):
                errors.append(f'{path.relative_to(ROOT)}: review unexpected API matching {pattern}')
    native = (ROOT / 'src/PasteDock.Desktop/Clipboard/WindowsClipboardService.cs').read_text(encoding='utf-8-sig')
    for required in ['AddClipboardFormatListener', 'ApartmentState.STA', 'payload.Text', 'FixedTimeEquals', 'GetClipboardSequenceNumber', 'IsInteractiveDesktop']:
        if required not in native:
            errors.append(f'Clipboard source invariant missing: {required}')
    if 'payload.Preview' in native:
        errors.append('Clipboard implementation references a preview payload')
    check('Narrow source safety checks', len(cs_files) + 7, errors,
          'No listed networking/shell/deserialization APIs in implementation; selected clipboard guards present. Not a security audit.')

    errors = []
    for path in files:
        if path.suffix.lower() in {'.ttf', '.otf', '.woff', '.woff2'}:
            errors.append(f'Unexpected bundled font: {path.relative_to(ROOT)}')
    for path in cs_files:
        if re.search(r'\b(?:TODO|FIXME|NotImplementedException)\b', path.read_text(encoding='utf-8-sig')):
            errors.append(f'Unfinished-code marker: {path.relative_to(ROOT)}')
    check('Packaging exclusions and unfinished-code markers', len(files), errors,
          'No bundled font files or TODO/FIXME/NotImplementedException markers; not a completeness claim')

    errors = []
    for path in [p for p in files if p.suffix == '.json']:
        try:
            json.loads(path.read_text(encoding='utf-8'))
        except (ValueError, UnicodeError) as exc:
            errors.append(f'{path.relative_to(ROOT)}: {exc}')
    check('JSON syntax', len([p for p in files if p.suffix == '.json']), errors, 'JSON parsing')

    errors = []
    repository = (ROOT / 'src/PasteDock.Core/Services/HistoryRepository.cs').read_text(encoding='utf-8-sig')
    store = (ROOT / 'src/PasteDock.Core/Storage/EncryptedFileStore.cs').read_text(encoding='utf-8-sig')
    shell = (ROOT / 'src/PasteDock.Desktop/ViewModels/ShellViewModel.cs').read_text(encoding='utf-8-sig')
    undo_vm = (ROOT / 'src/PasteDock.Desktop/ViewModels/ShellViewModel.Deletion.cs').read_text(encoding='utf-8-sig')
    ui_smoke = (ROOT / 'src/PasteDock.Desktop/Services/UiSmokeDiagnostics.cs').read_text(encoding='utf-8-sig')
    build = (ROOT / 'scripts/build.ps1').read_text(encoding='utf-8-sig')
    requirements = [
        ('repository', repository, ['SaveStateAsync(state, ct)', 'Volatile.Write(ref _state, state)', 'UndoLastDeleteAsync', 'DeleteWithUndoAsync']),
        ('store', store, ['state.Version is not (1 or 2)', 'state.Items.Concat(state.Undo?.Items ?? [])', 'e.BlobId).Distinct()']),
        ('exports', shell, ['ExportService.WriteBytesAsync(dialog.FileName, archive)', 'ExportService.WriteTextAsync(dialog.FileName', 'PayloadComposer.CombineAsync']),
        ('undo view model', undo_vm, ['UndoDeleteCommand', 'PermanentDeleteCommand', 'ForgetDeletionCommand']),
        ('build', build, ["-Mode '--ui-smoke'", 'WaitForExit(120000)', 'uiSmokeChecksPassed', '$buildId']),
    ]
    for label, source, required in requirements:
        for value in required:
            if value not in source:
                errors.append(f'{label}: missing reviewed source guard {value}')
    if 'File.WriteAllBytesAsync(dialog.FileName' in shell or 'File.WriteAllTextAsync(dialog.FileName' in shell:
        errors.append('A user export bypasses the atomic export service')
    smoke_masked = mask_literals(ui_smoke)
    if re.search(r'workspace\.InitializeAsync\s*\(|Clipboard\.(?:StartAsync|WriteAsync)\s*\(|SendInput\s*\(', smoke_masked):
        errors.append('UI smoke must not start the live workspace, access clipboard payloads or inject input')
    check('v0.2 data-loss and smoke isolation guards', sum(len(item[2]) for item in requirements) + 2, errors,
          'Narrow reviewed call-site presence/absence checks only; does not execute storage, bootstrap or WPF code')

    errors = []
    v030_requirements = [
        ('diff core', (ROOT / 'src/PasteDock.Core/Services/TextDiff.cs').read_text(encoding='utf-8-sig'), ['MaxEditDistance = 512', 'UsedFallback', 'ToUnifiedText']),
        ('power actions', (ROOT / 'src/PasteDock.Desktop/ViewModels/ShellViewModel.Power.cs').read_text(encoding='utf-8-sig'),
         ['PasteVisibleIndexAsync', 'PasteAsFileAsync', 'ExcludeSourceAsync', 'RequestDeleteSource', 'TogglePlainTargetAsync', 'ShouldForcePlain']),
        ('temporary files', (ROOT / 'src/PasteDock.Desktop/Services/TemporaryPasteFileService.cs').read_text(encoding='utf-8-sig'),
         ['TextFileFormatGuesser.GuessExtension', 'FileAttributes.Temporary', 'FileAttributes.NotContentIndexed', 'Retention = TimeSpan.FromHours(24)', 'DateTime.UtcNow:yyyyMMdd-HHmmss', 'CreatedUtc(path)']),
        ('advanced search', (ROOT / 'src/PasteDock.Core/Search/SearchQuery.cs').read_text(encoding='utf-8-sig'),
         ['is:protected', 'has:ocr', 'copies:', 'before:']),
        ('quick paste input', (ROOT / 'src/PasteDock.Desktop/Views/MainWindow.xaml.cs').read_text(encoding='utf-8-sig'),
         ['PasteVisibleIndexAsync', 'Key.D1', 'Key.D9', 'case Key.D:']),
        ('target rules', (ROOT / 'src/PasteDock.Core/Models/AppSettings.cs').read_text(encoding='utf-8-sig'), ['PlainTextPasteApps', 'NormalizeApps']),
        ('compare UI smoke', (ROOT / 'src/PasteDock.Desktop/Services/UiSmokeDiagnostics.cs').read_text(encoding='utf-8-sig'),
         ['light-compare-popup.png', 'WaitCommandAsync(shell.CompareCommand)', 'Two-clip diff popover rendered']),
    ]
    for label, source, required in v030_requirements:
        for value in required:
            if value not in source:
                errors.append(f'{label}: missing reviewed source guard {value}')
    quick_source = (ROOT / 'src/PasteDock.Desktop/Views/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
    if 'Key.NumPad1' in quick_source or 'Key.NumPad9' in quick_source:
        errors.append('quick paste input: Alt+NumPad must remain available for Windows character entry')
    check('v0.3 competitor-gap source guards', sum(len(item[2]) for item in v030_requirements) + 1, errors,
          'Source-presence and reviewed shortcut-boundary guards for the v0.3 additions; not runtime validation')

    report = {
        'product': 'PasteDock', 'validation_type': 'source-static-only',
        'environment': {'platform': platform.system(), 'python': platform.python_version(), 'dotnet_on_path': shutil.which('dotnet') is not None},
        'status': 'failed' if failures else 'passed-with-runtime-validation-outstanding',
        'counts': {'files_examined': len(files), 'csharp_files': len(cs_files),
                   'csharp_lines': sum(len(p.read_text(encoding='utf-8-sig').splitlines()) for p in cs_files),
                   'xaml_files': len([p for p in files if p.suffix == '.xaml']), 'declared_core_tests': len(cases)},
        'checks': checks,
        'not_performed': ['C# compilation/type checking', 'Core test execution', 'Windows application launch',
                          'DPAPI/clipboard/SendInput/OCR runtime checks', 'WPF layout and accessibility review',
                          'PowerShell SDK bootstrap and packaging execution', 'Windows ARM64 runtime validation'],
        'limitations': ['Structural checks cannot establish buildability or runtime correctness.',
                       'Source safety checks do not constitute a security audit.',
                       'Use scripts/test.ps1, scripts/build.ps1 and docs/VALIDATION.md on Windows.'],
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps({'status': report['status'], 'counts': report['counts'], 'failures': failures}, indent=2))
    print(f'Report: {args.report}')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
