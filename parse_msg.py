import xml.etree.ElementTree as ET

tree = ET.parse('./docs/Sequence-Diagram-Dispatch.drawio')
root = tree.getroot()

model = root.find('.//mxGraphModel')
rt = model.find('root')
for cell in rt.findall('mxCell'):
    if cell.get('edge') == '1' and 'endArrow=block' in cell.get('style', ''):
        print("MSG EDGE:", ET.tostring(cell).decode('utf-8'))
        break
