"""One-off: converts Corner's WPF UI code (../windows/src) into a first Avalonia draft in Corner.Mac/src/ported.

It keeps only the UI types (the ones sync_core.py drops), and rewrites the WPF names that have a direct Avalonia
equivalent: events, Visibility, tooltips, cursors, transform origins, dash arrays and the like. What it cannot
convert is fixed by hand afterwards, so do NOT re-run it over the ported files: they carry those hand fixes.
To bring over a later change to the Windows UI, run it into a scratch folder (--out) and diff.
"""
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sync_core import SRC, code_mask, DECL, DROP

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', 'Corner.Mac', 'src', 'ported'))
FILES = ['Corner.cs', 'Energy.cs', 'Plan.cs', 'Stats.cs', 'Checks.cs', 'Onboard.cs', 'Focus.cs', 'Glass.cs',
         'Toast.cs', 'Ui.cs', 'UiEnergy.cs', 'UiSleep.cs']
SKIP = {'Program', 'Autostart', 'Backdrop'}  # written by hand for the Mac


def keep_ui(text):
    """Only the top-level types in DROP (minus SKIP), each with the comment lines straight above it."""
    m = code_mask(text)
    parts = []
    pos = 0
    while True:
        d = DECL.search(m, pos)
        if not d: break
        depth = m[:d.start()].count('{') - m[:d.start()].count('}')
        b = m.find('{', d.end()); k = b; lvl = 0
        while True:
            if m[k] == '{': lvl += 1
            elif m[k] == '}':
                lvl -= 1
                if lvl == 0: break
            k += 1
        end = k + 1
        if depth == 1 and d.group(1) in DROP and d.group(1) not in SKIP:
            lines = text[:d.start()].split('\n')
            i = len(lines) - 1
            while i > 0 and lines[i - 1].strip().startswith('//'): i -= 1
            start = len('\n'.join(lines[:i])) + (1 if i > 0 else 0)
            parts.append(text[start:end])
        pos = end if depth == 1 else d.end()
    return parts


def statement_end(m, i):
    """Index of the ';' that ends the statement starting at i (depth 0 of (), {}, [])."""
    lvl = 0
    while i < len(m):
        c = m[i]
        if c in '({[': lvl += 1
        elif c in ')}]': lvl -= 1
        elif c == ';' and lvl == 0: return i
        i += 1
    raise ValueError('no end')


def wrap_events(text, wpf, helper):
    """x.MouseLeftButtonDown += handler;  ->  x.OnLeftDown(handler);"""
    pat = re.compile(r'\.' + wpf + r'\s*\+=\s*')
    while True:
        m = code_mask(text)
        hit = pat.search(m)
        if not hit: return text
        e = statement_end(m, hit.end())
        text = text[:hit.start()] + '.' + helper + '(' + text[hit.end():e] + ')' + text[e:]


RENAMES = [
    (r'\.MouseEnter\s*\+=', '.PointerEntered +='),
    (r'\.MouseLeave\s*\+=', '.PointerExited +='),
    (r'\.MouseMove\s*\+=', '.PointerMoved +='),
    (r'\.LostMouseCapture\s*\+=', '.PointerCaptureLost +='),
    (r'\.GotKeyboardFocus\s*\+=', '.GotFocus +='),
    (r'\.LostKeyboardFocus\s*\+=', '.LostFocus +='),
    (r'\.MouseWheel\s*\+=', '.PointerWheelChanged +='),
    (r'\bIsMouseOver\b', 'IsPointerOver'),
    (r'\bIsKeyboardFocused\b', 'IsFocused'),
    (r'\bActualWidth\b', 'Bounds.Width'),
    (r'\bActualHeight\b', 'Bounds.Height'),
    (r'\bFontWeights\.', 'FontWeight.'),
    (r'\bFontStyles\.', 'FontStyle.'),
    (r'\bFrameworkElement\b', 'Control'),
    (r'\bUIElement\b', 'Control'),
    (r'\bDependencyProperty\b', 'AvaloniaProperty'),
    (r'\bDependencyObject\b', 'AvaloniaObject'),
    (r'\bDropShadowEffect\b', 'DropShadowDirectionEffect'),
    (r'\bStrokeStartLineCap = PenLineCap\.Round, StrokeEndLineCap = PenLineCap\.Round', 'StrokeLineCap = PenLineCap.Round'),
    (r'\bStrokeLineJoin\b', 'StrokeJoin'),
    (r'RenderTransformOrigin = new Point\(([^()]*)\)', r'RenderTransformOrigin = new RelativePoint(\1, RelativeUnit.Relative)'),
    (r'new DoubleCollection\(new double\[\] \{([^}]*)\}\)', r'new AvaloniaList<double> {\1}'),
    (r'Panel\.SetZIndex\(([^,]+), ([^)]+)\)', r'\1.ZIndex = \2'),
    (r'Dispatcher\.BeginInvoke\(new Action\(', 'Dispatcher.UIThread.Post(new Action('),
    (r'\.TranslatePoint\(', '.Tp('),
    (r'\.PointToScreen\(', '.Pts('),
    (r'\bFocusVisualStyle = null,\s*', ''),
    (r'\bCaptureMouse\(\)', 'CaptureMouse()'),
    (r'System\.Windows\.Input\.Cursors', 'Cursors'),
    # Visibility -> IsVisible (Hidden has no Avalonia twin: left as a marker to fix by hand)
    (r'(?<!\w)Visibility\.Visible\b', 'true'),
    (r'(?<!\w)Visibility\.Collapsed\b', 'false'),
    (r'(?<!\w)Visibility\.Hidden\b', 'HIDDEN_FIXME'),
    (r'\.Visibility\b', '.IsVisible'),
    (r'(?<![\w.])Visibility(?=\s*=[^=])', 'IsVisible'),
    # tooltips: an attached property, written with Avalonia's property indexer
    (r'\.ToolTip\s*=(?!=)', '[ToolTip.TipProperty] ='),
    (r'(?<![\w.\[])ToolTip\s*=(?!=)', '[ToolTip.TipProperty] ='),
]

WPF_USING = re.compile(r'^\s*using\s+(?:\w+\s*=\s*)?System\.(?:Windows|Drawing|Runtime\.InteropServices)\b.*$\n?', re.M)


def convert(src):
    text = src
    text = wrap_events(text, 'MouseLeftButtonDown', 'OnLeftDown')
    text = wrap_events(text, 'MouseLeftButtonUp', 'OnLeftUp')
    text = wrap_events(text, 'PreviewMouseWheel', 'OnPreviewWheel')
    for a, b in RENAMES: text = re.sub(a, b, text)
    return text


def main():
    out = OUT
    if '--out' in sys.argv: out = sys.argv[sys.argv.index('--out') + 1]
    os.makedirs(out, exist_ok=True)
    for f in FILES:
        with open(os.path.join(SRC, f), encoding='utf-8-sig') as fh: text = fh.read()
        parts = keep_ui(text)
        if not parts: continue
        usings = '\n'.join(l for l in text.split('\n') if l.startswith('using ') and not WPF_USING.match(l + '\n'))
        body = '\n\n'.join(convert(p) for p in parts)
        head = '// Ported from corner/src/' + f + ' (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.\n'
        with open(os.path.join(out, f), 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(head + usings + '\n\nnamespace Corner\n{\n' + body + '\n}\n')
        print('%-12s %d types' % (f, len(parts)))


if __name__ == '__main__':
    main()
