import re

path = r"s:\VScodeProjects\Paddy-dev\Views\MainWindow.xaml.cs"

with open(path, "r", encoding="utf-8") as f:
    content = f.read()

# Fix ShowMessageBox calls that have extra WPF args (0x04, 0x30) which are now invalid
# Pattern: ShowMessageBox("msg", "caption", 0x04, 0x30);  -> ShowMessageBox("msg", "caption");
content = re.sub(
    r'ShowMessageBox\(([^)]+),\s*0x\w+,\s*0x\w+\)',
    lambda m: 'ShowMessageBox(' + m.group(1).strip() + ')',
    content
)

# Fix ShowMessageBox(this, ...) -> ShowMessageBox(...)
content = content.replace('ShowMessageBox(this,\n                    ', 'ShowMessageBox(\n                    ')
content = content.replace('ShowMessageBox(this,', 'ShowMessageBox(')

# Fix the confirm call - ShowMessageBox doesn't return a value
# Replace: var confirm = ShowMessageBox(...); if (confirm != 6) return;
# With: int confirm = MessageBoxW(IntPtr.Zero, ..., 0x04 | 0x30); if (confirm != 6) return;
content = re.sub(
    r'var confirm = ShowMessageBox\(\s*\$"Delete page \\"{\$?_activePadPage(?:\.Name|\!\.Name)\}\\".*?"Delete Pad Page"\)',
    'var confirm = MessageBoxW(System.IntPtr.Zero, $"Delete page \\"{_activePadPage!.Name}\\"? Its pads will move back to Favorites.", "Delete Pad Page", 0x04 | 0x30)',
    content,
    flags=re.DOTALL
)

print("Remaining fixes applied")

with open(path, "w", encoding="utf-8") as f:
    f.write(content)

print("Done.")
