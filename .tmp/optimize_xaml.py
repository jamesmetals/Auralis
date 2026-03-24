import re
import sys

file_path = r"C:\estudos\MelhorWindows\src\MelhorWindows.Desktop\MainWindow.xaml"

with open(file_path, "r", encoding="utf-8") as f:
    xaml = f.read()

# Performance optimization: DropShadowEffect
xaml = xaml.replace(
    '<DropShadowEffect x:Key="ShellShadow" BlurRadius="36" Color="#000000" Opacity="0.40" ShadowDepth="0" />',
    '<DropShadowEffect x:Key="ShellShadow" BlurRadius="12" Color="#000000" Opacity="0.60" ShadowDepth="2" RenderingBias="Performance" />'
)

# Geometry optimization (Anti-cliche: Brutalist sharp edges)
xaml = re.sub(r'CornerRadius="\d+(,\d+,\d+,\d+)?"', 'CornerRadius="0"', xaml)

# Palette transformation: Deep Blue/Purple -> Monochromatic Black/Graphite + Acid Green
replacements = {
    "#060E20": "#050505",
    "#091634": "#0f0f0f",
    "#0D214C": "#141414",
    "#071127": "#0a0a0a",
    "#050C1C": "#000000",
    "#0A1B40": "#080808",
    "#08142A": "#060606",
    "#060C1D": "#030303",
    "#0B2047": "#121212",
    "#0E2149": "#111111",
    "#0B1E44": "#0e0e0e",
    "#0B1A39": "#0d0d0d",
    "#101D42": "#151515",
    "#080F28": "#070707",
    "#101D38": "#131313",
    "#143066": "#222222",
    "#112A59": "#1c1c1c",
    "#102856": "#1a1a1a",
    "#122A59": "#1b1b1b",
    "#DEE5FF": "#F0F0F0",
    "#91AAEB": "#888888",
    "#5B74B1": "#666666",
    "#1E3768": "#333333",
    "#173265": "#2a2a2a",
    "#32569F": "#444444",
    "#26477E": "#3a3a3a",
    "#183664": "#2b2b2b",
    "#163261": "#282828",
    "#2C467E": "#404040",
    "#C0C1FF": "#39FF14",
    "#2522B0": "#000000",
    "#D9D7FF": "#39FF14",
    "#B5B7FF": "#30D810",
    "#D2D2FF": "#39FF14"
}

for old_hex, new_hex in replacements.items():
    xaml = re.sub(old_hex, new_hex, xaml, flags=re.IGNORECASE)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(xaml)

print("XAML visual updates completed.")
