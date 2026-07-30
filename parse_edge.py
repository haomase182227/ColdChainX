import xml.etree.ElementTree as ET

tree = ET.parse('./docs/Sequence-Diagram-Dispatch.drawio')
root = tree.getroot()

model = root.find('.//mxGraphModel')
rt = model.find('root')
for cell in rt.findall('mxCell'):
    if cell.get('edge') == '1':
        print("EDGE:", ET.tostring(cell).decode('utf-8'))
        break

for cell in rt.findall('mxCell'):
    if cell.get('style') and 'text' in cell.get('style') and cell.get('parent') != '1':
        print("LABEL:", ET.tostring(cell).decode('utf-8'))
        break
