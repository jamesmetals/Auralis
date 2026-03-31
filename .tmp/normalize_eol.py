import sys

file_path = r"c:\estudos\MelhorWindows\src\MelhorWindows.Desktop\MainWindow.xaml"

with open(file_path, 'rb') as f:
    raw = f.read()

# Normalize to LF first, then to CRLF
text = raw.decode('utf-8-sig')
text = text.replace('\r\n', '\n').replace('\r', '\n')
text = text.replace('\n', '\r\n')

with open(file_path, 'w', encoding='utf-8-sig', newline='') as f:
    f.write(text)

print(f"Normalized {len(text)} chars")
