import xml.etree.ElementTree as ET
import glob

files = ['docs/Sequence-Diagram-Dispatch.drawio', 'docs/Sequence-Diagram-IncidentReports.drawio']
for file in files:
    tree = ET.parse(file)
    root = tree.getroot()
    changed = 0
    for cell in root.iter('mxCell'):
        style = cell.get('style', '')
        if 'rounded=1' in style:
            # Replace rounded=1 with rounded=0
            new_style = style.replace('rounded=1', 'rounded=0')
            cell.set('style', new_style)
            changed += 1
    tree.write(file, encoding='utf-8', xml_declaration=True)
    print(f"Updated {changed} shapes to rounded=0 in {file}")
