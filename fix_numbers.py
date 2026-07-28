import xml.etree.ElementTree as ET
import re

file = 'docs/Sequence-Diagram-Authentication-User-Management.drawio'
tree = ET.parse(file)
root = tree.getroot()

changed = 0
for cell in root.iter('mxCell'):
    val = cell.get('value', '')
    if val:
        # Match "1. " or "10. " at the beginning
        new_val = re.sub(r'^\d+\.\s+', '', val)
        if new_val != val:
            cell.set('value', new_val)
            changed += 1

tree.write(file, encoding='utf-8', xml_declaration=True)
print(f"Removed numbering from {changed} elements.")
