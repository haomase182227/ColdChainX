import xml.etree.ElementTree as ET

tree = ET.parse('./docs/Sequence-Diagram-Dispatch.drawio')
root = tree.getroot()

model = root.find('.//mxGraphModel')
rt = model.find('root')
for cell in rt.findall('mxCell'):
    if cell.get('id') == 'message_from_1' or cell.get('id') == 'message_to_1':
        print("POINT:", ET.tostring(cell).decode('utf-8'))
