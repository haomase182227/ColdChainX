import xml.etree.ElementTree as ET

tree = ET.parse('./docs/Sequence-Diagram-Dispatch.drawio')
root = tree.getroot()

for diagram in root.findall('diagram'):
    model = diagram.find('mxGraphModel')
    if model:
        rt = model.find('root')
        for cell in rt.findall('mxCell'):
            val = cell.get('value', '')
            if val and len(val) > 0:
                print(f"{val}")
